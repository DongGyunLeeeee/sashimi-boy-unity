#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryPath,
    [Parameter(Mandatory)][ValidateSet('Developer','Reviewer')][string]$Role)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')
$script:sourceRoot = ConvertTo-SashimiPath -Path $RepositoryPath
Assert-SashimiNoReparsePoint -Path $script:sourceRoot
$script:sourceBytesRead = [long]0
$script:sourceBytesWritten = [long]0
$script:sourceOutputBytes = [long]0
$script:sourceRequests = 0
$script:sourceUtf8 = [Text.UTF8Encoding]::new($false,$true)
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class SashimiSourceLease {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFileW(string p, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder p, uint n, uint f);
    [StructLayout(LayoutKind.Sequential)] struct Info {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h, out Info i);
    public static void Check(SafeFileHandle h, string expected, bool directory) {
        var text = new StringBuilder(32768);
        uint size = GetFinalPathNameByHandleW(h,text,(uint)text.Capacity,0);
        Info info;
        if (size == 0 || size >= text.Capacity || !GetFileInformationByHandle(h,out info)) throw new IOException("SOURCE_HANDLE_REFUSED");
        string path = text.ToString();
        if (path.StartsWith(@"\\?\")) path = path.Substring(4);
        if (!String.Equals(Path.GetFullPath(path).TrimEnd('\\'),Path.GetFullPath(expected).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase) ||
            (info.Attributes & 0x400) != 0 || (!directory && info.Links != 1)) throw new IOException("SOURCE_HANDLE_REFUSED");
    }
    public static SafeFileHandle Directory(string path) {
        var handle = CreateFileW(path,0x80,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        try { if (handle.IsInvalid) throw new IOException("SOURCE_DIRECTORY_REFUSED"); Check(handle,path,true); return handle; }
        catch { handle.Dispose(); throw; }
    }
}
'@

function Open-SourceParentLeases {
    param([string]$FullPath,[switch]$Create)
    $leases = [Collections.Generic.List[IDisposable]]::new()
    try {
        $parent = [IO.Path]::GetDirectoryName($FullPath)
        $current = [IO.Path]::GetPathRoot($parent)
        $leases.Add([SashimiSourceLease]::Directory($current))
        foreach ($part in $parent.Substring($current.Length).Split('\',[StringSplitOptions]::RemoveEmptyEntries)) {
            $current = Join-Path $current $part
            if (-not [IO.Directory]::Exists($current)) {
                if (-not $Create -or -not (Test-SashimiPathWithin -Path $current -Root $script:sourceRoot)) { throw 'SOURCE_DIRECTORY_REFUSED' }
                [void][IO.Directory]::CreateDirectory($current)
            }
            $leases.Add([SashimiSourceLease]::Directory($current))
        }
        return ,$leases
    }
    catch { foreach ($lease in $leases) { $lease.Dispose() }; throw }
}

# This server exposes source data only. There is no command, URL, process,
# arbitrary directory, deletion, rename, Git or credential operation.
function Resolve-SourcePath {
    param([string]$Path, [switch]$Write)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 512 -or
        $Path -match '[\\:\x00-\x1f]' -or $Path.StartsWith('/') -or
        $Path -match '(^|/)(\.{1,2}|\.git|\.codex|\.agents|Library|Temp|Logs|UserSettings|obj|\.vs)(/|$)') {
        throw 'SOURCE_PATH_REFUSED'
    }
    foreach ($part in $Path.Split('/')) {
        if ([string]::IsNullOrEmpty($part) -or $part -match '[. ]$' -or
            $part -match '^(?i:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)') { throw 'SOURCE_PATH_REFUSED' }
    }
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ([IO.Path]::GetFileName($Path) -cnotin @('SPEC_VERSION','.gitignore','.gitattributes') -and
        $extension -notin @('.cs','.ps1','.md','.json','.txt','.asmdef','.asmref','.meta','.shader','.hlsl','.uss','.uxml','.unity','.prefab','.asset')) {
        throw 'SOURCE_TYPE_REFUSED'
    }
    if ($Write -and ($Role -cne 'Developer' -or
        $Path -match '(^|/)Art/Source(/|$)|^(Packages|ProjectSettings)/' -or
        $extension -in @('.unity','.prefab','.asset'))) { throw 'SOURCE_WRITE_REFUSED' }
    $full = [IO.Path]::GetFullPath((Join-Path $script:sourceRoot $Path))
    if (-not (Test-SashimiPathWithin -Path $full -Root $script:sourceRoot)) { throw 'SOURCE_PATH_REFUSED' }
    Assert-SashimiNoReparsePoint -Path $full
    return $full
}

function Read-SourceFile {
    param([string]$Path)
    $full = Resolve-SourcePath $Path
    $leases = Open-SourceParentLeases $full
    $stream = $null
    try {
        $stream = [IO.FileStream]::new($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        [SashimiSourceLease]::Check($stream.SafeFileHandle,$full,$false)
        if ($stream.Length -gt 1MB) { throw 'SOURCE_FILE_TOO_LARGE' }
        $script:sourceBytesRead += $stream.Length
        if ($script:sourceBytesRead -gt 128MB) { throw 'SOURCE_READ_BUDGET_EXCEEDED' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes,0,$bytes.Length)
        $text = $script:sourceUtf8.GetString($bytes)
        if (Test-SashimiRecognizableSensitiveText -Text $text) { throw 'SOURCE_SENSITIVE_CONTENT' }
        return [pscustomobject]@{ Text=$text; Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant(); Bytes=$bytes.Length }
    }
    finally { if ($null -ne $stream) { $stream.Dispose() }; foreach ($lease in $leases) { $lease.Dispose() } }
}

function Get-SourcePaths {
    $paths = [Collections.Generic.List[string]]::new()
    $stack = [Collections.Generic.Stack[string]]::new(); $stack.Push($script:sourceRoot)
    while ($stack.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $stack.Pop() -Force) {
            if ($entry.Name -in @('.git','.codex','.agents','Library','Temp','Logs','UserSettings','obj','.vs')) { continue }
            Assert-SashimiNoReparsePoint -Path $entry.FullName
            if ($entry.PSIsContainer) { $stack.Push($entry.FullName); continue }
            $relative = [IO.Path]::GetRelativePath($script:sourceRoot,$entry.FullName).Replace('\','/')
            try { [void](Resolve-SourcePath $relative) } catch { continue }
            if ($paths.Count -ge 20000) { throw 'SOURCE_FILE_COUNT_EXCEEDED' }
            $paths.Add($relative)
        }
    }
    return @($paths.ToArray() | Sort-Object -CaseSensitive)
}

function Invoke-SourceTool {
    param([string]$Name,[object]$Arguments)
    $fields = switch ($Name) {
        'list_files' { @('prefix','offset') }
        'read_file' { @('path','startLine','lineCount') }
        'write_file' { @('path','expectedSha256','content') }
        'replace_text' { @('path','expectedSha256','oldText','newText') }
        default { throw 'SOURCE_TOOL_REFUSED' }
    }
    if ($null -eq $Arguments -or @($Arguments.PSObject.Properties).Count -ne $fields.Count) { throw 'SOURCE_ARGUMENTS_REFUSED' }
    foreach ($field in $fields) {
        if ($null -eq $Arguments.PSObject.Properties[$field]) { throw 'SOURCE_ARGUMENTS_REFUSED' }
        if ($field -in @('offset','startLine','lineCount')) {
            if ($Arguments.$field -isnot [long] -and $Arguments.$field -isnot [int]) { throw 'SOURCE_ARGUMENTS_REFUSED' }
        }
        elseif ($Arguments.$field -isnot [string]) { throw 'SOURCE_ARGUMENTS_REFUSED' }
    }
    switch ($Name) {
        'replace_text' {
            [void](Resolve-SourcePath ([string]$Arguments.path) -Write)
            $source = Read-SourceFile ([string]$Arguments.path)
            if ($source.Sha256 -cne $Arguments.expectedSha256) { throw 'SOURCE_STALE_HASH' }
            $old = [string]$Arguments.oldText
            if ($old.Length -eq 0 -or $old.Length -gt 65536 -or $Arguments.newText.Length -gt 65536) { throw 'SOURCE_REPLACEMENT_REFUSED' }
            $index = $source.Text.IndexOf($old,[StringComparison]::Ordinal)
            if ($index -lt 0 -or $source.Text.IndexOf($old,$index+$old.Length,[StringComparison]::Ordinal) -ge 0) { throw 'SOURCE_REPLACEMENT_REFUSED' }
            $next = $source.Text.Substring(0,$index) + $Arguments.newText + $source.Text.Substring($index+$old.Length)
            return Invoke-SourceTool 'write_file' ([pscustomobject]@{path=$Arguments.path;expectedSha256=$Arguments.expectedSha256;content=$next})
        }
        'list_files' {
            $prefix = [string]$Arguments.prefix
            if ($prefix.Length -gt 512) { throw 'SOURCE_PREFIX_REFUSED' }
            $offset = [int]$Arguments.offset
            if ($offset -lt 0 -or $offset -gt 20000) { throw 'SOURCE_OFFSET_REFUSED' }
            $paths = @(Get-SourcePaths | Where-Object { $_.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) })
            return @{ files=@($paths | Select-Object -Skip $offset -First 200); total=$paths.Count; nextOffset=[Math]::Min($offset+200,$paths.Count) }
        }
        'read_file' {
            $start = [int]$Arguments.startLine; $count=[int]$Arguments.lineCount
            if ($start -lt 1 -or $count -lt 1 -or $count -gt 300) { throw 'SOURCE_LINE_RANGE_REFUSED' }
            $source = Read-SourceFile ([string]$Arguments.path)
            $lines = @($source.Text -split "`n")
            $text = [string]::Join("`n",[string[]]@($lines | Select-Object -Skip ($start-1) -First $count))
            if ($script:sourceUtf8.GetByteCount($text) -gt 65536) { throw 'SOURCE_PAGE_TOO_LARGE' }
            return @{ path=[string]$Arguments.path; sha256=$source.Sha256; totalLines=$lines.Count; startLine=$start; text=$text }
        }
        'write_file' {
            $full = Resolve-SourcePath ([string]$Arguments.path) -Write
            $expected = [string]$Arguments.expectedSha256
            $content = [string]$Arguments.content
            $bytes = $script:sourceUtf8.GetBytes($content)
            if ($bytes.Length -gt 1MB -or $script:sourceBytesWritten + $bytes.Length -gt 8MB) { throw 'SOURCE_WRITE_BUDGET_EXCEEDED' }
            if (Test-SashimiRecognizableSensitiveText -Text $content) { throw 'SOURCE_SENSITIVE_CONTENT' }
            $exists = Test-Path -LiteralPath $full -PathType Leaf
            if ($exists) {
                $before = Read-SourceFile ([string]$Arguments.path)
                if ($expected -cne $before.Sha256) { throw 'SOURCE_STALE_HASH' }
                if ([IO.Path]::GetExtension($full) -ieq '.meta') {
                    $oldGuid = [regex]::Match($before.Text,'(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
                    $newGuid = [regex]::Match($content,'(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
                    if (-not $oldGuid -or $oldGuid -cne $newGuid) { throw 'SOURCE_GUID_CHANGE_REFUSED' }
                }
            }
            elseif ($expected -cne 'missing') { throw 'SOURCE_STALE_HASH' }
            $leases = Open-SourceParentLeases $full -Create
            $mode = if ($exists) { [IO.FileMode]::Open } else { [IO.FileMode]::CreateNew }
            $stream = $null
            try {
                $stream = [IO.FileStream]::new($full,$mode,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
                [SashimiSourceLease]::Check($stream.SafeFileHandle,$full,$false)
                if ($exists) {
                    $current = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                    if ($current -cne $expected) { throw 'SOURCE_STALE_HASH' }
                }
                $stream.Position=0; $stream.Write($bytes,0,$bytes.Length); $stream.SetLength($bytes.Length); $stream.Flush($true)
            }
            finally { if ($null -ne $stream) { $stream.Dispose() }; foreach ($lease in $leases) { $lease.Dispose() } }
            $script:sourceBytesWritten += $bytes.Length
            return @{ path=[string]$Arguments.path; sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant(); bytes=$bytes.Length }
        }
        default { throw 'SOURCE_TOOL_REFUSED' }
    }
}

$stringSchema = @{type='string'}; $integerSchema=@{type='integer'}
$tools = @(
    @{name='list_files';description='List repository text source paths, 200 per page. Use empty prefix and offset zero to start.';
        inputSchema=@{type='object';additionalProperties=$false;required=@('prefix','offset');properties=@{prefix=$stringSchema;offset=$integerSchema}};annotations=@{readOnlyHint=$true;openWorldHint=$false}},
    @{name='read_file';description='Read up to 300 source lines and return the full file SHA-256 for optimistic writes.';
        inputSchema=@{type='object';additionalProperties=$false;required=@('path','startLine','lineCount');properties=@{path=$stringSchema;startLine=$integerSchema;lineCount=$integerSchema}};annotations=@{readOnlyHint=$true;openWorldHint=$false}}
)
if ($Role -ceq 'Developer') {
    $tools += @{name='write_file';description='Replace one UTF-8 source file after checking expectedSha256 from read_file, or use missing for a new file. Preserves existing meta GUIDs. Serialized assets must use their generator.';
        inputSchema=@{type='object';additionalProperties=$false;required=@('path','expectedSha256','content');properties=@{path=$stringSchema;expectedSha256=$stringSchema;content=$stringSchema}};
        annotations=@{readOnlyHint=$false;destructiveHint=$false;openWorldHint=$false}}
    $tools += @{name='replace_text';description='Replace exactly one literal text occurrence in a source file after checking the full file SHA-256. Read the relevant lines first.';
        inputSchema=@{type='object';additionalProperties=$false;required=@('path','expectedSha256','oldText','newText');properties=@{path=$stringSchema;expectedSha256=$stringSchema;oldText=$stringSchema;newText=$stringSchema}};
        annotations=@{readOnlyHint=$false;destructiveHint=$false;openWorldHint=$false}}
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
public static class SashimiSourceInput {
    public static string ReadLine() {
        var value = new StringBuilder();
        while (true) {
            int c = Console.In.Read();
            if (c < 0) return value.Length == 0 ? null : value.ToString();
            if (c == 10) return value.ToString();
            if (value.Length >= 2 * 1024 * 1024) throw new InvalidOperationException("SOURCE_REQUEST_TOO_LARGE");
            if (c != 13) value.Append((char)c);
        }
    }
}
'@
[Console]::InputEncoding=$script:sourceUtf8
[Console]::OutputEncoding=$script:sourceUtf8
while ($null -ne ($line=[SashimiSourceInput]::ReadLine())) {
    $script:sourceRequests++
    if ($script:sourceRequests -gt 500) { throw 'SOURCE_REQUEST_BUDGET_EXCEEDED' }
    $request=$line | ConvertFrom-Json -Depth 32 -DateKind String
    if ($null -eq $request.PSObject.Properties['id']) { continue }
    $response=@{jsonrpc='2.0';id=$request.id}
    try {
        switch ([string]$request.method) {
            'initialize' { $response.result=@{protocolVersion='2024-11-05';capabilities=@{tools=@{}};serverInfo=@{name='sashimi_source';version='1.0.0'}} }
            'ping' { $response.result=@{} }
            'tools/list' { $response.result=@{tools=$tools} }
            'tools/call' {
                $name=[string]$request.params.name
                if (@($tools.name) -cnotcontains $name) { throw 'SOURCE_TOOL_REFUSED' }
                $value=Invoke-SourceTool $name $request.params.arguments
                $response.result=@{content=@(@{type='text';text=($value | ConvertTo-Json -Depth 16 -Compress)});isError=$false}
            }
            default { $response.error=@{code=-32601;message='Unsupported method'} }
        }
    }
    catch {
        $code=[string]$_.Exception.Message
        if ($code -cnotmatch '^SOURCE_[A-Z_]+$') { $code='SOURCE_OPERATION_REFUSED' }
        $response.result=@{content=@(@{type='text';text=$code});isError=$true}
    }
    $json=$response | ConvertTo-Json -Depth 32 -Compress
    $script:sourceOutputBytes += $script:sourceUtf8.GetByteCount($json)
    if ($script:sourceOutputBytes -gt 8MB) { throw 'SOURCE_OUTPUT_BUDGET_EXCEEDED' }
    [Console]::Out.WriteLine($json)
}
