#requires -Version 7.5

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Owner-approved 2026-09-26. This is a fixed Host policy, never an Issue/PR or
# configuration supplied exclusion. It applies only to the three preview files.
function Test-SashimiToleratedPreviewPath {
    param([int]$IssueNumber, [string]$Path)
    return $IssueNumber -eq 20 -and @(
        'Assets/_SashimiBoy/Art/Generated/Previews/Stage01/SalmonAssembly_Initial.png',
        'Assets/_SashimiBoy/Art/Generated/Previews/Stage01/SalmonAssembly_Parts.png',
        'Assets/_SashimiBoy/Art/Generated/Previews/Stage01/SalmonAssembly_Anchors.png'
    ) -ccontains $Path
}

function Initialize-SashimiPreviewComparison {
    if ('Sashimi.Host.PreviewPng' -as [type]) { return }
    # Decode original RGB/RGBA samples, not a renderer's premultiplied/converted
    # bitmap. Fixed input/decode quotas also bound corrupt or compressed bombs.
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
namespace Sashimi.Host {
    public sealed class PreviewDifference {
        public int Width, Height, MaximumRgbDelta;
        public long PixelCount, ChangedPixels;
        public bool AlphaExact, MetadataExact, Passed;
    }
    public static class PreviewPng {
        const int MaximumBytes = 25 * 1024 * 1024;
        const int MaximumDecodedBytes = 32 * 1024 * 1024;
        sealed class Decoded { public int Width, Height, Channels; public byte[] Pixels, Metadata; }
        static uint UInt32(byte[] b, int p) => ((uint)b[p] << 24) | ((uint)b[p+1] << 16) | ((uint)b[p+2] << 8) | b[p+3];
        static readonly uint[] CrcTable = MakeCrcTable();
        static uint[] MakeCrcTable() {
            var table = new uint[256];
            for (uint i=0; i<256; i++) { uint c=i; for (int k=0;k<8;k++) c=(c & 1)!=0 ? 0xedb88320U^(c>>1) : c>>1; table[i]=c; }
            return table;
        }
        static uint Crc(byte[] b, int p, int n) {
            uint c=0xffffffffU; for(int i=0;i<n;i++) c=CrcTable[(c^b[p+i])&255]^(c>>8); return c^0xffffffffU;
        }
        static InvalidDataException Invalid() => new InvalidDataException("Preview PNG is malformed, unsupported, or exceeds its fixed quota.");
        // Avoid zlib read-ahead concealing trailing compressed data. IDAT size
        // and decoded size are already bounded before decompression starts.
        sealed class ExactInput : MemoryStream {
            public ExactInput(byte[] b) : base(b, false) {}
            public override int Read(byte[] b,int o,int n) => base.Read(b,o,Math.Min(n,1));
            public override int Read(Span<byte> b) => base.Read(b.Slice(0,Math.Min(b.Length,1)));
        }
        static int Paeth(int a,int b,int c) {
            int p=a+b-c, pa=Math.Abs(p-a), pb=Math.Abs(p-b), pc=Math.Abs(p-c);
            return pa<=pb && pa<=pc ? a : pb<=pc ? b : c;
        }
        static Decoded Decode(byte[] bytes) {
            byte[] signature={137,80,78,71,13,10,26,10};
            if(bytes==null || bytes.Length<45 || bytes.Length>MaximumBytes || !bytes.AsSpan(0,8).SequenceEqual(signature)) throw Invalid();
            int width=0,height=0,channels=0,p=8; bool header=false,data=false,endedData=false,end=false;
            using var idat=new MemoryStream(); using var metadata=new MemoryStream();
            while(p<bytes.Length) {
                if(bytes.Length-p<12) throw Invalid();
                uint n=UInt32(bytes,p); if(n>(uint)(bytes.Length-p-12)) throw Invalid();
                int size=(int)n; string type=Encoding.ASCII.GetString(bytes,p+4,4);
                for(int k=p+4;k<p+8;k++) if(!((bytes[k]>=65 && bytes[k]<=90)||(bytes[k]>=97 && bytes[k]<=122))) throw Invalid();
                if((bytes[p+6]&32)!=0 || Crc(bytes,p+4,size+4)!=UInt32(bytes,p+8+size)) throw Invalid();
                if(!header && type!="IHDR") throw Invalid();
                if(type=="IHDR") {
                    if(header || p!=8 || size!=13) throw Invalid();
                    uint w=UInt32(bytes,p+8),h=UInt32(bytes,p+12);
                    if(w==0 || h==0 || w>16384 || h>16384 || bytes[p+16]!=8 || (bytes[p+17]!=2 && bytes[p+17]!=6) || bytes[p+18]!=0 || bytes[p+19]!=0 || bytes[p+20]!=0) throw Invalid();
                    width=(int)w; height=(int)h; channels=bytes[p+17]==2 ? 3 : 4;
                    if((long)width*height*channels>MaximumDecodedBytes) throw Invalid();
                    header=true;
                } else if(type=="IDAT") {
                    if(endedData) throw Invalid();
                    if(!data) metadata.Write(Encoding.ASCII.GetBytes("IDAT"));
                    data=true; idat.Write(bytes,p+8,size);
                } else {
                    if(data) endedData=true;
                    if(type=="IEND") { if(!data || size!=0 || p+12!=bytes.Length) throw Invalid(); end=true; }
                    // tRNS adds transparency to RGB; palettes, animation and
                    // unknown critical chunks are outside this narrow policy.
                    else if(type=="tRNS" || type=="acTL" || type=="fcTL" || type=="fdAT" || (bytes[p+4]&32)==0) throw Invalid();
                }
                if(type!="IDAT") metadata.Write(bytes,p,size+12);
                p+=size+12; if(end) break;
            }
            if(!end || idat.Length<6) throw Invalid();
            byte[] compressed=idat.ToArray();
            if((compressed[0]&15)!=8 || (compressed[0]>>4)>7 || ((compressed[0]<<8)+compressed[1])%31!=0 || (compressed[1]&32)!=0) throw Invalid();
            byte[] pixels=new byte[width*height*channels]; int stride=width*channels;
            using var input=new ExactInput(compressed);
            using(var zlib=new ZLibStream(input,CompressionMode.Decompress,true)) {
                byte[] row=new byte[stride]; uint adlerA=1,adlerB=0;
                for(int y=0;y<height;y++) {
                    int filter=zlib.ReadByte(); if(filter<0 || filter>4) throw Invalid();
                    adlerA=(adlerA+(uint)filter)%65521; adlerB=(adlerB+adlerA)%65521;
                    zlib.ReadExactly(row);
                    for(int x=0;x<stride;x++) {
                        adlerA=(adlerA+row[x])%65521; adlerB=(adlerB+adlerA)%65521;
                        int at=y*stride+x, a=x>=channels ? pixels[at-channels] : 0, b=y>0 ? pixels[at-stride] : 0, c=y>0 && x>=channels ? pixels[at-stride-channels] : 0;
                        int predictor=filter==0 ? 0 : filter==1 ? a : filter==2 ? b : filter==3 ? (a+b)/2 : Paeth(a,b,c);
                        pixels[at]=unchecked((byte)(row[x]+predictor));
                    }
                }
                if(zlib.ReadByte()!=-1 || input.Position!=input.Length || UInt32(compressed,compressed.Length-4)!=((adlerB<<16)|adlerA)) throw Invalid();
            }
            return new Decoded { Width=width,Height=height,Channels=channels,Pixels=pixels,Metadata=SHA256.HashData(metadata.ToArray()) };
        }
        public static PreviewDifference Compare(byte[] first, byte[] second) {
            var a=Decode(first); var b=Decode(second);
            if(a.Width!=b.Width || a.Height!=b.Height || a.Channels!=b.Channels) throw Invalid();
            var result=new PreviewDifference { Width=a.Width,Height=a.Height,PixelCount=(long)a.Width*a.Height,AlphaExact=true,MetadataExact=a.Metadata.AsSpan().SequenceEqual(b.Metadata) };
            for(int p=0;p<a.Pixels.Length;p+=a.Channels) {
                bool changed=false;
                for(int c=0;c<3;c++) { int delta=Math.Abs(a.Pixels[p+c]-b.Pixels[p+c]); if(delta>0) changed=true; result.MaximumRgbDelta=Math.Max(result.MaximumRgbDelta,delta); }
                if(changed) result.ChangedPixels++;
                if(a.Channels==4 && a.Pixels[p+3]!=b.Pixels[p+3]) result.AlphaExact=false;
            }
            result.Passed=result.MetadataExact && result.AlphaExact && result.MaximumRgbDelta<=1 && result.ChangedPixels*1000<=result.PixelCount;
            return result;
        }
    }
}
'@
}

function Get-SashimiPreviewCaptures {
    param([string]$ProjectRoot, [AllowEmptyCollection()][object[]]$Snapshot, [int]$IssueNumber)
    $captures = [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Snapshot) {
        if (-not (Test-SashimiToleratedPreviewPath $IssueNumber $entry.Path) -or
            (Get-SashimiPropertyValue $entry 'Kind' 'File') -cne 'File') { continue }
        $path = Join-Path $ProjectRoot $entry.Path
        Assert-SashimiNoReparsePoint $path
        $stream = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try {
            if ($stream.Length -lt 1 -or $stream.Length -gt 25MB -or $stream.Length -ne $entry.Length) { throw 'Preview capture exceeds its quota or changed since the manifest.' }
            $bytes = [byte[]]::new([int]$stream.Length)
            $stream.ReadExactly($bytes,0,$bytes.Length)
            if ($stream.ReadByte() -ne -1 -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $entry.Sha256) {
                throw 'Preview capture changed since the manifest.'
            }
            $captures.Add($entry.Path,$bytes)
        } finally { $stream.Dispose() }
    }
    # Captures remain private in memory. Only hashes and comparison metrics may
    # enter the existing bounded JSON evidence files.
    return ,$captures
}

function Compare-SashimiGeneratedManifest {
    param(
        [AllowEmptyCollection()][object[]]$Before, [AllowEmptyCollection()][object[]]$After,
        [object]$BeforeCaptures, [object]$AfterCaptures, [int]$IssueNumber
    )
    $left=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $right=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Before) { $left.Add([string]$entry.Path,$entry) }
    foreach ($entry in $After) { $right.Add([string]$entry.Path,$entry) }
    $changed=[Collections.Generic.List[string]]::new()
    $previews=[Collections.Generic.List[object]]::new()
    $passed=$left.Count -eq $right.Count
    foreach ($path in $left.Keys) {
        if (-not $right.ContainsKey($path)) { $passed=$false; $changed.Add($path); continue }
        $a=$left[$path]; $b=$right[$path]
        $kindA=[string](Get-SashimiPropertyValue $a 'Kind' 'File'); $kindB=[string](Get-SashimiPropertyValue $b 'Kind' 'File')
        if ($kindA -cne $kindB -or $kindA -ceq 'Missing') { $passed=$false; $changed.Add($path); continue }
        if ([long]$a.Length -eq [long]$b.Length -and [string]$a.Sha256 -ceq [string]$b.Sha256) { continue }
        $changed.Add($path)
        if ($kindA -cne 'File' -or -not (Test-SashimiToleratedPreviewPath $IssueNumber $path)) { $passed=$false; continue }
        $evidence=[ordered]@{ Path=$path; BeforeSha256=$a.Sha256; AfterSha256=$b.Sha256; BeforeLength=$a.Length; AfterLength=$b.Length; Passed=$false; Metrics=$null; Error=$null }
        try {
            foreach ($pair in @(@($BeforeCaptures,$a),@($AfterCaptures,$b))) {
                $capture=$pair[0]; $entry=$pair[1]
                if ($null -eq $capture -or -not $capture.ContainsKey($path) -or $capture[$path].Length -ne $entry.Length -or
                    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$capture[$path])).ToLowerInvariant() -cne $entry.Sha256) {
                    throw 'Preview comparison requires original bytes bound to the manifest.'
                }
            }
            Initialize-SashimiPreviewComparison
            $evidence.Metrics=[Sashimi.Host.PreviewPng]::Compare($BeforeCaptures[$path],$AfterCaptures[$path])
            $evidence.Passed=$evidence.Metrics.Passed
        } catch { $evidence.Error='Original PNG bytes are unavailable, malformed, unsupported, or outside the fixed policy.' }
        if (-not $evidence.Passed) { $passed=$false }
        $previews.Add([pscustomobject]$evidence)
    }
    foreach ($path in $right.Keys) { if (-not $left.ContainsKey($path)) { $passed=$false; $changed.Add($path) } }
    return [pscustomobject]@{ Policy='Stage01SalmonPreviewRgb1PixelPermille1-v1'; Passed=$passed; ByteIdentical=($changed.Count -eq 0); ChangedPaths=$changed.ToArray(); Previews=$previews.ToArray() }
}

function Compare-SashimiGeneratorReproducibility {
    param(
        [object]$Run1, [object]$Run2, [object]$Committed,
        [int]$IssueNumber, [switch]$CompareSource, [switch]$CompareCommitted
    )
    $pairs=[Collections.Generic.List[object]]::new()
    $pairs.Add([pscustomobject]@{Pair='Run1-Run2 outputs';Result=(Compare-SashimiGeneratedManifest -Before $Run1.Output -After $Run2.Output -BeforeCaptures $Run1.Captures -AfterCaptures $Run2.Captures -IssueNumber $IssueNumber)})
    if ($CompareSource) {
        $pairs.Add([pscustomobject]@{Pair='Run1-Run2 complete source';Result=(Compare-SashimiGeneratedManifest -Before $Run1.Source -After $Run2.Source -BeforeCaptures $Run1.Captures -AfterCaptures $Run2.Captures -IssueNumber $IssueNumber)})
    }
    if ($CompareCommitted) {
        # Similarity is not transitive; neither committed comparison can be
        # inferred from the independent runs' comparison.
        $pairs.Add([pscustomobject]@{Pair='Committed-Run1';Result=(Compare-SashimiGeneratedManifest -Before $Committed.Output -After $Run1.Output -BeforeCaptures $Committed.Captures -AfterCaptures $Run1.Captures -IssueNumber $IssueNumber)})
        $pairs.Add([pscustomobject]@{Pair='Committed-Run2';Result=(Compare-SashimiGeneratedManifest -Before $Committed.Output -After $Run2.Output -BeforeCaptures $Committed.Captures -AfterCaptures $Run2.Captures -IssueNumber $IssueNumber)})
    }
    return [pscustomobject]@{
        Passed=(@($pairs | Where-Object { -not $_.Result.Passed }).Count -eq 0)
        CommittedPassed=(@($pairs | Where-Object { $_.Pair.StartsWith('Committed-', [StringComparison]::Ordinal) -and -not $_.Result.Passed }).Count -eq 0)
        Comparisons=$pairs.ToArray()
    }
}

function Assert-SashimiGeneratorManifestUnchanged {
    param([object[]]$Before, [object[]]$After)
    # No tolerance between observations of a single completed run. This also
    # detects Run2 writing back into Run1's project via an absolute path.
    $comparison=Compare-SashimiGeneratedManifest -Before $Before -After $After -IssueNumber 0
    if (-not $comparison.Passed) { throw 'The primary generator source manifest changed after Run1 evidence was captured.' }
}

$script:SashimiMinimumPowerShellVersion = [Version]'7.5.0'
if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion -lt $script:SashimiMinimumPowerShellVersion) {
    throw "SASHIMI BOY Host Automation requires PowerShell Core $script:SashimiMinimumPowerShellVersion or newer."
}

# Windows PowerShell hosts otherwise inherit an OEM code page. Every host script
# dot-sources this file before reading or writing protocol JSON.
$script:SashimiUtf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $script:SashimiUtf8NoBom
[Console]::OutputEncoding = $script:SashimiUtf8NoBom
$global:OutputEncoding = $script:SashimiUtf8NoBom

$script:SashimiHostSchemaVersion = 1
$script:SashimiExpectedRepository = 'DongGyunLeeeee/sashimi-boy-unity'
$script:SashimiExpectedRemoteUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity.git'
$script:SashimiExpectedGitLfsUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity.git/info/lfs'
$script:SashimiExpectedGitAuthorName = 'DongGyunLeeeee'
$script:SashimiExpectedGitAuthorEmail = '83210475+DongGyunLeeeee@users.noreply.github.com'
$script:SashimiExpectedProjectOwner = 'DongGyunLeeeee'
$script:SashimiExpectedProjectNumber = 1
$script:SashimiTaskName = 'SASHIMI BOY Host Orchestrator'
$script:SashimiStablePowerShell = 'C:\Program Files\PowerShell\7\pwsh.exe'
$script:SashimiRunMarkerName = '.sashimi-host-run.json'
$script:SashimiMutexName = 'Global\SashimiBoyHostOrchestrator'
$script:SashimiExecutableIdentityName = 'ExecutableIdentity.json'
$script:SashimiExecutableProperties = @('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','PowerShellExecutable','UnityExecutable')
$script:SashimiExecutableIdentityActive = $false
$script:SashimiBoundExecutableIdentities = @()
$script:SashimiConfiguredExecutablePaths = @{}
$script:SashimiProtectedInstallRoot = [IO.Path]::Combine(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),
    'SashimiBoyAutomation')
$script:SashimiProtectedCodexDistributionRoot = [IO.Path]::Combine(
    $script:SashimiProtectedInstallRoot,
    'CodexDistributions')

function ConvertTo-SashimiJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [AllowNull()]
        [object]$InputObject,

        [switch]$Pretty
    )

    process {
        if ($Pretty) {
            return ($InputObject | ConvertTo-Json -Depth 32)
        }
        return ($InputObject | ConvertTo-Json -Depth 32 -Compress)
    }
}

function Get-SashimiPropertyValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][object]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }
    return $property.Value
}

function ConvertTo-SashimiPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Path,
        [switch]$AllowMissing,
        [switch]$Lexical
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not [IO.Path]::IsPathRooted($expanded)) {
        $expanded = Join-Path -Path (Get-Location).ProviderPath -ChildPath $expanded
    }
    if (-not $Lexical -and (Test-Path -LiteralPath $expanded)) {
        $expanded = (Resolve-Path -LiteralPath $expanded -ErrorAction Stop).ProviderPath
    }
    elseif (-not $AllowMissing -and -not (Test-Path -LiteralPath $expanded)) {
        throw "Path does not exist: $Path"
    }
    $full = [IO.Path]::GetFullPath($expanded)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) {
        $full = $full.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    }
    return $full
}

function Test-SashimiPathEqual {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Left, [Parameter(Mandatory = $true)][string]$Right)

    $leftPath = ConvertTo-SashimiPath -Path $Left -AllowMissing
    $rightPath = ConvertTo-SashimiPath -Path $Right -AllowMissing
    return [string]::Equals($leftPath, $rightPath, [StringComparison]::OrdinalIgnoreCase)
}

function Test-SashimiPathWithin {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)

    $candidate = ConvertTo-SashimiPath -Path $Path -AllowMissing
    $boundary = ConvertTo-SashimiPath -Path $Root -AllowMissing
    if (Test-SashimiPathEqual -Left $candidate -Right $boundary) {
        return $false
    }
    return $candidate.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SashimiNoReparsePoint {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$Recurse)

    $normalized = ConvertTo-SashimiPath -Path $Path -AllowMissing -Lexical
    $cursor = $normalized
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are forbidden in host automation paths: $($item.FullName)"
            }
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }

    if ($Recurse -and (Test-Path -LiteralPath $normalized -PathType Container)) {
        foreach ($entry in @(Get-ChildItem -LiteralPath $normalized -Force -Recurse -ErrorAction Stop)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing recursive cleanup because a reparse point exists: $($entry.FullName)"
            }
        }
    }
}

function Write-SashimiUtf8File {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )

    $parent = Split-Path -Parent (ConvertTo-SashimiPath -Path $Path -AllowMissing)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, $script:SashimiUtf8NoBom)
}

function Read-SashimiJsonFile {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = ConvertTo-SashimiPath -Path $Path
    try {
        return ([IO.File]::ReadAllText($resolved, [Text.Encoding]::UTF8) | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop)
    }
    catch {
        throw "Invalid UTF-8 JSON file '$resolved': $($_.Exception.Message)"
    }
}

function Get-SashimiTextSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $bytes = $script:SashimiUtf8NoBom.GetBytes($Text)
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Get-SashimiPullRequestContentSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Title,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Body
    )

    # Property order and the v1 tag are part of the pin contract. Hash the
    # exact, unredacted source text so title/body edits cannot remain current
    # merely because the PR number, ref, and head commit did not change.
    $canonical = [ordered]@{
        Schema = 'sashimi-pr-content-v1'
        Title = $Title
        Body = $Body
    }
    return Get-SashimiTextSha256 -Text (ConvertTo-SashimiJson -InputObject $canonical)
}

function ConvertTo-SashimiCanonicalConversationRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Record,
        [ValidateSet('IssueComment', 'PullRequestComment', 'PullRequestReview')][string]$Kind
    )

    $recordKind = $Kind
    if ([string]::IsNullOrWhiteSpace($recordKind)) {
        $recordKind = [string](Get-SashimiPropertyValue $Record 'Kind' '')
        switch -CaseSensitive ($recordKind) {
            'Comment' {
                $url = [string](Get-SashimiPropertyValue $Record 'Url' (Get-SashimiPropertyValue $Record 'url' ''))
                $recordKind = if ($url -match '/pull/') { 'PullRequestComment' } else { 'IssueComment' }
            }
            'Review' { $recordKind = 'PullRequestReview' }
            '' { $recordKind = 'IssueComment' }
        }
    }
    if (@('IssueComment', 'PullRequestComment', 'PullRequestReview') -cnotcontains $recordKind) {
        throw "Unsupported conversation record kind '$recordKind'."
    }

    $authorValue = Get-SashimiPropertyValue $Record 'author' $null
    $commitValue = Get-SashimiPropertyValue $Record 'commit' $null
    $createdAt = [string](Get-SashimiPropertyValue $Record 'CreatedAt' (Get-SashimiPropertyValue $Record 'createdAt' ''))
    $submittedAt = [string](Get-SashimiPropertyValue $Record 'SubmittedAt' (Get-SashimiPropertyValue $Record 'submittedAt' ''))
    if ($recordKind -ceq 'PullRequestReview' -and [string]::IsNullOrWhiteSpace($createdAt)) { $createdAt = $submittedAt }
    if ($recordKind -ceq 'PullRequestReview' -and [string]::IsNullOrWhiteSpace($submittedAt)) { $submittedAt = $createdAt }
    $updatedAt = [string](Get-SashimiPropertyValue $Record 'UpdatedAt' (Get-SashimiPropertyValue $Record 'updatedAt' ''))
    if ([string]::IsNullOrWhiteSpace($updatedAt)) { $updatedAt = $createdAt }

    # Property insertion order is part of the v1 digest contract. Values are
    # deliberately not redacted or normalized: the digest pins the exact text
    # and metadata that queue eligibility consumed, while only the digest is
    # carried into mutation requests.
    return [pscustomobject][ordered]@{
        Kind = $recordKind
        Url = [string](Get-SashimiPropertyValue $Record 'Url' (Get-SashimiPropertyValue $Record 'url' ''))
        CreatedAt = $createdAt
        UpdatedAt = $updatedAt
        SubmittedAt = if ($recordKind -ceq 'PullRequestReview') { $submittedAt } else { '' }
        AuthorLogin = [string](Get-SashimiPropertyValue $Record 'AuthorLogin' (Get-SashimiPropertyValue $authorValue 'login' ''))
        AuthorAssociation = [string](Get-SashimiPropertyValue $Record 'AuthorAssociation' (Get-SashimiPropertyValue $Record 'authorAssociation' ''))
        WasEdited = [bool](Get-SashimiPropertyValue $Record 'WasEdited' (Get-SashimiPropertyValue $Record 'includesCreatedEdit' $false))
        Body = [string](Get-SashimiPropertyValue $Record 'Body' (Get-SashimiPropertyValue $Record 'body' ''))
        ReviewState = if ($recordKind -ceq 'PullRequestReview') { [string](Get-SashimiPropertyValue $Record 'ReviewState' (Get-SashimiPropertyValue $Record 'State' (Get-SashimiPropertyValue $Record 'state' ''))) } else { '' }
        CommitOid = if ($recordKind -ceq 'PullRequestReview') { ([string](Get-SashimiPropertyValue $Record 'CommitOid' (Get-SashimiPropertyValue $commitValue 'oid' (Get-SashimiPropertyValue $Record 'HeadSha' '')))).ToLowerInvariant() } else { '' }
    }
}

function Get-SashimiConversationSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Records)

    $serialized = [Collections.Generic.List[string]]::new()
    foreach ($record in @($Records)) {
        $canonical = ConvertTo-SashimiCanonicalConversationRecord -Record $record
        $serialized.Add((ConvertTo-SashimiJson -InputObject $canonical))
    }
    $serialized.Sort([StringComparer]::Ordinal)
    $canonicalJson = '[' + [string]::Join(',', $serialized) + ']'
    return Get-SashimiTextSha256 -Text $canonicalJson
}

function Test-SashimiHarnessMode {
    [CmdletBinding()]
    param()
    return [string]::Equals($env:SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS, '1', [StringComparison]::Ordinal)
}

function Assert-SashimiFixtureAllowed {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FixturePath, [switch]$DryRun)

    if (-not $DryRun -and -not (Test-SashimiHarnessMode)) {
        throw 'Fixture adapters are allowed only in -DryRun or the explicit host-automation test harness.'
    }
    return ConvertTo-SashimiPath -Path $FixturePath
}

function ConvertTo-SashimiExecutablePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$RequireFile
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path) -or
        $Path -cnotmatch '^[A-Za-z]:\\') {
        throw "$Name must be an absolute local Windows executable path; PATH-resolved names are forbidden."
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($Path, $fullPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([IO.Path]::GetExtension($fullPath), '.exe', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must be a canonical absolute .exe path."
    }
    if (-not $RequireFile) { return $fullPath }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Name executable file does not exist: $fullPath"
    }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::Equals($item.FullName, $fullPath, [StringComparison]::OrdinalIgnoreCase) -or
        [int64]$item.Length -lt 1) {
        throw "$Name must identify its exact canonical non-empty, non-reparse executable file."
    }
    return $item.FullName
}

function Get-SashimiCodexDistributionHash {
    param([Parameter(Mandatory)][object]$Executable)
    $hostEntry = Get-SashimiPropertyValue $Executable 'CodeModeHost' $null
    if ($null -eq $hostEntry -or [string]$hostEntry.FileName -cne 'codex-code-mode-host.exe') {
        throw 'Codex executable identity must bind its code-mode host companion.'
    }
    foreach ($entry in @($Executable,$hostEntry)) {
        if ([string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$entry.Length -lt 1) {
            throw 'Codex distribution requires two complete executable identities.'
        }
    }
    $lines = @(
        [string]::Join([char]0,@('codex.exe',[string]$Executable.Sha256,[string]$Executable.Length)),
        [string]::Join([char]0,@('codex-code-mode-host.exe',[string]$hostEntry.Sha256,[string]$hostEntry.Length))
    )
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]::Join([char]10,$lines))
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Import-SashimiExecutableIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Config,
        [Parameter(Mandatory = $true)][string]$ConfigPath
    )

    $script:SashimiExecutableIdentityActive = $false
    $script:SashimiBoundExecutableIdentities = @()
    $script:SashimiConfiguredExecutablePaths = @{}
    foreach ($name in $script:SashimiExecutableProperties) {
        $configuredPath = ConvertTo-SashimiExecutablePath -Name $name -Path ([string]$Config.$name)
        $Config.$name = $configuredPath
        $script:SashimiConfiguredExecutablePaths[$name] = $configuredPath
    }

    $identityPath = Join-Path (Split-Path -Parent $ConfigPath) $script:SashimiExecutableIdentityName
    if (-not (Test-Path -LiteralPath $identityPath)) { return }
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
        throw 'Executable identity must be a plain sibling file of Config.json.'
    }
    Assert-SashimiNoReparsePoint -Path $identityPath
    $identity = Read-SashimiJsonFile -Path $identityPath
    if ([int](Get-SashimiPropertyValue $identity 'SchemaVersion' 0) -ne 2) {
        throw 'Executable identity SchemaVersion must be 2.'
    }
    $entries = @($identity.Executables)
    if ($entries.Count -ne $script:SashimiExecutableProperties.Count) {
        throw "Executable identity must contain exactly $($script:SashimiExecutableProperties.Count) bound tools."
    }

    $verified = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $script:SashimiExecutableProperties.Count; $index++) {
        $name = $script:SashimiExecutableProperties[$index]
        $entry = $entries[$index]
        $entryPath = ConvertTo-SashimiExecutablePath -Name $name -Path ([string]$entry.Path) -RequireFile
        if ([string]$entry.Name -cne $name -or [int64]$entry.Length -lt 1 -or
            [string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            -not [string]::Equals($entryPath, [string]$Config.$name, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Executable identity entry does not match Config.json at index $index."
        }
        $item = Get-Item -LiteralPath $entryPath -Force -ErrorAction Stop
        $currentHash = (Get-FileHash -LiteralPath $entryPath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        if ([int64]$item.Length -ne [int64]$entry.Length -or $currentHash -cne [string]$entry.Sha256) {
            throw "$name failed executable identity verification while importing Config.json."
        }
        $verifiedEntry = [ordered]@{
                Name = $name
                Path = $entryPath
                Length = [int64]$entry.Length
                Sha256 = [string]$entry.Sha256
            }
        if ($name -ceq 'CodexExecutable') {
            [void](Get-SashimiCodexDistributionHash -Executable $entry)
            $companion = $entry.CodeModeHost
            $companionPath = Join-Path (Split-Path -Parent $entryPath) 'codex-code-mode-host.exe'
            [void](ConvertTo-SashimiExecutablePath -Name CodexCodeModeHost -Path $companionPath -RequireFile)
            Assert-SashimiNoReparsePoint -Path $companionPath
            $companionFile = Get-Item -LiteralPath $companionPath -Force -ErrorAction Stop
            if ($companionFile.Length -ne [int64]$companion.Length -or
                (Get-FileHash -LiteralPath $companionPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$companion.Sha256) {
                throw 'Codex code-mode host failed executable identity verification while importing Config.json.'
            }
            $verifiedEntry.CodeModeHost = [pscustomobject]@{ FileName='codex-code-mode-host.exe'; Length=[int64]$companion.Length; Sha256=[string]$companion.Sha256 }
        }
        $verified.Add([pscustomobject]$verifiedEntry)
    }
    $script:SashimiBoundExecutableIdentities = $verified.ToArray()
    $script:SashimiExecutableIdentityActive = $true
}

function Get-SashimiConfiguredExecutablePath {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateSet('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','PowerShellExecutable','UnityExecutable')][string]$Name)

    if (-not $script:SashimiConfiguredExecutablePaths.ContainsKey($Name)) {
        throw "Host config has not bound '$Name' for process launch."
    }
    $path = [string]$script:SashimiConfiguredExecutablePaths[$Name]
    if ([string]::IsNullOrWhiteSpace($path)) { throw "Host config bound an empty '$Name' path." }
    return $path
}

function Assert-SashimiBoundExecutableIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FilePath)

    if (-not $script:SashimiExecutableIdentityActive) { return }
    $candidate = ConvertTo-SashimiExecutablePath -Name 'Process FilePath' -Path $FilePath -RequireFile
    $matches = @($script:SashimiBoundExecutableIdentities | Where-Object {
            [string]::Equals([string]$_.Path, $candidate, [StringComparison]::OrdinalIgnoreCase)
        })
    if ($matches.Count -lt 1) {
        throw 'Process FilePath is not one of the executables bound by the protected identity file.'
    }
    foreach ($entry in $matches) {
        $item = Get-Item -LiteralPath $candidate -Force -ErrorAction Stop
        $currentHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        if ([int64]$item.Length -ne [int64]$entry.Length -or $currentHash -cne [string]$entry.Sha256) {
            throw "$($entry.Name) changed after executable identity verification; process launch refused."
        }
    }
}

function Test-SashimiExecutableIdentityActive {
    [CmdletBinding()]
    param()

    return [bool]$script:SashimiExecutableIdentityActive
}

function Get-SashimiFileSystemAccessRules {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $sections = [Security.AccessControl.AccessControlSections]::Access
    $security = if ($item.PSIsContainer) {
        [IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]$item, $sections)
    }
    else {
        [IO.FileSystemAclExtensions]::GetAccessControl([IO.FileInfo]$item, $sections)
    }
    return @($security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
}

function Get-SashimiFileSystemOwnerSid {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $sections = [Security.AccessControl.AccessControlSections]::Owner
    $security = if ($item.PSIsContainer) {
        [IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]$item, $sections)
    }
    else {
        [IO.FileSystemAclExtensions]::GetAccessControl([IO.FileInfo]$item, $sections)
    }
    return [string]$security.GetOwner([Security.Principal.SecurityIdentifier]).Value
}

function Assert-SashimiProtectedCodexExecutable {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FilePath)

    $candidate = ConvertTo-SashimiExecutablePath -Name 'CodexExecutable' -Path $FilePath -RequireFile
    if (-not $script:SashimiExecutableIdentityActive) {
        if (Test-SashimiHarnessMode) { return $candidate }
        throw 'A live Codex launch requires the protected executable identity manifest.'
    }

    $identityEntries = @($script:SashimiBoundExecutableIdentities | Where-Object {
            [string]$_.Name -ceq 'CodexExecutable' -and
            [string]::Equals([string]$_.Path, $candidate, [StringComparison]::OrdinalIgnoreCase)
        })
    if ($identityEntries.Count -ne 1) {
        throw 'CodexExecutable is not the exact Codex identity bound by the protected manifest.'
    }

    $protectedInstallRoot = ConvertTo-SashimiPath -Path $script:SashimiProtectedInstallRoot -AllowMissing -Lexical
    $protectedRoot = ConvertTo-SashimiPath -Path $script:SashimiProtectedCodexDistributionRoot -AllowMissing -Lexical
    $expectedProtectedRoot = ConvertTo-SashimiPath -Path (Join-Path $protectedInstallRoot 'CodexDistributions') -AllowMissing -Lexical
    if (-not (Test-SashimiPathEqual -Left $protectedRoot -Right $expectedProtectedRoot)) {
        throw 'The protected Codex distribution root is not the exact child of the protected install root.'
    }
    $distributionHash = Get-SashimiCodexDistributionHash -Executable $identityEntries[0]
    $expectedDistributionRoot = ConvertTo-SashimiPath -Path (Join-Path $protectedRoot $distributionHash) -AllowMissing -Lexical
    $expectedCodexPath = ConvertTo-SashimiPath -Path (Join-Path $expectedDistributionRoot 'codex.exe') -AllowMissing -Lexical
    if (-not (Test-SashimiPathEqual -Left $candidate -Right $expectedCodexPath)) {
        throw "CodexExecutable must be the exact content-addressed path '$expectedCodexPath'."
    }
    Assert-SashimiNoReparsePoint -Path $candidate
    $companionPath = Join-Path $expectedDistributionRoot 'codex-code-mode-host.exe'
    [void](ConvertTo-SashimiExecutablePath -Name CodexCodeModeHost -Path $companionPath -RequireFile)
    Assert-SashimiNoReparsePoint -Path $companionPath
    $companion = $identityEntries[0].CodeModeHost
    if ((Get-Item -LiteralPath $companionPath -Force).Length -ne [int64]$companion.Length -or
        (Get-FileHash -LiteralPath $companionPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$companion.Sha256) {
        throw 'Codex code-mode host changed after executable identity verification.'
    }
    $distributionItems = @(Get-ChildItem -LiteralPath $expectedDistributionRoot -Force -ErrorAction Stop)
    if ($distributionItems.Count -ne 2 -or @($distributionItems | Where-Object {
        $_.PSIsContainer -or @('codex.exe','codex-code-mode-host.exe') -cnotcontains $_.Name
    }).Count -ne 0) { throw 'Protected Codex distribution must contain exactly its two bound executables.' }

    # Only Windows servicing identities may write the executable or a parent in
    # the protected distribution. Read/execute ACEs for ordinary users are
    # expected and do not intersect this deliberately granular write mask.
    $trustedWriterSids = @(
        'S-1-5-18',       # LOCAL SYSTEM
        'S-1-5-32-544',   # BUILTIN\Administrators
        'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464' # TrustedInstaller
    )
    $writeMask = [Security.AccessControl.FileSystemRights]::WriteData -bor
        [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($leaf in @($candidate,$companionPath)) {
      $cursor = $leaf
      while ($true) {
        if (-not (Test-Path -LiteralPath $cursor)) { throw "Protected Codex path disappeared: $cursor" }
        $ownerSid = Get-SashimiFileSystemOwnerSid -Path $cursor
        if ($trustedWriterSids -cnotcontains $ownerSid) {
            throw "Protected Codex path is owned by untrusted SID '$ownerSid': $cursor"
        }
        foreach ($rule in @(Get-SashimiFileSystemAccessRules -Path $cursor)) {
            if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
                (($rule.FileSystemRights -band $writeMask) -eq 0)) {
                continue
            }
            $sid = [string]$rule.IdentityReference.Value
            if ($trustedWriterSids -cnotcontains $sid) {
                throw "Protected Codex path grants write-like access to untrusted SID '$sid': $cursor"
            }
        }
        if (Test-SashimiPathEqual -Left $cursor -Right $protectedInstallRoot) { break }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or
            -not ((Test-SashimiPathEqual -Left $parent -Right $protectedInstallRoot) -or
                (Test-SashimiPathWithin -Path $parent -Root $protectedInstallRoot))) {
            throw 'Protected Codex ancestor walk escaped its protected install root.'
        }
        $cursor = $parent
      }
    }
    return $candidate
}

function Close-SashimiExecutableLaunchLease {
    param([AllowNull()][object]$Lease)
    if ($null -eq $Lease) { return }
    foreach ($stream in @($Lease.Stream) + @(Get-SashimiPropertyValue $Lease 'CompanionStreams' @())) {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Open-SashimiExecutableLaunchLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][ValidateSet('Generic','Git','GitHub','Codex','Unity')][string]$Kind,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = ''
    )

    $candidate = ConvertTo-SashimiExecutablePath -Name 'Process FilePath' -Path $FilePath -RequireFile
    Assert-SashimiFixtureExecutableBoundary -FilePath $candidate -Kind $Kind -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory
    if ($Kind -ceq 'Codex') { [void](Assert-SashimiProtectedCodexExecutable -FilePath $candidate) }
    Assert-SashimiBoundExecutableIdentity -FilePath $candidate

    $stream = $null
    $companionStreams = [Collections.Generic.List[IO.FileStream]]::new()
    try {
        # FileShare.Read lets the image loader read this exact file while
        # preventing ordinary write/delete replacement until Process.Start has
        # consumed the path. The subsequent hash is computed from this lease.
        $stream = [IO.File]::Open($candidate, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $matches = @($script:SashimiBoundExecutableIdentities | Where-Object {
                [string]::Equals([string]$_.Path, $candidate, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($script:SashimiExecutableIdentityActive -and $matches.Count -lt 1) {
            throw 'Process FilePath is not one of the executables bound by the protected identity file.'
        }
        foreach ($entry in $matches) {
            $hasher = [Security.Cryptography.SHA256]::Create()
            try {
                $stream.Position = 0
                $currentHash = ([Convert]::ToHexString($hasher.ComputeHash($stream))).ToLowerInvariant()
            }
            finally { $hasher.Dispose() }
            if ([int64]$stream.Length -ne [int64]$entry.Length -or $currentHash -cne [string]$entry.Sha256) {
                throw "$($entry.Name) changed immediately before process creation; launch refused."
            }
        }
        if ($Kind -ceq 'Codex' -and $script:SashimiExecutableIdentityActive) {
            $codexEntry = @($matches | Where-Object { [string]$_.Name -ceq 'CodexExecutable' })[0]
            $companion = $codexEntry.CodeModeHost
            $companionPath = Join-Path (Split-Path -Parent $candidate) 'codex-code-mode-host.exe'
            $companionStream = [IO.File]::Open($companionPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            $companionStreams.Add($companionStream)
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $companionHash = ([Convert]::ToHexString($hasher.ComputeHash($companionStream))).ToLowerInvariant() }
            finally { $hasher.Dispose() }
            if ($companionStream.Length -ne [int64]$companion.Length -or $companionHash -cne [string]$companion.Sha256) {
                throw 'Codex code-mode host changed immediately before process creation; launch refused.'
            }
        }
        Assert-SashimiNoReparsePoint -Path $candidate
        if ($Kind -ceq 'Codex') { [void](Assert-SashimiProtectedCodexExecutable -FilePath $candidate) }
        return [pscustomobject][ordered]@{ Path = $candidate; Stream = $stream; CompanionStreams=$companionStreams.ToArray() }
    }
    catch {
        if ($null -ne $stream) { $stream.Dispose() }
        foreach ($companionStream in $companionStreams) { $companionStream.Dispose() }
        throw
    }
}

function Get-SashimiMarkedFixtureRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    # This ownership marker is the existing test-suite contract, not a runtime
    # authorization mechanism. Compiler inputs must remain below this boundary.
    $candidate = ConvertTo-SashimiPath -Path $Path -AllowMissing -Lexical
    Assert-SashimiNoReparsePoint -Path $candidate
    $cursor = Split-Path -Parent $candidate
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    while (-not [string]::IsNullOrWhiteSpace($cursor) -and
        (Test-SashimiPathWithin -Path $cursor -Root $temporaryRoot)) {
        $markerPath = Join-Path $cursor '.host-tests-owner.json'
        if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
            Assert-SashimiNoReparsePoint -Path $markerPath
            $marker = Read-SashimiJsonFile -Path $markerPath
            $runId = [string](Get-SashimiPropertyValue $marker 'RunId' '')
            $rootValue = [string](Get-SashimiPropertyValue $marker 'Root' '')
            if ([int](Get-SashimiPropertyValue $marker 'SchemaVersion' 0) -eq 1 -and
                $runId -cmatch '^[0-9a-f]{32}$' -and
                (Split-Path -Leaf $cursor) -ceq ('SashimiBoyHostTests-' + $runId) -and
                -not [string]::IsNullOrWhiteSpace($rootValue) -and
                (Test-SashimiPathEqual -Left $rootValue -Right $cursor)) { return $cursor }
            break
        }
        $cursor = Split-Path -Parent $cursor
    }
    throw 'FIXTURE_COMPILER_REFUSED: input or output is outside a valid marked fixture.'
}

function Assert-SashimiFixtureCompilerInvocation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = ''
    )

    if (-not (Test-SashimiHarnessMode)) {
        throw 'FIXTURE_COMPILER_REFUSED: compilation is a test-harness bootstrap operation only.'
    }
    $compilerPaths = @(
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    )
    if ($compilerPaths -inotcontains $FilePath) {
        throw 'FIXTURE_COMPILER_REFUSED: compiler is not the exact Windows Framework compiler.'
    }
    if ($ArgumentList.Count -ne 9 -or [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        throw 'FIXTURE_COMPILER_REFUSED: expected the fixed nine-argument fixture compilation plan.'
    }
    $prefix = @('/nologo','/noconfig','/nostdlib+','/target:exe')
    for ($i=0; $i -lt $prefix.Count; $i++) {
        if ($ArgumentList[$i] -cne $prefix[$i]) {
            throw 'FIXTURE_COMPILER_REFUSED: unexpected compiler switch or response-file request.'
        }
    }
    if (-not $ArgumentList[4].StartsWith('/out:',[StringComparison]::Ordinal)) {
        throw 'FIXTURE_COMPILER_REFUSED: explicit output is required.'
    }
    $sourcePath = [string]$ArgumentList[8]
    $outputPath = ([string]$ArgumentList[4]).Substring(5)
    foreach ($path in @($sourcePath,$outputPath,$WorkingDirectory)) {
        if (-not [IO.Path]::IsPathFullyQualified($path) -or
            -not [string]::Equals($path,[IO.Path]::GetFullPath($path),[StringComparison]::OrdinalIgnoreCase)) {
            throw 'FIXTURE_COMPILER_REFUSED: source, output and working directory must be canonical absolute paths.'
        }
        Assert-SashimiNoReparsePoint -Path $path
    }
    $pairs = @{
        'SashimiHostFakeTool.cs' = 'SashimiHostFakeTool.exe'
        'fake-codex.cs' = 'fake-codex.exe'
        'fake-unity-descendant.cs' = 'fake-unity-descendant.exe'
    }
    $sourceName = [IO.Path]::GetFileName($sourcePath)
    if (-not $pairs.ContainsKey($sourceName) -or
        [IO.Path]::GetFileName($outputPath) -cne [string]$pairs[$sourceName]) {
        throw 'FIXTURE_COMPILER_REFUSED: only the three reviewed fake-adapter source/output pairs are accepted.'
    }
    $sourceRoot = Get-SashimiMarkedFixtureRoot -Path $sourcePath
    $outputRoot = Get-SashimiMarkedFixtureRoot -Path $outputPath
    if (-not (Test-SashimiPathEqual -Left $sourceRoot -Right $outputRoot) -or
        -not (Test-SashimiPathEqual -Left (Split-Path -Parent $sourcePath) -Right $WorkingDirectory) -or
        -not (Test-SashimiPathEqual -Left (Split-Path -Parent $outputPath) -Right $WorkingDirectory)) {
        throw 'FIXTURE_COMPILER_REFUSED: compiler working directory, source and output must share the owned fixture directory.'
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
        (Get-Item -LiteralPath $sourcePath).Length -gt 1048576) {
        throw 'FIXTURE_COMPILER_REFUSED: source is absent, not a file, or over the one-MiB bootstrap limit.'
    }
    $compilerDirectory = Split-Path -Parent $FilePath
    $references = @('mscorlib.dll','System.dll','System.Core.dll')
    for ($i=0; $i -lt $references.Count; $i++) {
        $referencePath = Join-Path $compilerDirectory $references[$i]
        if ($ArgumentList[5+$i] -cne ('/reference:' + $referencePath) -or
            -not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
            throw 'FIXTURE_COMPILER_REFUSED: unexpected or missing Framework reference.'
        }
        Assert-SashimiNoReparsePoint -Path $referencePath
    }
    Assert-SashimiNoReparsePoint -Path $FilePath
    $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $FilePath -ErrorAction Stop
    if ([string]$signature.Status -cne 'Valid' -or $null -eq $signature.SignerCertificate -or
        [string]$signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=(?:"Microsoft Corporation"|Microsoft Corporation)(?:,|$)') {
        throw 'FIXTURE_COMPILER_REFUSED: the exact Framework compiler lacks a valid Microsoft signature.'
    }
    # The caller still applies the executable identity check and launch lease.
}

function Assert-SashimiFixtureExecutableBoundary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$Kind,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = ''
    )

    if (-not (Test-SashimiHarnessMode)) { return }
    if ($Kind -ceq 'Generic' -and @(
            'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
            'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
        ) -icontains $FilePath) {
        Assert-SashimiFixtureCompilerInvocation -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory
        return
    }
    # The harness may run reviewed PowerShell scripts, but every other child
    # must be fixture-owned. A partially customized config must never fall
    # back to installed Git, gh, Unity, or Codex, even for a read-only probe.
    if ($Kind -ceq 'Generic' -and [string]::Equals($FilePath,
            'C:\Program Files\PowerShell\7\pwsh.exe', [StringComparison]::OrdinalIgnoreCase)) { return }
    $cursor = Split-Path -Parent $FilePath
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    while (-not [string]::IsNullOrWhiteSpace($cursor) -and
        (Test-SashimiPathWithin -Path $cursor -Root $temporaryRoot)) {
        $markerPath = Join-Path $cursor '.host-tests-owner.json'
        if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
            Assert-SashimiNoReparsePoint -Path $FilePath
            $marker = Read-SashimiJsonFile -Path $markerPath
            if ([string]$marker.RunId -cmatch '^[0-9a-f]{32}$' -and
                (Split-Path -Leaf $cursor) -ceq ('SashimiBoyHostTests-' + [string]$marker.RunId) -and
                (Test-SashimiPathEqual -Left ([string]$marker.Root) -Right $cursor)) { return }
            break
        }
        $cursor = Split-Path -Parent $cursor
    }
    $leaf = [IO.Path]::GetFileName($FilePath) -replace '[^A-Za-z0-9._-]', '_'
    if ($leaf.Length -gt 80) { $leaf = $leaf.Substring(0,80) }
    $kindLabel = $Kind -replace '[^A-Za-z0-9_-]', '_'
    if ($kindLabel.Length -gt 24) { $kindLabel = $kindLabel.Substring(0,24) }
    throw "FIXTURE_LIVE_BOUNDARY_REFUSED: kind=$kindLabel; executable=$leaf; executable is not owned by the marked fixture; installed-tool fallback is prohibited."
}

function Get-SashimiJsonObjectMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
        throw "$Context must be a JSON object."
    }
    $map = [Collections.Generic.Dictionary[string,Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
    $caseInsensitiveNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($property in $Element.EnumerateObject()) {
        $name = [string]$property.Name
        if (-not $caseInsensitiveNames.Add($name)) {
            throw "$Context contains a duplicate or case-variant property '$name'."
        }
        if (-not $map.TryAdd($name, $property.Value.Clone())) {
            throw "$Context contains duplicate property '$name'."
        }
    }
    return ,$map
}

function Assert-SashimiExactJsonObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context,
        [Parameter(Mandatory = $true)][string[]]$PropertyNames
    )

    $map = Get-SashimiJsonObjectMap -Element $Element -Context $Context
    if ($map.Count -ne $PropertyNames.Count) {
        throw "$Context must contain exactly: $([string]::Join(', ', $PropertyNames))."
    }
    foreach ($propertyName in $PropertyNames) {
        if (-not $map.ContainsKey($propertyName)) { throw "$Context is missing property '$propertyName'." }
    }
    foreach ($actualName in @($map.Keys)) {
        if ($PropertyNames -cnotcontains $actualName) { throw "$Context contains unknown property '$actualName'." }
    }
    return ,$map
}

function Assert-SashimiJsonKind {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][Text.Json.JsonValueKind]$Kind,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Element.ValueKind -ne $Kind) {
        throw "$Context must be JSON $($Kind.ToString().ToLowerInvariant())."
    }
}

function Assert-SashimiJsonInteger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-SashimiJsonKind -Element $Element -Kind Number -Context $Context
    $integer = 0
    if (-not $Element.TryGetInt32([ref]$integer)) { throw "$Context must be a 32-bit JSON integer." }
}

function Assert-SashimiJsonStringArray {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-SashimiJsonKind -Element $Element -Kind Array -Context $Context
    $index = 0
    foreach ($value in $Element.EnumerateArray()) {
        Assert-SashimiJsonKind -Element $value -Kind String -Context "$Context[$index]"
        $index++
    }
}

function Assert-SashimiConfigDecodedValues {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)
    # Audit decoded JSON strings too: Unicode escapes must not conceal tokens.
    switch ($Element.ValueKind) {
        Object {
            foreach ($property in $Element.EnumerateObject()) {
                # Audit decoded keys before they can enter schema diagnostics.
                $nameDocument = [Text.Json.JsonDocument]::Parse(($property.Name | ConvertTo-Json -Compress))
                try { Assert-SashimiConfigDecodedValues -Element $nameDocument.RootElement }
                finally { $nameDocument.Dispose() }
                Assert-SashimiConfigDecodedValues -Element $property.Value
            }
        }
        Array {
            foreach ($value in $Element.EnumerateArray()) {
                Assert-SashimiConfigDecodedValues -Element $value
            }
        }
        String {
            $value = $Element.GetString()
            if ($value -match '(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+' -or
                $value -match '(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}' -or
                $value -match '(?i)\bsk-[A-Za-z0-9_-]{8,}' -or
                $value -match '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or
                $value -match '(?i)://[^\s/@:"]+:[^\s/@"]+@') {
                throw 'Configuration contains recognizable credential material in a decoded value.'
            }
        }
    }
}

function Assert-SashimiHostConfigJsonSchema {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$JsonText)

    # Configuration is non-secret data. Endpoint, proxy, CA, credential, and
    # authentication-location controls have no supported schema field, and
    # recognizable credentials are rejected before object materialization.
    if ($JsonText -match '(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+' -or
        $JsonText -match '(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}' -or
        $JsonText -match '(?i)\bsk-[A-Za-z0-9_-]{8,}' -or
        $JsonText -match '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or
        $JsonText -match '(?i)://[^\s/@:"]+:[^\s/@"]+@') {
        throw 'Configuration contains recognizable credential material.'
    }

    $document = $null
    try {
        $options = [Text.Json.JsonDocumentOptions]::new()
        $options.AllowTrailingCommas = $false
        $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
        $options.MaxDepth = 64
        try { $document = [Text.Json.JsonDocument]::Parse($JsonText, $options) }
        catch { throw 'Configuration is invalid JSON; parser input is not retained.' }
        Assert-SashimiConfigDecodedValues -Element $document.RootElement
        $rootNames = @(
            'SchemaVersion','Repository','ProjectOwner','ProjectNumber','DefaultBranch','RemoteUrl','RunRoot','ArtifactRetentionDays',
            'GitExecutable','GitLfsExecutable','GitAuthorName','GitAuthorEmail','GitHubCli','CodexExecutable','PowerShellExecutable','UnityExecutable',
            'ExpectedUnityVersion','MutexName','Task','Timeouts','Retry','Security','IssueValidations'
        )
        $root = Assert-SashimiExactJsonObject -Element $document.RootElement -Context 'Config' -PropertyNames $rootNames
        foreach ($name in @('SchemaVersion','ProjectNumber','ArtifactRetentionDays')) {
            Assert-SashimiJsonInteger -Element $root[$name] -Context "Config.$name"
        }
        foreach ($name in @('Repository','ProjectOwner','DefaultBranch','RemoteUrl','RunRoot','GitExecutable','GitLfsExecutable','GitAuthorName','GitAuthorEmail','GitHubCli','CodexExecutable','PowerShellExecutable','UnityExecutable','ExpectedUnityVersion','MutexName')) {
            Assert-SashimiJsonKind -Element $root[$name] -Kind String -Context "Config.$name"
        }

        $task = Assert-SashimiExactJsonObject -Element $root['Task'] -Context 'Config.Task' -PropertyNames @('Name','User','IntervalMinutes','StartWhenAvailable','WakeToRun','MultipleInstances')
        foreach ($name in @('Name','User','MultipleInstances')) {
            Assert-SashimiJsonKind -Element $task[$name] -Kind String -Context "Config.Task.$name"
        }
        Assert-SashimiJsonInteger -Element $task['IntervalMinutes'] -Context 'Config.Task.IntervalMinutes'
        foreach ($name in @('StartWhenAvailable','WakeToRun')) {
            if ($task[$name].ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) {
                throw "Config.Task.$name must be JSON boolean."
            }
        }

        $timeouts = Assert-SashimiExactJsonObject -Element $root['Timeouts'] -Context 'Config.Timeouts' -PropertyNames @('CodexSeconds','GitSeconds','GitHubSeconds','UnityStageSeconds','GeneratorSeconds')
        foreach ($name in @($timeouts.Keys)) { Assert-SashimiJsonInteger -Element $timeouts[$name] -Context "Config.Timeouts.$name" }
        $retry = Assert-SashimiExactJsonObject -Element $root['Retry'] -Context 'Config.Retry' -PropertyNames @('MaximumAttempts','CooldownSeconds')
        foreach ($name in @($retry.Keys)) { Assert-SashimiJsonInteger -Element $retry[$name] -Context "Config.Retry.$name" }

        $security = Assert-SashimiExactJsonObject -Element $root['Security'] -Context 'Config.Security' -PropertyNames @('AuthorizedPrAuthors','CodexWorkspaceWriteNetworkAccess','ProtectedPathPatterns','ArtifactExclusionPatterns')
        foreach ($name in @('AuthorizedPrAuthors','ProtectedPathPatterns','ArtifactExclusionPatterns')) {
            Assert-SashimiJsonStringArray -Element $security[$name] -Context "Config.Security.$name"
        }
        if ($security['CodexWorkspaceWriteNetworkAccess'].ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) {
            throw 'Config.Security.CodexWorkspaceWriteNetworkAccess must be JSON boolean.'
        }

        $validationMap = Get-SashimiJsonObjectMap -Element $root['IssueValidations'] -Context 'Config.IssueValidations'
        foreach ($validationName in @($validationMap.Keys)) {
            if ($validationName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or
                $validationName -match '(?i)(?:token|secret|password|credential|proxy|endpoint|certificate|auth|codex.?home|base.?url)') {
                throw "Config.IssueValidations property '$validationName' has an invalid identifier."
            }
            $context = "Config.IssueValidations.$validationName"
            $definition = Assert-SashimiExactJsonObject -Element $validationMap[$validationName] -Context $context -PropertyNames @('IssueNumber','UnityExecuteMethod','Arguments','DeterminismPaths','ScreenshotPaths','PreviewPaths','AllowedProtectedPathPatterns')
            Assert-SashimiJsonInteger -Element $definition['IssueNumber'] -Context "$context.IssueNumber"
            Assert-SashimiJsonKind -Element $definition['UnityExecuteMethod'] -Kind String -Context "$context.UnityExecuteMethod"
            foreach ($name in @('Arguments','DeterminismPaths','ScreenshotPaths','PreviewPaths','AllowedProtectedPathPatterns')) {
                Assert-SashimiJsonStringArray -Element $definition[$name] -Context "$context.$name"
            }
        }
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function Import-SashimiHostConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    $normalizedConfigPath = ConvertTo-SashimiPath -Path $ConfigPath
    Assert-SashimiNoReparsePoint -Path $normalizedConfigPath
    try {
        $configBytes = [IO.File]::ReadAllBytes($normalizedConfigPath)
        $configText = [Text.UTF8Encoding]::new($false, $true).GetString($configBytes)
        Assert-SashimiHostConfigJsonSchema -JsonText $configText
        $config = $configText | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch {
        throw "Invalid strict-schema UTF-8 configuration '$normalizedConfigPath': $($_.Exception.Message)"
    }
    if ([int](Get-SashimiPropertyValue $config 'SchemaVersion' 0) -ne $script:SashimiHostSchemaVersion) {
        throw "Config SchemaVersion must be $script:SashimiHostSchemaVersion."
    }
    if ([string](Get-SashimiPropertyValue $config 'Repository' '') -cne $script:SashimiExpectedRepository) {
        throw "Config Repository must be exactly '$script:SashimiExpectedRepository'."
    }
    if ([string](Get-SashimiPropertyValue $config 'ProjectOwner' '') -cne $script:SashimiExpectedProjectOwner -or
        [int](Get-SashimiPropertyValue $config 'ProjectNumber' 0) -ne $script:SashimiExpectedProjectNumber) {
        throw "Config must target Project '$script:SashimiExpectedProjectOwner/$script:SashimiExpectedProjectNumber'."
    }
    if ([string](Get-SashimiPropertyValue $config 'DefaultBranch' '') -cne 'main') {
        throw "Config DefaultBranch must be exactly 'main'."
    }
    if ([string](Get-SashimiPropertyValue $config 'RemoteUrl' '') -cne $script:SashimiExpectedRemoteUrl) {
        throw "Config RemoteUrl must be exactly '$script:SashimiExpectedRemoteUrl'."
    }
    if ([string](Get-SashimiPropertyValue $config 'MutexName' '') -cne $script:SashimiMutexName) {
        throw "Config MutexName must be exactly '$script:SashimiMutexName'."
    }

    $retention = [int](Get-SashimiPropertyValue $config 'ArtifactRetentionDays' 0)
    if ($retention -lt 1 -or $retention -gt 365) {
        throw 'ArtifactRetentionDays must be between 1 and 365.'
    }
    $runRoot = [string](Get-SashimiPropertyValue $config 'RunRoot' '')
    if ([string]::IsNullOrWhiteSpace($runRoot)) { throw 'Config RunRoot is required.' }
    $expandedRunRoot = ConvertTo-SashimiPath -Path $runRoot -AllowMissing -Lexical
    $expectedParent = ConvertTo-SashimiPath -Path (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SashimiBoyAutomation\Runs') -AllowMissing -Lexical
    if (-not (Test-SashimiPathEqual -Left $expandedRunRoot -Right $expectedParent) -and -not (Test-SashimiHarnessMode)) {
        throw "RunRoot must be exactly '$expectedParent' outside the test harness."
    }

    foreach ($required in @('GitExecutable', 'GitLfsExecutable', 'GitHubCli', 'CodexExecutable', 'PowerShellExecutable', 'UnityExecutable', 'GitAuthorName', 'GitAuthorEmail', 'Task', 'Timeouts', 'Retry', 'Security')) {
        if ($null -eq $config.PSObject.Properties[$required]) {
            throw "Config is missing required property '$required'."
        }
    }
    Import-SashimiExecutableIdentity -Config $config -ConfigPath $normalizedConfigPath
    if ([string]$config.PowerShellExecutable -cne $script:SashimiStablePowerShell) {
        throw "PowerShellExecutable must be '$script:SashimiStablePowerShell'."
    }
    if ([string]$config.GitAuthorName -cne $script:SashimiExpectedGitAuthorName -or
        [string]$config.GitAuthorEmail -cne $script:SashimiExpectedGitAuthorEmail) {
        throw 'GitAuthorName and GitAuthorEmail must equal the immutable repository-owner identity.'
    }
    if ([int]$config.Task.IntervalMinutes -ne 15 -or [string]$config.Task.Name -cne $script:SashimiTaskName -or
        [string]$config.Task.User -cne '02031' -or -not [bool]$config.Task.StartWhenAvailable -or
        -not [bool]$config.Task.WakeToRun -or [string]$config.Task.MultipleInstances -cne 'IgnoreNew') {
        throw 'Task configuration must retain the exact identity, name, interval, availability, wake, and IgnoreNew contract.'
    }
    if ([string]$config.ExpectedUnityVersion -cne '6000.4.0f1') {
        throw "ExpectedUnityVersion must be exactly '6000.4.0f1'."
    }
    foreach ($timeoutName in @('CodexSeconds','GitSeconds','GitHubSeconds','UnityStageSeconds','GeneratorSeconds')) {
        $timeoutValue = [int]$config.Timeouts.$timeoutName
        if ($timeoutValue -lt 1 -or $timeoutValue -gt 86400) {
            throw "Timeouts.$timeoutName must be between 1 and 86400 seconds."
        }
    }
    if ([int]$config.Retry.MaximumAttempts -lt 1 -or [int]$config.Retry.MaximumAttempts -gt 10 -or [int]$config.Retry.CooldownSeconds -lt 0 -or [int]$config.Retry.CooldownSeconds -gt 3600) {
        throw 'Retry settings are outside the supported bounds.'
    }
    $authorizedAuthors = @($config.Security.AuthorizedPrAuthors | ForEach-Object { [string]$_ })
    if ($authorizedAuthors.Count -lt 1 -or @($authorizedAuthors | Where-Object { $_ -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$' }).Count -gt 0) {
        throw 'Security.AuthorizedPrAuthors must contain at least one valid GitHub login.'
    }
    $mandatoryProtectedPatterns = @(
        'Assets/_SashimiBoy/Art/Source/**','Assets/**/*.unity','Assets/**/*.prefab','Assets/**/*.fbx',
        'Assets/**/*.wav','Assets/**/*.mp3','Packages/**','ProjectSettings/**'
    )
    $configuredProtectedPatterns = @($config.Security.ProtectedPathPatterns | ForEach-Object { [string]$_ })
    $missingProtectedPatterns = @($mandatoryProtectedPatterns | Where-Object { $configuredProtectedPatterns -cnotcontains $_ })
    if ($missingProtectedPatterns.Count -gt 0) { throw "Security.ProtectedPathPatterns is missing immutable protection: $($missingProtectedPatterns -join ', ')." }
    $artifactExclusions = @($config.Security.ArtifactExclusionPatterns | ForEach-Object { [string]$_ })
    foreach ($requiredExclusion in @('**/.git/**','**/.codex/**','**/*Save*/**')) {
        if ($artifactExclusions -cnotcontains $requiredExclusion) { throw "Security.ArtifactExclusionPatterns must include '$requiredExclusion'." }
    }
    if ([bool]$config.Security.CodexWorkspaceWriteNetworkAccess) {
        throw 'Security.CodexWorkspaceWriteNetworkAccess must remain false.'
    }
    foreach ($validationProperty in @($config.IssueValidations.PSObject.Properties)) {
        $definition = $validationProperty.Value
        if ([int]$definition.IssueNumber -lt 1 -or
            [string]$definition.UnityExecuteMethod -cnotmatch '^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+$') {
            throw "IssueValidations.$($validationProperty.Name) has an invalid issue number or Unity execute method."
        }
        foreach ($argument in @($definition.Arguments)) {
            $argumentText = [string]$argument
            if ($argumentText -match '[\x00\r\n]' -or $argumentText.Length -gt 1024 -or
                $argumentText -match '(?i)(?:^|[^A-Za-z0-9])(?:access[_-]?token|refresh[_-]?token|api[_-]?key|token|password|secret|credential|authorization|proxy|endpoint|base[_-]?url|ssl[_-]?cert|ca[_-]?bundle|codex[_-]?home|auth[_-]?(?:file|path|dir))(?:$|[^A-Za-z0-9])') {
                throw "IssueValidations.$($validationProperty.Name).Arguments contains an unsafe or secret-bearing value."
            }
        }
    }
    $config.RunRoot = $expandedRunRoot
    return $config
}

function Protect-SashimiText {
    [CmdletBinding()]
    param([AllowNull()][object]$Text)

    if ($null -eq $Text) { return '' }
    $value = [string]$Text
    $patterns = @(
        '(?i)\b(?:Proxy-)?Authorization\s*:\s*Basic\s+[A-Za-z0-9+/=]{8,}',
        '(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+',
        '(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}',
        '(?i)\bsk-[A-Za-z0-9_-]{8,}',
        '(?i)(?:"|'''')?(?:access[_-]?token|refresh[_-]?token|api[_-]?key|token|password|secret|credential)(?:"|'''')?\s*[=:]\s*(?:"[^"\r\n]*"|''''[^''''\r\n]*''''|[^\s,;}]+)',
        '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    )
    foreach ($pattern in $patterns) {
        $value = [regex]::Replace($value, $pattern, '[REDACTED_SECRET]')
    }
    $profilePath = [Environment]::GetFolderPath('UserProfile')
    if (-not [string]::IsNullOrWhiteSpace($profilePath)) {
        $value = $value.Replace($profilePath, '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
        $escapedProfilePath = $profilePath.Replace('\', '\\')
        $value = $value.Replace($escapedProfilePath, '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
        $forwardProfilePath = $profilePath.Replace('\', '/')
        $value = $value.Replace($forwardProfilePath, '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
    }
    $value = [regex]::Replace($value, '(?i)[A-Z]:\\[^\r\n"'']*\\(?:Save|Saves|SaveData|LocalLow)\\[^\r\n"'']*', '[REDACTED_SAVE_PATH]')
    return $value
}

function Protect-SashimiTextWithExactValues {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Text,
        [string[]]$ExactValues = @()
    )

    $value = Protect-SashimiText -Text $Text
    foreach ($exactValue in @($ExactValues | Where-Object {
                -not [string]::IsNullOrEmpty($_) -and $_.Length -ge 8 -and $_.Length -le 4096
            } | Sort-Object Length -Descending)) {
        $value = $value.Replace([string]$exactValue, '[REDACTED_SECRET]', [StringComparison]::Ordinal)
    }
    return $value
}

function Test-SashimiSensitiveEnvironmentName {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Name)

    $normalized = $Name.ToUpperInvariant()
    $exactNames = @(
        'GH_TOKEN','GITHUB_TOKEN','GITHUB_PAT','GITHUB_OAUTH_TOKEN',
        'OPENAI_API_KEY','CODEX_API_KEY','ANTHROPIC_API_KEY',
        'AWS_ACCESS_KEY_ID','AWS_SECRET_ACCESS_KEY','AWS_SESSION_TOKEN','AWS_SECURITY_TOKEN',
        'AZURE_CLIENT_SECRET','AZURE_CLIENT_CERTIFICATE_PATH','GOOGLE_APPLICATION_CREDENTIALS',
        'NPM_TOKEN','NODE_AUTH_TOKEN','NUGET_AUTH_TOKEN','PYPI_TOKEN','TWINE_PASSWORD',
        'DOCKER_AUTH_CONFIG','KUBECONFIG','GIT_ASKPASS','SSH_ASKPASS','SSH_AUTH_SOCK','SSH_AGENT_PID',
        'CI_JOB_TOKEN','SYSTEM_ACCESSTOKEN','VAULT_TOKEN','SENTRY_AUTH_TOKEN'
    )
    if ($exactNames -ccontains $normalized) { return $true }

    return $normalized -match '(?:^|_)(?:TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|PRIVATE_?KEY|CLIENT_?SECRET|ACCESS_?KEY(?:_ID)?|ACCOUNT_?KEY|CREDENTIALS?|AUTHORIZATION|BEARER|CONNECTION_?STRING|COOKIE)(?:_|$)' -or
        $normalized -match '(?:^|_)(?:ASKPASS|KUBECONFIG|NETRC|KEYSTORE|PFX)$'
}

function Test-SashimiRecognizableSensitiveText {
    [CmdletBinding()]
    param(
        [AllowNull()][string]$Text,
        [string[]]$SensitiveValues = @()
    )

    if ([string]::IsNullOrEmpty($Text)) { return $false }
    if (-not [string]::Equals($Text, (Protect-SashimiText -Text $Text), [StringComparison]::Ordinal)) {
        return $true
    }
    if ($Text -match '(?i)\b(?:Proxy-)?Authorization\s*:\s*Basic\s+[A-Za-z0-9+/=]{8,}' -or
        $Text -match '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or
        $Text -match '(?i)(?:^|[\\/])(?:\.ssh|\.aws|\.azure|\.kube|\.codex)(?:[\\/]|$)' -or
        $Text -match '(?i)(?:^|[\\/])(?:auth\.json|credentials(?:\.json)?|\.netrc|_netrc|id_rsa|id_ed25519)(?:$|[\\/])' -or
        $Text -match '(?i)://[^\s/@:]+:[^\s/@]+@') {
        return $true
    }
    foreach ($sensitiveValue in @($SensitiveValues)) {
        if ([string]::IsNullOrEmpty($sensitiveValue) -or $sensitiveValue.Length -lt 8 -or $sensitiveValue.Length -gt 4096) { continue }
        if ($Text.IndexOf($sensitiveValue, [StringComparison]::Ordinal) -ge 0) { return $true }
    }
    return $false
}

function Get-SashimiSensitiveEnvironmentEntries {
    [CmdletBinding()]
    param()

    $entries = New-Object 'System.Collections.Generic.List[object]'
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ((Test-SashimiSensitiveEnvironmentName -Name $name) -or
            (Test-SashimiRecognizableSensitiveText -Text $value)) {
            $entries.Add([pscustomobject][ordered]@{ Name = $name; Value = $value })
        }
    }
    return $entries.ToArray()
}

function Get-SashimiCodexEnvironmentPolicy {
    [CmdletBinding()]
    param()

    # Never project task-environment values into Codex. The OS locations below
    # are re-derived through Windows known-folder APIs so poisoned HOME,
    # USERPROFILE, APPDATA, TEMP, PATH, proxy, endpoint, CA, or CODEX_HOME values
    # cannot redirect transport, trust, command lookup, or authentication.
    $windowsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    $profilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $appDataPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $localAppDataPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    foreach ($requiredPath in @($windowsPath,$profilePath,$appDataPath,$localAppDataPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredPath) -or -not [IO.Path]::IsPathFullyQualified($requiredPath)) {
            throw 'Windows did not provide a required canonical known-folder path for the Codex environment.'
        }
    }
    $tempPath = [IO.Path]::Combine($localAppDataPath, 'Temp')
    $overrides = [ordered]@{
        SystemRoot = [IO.Path]::GetFullPath($windowsPath)
        WINDIR = [IO.Path]::GetFullPath($windowsPath)
        USERPROFILE = [IO.Path]::GetFullPath($profilePath)
        APPDATA = [IO.Path]::GetFullPath($appDataPath)
        LOCALAPPDATA = [IO.Path]::GetFullPath($localAppDataPath)
        TEMP = [IO.Path]::GetFullPath($tempPath)
        TMP = [IO.Path]::GetFullPath($tempPath)
        DOTNET_CLI_UI_LANGUAGE = 'en-US'
        NO_COLOR = '1'
        GIT_TERMINAL_PROMPT = '0'
        GCM_INTERACTIVE = 'Never'
    }

    return [pscustomobject][ordered]@{
        SchemaVersion = 2
        Mode = 'HermeticAllowList'
        Authentication = 'CredentialStoreOnly'
        ClearInherited = $true
        AllowedNames = @($overrides.Keys)
        # Retained for older process-runner contracts, but clearing the entire
        # environment is the actual case-insensitive security boundary.
        RemoveNames = @([Environment]::GetEnvironmentVariables('Process').Keys | ForEach-Object { [string]$_ } | Sort-Object -Unique)
        Overrides = $overrides
    }
}

function Assert-SashimiCodexWorkspaceConfigurationAbsent {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepositoryPath)

    # `--ignore-user-config` excludes only the user's CODEX_HOME config and
    # `--ignore-rules` excludes exec-policy rule files. Codex also discovers
    # repository-scoped .codex configuration, hooks, plugins, and MCP launchers.
    # Those bytes are branch-controlled, so no such tree may be present when a
    # Host-owned Codex process is created.
    $root = ConvertTo-SashimiPath -Path $RepositoryPath
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw 'Codex workspace validation requires an existing repository directory.'
    }
    Assert-SashimiNoReparsePoint -Path $root

    $pending = [Collections.Generic.Queue[IO.DirectoryInfo]]::new()
    $pending.Enqueue([IO.DirectoryInfo]::new($root))
    $entryCount = 0
    try {
        while ($pending.Count -gt 0) {
            $directory = $pending.Dequeue()
            foreach ($entry in $directory.EnumerateFileSystemInfos('*',[IO.SearchOption]::TopDirectoryOnly)) {
                $entryCount++
                if ($entryCount -gt 250000) {
                    throw 'Codex workspace validation exceeded its fixed entry bound.'
                }
                if ([string]::Equals($entry.Name,'.codex',[StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Codex workspace contains forbidden repository-scoped .codex state.'
                }
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Codex workspace contains a reparse point; repository-scoped configuration absence cannot be established.'
                }
                if ($entry -is [IO.DirectoryInfo] -and
                    -not [string]::Equals($entry.Name,'.git',[StringComparison]::OrdinalIgnoreCase)) {
                    $pending.Enqueue([IO.DirectoryInfo]$entry)
                }
            }
        }
    }
    catch {
        if ($_.Exception.Message -in @(
                'Codex workspace validation exceeded its fixed entry bound.',
                'Codex workspace contains forbidden repository-scoped .codex state.',
                'Codex workspace contains a reparse point; repository-scoped configuration absence cannot be established.')) {
            throw
        }
        throw 'Codex workspace could not be enumerated safely for repository-scoped configuration.'
    }
}

function Protect-SashimiData {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [ValidateRange(0, 32)][int]$Depth = 0
    )

    if ($null -eq $Value) { return $null }
    if ($Depth -ge 32) { return '[REDACTED_DEPTH]' }
    if ($Value -is [string] -or $Value -is [char]) {
        return Protect-SashimiText -Text ([string]$Value)
    }
    if ($Value -is [bool] -or $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or $Value -is [int32] -or
        $Value -is [uint32] -or $Value -is [int64] -or $Value -is [uint64] -or
        $Value -is [single] -or $Value -is [double] -or $Value -is [decimal] -or
        $Value -is [DateTime] -or $Value -is [DateTimeOffset] -or $Value -is [Guid] -or
        $Value -is [Version]) {
        return $Value
    }
    if ($Value -is [Collections.IDictionary]) {
        $copy = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $copy[[string]$key] = Protect-SashimiData -Value $Value[$key] -Depth ($Depth + 1)
        }
        return $copy
    }
    if ($Value -is [Collections.IEnumerable]) {
        $items = @($Value | ForEach-Object { Protect-SashimiData -Value $_ -Depth ($Depth + 1) })
        return ,$items
    }

    $properties = @($Value.PSObject.Properties | Where-Object { $_.MemberType -in @('NoteProperty','Property','AliasProperty','ScriptProperty') })
    if ($properties.Count -gt 0) {
        $copy = [ordered]@{}
        foreach ($property in $properties) {
            try {
                $copy[$property.Name] = Protect-SashimiData -Value $property.Value -Depth ($Depth + 1)
            }
            catch {
                $copy[$property.Name] = '[REDACTED_UNREADABLE]'
            }
        }
        return $copy
    }
    return Protect-SashimiText -Text ([string]$Value)
}

function Format-SashimiCommand {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FilePath, [string[]]$ArgumentList = @())

    $parts = foreach ($part in @($FilePath) + @($ArgumentList)) {
        $safe = Protect-SashimiText -Text ([string]$part)
        if ($safe -match '[\s'']' -or $safe.Length -eq 0) { "'" + $safe.Replace("'", "''") + "'" } else { $safe }
    }
    return [string]::Join(' ', $parts)
}

function Assert-SashimiSafeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [ValidateSet('Generic', 'Git', 'GitHub', 'Codex', 'Unity')][string]$Kind = 'Generic'
    )

    $args = @($ArgumentList)
    $lower = @($args | ForEach-Object { ([string]$_).ToLowerInvariant() })
    $joined = [string]::Join(' ', $lower)
    if ($joined -match '(?:^|\s)(?:--dangerously-bypass-approvals-and-sandbox|danger-full-access)(?:\s|$)') {
        throw 'Forbidden Codex privilege-bypass option detected.'
    }
    if ($Kind -eq 'Git') {
        if ($joined -match '(?:^|\s)reset\s+--hard(?:\s|$)' -or
            $joined -match '(?:^|\s)clean(?:\s|$)' -or
            $joined -match '(?:^|\s)rebase(?:\s|$)' -or
            $joined -match '(?:^|\s)push(?:\s+[^\s]+)*\s+(?:--force(?:-with-lease)?|-f|--delete)(?:\s|$)' -or
            $joined -match '(?:^|\s)push(?:\s+[^\s]+)*\s+:[^\s]+') {
            throw 'Forbidden destructive Git operation detected.'
        }
    }
    if ($Kind -eq 'GitHub') {
        if ($joined -match '(?:^|\s)pr\s+merge(?:\s|$)' -or
            $joined -match '(?:^|\s)issue\s+close(?:\s|$)' -or
            $joined -match 'mergepullrequest' -or
            $joined -match '(?:status|name)\s*[=:]\s*done') {
            throw 'Forbidden GitHub merge, close, or Done mutation detected.'
        }
    }
    return $true
}

function Update-SashimiOwnedProcessLedger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('Add', 'Remove')][string]$Action,
        [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$ProcessId,
        [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$StartTimeUtc
    )

    $ledgerPath = ConvertTo-SashimiPath -Path $Path -AllowMissing -Lexical
    $ledgerHash = Get-SashimiTextSha256 -Text $ledgerPath.ToLowerInvariant()
    $ledgerMutex = [Threading.Mutex]::new($false, "Local\SashimiBoyHostPidLedger-$ledgerHash")
    $acquired = $false
    $temporaryPath = ''
    try {
        try { $acquired = $ledgerMutex.WaitOne(30000) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw "Timed out acquiring the owned-process ledger lock." }

        $processes = New-Object 'System.Collections.Generic.List[object]'
        if (Test-Path -LiteralPath $ledgerPath -PathType Leaf) {
            $existing = Read-SashimiJsonFile -Path $ledgerPath
            if ([int](Get-SashimiPropertyValue $existing 'SchemaVersion' 0) -ne 1) {
                throw 'Owned-process ledger has an unsupported schema.'
            }
            foreach ($record in @((Get-SashimiPropertyValue $existing 'Processes' @()))) {
                $recordId = [int](Get-SashimiPropertyValue $record 'Id' 0)
                $recordStart = [string](Get-SashimiPropertyValue $record 'StartTimeUtc' '')
                if ($recordId -lt 1 -or [string]::IsNullOrWhiteSpace($recordStart)) {
                    throw 'Owned-process ledger contains an invalid record.'
                }
                if ($Action -ceq 'Remove' -and $recordId -eq $ProcessId -and
                    [string]::Equals($recordStart, $StartTimeUtc, [StringComparison]::Ordinal)) {
                    continue
                }
                $processes.Add([ordered]@{ Id=$recordId; StartTimeUtc=$recordStart })
            }
        }
        if ($Action -ceq 'Add') {
            $duplicate = @($processes | Where-Object {
                [int]$_.Id -eq $ProcessId -and
                [string]::Equals([string]$_.StartTimeUtc, $StartTimeUtc, [StringComparison]::Ordinal)
            })
            if ($duplicate.Count -eq 0) {
                $processes.Add([ordered]@{ Id=$ProcessId; StartTimeUtc=$StartTimeUtc })
            }
        }

        $recordObject = [ordered]@{
            SchemaVersion = 1
            ProcessIds = @($processes | ForEach-Object { [int]$_.Id })
            Processes = $processes.ToArray()
            UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
        $parent = Split-Path -Parent $ledgerPath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            [IO.Directory]::CreateDirectory($parent) | Out-Null
        }
        $temporaryPath = Join-Path $parent ('.owned-process-ledger-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($temporaryPath, (ConvertTo-SashimiJson $recordObject), $script:SashimiUtf8NoBom)
        [IO.File]::Move($temporaryPath, $ledgerPath, $true)
        $temporaryPath = ''
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
            try { [IO.File]::Delete($temporaryPath) } catch { }
        }
        if ($acquired) { try { $ledgerMutex.ReleaseMutex() } catch { } }
        $ledgerMutex.Dispose()
    }
}

function Stop-SashimiOwnedProcessTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Diagnostics.Process]$Process,
        [ValidateRange(1, 60000)][int]$WaitMilliseconds = 10000
    )

    try {
        if ($Process.HasExited) { return $true }
        $Process.Kill($true)
        $waited = $Process.WaitForExit($WaitMilliseconds)
        return ($waited -and $Process.HasExited)
    }
    catch {
        try { return [bool]$Process.HasExited } catch { return $false }
    }
}

function Initialize-SashimiKillOnCloseProcessNative {
    [CmdletBinding()]
    param()

    if ('SashimiBoyAutomation.KillOnCloseProcess' -as [type]) { return }
    Microsoft.PowerShell.Utility\Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SashimiBoyAutomation
{
    public sealed class KillOnCloseProcessResult
    {
        public int ExitCode { get; set; } = 127;
        public string StandardOutput { get; set; } = "";
        public string StandardError { get; set; } = "";
        public bool TimedOut { get; set; }
        public bool Cancelled { get; set; }
        public bool TerminationConfirmed { get; set; }
        public bool KillOnCloseJobAssigned { get; set; }
        public int[] RemainingDescendantProcessIds { get; set; } = Array.Empty<int>();
        public int ProcessId { get; set; }
        public long DurationMilliseconds { get; set; }
        public string FailureCode { get; set; } = "";
    }

    public static class KillOnCloseProcess
    {
        private const UInt32 STARTF_USESTDHANDLES = 0x00000100;
        private const UInt32 HANDLE_FLAG_INHERIT = 0x00000001;
        private const UInt32 CREATE_SUSPENDED = 0x00000004;
        private const UInt32 CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const UInt32 CREATE_NO_WINDOW = 0x08000000;
        private const UInt32 WAIT_OBJECT_0 = 0x00000000;
        private const UInt32 WAIT_TIMEOUT = 0x00000102;
        private const UInt32 WAIT_FAILED = 0xffffffff;
        private const UInt32 RESUME_FAILED = 0xffffffff;
        private const UInt32 STILL_ACTIVE = 259;
        private const UInt32 SYNCHRONIZE = 0x00100000;
        private const int ERROR_MORE_DATA = 234;
        private const int JobObjectExtendedLimitInformation = 9;
        private const int JobObjectBasicProcessIdList = 3;
        private const UInt32 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const UInt32 FORCED_TERMINATION_EXIT_CODE = 0x53415348;
        private const int TERMINATION_CONFIRM_MILLISECONDS = 10000;
        private const int PIPE_DRAIN_MILLISECONDS = 10000;
        private const int STDOUT_LIMIT_BYTES = 16 * 1024 * 1024;
        private const int STDERR_LIMIT_BYTES = 1 * 1024 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public UInt32 dwX;
            public UInt32 dwY;
            public UInt32 dwXSize;
            public UInt32 dwYSize;
            public UInt32 dwXCountChars;
            public UInt32 dwYCountChars;
            public UInt32 dwFillAttribute;
            public UInt32 dwFlags;
            public UInt16 wShowWindow;
            public UInt16 cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public UInt32 dwProcessId;
            public UInt32 dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public UInt32 LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public UInt32 ActiveProcessLimit;
            public UIntPtr Affinity;
            public UInt32 PriorityClass;
            public UInt32 SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public UInt64 ReadOperationCount;
            public UInt64 WriteOperationCount;
            public UInt64 OtherOperationCount;
            public UInt64 ReadTransferCount;
            public UInt64 WriteTransferCount;
            public UInt64 OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SECURITY_ATTRIBUTES attributes, UInt32 size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr handle, UInt32 mask, UInt32 flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, UInt32 creationFlags,
            IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, UInt32 informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(IntPtr job, int informationClass, IntPtr information,
            UInt32 informationLength, out UInt32 returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UInt32 ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UInt32 WaitForSingleObject(IntPtr handle, UInt32 milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(UInt32 desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, UInt32 processId);

        private static Win32Exception Error(string operation)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed");
        }

        private static void CloseNativeHandle(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        private static string QuoteArgument(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("A native argument contains NUL.");
            StringBuilder quoted = new StringBuilder();
            quoted.Append('"');
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"')
                {
                    quoted.Append('\\', slashes * 2 + 1);
                    quoted.Append('"');
                    slashes = 0;
                    continue;
                }
                quoted.Append('\\', slashes);
                slashes = 0;
                quoted.Append(c);
            }
            quoted.Append('\\', slashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static StringBuilder BuildCommandLine(string executable, string[] arguments)
        {
            StringBuilder command = new StringBuilder(QuoteArgument(executable));
            foreach (string argument in arguments ?? Array.Empty<string>())
            {
                command.Append(' ');
                command.Append(QuoteArgument(argument ?? ""));
            }
            return command;
        }

        private static IntPtr BuildEnvironmentBlock(IDictionary<string,string> environment)
        {
            List<string> keys = new List<string>(environment.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder block = new StringBuilder();
            foreach (string key in keys)
            {
                if (String.IsNullOrEmpty(key) || key.IndexOf('=') >= 0 || key.IndexOf('\0') >= 0)
                    throw new ArgumentException("An environment name is invalid.");
                string value = environment[key] ?? "";
                if (value.IndexOf('\0') >= 0) throw new ArgumentException("An environment value contains NUL.");
                block.Append(key).Append('=').Append(value).Append('\0');
            }
            block.Append('\0');
            return Marshal.StringToHGlobalUni(block.ToString());
        }

        private static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw Error("CreateJobObject");
            IntPtr buffer = IntPtr.Zero;
            try
            {
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                UInt32 size = (UInt32)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                buffer = Marshal.AllocHGlobal((int)size);
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, size))
                    throw Error("SetInformationJobObject");
                return job;
            }
            catch { CloseHandle(job); throw; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }

        private static int[] QueryJobProcessIds(IntPtr job)
        {
            int capacity = 32;
            while (capacity <= 4096)
            {
                int size = 8 + capacity * IntPtr.Size;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    UInt32 returned;
                    if (QueryInformationJobObject(job, JobObjectBasicProcessIdList, buffer, (UInt32)size, out returned))
                    {
                        UInt32 count = unchecked((UInt32)Marshal.ReadInt32(buffer, 4));
                        if (count > capacity) { capacity *= 2; continue; }
                        int[] result = new int[count];
                        for (int index = 0; index < count; index++)
                            result[index] = unchecked((int)Marshal.ReadIntPtr(buffer, 8 + index * IntPtr.Size).ToInt64());
                        return result;
                    }
                    int error = Marshal.GetLastWin32Error();
                    if (error != ERROR_MORE_DATA) throw new Win32Exception(error, "QueryInformationJobObject failed");
                }
                finally { Marshal.FreeHGlobal(buffer); }
                capacity *= 2;
            }
            throw new InvalidOperationException("Job process count exceeded its fixed bound.");
        }

        public static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes)
        {
            using (MemoryStream capture = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0) return capture.ToArray();
                    if (capture.Length + read > maximumBytes) throw new InvalidDataException("OUTPUT_LIMIT_EXCEEDED");
                    capture.Write(buffer, 0, read);
                }
            }
        }

        private static async Task WriteInputAsync(Stream stream, byte[] content)
        {
            try
            {
                if (content.Length != 0) await stream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            finally { stream.Dispose(); }
        }

        private static int[] TerminateJobAndConfirmEmpty(ref IntPtr job)
        {
            // A one-time PID snapshot is insufficient: an already captured
            // child can create another job member after enumeration and before
            // the job handle closes. Terminate while retaining the job handle,
            // then query the kernel-owned membership repeatedly until it is
            // actually empty. Only then may the Host trust post-Unity Git
            // state. KILL_ON_JOB_CLOSE remains the independent exception path.
            if (!TerminateJobObject(job, FORCED_TERMINATION_EXIT_CODE))
                throw Error("TerminateJobObject");

            Stopwatch confirmation = Stopwatch.StartNew();
            int[] active = QueryJobProcessIds(job);
            while (active.Length != 0 && confirmation.ElapsedMilliseconds < TERMINATION_CONFIRM_MILLISECONDS)
            {
                System.Threading.Thread.Sleep(10);
                active = QueryJobProcessIds(job);
            }
            if (active.Length != 0)
                throw new InvalidOperationException("The Unity job process tree did not become empty within the termination-confirmation bound.");

            CloseNativeHandle(ref job);
            return Array.Empty<int>();
        }

        public static KillOnCloseProcessResult Run(string executable, string[] arguments, string workingDirectory,
            string standardInput, IDictionary<string,string> environment, int timeoutSeconds, string cancellationMarkerPath,
            Action<int,string,bool> updateLedger, Action captureGuard)
        {
            KillOnCloseProcessResult result = new KillOnCloseProcessResult();
            Stopwatch stopwatch = Stopwatch.StartNew();
            IntPtr job = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            IntPtr stdoutRead = IntPtr.Zero, stdoutWrite = IntPtr.Zero;
            IntPtr stderrRead = IntPtr.Zero, stderrWrite = IntPtr.Zero;
            IntPtr stdinRead = IntPtr.Zero, stdinWrite = IntPtr.Zero;
            PROCESS_INFORMATION process = new PROCESS_INFORMATION();
            bool processCreated = false, jobClosed = false;
            string processStartTime = null;
            SafeFileHandle stdoutSafe = null, stderrSafe = null, stdinSafe = null;
            FileStream stdoutStream = null, stderrStream = null, stdinStream = null;
            Task<byte[]> stdoutTask = null, stderrTask = null;
            Task stdinTask = null;
            try
            {
                SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES();
                attributes.nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
                attributes.bInheritHandle = true;
                if (!CreatePipe(out stdoutRead, out stdoutWrite, ref attributes, 0)) throw Error("CreatePipe(stdout)");
                if (!CreatePipe(out stderrRead, out stderrWrite, ref attributes, 0)) throw Error("CreatePipe(stderr)");
                if (!CreatePipe(out stdinRead, out stdinWrite, ref attributes, 0)) throw Error("CreatePipe(stdin)");
                if (!SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0) ||
                    !SetHandleInformation(stderrRead, HANDLE_FLAG_INHERIT, 0) ||
                    !SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0)) throw Error("SetHandleInformation");

                STARTUPINFO startup = new STARTUPINFO();
                startup.cb = Marshal.SizeOf<STARTUPINFO>();
                startup.dwFlags = STARTF_USESTDHANDLES;
                startup.hStdInput = stdinRead;
                startup.hStdOutput = stdoutWrite;
                startup.hStdError = stderrWrite;
                environmentBlock = BuildEnvironmentBlock(environment);
                job = CreateKillOnCloseJob();
                UInt32 flags = CREATE_NO_WINDOW | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT;
                if (!CreateProcessW(executable, BuildCommandLine(executable, arguments), IntPtr.Zero, IntPtr.Zero,
                    true, flags, environmentBlock, workingDirectory, ref startup, out process)) throw Error("CreateProcessW");
                processCreated = true;
                result.ProcessId = unchecked((int)process.dwProcessId);
                using (Process owned = Process.GetProcessById(result.ProcessId))
                    processStartTime = owned.StartTime.ToUniversalTime().ToString("o");
                if (updateLedger != null) updateLedger(result.ProcessId, processStartTime, true);
                // The primary thread is still suspended here. No editor/code
                // instruction can execute until kernel job assignment succeeds.
                if (!AssignProcessToJobObject(job, process.hProcess)) throw Error("AssignProcessToJobObject");
                result.KillOnCloseJobAssigned = true;

                CloseNativeHandle(ref stdoutWrite);
                CloseNativeHandle(ref stderrWrite);
                CloseNativeHandle(ref stdinRead);
                stdoutSafe = new SafeFileHandle(stdoutRead, true); stdoutRead = IntPtr.Zero;
                stderrSafe = new SafeFileHandle(stderrRead, true); stderrRead = IntPtr.Zero;
                stdinSafe = new SafeFileHandle(stdinWrite, true); stdinWrite = IntPtr.Zero;
                // CreatePipe returns synchronous handles. FileStream's async
                // methods safely dispatch synchronous pipe I/O to worker tasks
                // when isAsync is false; claiming OVERLAPPED here is invalid.
                stdoutStream = new FileStream(stdoutSafe, FileAccess.Read, 8192, false);
                stderrStream = new FileStream(stderrSafe, FileAccess.Read, 8192, false);
                stdinStream = new FileStream(stdinSafe, FileAccess.Write, 8192, false);
                stdoutTask = ReadBoundedAsync(stdoutStream, STDOUT_LIMIT_BYTES);
                stderrTask = ReadBoundedAsync(stderrStream, STDERR_LIMIT_BYTES);
                byte[] inputBytes = new UTF8Encoding(false, true).GetBytes(standardInput ?? "");
                stdinTask = WriteInputAsync(stdinStream, inputBytes);
                stdinStream = null; stdinSafe = null;

                if (ResumeThread(process.hThread) == RESUME_FAILED) throw Error("ResumeThread");
                CloseNativeHandle(ref process.hThread);
                bool mainExited = false;
                long lastCaptureCheck = -250;
                while (!mainExited)
                {
                    if (captureGuard != null && stopwatch.ElapsedMilliseconds - lastCaptureCheck >= 250)
                    {
                        try { captureGuard(); }
                        catch { result.FailureCode = "FILE_CAPTURE_BOUNDARY_FAILED"; break; }
                        lastCaptureCheck = stopwatch.ElapsedMilliseconds;
                    }
                    UInt32 wait = WaitForSingleObject(process.hProcess, 50);
                    if (wait == WAIT_OBJECT_0) { mainExited = true; break; }
                    if (wait == WAIT_FAILED) throw Error("WaitForSingleObject");
                    if (wait != WAIT_TIMEOUT) throw new InvalidOperationException("Unexpected process wait result.");
                    if ((stdoutTask.IsFaulted || stderrTask.IsFaulted))
                    {
                        result.FailureCode = "OUTPUT_CAPTURE_FAILED";
                        break;
                    }
                    if (!String.IsNullOrWhiteSpace(cancellationMarkerPath) && File.Exists(cancellationMarkerPath))
                    {
                        result.Cancelled = true;
                        break;
                    }
                    if (stopwatch.Elapsed.TotalSeconds >= timeoutSeconds)
                    {
                        result.TimedOut = true;
                        break;
                    }
                }

                result.RemainingDescendantProcessIds = TerminateJobAndConfirmEmpty(ref job);
                jobClosed = true;
                UInt32 mainConfirmation = WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_MILLISECONDS);
                result.TerminationConfirmed = mainConfirmation == WAIT_OBJECT_0 && result.RemainingDescendantProcessIds.Length == 0;
                if (captureGuard != null)
                {
                    try { captureGuard(); }
                    catch { result.FailureCode = "FILE_CAPTURE_BOUNDARY_FAILED"; }
                }
                bool outputTasksCompleted = false;
                try { outputTasksCompleted = Task.WaitAll(new Task[] { stdoutTask, stderrTask }, PIPE_DRAIN_MILLISECONDS); }
                catch (AggregateException) { outputTasksCompleted = stdoutTask.IsCompleted && stderrTask.IsCompleted; }
                if (!outputTasksCompleted)
                {
                    result.FailureCode = "OUTPUT_DRAIN_TIMEOUT";
                    result.TerminationConfirmed = false;
                }
                else if (stdoutTask.IsFaulted || stderrTask.IsFaulted)
                {
                    result.FailureCode = "OUTPUT_LIMIT_EXCEEDED_OR_READ_FAILED";
                }
                else
                {
                    try
                    {
                        UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                        result.StandardOutput = strictUtf8.GetString(stdoutTask.GetAwaiter().GetResult());
                        result.StandardError = strictUtf8.GetString(stderrTask.GetAwaiter().GetResult());
                    }
                    catch (InvalidDataException) { result.FailureCode = "OUTPUT_LIMIT_EXCEEDED"; }
                    catch (DecoderFallbackException) { result.FailureCode = "OUTPUT_INVALID_UTF8"; }
                }
                if (stdinTask != null && String.IsNullOrEmpty(result.FailureCode))
                {
                    bool inputCompleted = Task.WaitAny(new Task[] { stdinTask }, 1000) == 0;
                    if (!inputCompleted || stdinTask.IsFaulted || stdinTask.IsCanceled)
                        result.FailureCode = "INPUT_WRITE_UNCONFIRMED";
                }

                UInt32 nativeExit;
                if (mainConfirmation == WAIT_OBJECT_0 && GetExitCodeProcess(process.hProcess, out nativeExit) && nativeExit != STILL_ACTIVE)
                    result.ExitCode = unchecked((int)nativeExit);
                if (result.TimedOut) result.ExitCode = 124;
                else if (result.Cancelled) result.ExitCode = 125;
                else if (!String.IsNullOrEmpty(result.FailureCode) || !result.TerminationConfirmed) result.ExitCode = 127;
                return result;
            }
            finally
            {
                if (!jobClosed && job != IntPtr.Zero)
                {
                    if (processCreated)
                    {
                        if (result.KillOnCloseJobAssigned)
                            TerminateJobObject(job, FORCED_TERMINATION_EXIT_CODE);
                        else
                            // Assignment failed before ResumeThread. This
                            // suspended process is not a member of our job,
                            // so closing the job cannot terminate it.
                            TerminateProcess(process.hProcess, FORCED_TERMINATION_EXIT_CODE);
                    }
                    CloseNativeHandle(ref job);
                    if (processCreated)
                    {
                        UInt32 cleanupWait = WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_MILLISECONDS);
                        // An unassigned process has never resumed and cannot
                        // have descendants. Retain its ledger if termination
                        // is unconfirmed; never resume it to obtain cleanup.
                        if (!result.KillOnCloseJobAssigned)
                            result.TerminationConfirmed = cleanupWait == WAIT_OBJECT_0;
                    }
                }
                if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
                if (stdoutStream != null) stdoutStream.Dispose();
                if (stderrStream != null) stderrStream.Dispose();
                if (stdinStream != null) stdinStream.Dispose();
                if (stdoutSafe != null) stdoutSafe.Dispose();
                if (stderrSafe != null) stderrSafe.Dispose();
                if (stdinSafe != null) stdinSafe.Dispose();
                CloseNativeHandle(ref process.hThread);
                CloseNativeHandle(ref process.hProcess);
                CloseNativeHandle(ref stdinRead); CloseNativeHandle(ref stdinWrite);
                CloseNativeHandle(ref stdoutRead); CloseNativeHandle(ref stdoutWrite);
                CloseNativeHandle(ref stderrRead); CloseNativeHandle(ref stderrWrite);
                stopwatch.Stop();
                result.DurationMilliseconds = stopwatch.ElapsedMilliseconds;
                if (result.TerminationConfirmed && processStartTime != null && updateLedger != null)
                    updateLedger(result.ProcessId, processStartTime, false);
            }
        }
    }
}
'@ -ErrorAction Stop
}

function Remove-SashimiSensitiveToolEnvironment {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Environment)

    # These values exist only long enough to redact this one child process's
    # output. Bounds prevent an inherited environment from creating an
    # unbounded in-memory secret collection, and the values are never returned
    # in a result object or written to a diagnostic record.
    $maximumValues = 64
    $maximumValueLength = 4096
    $maximumTotalLength = 65536
    $totalLength = 0
    $boundsExceeded = $false
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $values = [Collections.Generic.List[string]]::new()
    foreach ($name in @($Environment.Keys | ForEach-Object { [string]$_ })) {
        if (-not (Test-SashimiSensitiveEnvironmentName -Name $name)) { continue }
        $candidate = [string]$Environment[$name]
        [void]$Environment.Remove($name)
        if ([string]::IsNullOrEmpty($candidate) -or $candidate.Length -lt 8) { continue }
        if ($candidate.Length -gt $maximumValueLength) {
            $boundsExceeded = $true
            continue
        }
        if (-not $seen.Add($candidate)) { continue }
        if ($values.Count -ge $maximumValues -or ($totalLength + $candidate.Length) -gt $maximumTotalLength) {
            $boundsExceeded = $true
            continue
        }
        $values.Add($candidate)
        $totalLength += $candidate.Length
    }
    if ($boundsExceeded) {
        throw 'Sensitive inherited environment exceeded the bounded Git/GitHub redaction policy; process launch refused.'
    }
    return $values.ToArray()
}

function Test-SashimiInheritedToolEnvironmentName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('Git','GitHub')][string]$Kind
    )

    $commonPoison = $Name -match '^(?i:GIT_|GCM_|SSH_)' -or
        $Name -match '^(?i:HTTP_PROXY|HTTPS_PROXY|ALL_PROXY|NO_PROXY|PAGER|EDITOR|VISUAL)$'
    if ($Kind -eq 'Git') { return $commonPoison }
    return $commonPoison -or
        $Name -match '^(?i:GH_.*|GITHUB_TOKEN|GITHUB_ENTERPRISE_TOKEN)$'
}

function Assert-SashimiToolEnvironmentOverride {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][ValidateSet('Git','GitHub')][string]$Kind
    )

    if (Test-SashimiSensitiveEnvironmentName -Name $Name) {
        throw "$Kind environment override '$Name' is credential-shaped and forbidden."
    }
    if (-not (Test-SashimiInheritedToolEnvironmentName -Name $Name -Kind $Kind)) { return }
    if ($Kind -eq 'Git') {
        if (($Name -ieq 'GIT_TERMINAL_PROMPT' -and $Value -ceq '0') -or
            ($Name -ieq 'GCM_INTERACTIVE' -and $Value -ceq 'Never') -or
            ($Name -ieq 'GIT_LFS_SKIP_SMUDGE' -and $Value -ceq '1')) { return }
        throw "Git environment override '$Name' is not in the fixed Host allowlist."
    }
    if (($Name -ieq 'GH_PROMPT_DISABLED' -and $Value -ceq '1') -or
        ($Name -ieq 'GIT_TERMINAL_PROMPT' -and $Value -ceq '0') -or
        ($Name -ieq 'GH_FORCE_TTY' -and $Value -ceq 'never')) { return }
    throw "GitHub CLI environment override '$Name' is not in the fixed Host allowlist."
}

function Get-SashimiContentDiffArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryPath,
        [string]$BaselineRef = ''
    )

    # With index auto-refresh disabled, name-only output can include files
    # whose stat data changed but whose contents did not. Numstat compares
    # contents; disable rename pairing so every record has exactly one path.
    $arguments = @('-C',$RepositoryPath,'diff','--numstat','-z','--no-renames',
        '--no-ext-diff','--no-textconv','--diff-filter=ACDMRTUXB')
    if (-not [string]::IsNullOrEmpty($BaselineRef)) { $arguments += $BaselineRef }
    return $arguments + @('--')
}

function ConvertFrom-SashimiNumstatPathList {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return @() }
    if ($Text[$Text.Length - 1] -ne [char]0) {
        throw 'Git numstat output is missing its final NUL terminator.'
    }
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($record in $Text.Substring(0,$Text.Length - 1).Split([char]0)) {
        $match = [regex]::Match($record,'\A(?:[0-9]+\t[0-9]+|-\t-)\t([^\x00]+)\z')
        if (-not $match.Success) {
            throw 'Git numstat output contains a malformed or rename-paired record.'
        }
        # Zero counts can represent empty-file or mode-only changes. Preserve
        # every path Git emits, including binary, added and deleted files.
        $paths.Add($match.Groups[1].Value)
    }
    return $paths.ToArray()
}

function Set-SashimiFixedGitProcessEnvironment {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][Diagnostics.ProcessStartInfo]$StartInfo)

    # Git's system/global config, credential helpers, hooks, filters, URL
    # rewrites, fsmonitor and external diff commands are executable input. A
    # scheduled run must not inherit any of them from the interactive user.
    # The two helper programs below are exact paths from the protected config;
    # their identity is re-hashed before every Git or Git LFS process launch.
    $gitLfsPath = Get-SashimiConfiguredExecutablePath -Name GitLfsExecutable
    $gitHubCliPath = Get-SashimiConfiguredExecutablePath -Name GitHubCli
    if (-not $script:SashimiExecutableIdentityActive -and -not (Test-SashimiHarnessMode)) {
        throw 'Protected executable identity is required before a live Git process may start.'
    }
    foreach ($helperPath in @($gitLfsPath, $gitHubCliPath)) {
        if ($helperPath -match '[\x00-\x1f"''&|<>^%!`]') {
            throw 'A Git helper executable path contains shell-significant characters and is forbidden.'
        }
        if ($script:SashimiExecutableIdentityActive) {
            Assert-SashimiBoundExecutableIdentity -FilePath $helperPath
        }
    }

    $quotedLfs = '"' + $gitLfsPath.Replace('\','/') + '"'
    $quotedGh = '"' + $gitHubCliPath.Replace('\','/') + '"'
    $fixedConfig = @(
        [pscustomobject]@{ Key='core.hooksPath'; Value='NUL' },
        # Fresh run roots plus Unity asset names exceed MAX_PATH. Global Git
        # config is intentionally disabled, so this must be Host-owned too.
        [pscustomobject]@{ Key='core.longpaths'; Value='true' },
        [pscustomobject]@{ Key='core.fsmonitor'; Value='false' },
        [pscustomobject]@{ Key='core.attributesFile'; Value='NUL' },
        [pscustomobject]@{ Key='core.askPass'; Value='' },
        [pscustomobject]@{ Key='core.editor'; Value='false' },
        [pscustomobject]@{ Key='core.sshCommand'; Value='' },
        [pscustomobject]@{ Key='core.pager'; Value='cat' },
        [pscustomobject]@{ Key='gc.auto'; Value='0' },
        [pscustomobject]@{ Key='maintenance.auto'; Value='false' },
        [pscustomobject]@{ Key='sequence.editor'; Value='false' },
        [pscustomobject]@{ Key='diff.external'; Value='' },
        # Porcelain diff can refresh stat-only index entries even with
        # GIT_OPTIONAL_LOCKS=0. Inspection must preserve exact index bytes;
        # content and whitespace detection remain enabled.
        [pscustomobject]@{ Key='diff.autoRefreshIndex'; Value='false' },
        [pscustomobject]@{ Key='commit.gpgSign'; Value='false' },
        [pscustomobject]@{ Key='tag.gpgSign'; Value='false' },
        [pscustomobject]@{ Key='credential.interactive'; Value='never' },
        [pscustomobject]@{ Key='credential.helper'; Value='' },
        [pscustomobject]@{ Key='credential.https://github.com.helper'; Value=('!' + $quotedGh + ' auth git-credential') },
        [pscustomobject]@{ Key='credential.https://github.com.useHttpPath'; Value='false' },
        [pscustomobject]@{ Key='remote.sashimi-canonical.url'; Value=$script:SashimiExpectedRemoteUrl },
        [pscustomobject]@{ Key='remote.sashimi-canonical.pushurl'; Value=$script:SashimiExpectedRemoteUrl },
        # Git LFS otherwise gives repository-local lfs.url/lfs.pushurl,
        # remote.*.lfsurl/lfspushurl, and .lfsconfig authority over its HTTP
        # destination. Command-scope values have higher precedence and bind
        # both download and upload traffic to the immutable reviewed endpoint.
        [pscustomobject]@{ Key='lfs.url'; Value=$script:SashimiExpectedGitLfsUrl },
        [pscustomobject]@{ Key='lfs.pushurl'; Value=$script:SashimiExpectedGitLfsUrl },
        [pscustomobject]@{ Key='remote.origin.lfsurl'; Value=$script:SashimiExpectedGitLfsUrl },
        [pscustomobject]@{ Key='remote.origin.lfspushurl'; Value=$script:SashimiExpectedGitLfsUrl },
        [pscustomobject]@{ Key='remote.sashimi-canonical.lfsurl'; Value=$script:SashimiExpectedGitLfsUrl },
        [pscustomobject]@{ Key='remote.sashimi-canonical.lfspushurl'; Value=$script:SashimiExpectedGitLfsUrl },
        # LFS otherwise learns Basic auth after the first 401 and persists
        # lfs.<endpoint>.access in .git/config. Pin only this reviewed endpoint
        # in command scope so pull/push preserve the exact Git-control snapshot.
        [pscustomobject]@{ Key=('lfs.' + $script:SashimiExpectedGitLfsUrl + '.access'); Value='basic' },
        [pscustomobject]@{ Key='lfs.basictransfersonly'; Value='true' },
        [pscustomobject]@{ Key='http.extraHeader'; Value='' },
        [pscustomobject]@{ Key='http.proxy'; Value='' },
        [pscustomobject]@{ Key='https.proxy'; Value='' },
        [pscustomobject]@{ Key='filter.lfs.process'; Value=($quotedLfs + ' filter-process') },
        [pscustomobject]@{ Key='filter.lfs.smudge'; Value=($quotedLfs + ' smudge -- %f') },
        [pscustomobject]@{ Key='filter.lfs.clean'; Value=($quotedLfs + ' clean -- %f') },
        [pscustomobject]@{ Key='filter.lfs.required'; Value='true' },
        [pscustomobject]@{ Key='protocol.allow'; Value='never' },
        [pscustomobject]@{ Key='protocol.https.allow'; Value='always' },
        [pscustomobject]@{ Key='protocol.http.allow'; Value='never' },
        [pscustomobject]@{ Key='protocol.ssh.allow'; Value='never' },
        [pscustomobject]@{ Key='protocol.git.allow'; Value='never' },
        [pscustomobject]@{ Key='protocol.file.allow'; Value='never' },
        [pscustomobject]@{ Key='protocol.ext.allow'; Value='never' },
        [pscustomobject]@{ Key='transfer.fsckObjects'; Value='true' },
        [pscustomobject]@{ Key='fetch.fsckObjects'; Value='true' },
        [pscustomobject]@{ Key='receive.fsckObjects'; Value='true' }
    )

    $StartInfo.Environment['GIT_CONFIG_NOSYSTEM'] = '1'
    $StartInfo.Environment['GIT_CONFIG_SYSTEM'] = 'NUL'
    $StartInfo.Environment['GIT_CONFIG_GLOBAL'] = 'NUL'
    $StartInfo.Environment['GIT_ATTR_NOSYSTEM'] = '1'
    $StartInfo.Environment['GIT_PROTOCOL_FROM_USER'] = '0'
    $StartInfo.Environment['GIT_ALLOW_PROTOCOL'] = 'https'
    # Read-only status/snapshot commands must not opportunistically refresh the
    # exact index bytes that the Git-control guard is comparing. Mandatory
    # write-command locks remain enabled by Git itself.
    $StartInfo.Environment['GIT_OPTIONAL_LOCKS'] = '0'
    $StartInfo.Environment['GIT_CONFIG_COUNT'] = [string]$fixedConfig.Count
    for ($index = 0; $index -lt $fixedConfig.Count; $index++) {
        $StartInfo.Environment["GIT_CONFIG_KEY_$index"] = [string]$fixedConfig[$index].Key
        $StartInfo.Environment["GIT_CONFIG_VALUE_$index"] = [string]$fixedConfig[$index].Value
    }
}

function Test-SashimiProcessSecret {
    param([string]$Text,[string[]]$SensitiveValues=@())
    # Host results legitimately include their own workspace paths. Audit the
    # original content for credentials without treating such path metadata as
    # a file disclosure. Codex keeps its stricter source/profile policy.
    if ($Text -match '(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}|\bBearer\s+[A-Za-z0-9._~+/=-]+|\b(?:Proxy-)?Authorization\s*:\s*Basic\s+[A-Za-z0-9+/=]{8,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|://[^\s/@:]+:[^\s/@]+@') { return $true }
    foreach ($value in $SensitiveValues) {
        if ($value.Length -ge 8 -and $value.Length -le 4096 -and $Text.Contains($value,[StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Invoke-SashimiHostProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 600,
        [AllowEmptyString()][string]$StandardInput = '',
        [hashtable]$Environment = @{},
        [string[]]$RemoveEnvironmentVariables = @(),
        [ValidateSet('Generic', 'Git', 'GitHub', 'Codex', 'Unity')][string]$Kind = 'Generic',
        [string]$InvocationRecordPath,
        [string]$OwnedProcessRecordPath,
        [string]$CancellationMarkerPath,
        [string]$CodexWorkspacePath,
        [string[]]$CaptureRoots = @(),
        [switch]$ClearEnvironment,
        [switch]$PreserveRawOutputInMemory,
        [switch]$RequireKillOnCloseJob,
        [switch]$DryRun
    )

    [void](Assert-SashimiSafeCommand -FilePath $FilePath -ArgumentList $ArgumentList -Kind $Kind)
    if ($PreserveRawOutputInMemory -and ($Kind -cne 'Codex' -or -not [string]::IsNullOrWhiteSpace($InvocationRecordPath))) {
        throw 'Unredacted in-memory output is allowed only for Codex without an invocation-record path.'
    }
    # All Host children need the same pre-resume ownership and kernel tree
    # lifetime, including Git credential/LFS helpers and PowerShell adapters.
    # A parent exiting cannot turn its later descendants into unowned work.
    $RequireKillOnCloseJob = $true
    if ([string]::IsNullOrWhiteSpace($OwnedProcessRecordPath) -and
        -not [string]::IsNullOrWhiteSpace($CancellationMarkerPath)) {
        $candidateRun = Split-Path -Parent $CancellationMarkerPath
        if (Test-Path -LiteralPath (Join-Path $candidateRun $script:SashimiRunMarkerName) -PathType Leaf) {
            Assert-SashimiRunIdentity -RunId (Split-Path -Leaf $candidateRun)
            Assert-SashimiNoReparsePoint -Path $candidateRun
            $owner = Read-SashimiJsonFile (Join-Path $candidateRun $script:SashimiRunMarkerName)
            if ($owner.SchemaVersion -ne 1 -or $owner.RunId -cne (Split-Path -Leaf $candidateRun)) { throw 'Run marker mismatch.' }
            $OwnedProcessRecordPath = Join-Path $candidateRun 'State\OwnedHostPids.json'
        }
    }
    if ($Kind -ceq 'Codex') {
        if (-not $ClearEnvironment) { throw 'Codex process launch requires a cleared inherited environment.' }
        if ([string]::IsNullOrWhiteSpace($WorkingDirectory) -or [string]::IsNullOrWhiteSpace($CodexWorkspacePath)) {
            throw 'Codex process launch requires an explicit repository workspace path.'
        }
        $canonicalWorkingDirectory = ConvertTo-SashimiPath -Path $WorkingDirectory
        $canonicalCodexWorkspace = ConvertTo-SashimiPath -Path $CodexWorkspacePath
        if (-not (Test-SashimiPathEqual -Left $canonicalWorkingDirectory -Right $canonicalCodexWorkspace)) {
            throw 'Codex process working directory must equal its validated repository workspace.'
        }
        $expectedPolicy = Get-SashimiCodexEnvironmentPolicy
        $expectedOverrides = $expectedPolicy.Overrides
        if ($Environment.Count -ne $expectedOverrides.Count) {
            throw 'Codex process environment does not match the exact hermetic allowlist.'
        }
        foreach ($name in @($expectedOverrides.Keys)) {
            if (-not $Environment.ContainsKey([string]$name) -or
                -not [string]::Equals([string]$Environment[[string]$name], [string]$expectedOverrides[[string]$name], [StringComparison]::Ordinal)) {
                throw "Codex process environment differs from the fixed value for '$name'."
            }
        }
    }
    $commandText = Format-SashimiCommand -FilePath $FilePath -ArgumentList $ArgumentList
    if ($DryRun) {
        return [pscustomobject][ordered]@{
            FilePath = Protect-SashimiText $FilePath; Arguments = @($ArgumentList | ForEach-Object { Protect-SashimiText $_ }); Command = $commandText
            ExitCode = 0; StdOut = ''; StdErr = ''; Succeeded = $true
            TimedOut = $false; Cancelled = $false; TerminationConfirmed = $true
            KillOnCloseJobAssigned = $false; RemainingDescendantProcessIds = @()
            ProcessId = $null; DurationMilliseconds = 0; DryRun = $true
        }
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    if ($ClearEnvironment) { $startInfo.Environment.Clear() }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = ConvertTo-SashimiPath -Path $WorkingDirectory
    }
    else {
        $startInfo.WorkingDirectory = ConvertTo-SashimiPath -Path (Get-Location).ProviderPath
    }
    foreach ($argument in @($ArgumentList)) { [void]$startInfo.ArgumentList.Add([string]$argument) }
    $sensitiveOutputValues = @()
    if ($Kind -in @('Git','GitHub')) {
        $sensitiveOutputValues = @(Remove-SashimiSensitiveToolEnvironment -Environment $startInfo.Environment)
    }
    # Remove caller-requested and ambient tool controls before applying the
    # small, explicit Host environment. This prevents inherited repository,
    # config/helper, askpass, SSH, proxy, editor, pager, and GH routing state
    # from overriding the pinned command contract.
    foreach ($name in @($RemoveEnvironmentVariables)) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$startInfo.Environment.Remove([string]$name)
        }
    }
    if ($Kind -in @('Git','GitHub')) {
        foreach ($name in @($startInfo.Environment.Keys | ForEach-Object { [string]$_ })) {
            if (Test-SashimiInheritedToolEnvironmentName -Name $name -Kind $Kind) {
                [void]$startInfo.Environment.Remove($name)
            }
        }
    }
    if ($Kind -cne 'Codex') {
        $startInfo.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
        $startInfo.Environment['NO_COLOR'] = '1'
    }
    if ($Kind -ceq 'GitHub') { $startInfo.Environment['GH_FORCE_TTY'] = 'never' }
    foreach ($entry in $Environment.GetEnumerator()) {
        if ($Kind -in @('Git','GitHub')) {
            Assert-SashimiToolEnvironmentOverride -Name ([string]$entry.Key) -Value ([string]$entry.Value) -Kind $Kind
        }
        $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
    }
    if ($Kind -eq 'Git') {
        $startInfo.Environment['GIT_TERMINAL_PROMPT'] = '0'
        $startInfo.Environment['GCM_INTERACTIVE'] = 'Never'
        Set-SashimiFixedGitProcessEnvironment -StartInfo $startInfo
    }
    elseif ($Kind -eq 'GitHub') {
        $startInfo.Environment['GH_PROMPT_DISABLED'] = '1'
        $startInfo.Environment['GIT_TERMINAL_PROMPT'] = '0'
    }

    if ($RequireKillOnCloseJob) {
        Initialize-SashimiKillOnCloseProcessNative
        $native = $null
        $nativeException = $null
        $launchLease = $null
        try {
            # CreateProcessW returns with the primary thread suspended. The
            # native boundary assigns the process to its kill-on-close job
            # before ResumeThread, then closes the job and confirms that every
            # captured descendant has exited before returning control here.
            if ($Kind -ceq 'Codex') {
                Assert-SashimiCodexWorkspaceConfigurationAbsent -RepositoryPath $CodexWorkspacePath
            }
            $launchLease = Open-SashimiExecutableLaunchLease -FilePath $FilePath -Kind $Kind -ArgumentList $ArgumentList -WorkingDirectory $startInfo.WorkingDirectory
            $ledgerCallback = $null
            if (-not [string]::IsNullOrWhiteSpace($OwnedProcessRecordPath)) {
                $ledgerCallback = [Action[int,string,bool]]{
                    param($childPid, $createdUtc, $add)
                    Update-SashimiOwnedProcessLedger -Path $OwnedProcessRecordPath `
                        -Action $(if ($add) { 'Add' } else { 'Remove' }) `
                        -ProcessId $childPid -StartTimeUtc $createdUtc
                }
            }
            $captureGuard = $null
            if ($CaptureRoots.Count -gt 0) {
                $captureGuard = [Action]{ Assert-SashimiCaptureQuota -Roots $CaptureRoots }
            }
            $native = [SashimiBoyAutomation.KillOnCloseProcess]::Run(
                [string]$FilePath,
                [string[]]@($ArgumentList),
                [string]$startInfo.WorkingDirectory,
                [string]$StandardInput,
                [Collections.Generic.IDictionary[string,string]]$startInfo.Environment,
                [int]$TimeoutSeconds,
                [string]$CancellationMarkerPath,
                $ledgerCallback, $captureGuard)
        }
        catch {
            # Policy/identity rejection precedes process creation. Preserve
            # that exception contract and never write a post-launch record.
            if ($null -eq $launchLease) { throw }
            $nativeException = $_.Exception
        }
        finally {
            if ($null -ne $launchLease) { try { Close-SashimiExecutableLaunchLease $launchLease } catch { } }
        }
        if ($null -ne $nativeException) {
            $native = [pscustomobject][ordered]@{
                ExitCode = 127; StandardOutput = ''; StandardError = $nativeException.Message
                TimedOut = $false; Cancelled = $false; TerminationConfirmed = $false
                KillOnCloseJobAssigned = $false; RemainingDescendantProcessIds = @()
                ProcessId = 0; DurationMilliseconds = 0; FailureCode = 'JOB_BOUNDARY_FAILED'
            }
        }
        $nativeFailure = [string]$native.FailureCode
        $stdout = [string]$native.StandardOutput
        $stderr = if ([string]::IsNullOrWhiteSpace($nativeFailure)) {
            [string]$native.StandardError
        }
        else {
            "The bounded suspended process boundary failed closed: $nativeFailure"
        }
        if ($Kind -cne 'Codex' -and
            (Test-SashimiProcessSecret -Text ($stdout + "`n" + $stderr) -SensitiveValues $sensitiveOutputValues)) {
            # Audit original decoded bytes before redaction or retention. Never
            # turn disclosure into a successful process by replacing its text.
            $nativeFailure = 'SENSITIVE_PROCESS_OUTPUT'
            $native.ExitCode = 127
            $stdout = ''
            $stderr = 'Host process output was rejected before retention: sensitive content.'
        }
        $terminationConfirmed = [bool]$native.TerminationConfirmed
        $remainingDescendants = @($native.RemainingDescendantProcessIds | ForEach-Object { [int]$_ })
        $result = [pscustomobject][ordered]@{
            FilePath = Protect-SashimiText $FilePath
            Arguments = @($ArgumentList | ForEach-Object { Protect-SashimiText $_ })
            Command = $commandText
            ExitCode = [int]$native.ExitCode
            StdOut = Protect-SashimiText $stdout.TrimEnd()
            StdErr = Protect-SashimiText $stderr.TrimEnd()
            Succeeded = ([int]$native.ExitCode -eq 0 -and -not [bool]$native.TimedOut -and
                -not [bool]$native.Cancelled -and [string]::IsNullOrWhiteSpace($nativeFailure) -and
                $terminationConfirmed -and [bool]$native.KillOnCloseJobAssigned -and $remainingDescendants.Count -eq 0)
            TimedOut = [bool]$native.TimedOut
            Cancelled = [bool]$native.Cancelled
            TerminationConfirmed = $terminationConfirmed
            KillOnCloseJobAssigned = [bool]$native.KillOnCloseJobAssigned
            RemainingDescendantProcessIds = $remainingDescendants
            ProcessId = if ([int]$native.ProcessId -gt 0) { [int]$native.ProcessId } else { $null }
            DurationMilliseconds = [int64]$native.DurationMilliseconds
            DryRun = $false
        }
        if ($PreserveRawOutputInMemory) {
            $result | Add-Member -NotePropertyName UnredactedStdOut -NotePropertyValue $stdout
            $result | Add-Member -NotePropertyName UnredactedStdErr -NotePropertyValue $stderr
        }
        if (-not [string]::IsNullOrWhiteSpace($InvocationRecordPath)) {
            Write-SashimiUtf8File -Path $InvocationRecordPath -Content (ConvertTo-SashimiJson $result)
        }
        return $result
    }

    Initialize-SashimiKillOnCloseProcessNative
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $cancelled = $false
    $exitCode = 127
    $stdout = ''
    $stderr = ''
    $pidValue = $null
    $started = $false
    $terminationConfirmed = $false
    $stdoutTask = $null
    $stderrTask = $null
    $stdinTask = $null
    $stdinClosed = $false
    $processStartTimeUtc = ''
    $launchLease = $null
    $preLaunchException = $null
    try {
        if ($Kind -ceq 'Codex') {
            # Keep this immediately adjacent to the protected executable lease:
            # no Host parsing, logging, or artifact operation occurs between the
            # repository policy check, final executable hash, and Process.Start.
            Assert-SashimiCodexWorkspaceConfigurationAbsent -RepositoryPath $CodexWorkspacePath
        }
        # Re-open and hash the executable under a no-write/no-delete lease only
        # after all process preparation, then retain that lease through
        # Process.Start. A same-administrator attacker can still subvert Windows
        # process creation or replace an ancestor with privileged operations;
        # that same-admin boundary is explicitly outside this task-user threat
        # model and is never treated as protection from a hostile administrator.
        $launchLease = Open-SashimiExecutableLaunchLease -FilePath $FilePath -Kind $Kind -ArgumentList $ArgumentList -WorkingDirectory $startInfo.WorkingDirectory
        $started = [bool]$process.Start()
        if (-not $started) { throw "Unable to start process: $commandText" }
        Close-SashimiExecutableLaunchLease $launchLease
        $launchLease = $null
        $pidValue = $process.Id
        if (-not [string]::IsNullOrWhiteSpace($OwnedProcessRecordPath)) {
            $processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
            Update-SashimiOwnedProcessLedger -Path $OwnedProcessRecordPath -Action Add -ProcessId $pidValue -StartTimeUtc $processStartTimeUtc
        }
        # Capture original bytes under fixed bounds and decode with throwing
        # UTF-8 only after process-tree termination. StreamReader's replacement
        # fallback and ReadToEndAsync would make malformed or unbounded output
        # indistinguishable from validated child text.
        $stdoutTask = [SashimiBoyAutomation.KillOnCloseProcess]::ReadBoundedAsync($process.StandardOutput.BaseStream, 16777216)
        $stderrTask = [SashimiBoyAutomation.KillOnCloseProcess]::ReadBoundedAsync($process.StandardError.BaseStream, 1048576)
        if ($StandardInput.Length -gt 0) {
            # Never synchronously write a potentially large Codex prompt to a
            # pipe. A child that stops reading must remain subject to the same
            # cancellation and timeout loop as the rest of the process.
            $stdinTask = $process.StandardInput.WriteAsync($StandardInput)
        }
        else {
            $process.StandardInput.Close()
            $stdinClosed = $true
        }
        while ($true) {
            if (-not $stdinClosed -and $null -ne $stdinTask -and $stdinTask.IsCompleted) {
                [void]$stdinTask.GetAwaiter().GetResult()
                $process.StandardInput.Close()
                $stdinClosed = $true
            }
            if ($process.WaitForExit(100)) { break }
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted) {
                $terminationConfirmed = Stop-SashimiOwnedProcessTree -Process $process
                throw 'Child output exceeded its fixed capture bound or could not be read.'
            }
            if (-not [string]::IsNullOrWhiteSpace($CancellationMarkerPath) -and (Test-Path -LiteralPath $CancellationMarkerPath -PathType Leaf)) {
                $cancelled = $true
                $terminationConfirmed = Stop-SashimiOwnedProcessTree -Process $process
                break
            }
            if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                $terminationConfirmed = Stop-SashimiOwnedProcessTree -Process $process
                break
            }
        }
        if (-not $timedOut -and -not $cancelled) {
            $terminationConfirmed = [bool]$process.HasExited
            if (-not $stdinClosed -and $null -ne $stdinTask) {
                # An exited child closes its pipe. Confirm the asynchronous
                # prompt write also completed; otherwise the run is unsafe.
                if (-not $stdinTask.Wait(1000)) { throw 'The child exited before its standard-input write could be confirmed.' }
                [void]$stdinTask.GetAwaiter().GetResult()
                $process.StandardInput.Close()
                $stdinClosed = $true
            }
        }
        if ($terminationConfirmed) {
            $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
            $stdout = $strictUtf8.GetString([byte[]]$stdoutTask.GetAwaiter().GetResult())
            $stderr = $strictUtf8.GetString([byte[]]$stderrTask.GetAwaiter().GetResult())
        }
        else {
            $stderr = 'The owned process could not be confirmed terminated; its PID record was preserved.'
        }
        if (-not $timedOut -and -not $cancelled -and $process.HasExited) { $exitCode = $process.ExitCode }
        elseif ($timedOut) { $exitCode = 124 }
        elseif ($cancelled) { $exitCode = 125 }
    }
    catch {
        if (-not $started) { $preLaunchException = $_ }
        $stderr = $_.Exception.Message
        $exitCode = 127
        if ($started) {
            try {
                if (-not $process.HasExited) {
                    $terminationConfirmed = Stop-SashimiOwnedProcessTree -Process $process
                }
                else { $terminationConfirmed = $true }
            }
            catch { }
        }
    }
    finally {
        $stopwatch.Stop()
        if ($null -ne $launchLease) {
            try { Close-SashimiExecutableLaunchLease $launchLease } catch { }
        }
        if ($terminationConfirmed -and -not [string]::IsNullOrWhiteSpace($OwnedProcessRecordPath) -and
            $null -ne $pidValue -and -not [string]::IsNullOrWhiteSpace($processStartTimeUtc)) {
            try {
                Update-SashimiOwnedProcessLedger -Path $OwnedProcessRecordPath -Action Remove -ProcessId $pidValue -StartTimeUtc $processStartTimeUtc
            }
            catch { }
        }
        $process.Dispose()
    }

    # Identity, ACL, reparse, and launch-lease failures occur before a child is
    # created. Preserve them as terminal Host exceptions so callers cannot
    # mistake a synthetic process result for an executed command, and so no
    # post-launch invocation record is written for a process that never began.
    if ($null -ne $preLaunchException) { throw $preLaunchException }

    $presentedStdOut = if ($PreserveRawOutputInMemory) { '' } else { Protect-SashimiTextWithExactValues -Text $stdout.TrimEnd() -ExactValues $sensitiveOutputValues }
    $presentedStdErr = if ($PreserveRawOutputInMemory) { '' } else { Protect-SashimiTextWithExactValues -Text $stderr.TrimEnd() -ExactValues $sensitiveOutputValues }
    $result = [pscustomobject][ordered]@{
        FilePath = Protect-SashimiText $FilePath
        Arguments = @($ArgumentList | ForEach-Object { Protect-SashimiText $_ })
        Command = $commandText
        ExitCode = [int]$exitCode
        StdOut = $presentedStdOut
        StdErr = $presentedStdErr
        Succeeded = ($exitCode -eq 0 -and -not $timedOut -and -not $cancelled -and $terminationConfirmed)
        TimedOut = $timedOut
        Cancelled = $cancelled
        TerminationConfirmed = $terminationConfirmed
        KillOnCloseJobAssigned = $false
        RemainingDescendantProcessIds = @()
        ProcessId = $pidValue
        DurationMilliseconds = [int64]$stopwatch.ElapsedMilliseconds
        DryRun = $false
    }
    if ($PreserveRawOutputInMemory) {
        # These properties are an adapter-only in-memory handoff. The parameter
        # contract above forbids serializing this result through the common
        # invocation-record path; the adapter audits and then drops them.
        $result | Add-Member -NotePropertyName UnredactedStdOut -NotePropertyValue $stdout
        $result | Add-Member -NotePropertyName UnredactedStdErr -NotePropertyValue $stderr
    }
    if (-not [string]::IsNullOrWhiteSpace($InvocationRecordPath)) {
        Write-SashimiUtf8File -Path $InvocationRecordPath -Content (ConvertTo-SashimiJson $result)
    }
    $sensitiveOutputValues = @()
    return $result
}

function Enter-SashimiHostMutex {
    [CmdletBinding()]
    param([string]$Name = 'Global\SashimiBoyHostOrchestrator', [ValidateRange(0, 60000)][int]$TimeoutMilliseconds = 0)

    $createdNew = $false
    $mutex = [Threading.Mutex]::new($false, $Name, [ref]$createdNew)
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne($TimeoutMilliseconds)
    }
    catch [Threading.AbandonedMutexException] {
        $acquired = $true
    }
    return [pscustomobject][ordered]@{ Name = $Name; Mutex = $mutex; Acquired = $acquired; CreatedNew = $createdNew }
}

function Exit-SashimiHostMutex {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Lease)

    try {
        if ([bool]$Lease.Acquired) { $Lease.Mutex.ReleaseMutex() }
    }
    finally { $Lease.Mutex.Dispose() }
}

function Assert-SashimiCaptureQuota {
    param([Parameter(Mandatory)][string[]]$Roots)
    [long]$bytes = 0
    $entries = 0
    $pending = [Collections.Generic.Stack[string]]::new()
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root)) { throw 'Capture root is empty.' }
        Assert-SashimiNoReparsePoint -Path $root
        if (Test-Path -LiteralPath $root) { $pending.Push($root) }
    }
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop) {
            $entries++
            if ($entries -gt 512 -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Capture tree exceeded its entry boundary.'
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
            $limit = if ($entry.Extension -ieq '.log') { 8MB } elseif ($entry.Extension -ieq '.png') { 25MB } else { 16MB }
            $bytes += $entry.Length
            if ($entry.Length -gt $limit -or $bytes -gt 160MB) { throw 'Capture byte quota exceeded.' }
        }
    }
}

function Get-SashimiArtifactManifest {
    param([Parameter(Mandatory)][string]$Root)
    Assert-SashimiCaptureQuota -Roots @($Root)
    $records = [Collections.Generic.List[object]]::new()
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop) {
            Assert-SashimiNoReparsePoint -Path $entry.FullName
            if ($entry.PSIsContainer) {
                $records.Add([pscustomobject]@{ Path=[IO.Path]::GetRelativePath($Root,$entry.FullName).Replace('\','/') + '/'; Length=[long]0; Sha256='' })
                $pending.Push($entry.FullName); continue
            }
            $stream = [IO.FileStream]::new($entry.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try {
                if ($stream.Length -ne $entry.Length -or $stream.Length -gt 25MB) { throw 'Artifact changed during audit.' }
                $bytes = [byte[]]::new([int]$stream.Length); $stream.ReadExactly($bytes,0,$bytes.Length)
                if ($entry.Extension -ine '.png') {
                    $text = [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
                    if (Test-SashimiProcessSecret -Text $text) { throw 'Artifact contains sensitive output.' }
                }
                else {
                    if ($bytes.Length -lt 8 -or [Convert]::ToHexString($bytes,0,8) -cne '89504E470D0A1A0A') { throw 'Artifact is not a PNG.' }
                    if (Test-SashimiProcessSecret -Text ([Text.Encoding]::Latin1.GetString($bytes))) { throw 'PNG contains sensitive output.' }
                }
                $records.Add([pscustomobject]@{ Path=[IO.Path]::GetRelativePath($Root,$entry.FullName).Replace('\','/');
                    Length=[long]$bytes.Length; Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() })
            }
            finally { $stream.Dispose() }
        }
    }
    return @($records.ToArray() | Sort-Object Path -CaseSensitive)
}

function Write-SashimiArtifactSeal {
    param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$SealPath)
    $first = @(Get-SashimiArtifactManifest -Root $Root)
    $second = @(Get-SashimiArtifactManifest -Root $Root)
    if ((ConvertTo-SashimiJson $first) -cne (ConvertTo-SashimiJson $second)) { throw 'Artifact tree changed while sealing.' }
    Write-SashimiUtf8File -Path $SealPath -Content (ConvertTo-SashimiJson ([ordered]@{SchemaVersion=1;Files=$first}))
}

function Assert-SashimiArtifactSeal {
    param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$SealPath)
    Assert-SashimiNoReparsePoint -Path $SealPath
    if ((Get-Item -LiteralPath $SealPath).Length -gt 1MB) { throw 'Artifact seal exceeds its quota.' }
    $seal = Read-SashimiJsonFile $SealPath
    if ($seal.SchemaVersion -ne 1 -or $seal.Files -isnot [array]) { throw 'Invalid artifact seal.' }
    $actual = @(Get-SashimiArtifactManifest -Root $Root)
    if ((ConvertTo-SashimiJson @($seal.Files)) -cne (ConvertTo-SashimiJson $actual)) { throw 'Artifact set or content changed after validation.' }
}

function Assert-SashimiRunArtifactBoundary {
    param([Parameter(Mandatory)][string]$RunPath,[switch]$RequireFinalSeal)
    $root = Join-Path $RunPath 'Artifacts'
    Assert-SashimiCaptureQuota -Roots @($root)
    if ($RequireFinalSeal) {
        Assert-SashimiArtifactSeal -Root $root -SealPath (Join-Path $RunPath 'State/Artifacts.seal.json')
        return
    }
    $allowed = @('DraftPullRequest.md','HandoffCompletion.md','Failure.md','DeliveryResumeHandoff.md',
        'ReviewDecision.json','ReviewFinding.md','ReviewFixHandoff.md','OwnerVerificationChecklist.md','ReviewerFailure.md','RunResult.json')
    foreach ($entry in Get-ChildItem -LiteralPath $root -Force) {
        if ($entry.PSIsContainer) {
            if ($entry.Name -cnotin @('Unity','Codex')) { throw 'Unexpected run artifact directory.' }
            Assert-SashimiArtifactSeal -Root $entry.FullName -SealPath (Join-Path $RunPath ('State/' + $entry.Name + '.seal.json'))
        }
        elseif ($entry.Name -cnotin $allowed) { throw 'Unexpected run artifact file.' }
    }
    [void](Get-SashimiArtifactManifest -Root $root)
}

function Write-SashimiStageArtifactSeal {
    param([string]$RunPath,[ValidateSet('Unity','Codex')][string]$Scope)
    $root = Join-Path $RunPath ('Artifacts/' + $Scope)
    if (Test-Path -LiteralPath $root -PathType Container) {
        Write-SashimiArtifactSeal -Root $root -SealPath (Join-Path $RunPath ('State/' + $Scope + '.seal.json'))
    }
}

function Assert-SashimiRunIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunId)
    if ($RunId -notmatch '^\d{8}T\d{6}Z-[0-9a-f]{32}$') { throw "Invalid run ID: $RunId" }
}

function New-SashimiRunWorkspace {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunRoot, [string]$RunId)

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $RunId = ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N'))
    }
    Assert-SashimiRunIdentity -RunId $RunId
    $root = ConvertTo-SashimiPath -Path $RunRoot -AllowMissing -Lexical
    Assert-SashimiNoReparsePoint -Path $root
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $runPath = Join-Path $root $RunId
    if (Test-Path -LiteralPath $runPath) { throw "Run path already exists: $runPath" }
    [IO.Directory]::CreateDirectory($runPath) | Out-Null
    foreach ($name in @('Repository', 'Artifacts', 'State')) { [IO.Directory]::CreateDirectory((Join-Path $runPath $name)) | Out-Null }
    $marker = [ordered]@{ SchemaVersion = 1; RunId = $RunId; CreatedAtUtc = [DateTime]::UtcNow.ToString('o'); OwnerPid = $PID }
    Write-SashimiUtf8File -Path (Join-Path $runPath $script:SashimiRunMarkerName) -Content (ConvertTo-SashimiJson $marker)
    return [pscustomobject][ordered]@{
        RunId = $RunId; RunPath = $runPath; RepositoryPath = (Join-Path $runPath 'Repository')
        ArtifactsPath = (Join-Path $runPath 'Artifacts'); StatePath = (Join-Path $runPath 'State')
        MarkerPath = (Join-Path $runPath $script:SashimiRunMarkerName)
    }
}

function Get-SashimiOwnedRun {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunPath, [Parameter(Mandatory = $true)][string]$RunRoot)

    $root = ConvertTo-SashimiPath -Path $RunRoot -AllowMissing -Lexical
    $path = ConvertTo-SashimiPath -Path $RunPath -Lexical
    if (-not (Test-SashimiPathWithin -Path $path -Root $root)) { throw "Run is outside the configured root: $path" }
    Assert-SashimiNoReparsePoint -Path $path -Recurse
    $runId = Split-Path -Leaf $path
    Assert-SashimiRunIdentity $runId
    $markerPath = Join-Path $path $script:SashimiRunMarkerName
    $marker = Read-SashimiJsonFile $markerPath
    if ([int]$marker.SchemaVersion -ne 1 -or [string]$marker.RunId -cne $runId) {
        throw 'Run ownership marker does not match its directory.'
    }
    return [pscustomobject]@{ RunId = $runId; RunPath = $path; Marker = $marker; MarkerPath = $markerPath }
}

function Assert-SashimiRunProcessLedgersCleared {
    param([Parameter(Mandatory)][string]$RunPath)
    foreach ($name in @('OwnedHostPids.json','OwnedUnityPids.json','OwnedCodexPids.json')) {
        $path = Join-Path $RunPath (Join-Path 'State' $name)
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Assert-SashimiNoReparsePoint -Path $path
        $ledger = Read-SashimiJsonFile $path
        if ([int](Get-SashimiPropertyValue $ledger 'SchemaVersion' 0) -ne 1 -or
            $null -eq $ledger.PSObject.Properties['Processes'] -or
            @($ledger.Processes).Count -ne 0) {
            throw 'Run process termination is unconfirmed; preserve the workspace and its ledger.'
        }
    }
}

function Remove-SashimiRunRepository {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunPath, [Parameter(Mandatory = $true)][string]$RunRoot, [switch]$DryRun)

    try {
        $owned = Get-SashimiOwnedRun -RunPath $RunPath -RunRoot $RunRoot
        Assert-SashimiRunProcessLedgersCleared -RunPath $owned.RunPath
        $repositoryPath = Join-Path $owned.RunPath 'Repository'
        Assert-SashimiNoReparsePoint -Path $repositoryPath -Recurse
        if (-not $DryRun -and (Test-Path -LiteralPath $repositoryPath)) {
            Remove-Item -LiteralPath $repositoryPath -Recurse -Force -ErrorAction Stop
        }
        return [pscustomobject]@{ Success = $true; Removed = -not $DryRun; Preserved = $DryRun; Path = 'Repository'; Error = '' }
    }
    catch {
        return [pscustomobject]@{ Success = $false; Removed = $false; Preserved = $true; Path = 'Repository'; Error = 'RepositoryCleanupFailed' }
    }
}

function Invoke-SashimiRetention {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [ValidateRange(1, 365)][int]$RetentionDays = 14,
        [switch]$DryRun
    )

    $root = ConvertTo-SashimiPath -Path $RunRoot -AllowMissing -Lexical
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return @() }
    Assert-SashimiNoReparsePoint -Path $root
    $cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
    $results = New-Object 'System.Collections.Generic.List[object]'
    foreach ($directory in @(Get-ChildItem -LiteralPath $root -Directory -Force)) {
        if ($directory.LastWriteTimeUtc -ge $cutoff) { continue }
        try {
            $owned = Get-SashimiOwnedRun -RunPath $directory.FullName -RunRoot $root
            Assert-SashimiRunProcessLedgersCleared -RunPath $owned.RunPath
            Assert-SashimiRunArtifactBoundary -RunPath $owned.RunPath -RequireFinalSeal
            $liveOwnedProcesses = New-Object 'System.Collections.Generic.List[int]'
            foreach ($ledgerName in @('OwnedHostPids.json','OwnedUnityPids.json')) {
                $ledgerPath = Join-Path $owned.RunPath (Join-Path 'State' $ledgerName)
                if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) { continue }
                $ledger = Read-SashimiJsonFile -Path $ledgerPath
                if ([int](Get-SashimiPropertyValue $ledger 'SchemaVersion' 0) -ne 1) {
                    throw "Retention refused an invalid owned-process ledger: $ledgerName"
                }
                foreach ($processRecord in @((Get-SashimiPropertyValue $ledger 'Processes' @()))) {
                    $ownedPid = [int](Get-SashimiPropertyValue $processRecord 'Id' 0)
                    $ownedStart = [string](Get-SashimiPropertyValue $processRecord 'StartTimeUtc' '')
                    if ($ownedPid -lt 1 -or [string]::IsNullOrWhiteSpace($ownedStart)) {
                        throw "Retention refused a malformed owned-process record: $ledgerName"
                    }
                    $ownedProcess = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
                    if ($null -ne $ownedProcess -and
                        [string]::Equals($ownedProcess.StartTime.ToUniversalTime().ToString('o'), $ownedStart, [StringComparison]::Ordinal)) {
                        $liveOwnedProcesses.Add($ownedPid)
                    }
                }
            }
            if ($liveOwnedProcesses.Count -gt 0) {
                $results.Add([pscustomobject]@{ RunId = $owned.RunId; Removed = $false; Preserved = $true; Reason = 'LiveOwnedProcess'; Error = '' })
                continue
            }
            if (-not $DryRun) {
                Assert-SashimiNoReparsePoint -Path $owned.RunPath -Recurse
                Remove-Item -LiteralPath $owned.RunPath -Recurse -Force -ErrorAction Stop
            }
            $results.Add([pscustomobject]@{ RunId = $owned.RunId; Removed = -not $DryRun; Preserved = $DryRun; Reason = if ($DryRun) { 'DryRun' } else { 'Expired' }; Error = '' })
        }
        catch {
            $results.Add([pscustomobject]@{ RunId = $directory.Name; Removed = $false; Preserved = $true; Reason = 'RetentionFailure'; Error = Protect-SashimiText $_.Exception.Message })
        }
    }
    return $results.ToArray()
}

function Write-SashimiRunState {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$StatePath, [Parameter(Mandatory = $true)][object]$State)

    Write-SashimiUtf8File -Path $StatePath -Content (ConvertTo-SashimiJson $State -Pretty)
}

function Test-SashimiCancellation {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunPath)
    return (Test-Path -LiteralPath (Join-Path $RunPath 'cancel.requested') -PathType Leaf)
}

function Assert-SashimiTransition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Developer', 'Reviewer')][string]$Role,
        [Parameter(Mandatory = $true)][string]$From,
        [Parameter(Mandatory = $true)][string]$To
    )

    if ($To -ceq 'Done') { throw 'No host automation role may transition an Issue to Done.' }
    $key = "$Role|$From|$To"
    $allowed = @(
        'Developer|Ready|In Progress',
        'Developer|In Progress|Review',
        'Reviewer|Review|In Progress',
        'Reviewer|Review|Verification'
    )
    if ($allowed -cnotcontains $key) { throw "Forbidden Project transition: $key" }
    return $true
}

function Test-SashimiPullRequestTrust {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$PullRequest,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string[]]$AuthorizedAuthors
    )

    $reasons = New-Object 'System.Collections.Generic.List[string]'
    if ([string](Get-SashimiPropertyValue $PullRequest 'State' '') -cne 'OPEN') { $reasons.Add('PullRequestNotOpen') }
    if (-not [bool](Get-SashimiPropertyValue $PullRequest 'IsDraft' $false)) { $reasons.Add('PullRequestNotDraft') }
    if ([string](Get-SashimiPropertyValue $PullRequest 'BaseRefName' '') -cne 'main') { $reasons.Add('PullRequestBaseNotMain') }
    if ([string](Get-SashimiPropertyValue $PullRequest 'BaseRepository' '') -cne $Repository) { $reasons.Add('PullRequestBaseRepositoryMismatch') }
    if ([string](Get-SashimiPropertyValue $PullRequest 'HeadRepository' '') -cne $Repository) { $reasons.Add('ForkPullRequestRejected') }
    if ([bool](Get-SashimiPropertyValue $PullRequest 'IsCrossRepository' $false)) { $reasons.Add('ForkPullRequestRejected') }
    $author = [string](Get-SashimiPropertyValue $PullRequest 'AuthorLogin' '')
    if ($AuthorizedAuthors -cnotcontains $author) { $reasons.Add('UnauthorizedPullRequestAuthor') }
    $sha = [string](Get-SashimiPropertyValue $PullRequest 'HeadSha' '')
    if ($sha -notmatch '^[0-9a-fA-F]{40}$') { $reasons.Add('InvalidPullRequestHeadSha') }
    $ref = [string](Get-SashimiPropertyValue $PullRequest 'HeadRef' '')
    $invalidRefComponent = @($ref -split '/' | Where-Object { $_ -match '^\.' -or $_ -match '(?i)\.lock$' }).Count -gt 0
    if ($ref -notmatch '^(?!/)(?!.*\.\.)(?!.*@\{)(?!.*//)(?!.*[\x00-\x20\x7f~^:?*\[\\])(?!.*/$)(?!.*\.$).+$' -or $ref -ceq '@' -or $invalidRefComponent) {
        $reasons.Add('InvalidPullRequestHeadRef')
    }
    return [pscustomobject]@{ Trusted = ($reasons.Count -eq 0); Reasons = $reasons.ToArray() }
}

function Test-SashimiPinnedPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Pinned, [Parameter(Mandatory = $true)][object]$Live)

    foreach ($name in @('Number', 'HeadSha', 'HeadRef', 'HeadRepository', 'BaseRepository', 'BaseRefName', 'State', 'IsDraft', 'ContentSha256')) {
        if ([string](Get-SashimiPropertyValue $Pinned $name '') -cne [string](Get-SashimiPropertyValue $Live $name '')) {
            return [pscustomobject]@{ Current = $false; ChangedField = $name }
        }
    }
    return [pscustomobject]@{ Current = $true; ChangedField = '' }
}

function ConvertFrom-SashimiHandoffMarker {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Body)

    $match = [regex]::Match($Body, '(?s)^\s*<!-- sashimi-boy-automation-handoff:v1\r?\n(?<content>.*?)\r?\n-->\s*$')
    if (-not $match.Success) { return $null }
    $values = @{}
    foreach ($line in @($match.Groups['content'].Value -split '\r?\n')) {
        if ($line -notmatch '^(?<key>[A-Za-z][A-Za-z0-9]*): (?<value>[^\r\n]*)$') { return $null }
        if ($values.ContainsKey($Matches.key)) { return $null }
        $values[$Matches.key] = $Matches.value
    }
    $expected = @('mode', 'issue', 'pr', 'head', 'sourceRole', 'reason', 'findingUrl', 'pendingCommand')
    if ($values.Count -ne $expected.Count -or @($expected | Where-Object { -not $values.ContainsKey($_) }).Count -gt 0) { return $null }
    $issue = 0; $pr = 0
    if (-not [int]::TryParse($values.issue, [ref]$issue) -or $issue -lt 1 -or
        -not [int]::TryParse($values.pr, [ref]$pr) -or $pr -lt 1 -or
        $values.head -notmatch '^[0-9a-fA-F]{40}$' -or
        @('ReviewFix', 'DeliveryResume') -cnotcontains $values.mode) { return $null }
    return [pscustomobject][ordered]@{
        Mode = $values.mode; IssueNumber = $issue; PullRequestNumber = $pr; HeadSha = $values.head.ToLowerInvariant()
        SourceRole = $values.sourceRole; Reason = $values.reason; FindingUrl = $values.findingUrl; PendingCommand = $values.pendingCommand
    }
}

function Test-SashimiHandoffContract {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Handoff, [Parameter(Mandatory = $true)][int]$IssueNumber, [Parameter(Mandatory = $true)][object]$PullRequest)

    $valid = ([int]$Handoff.IssueNumber -eq $IssueNumber -and [int]$Handoff.PullRequestNumber -eq [int]$PullRequest.Number -and
        [string]$Handoff.HeadSha -ceq ([string]$PullRequest.HeadSha).ToLowerInvariant())
    if (-not $valid) { return $false }
    if ([string]$Handoff.Mode -ceq 'ReviewFix') {
        if ([string]$Handoff.SourceRole -ceq 'Reviewer') {
            $findingUri = $null
            return (@('review-blocker', 'review-major') -ccontains [string]$Handoff.Reason -and
                [Uri]::TryCreate([string]$Handoff.FindingUrl, [UriKind]::Absolute, [ref]$findingUri) -and
                $findingUri.Scheme -ceq 'https')
        }
        return ([string]$Handoff.SourceRole -ceq 'Owner' -and [string]$Handoff.Reason -ceq 'owner-verification-fail')
    }
    $allowedReasons = @('unity-lock', 'unity-process', 'protected-worktree-dirty', 'disk-space', 'network', 'authentication', 'runner-failure', 'required-check-transient')
    return ([string]$Handoff.SourceRole -ceq 'Developer' -and $allowedReasons -ccontains [string]$Handoff.Reason -and -not [string]::IsNullOrWhiteSpace([string]$Handoff.PendingCommand))
}

function Get-SashimiNUnitSummary {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Unity result XML is missing: $Path" }
    try { [xml]$xml = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) } catch { throw "Invalid Unity result XML '$Path': $($_.Exception.Message)" }
    $root = $xml.'test-run'
    if ($null -eq $root) { $root = $xml.'test-results' }
    if ($null -eq $root) { throw "Unity result XML has no supported root: $Path" }
    $total = [int]$root.total
    $passed = [int]$root.passed
    $failed = [int]$root.failed
    $skipped = [int]$root.skipped
    $inconclusive = [int]$root.inconclusive
    $strict = ([string]$root.result -ceq 'Passed' -and $total -gt 0 -and $passed -eq $total -and $failed -eq 0 -and $skipped -eq 0 -and $inconclusive -eq 0)
    return [pscustomobject]@{ Result = [string]$root.result; Total = $total; Passed = $passed; Failed = $failed; Skipped = $skipped; Inconclusive = $inconclusive; StrictPass = $strict }
}

function Invoke-SashimiWithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Operation,
        [ValidateRange(1, 10)][int]$MaximumAttempts = 3,
        [ValidateRange(0, 3600)][int]$CooldownSeconds = 30,
        [string]$CancellationMarkerPath,
        [scriptblock]$ShouldRetry = { param($result, $errorRecord) $null -ne $errorRecord }
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        if ($CancellationMarkerPath -and (Test-Path -LiteralPath $CancellationMarkerPath -PathType Leaf)) { throw 'Run cancellation was requested during retry processing.' }
        $result = $null; $caught = $null
        try { $result = & $Operation $attempt } catch { $caught = $_ }
        $retry = [bool](& $ShouldRetry $result $caught)
        if (-not $retry) {
            if ($null -ne $caught) { throw $caught }
            return $result
        }
        if ($attempt -eq $MaximumAttempts) {
            if ($null -ne $caught) { throw $caught }
            throw "Operation failed after $MaximumAttempts attempts."
        }
        for ($second = 0; $second -lt $CooldownSeconds; $second++) {
            if ($CancellationMarkerPath -and (Test-Path -LiteralPath $CancellationMarkerPath -PathType Leaf)) { throw 'Run cancellation was requested during retry cooldown.' }
            Start-Sleep -Seconds 1
        }
    }
}
function Test-SashimiUnityDefaultSerialization {
    param([Parameter(Mandatory)][string]$Before, [Parameter(Mandatory)][string]$After)
    $expected = $Before.Replace("`r`n", "`n")
    $replacements = @(
        @('  targetPixelDensity: 0', '  targetPixelDensity: 30'),
        @('  buildNumber: {}', "  buildNumber:`n    Standalone: 0`n    VisionOS: 0`n    iPhone: 0`n    tvOS: 0"),
        @('  iOSTargetOSVersionString: ', '  iOSTargetOSVersionString: 15.0'),
        @('  tvOSTargetOSVersionString: ', '  tvOSTargetOSVersionString: 15.0'),
        @('  VisionOSTargetOSVersionString: ', '  VisionOSTargetOSVersionString: 1.0'),
        @('  macOSTargetOSVersion: ', '  macOSTargetOSVersion: 12.0')
    )
    foreach ($pair in $replacements) {
        $pattern = '(?m)^' + [regex]::Escape($pair[0]) + '$'
        if ([regex]::Matches($expected, $pattern).Count -ne 1) { return $false }
        $expected = [regex]::Replace($expected, $pattern, [string]$pair[1])
    }
    return [string]::Equals($expected, $After.Replace("`r`n", "`n"), [StringComparison]::Ordinal)
}

function Get-SashimiReviewFindingSchema {
    # A severity label alone is not evidence for returning work to Developer.
    $properties = [ordered]@{
        severity = [ordered]@{ type='string'; enum=@('Blocker','Major','Minor') }
        category = [ordered]@{ type='string'; enum=@('Defect','HumanCheck','Infrastructure','Unverified') }
        basis = [ordered]@{ type='string'; enum=@('Reproduction','CodePath','RenderedEvidence','None') }
    }
    foreach ($name in @('title','evidence','requirement','location','expected','actual','reproduction','recommendation')) {
        $properties[$name] = [ordered]@{ type='string' }
    }
    return [ordered]@{ type='object'; additionalProperties=$false; required=@($properties.Keys); properties=$properties }
}

function Assert-SashimiReviewFinding {
    param([Parameter(Mandatory)][object]$Finding)
    $schema = Get-SashimiReviewFindingSchema
    $names = @($Finding.PSObject.Properties.Name)
    if ($names.Count -ne $schema.required.Count -or @($names | Where-Object { $schema.required -cnotcontains $_ }).Count -ne 0) {
        throw 'Review finding does not satisfy the evidence contract.'
    }
    foreach ($name in $schema.required) {
        $value = $Finding.$name
        if ($value -isnot [string] -or $value.Length -gt 8192) { throw 'Review finding contains an invalid or oversized field.' }
    }
    if (@('Blocker','Major','Minor') -cnotcontains $Finding.severity -or
        @('Defect','HumanCheck','Infrastructure','Unverified') -cnotcontains $Finding.category -or
        @('Reproduction','CodePath','RenderedEvidence','None') -cnotcontains $Finding.basis -or
        [string]::IsNullOrWhiteSpace($Finding.title) -or $Finding.title.Length -gt 512 -or
        [string]::IsNullOrWhiteSpace($Finding.evidence)) {
        throw 'Review finding classification is invalid.'
    }
    if ($Finding.category -ceq 'Defect') {
        if ($Finding.basis -ceq 'None') { throw 'A defect requires observed or deterministic code-path evidence.' }
        foreach ($name in @('requirement','location','expected','actual','reproduction','recommendation')) {
            if ([string]::IsNullOrWhiteSpace($Finding.$name)) { throw 'A defect lacks its requirement, location, behavior, reproduction, or correction.' }
        }
    }
}

function Get-SashimiReviewDisposition {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Findings)
    foreach ($finding in $Findings) { Assert-SashimiReviewFinding $finding }
    $blocking = @($Findings | Where-Object { $_.category -ceq 'Defect' -and @('Blocker','Major') -ccontains $_.severity })
    $incomplete = @($Findings | Where-Object { @('Infrastructure','Unverified') -ccontains $_.category })
    $manual = @($Findings | Where-Object { $_.category -ceq 'HumanCheck' })
    $minor = @($Findings | Where-Object { $_.category -ceq 'Defect' -and $_.severity -ceq 'Minor' })
    return [pscustomobject]@{
        # Missing evidence never becomes either an automatic failure or a PASS.
        Disposition = if ($blocking.Count -gt 0) { 'NeedsChanges' } elseif ($incomplete.Count -gt 0) { 'Incomplete' } else { 'AutomatedPassCandidate' }
        Blocking = $blocking; Incomplete = $incomplete; Manual = $manual; Minor = $minor
    }
}

function ConvertFrom-SashimiLfsObjectManifest {
    param([Parameter(Mandatory)][string]$Json)
    if ($Json.Length -gt 8MB) { throw 'LFS manifest exceeds the metadata size limit.' }
    $document = [Text.Json.JsonDocument]::Parse($Json)
    try {
        $files = $document.RootElement.GetProperty('files')
        if ($files.ValueKind -ne [Text.Json.JsonValueKind]::Array) { throw 'LFS manifest files must be an array.' }
        if ($files.GetArrayLength() -gt 10000) { throw 'LFS manifest exceeds the file count limit.' }
        $objects = [Collections.Generic.Dictionary[string,long]]::new([StringComparer]::Ordinal)
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $totalBytes = 0L
        $entries = [Collections.Generic.List[object]]::new()
        foreach ($file in $files.EnumerateArray()) {
            $oid = $file.GetProperty('oid').GetString()
            $size = $file.GetProperty('size').GetInt64()
            $name = $file.GetProperty('name').GetString()
            if ($oid -cnotmatch '^[0-9a-f]{64}$' -or $size -lt 0 -or
                $file.GetProperty('oid_type').GetString() -cne 'sha256' -or
                $file.GetProperty('version').GetString() -cne 'https://git-lfs.github.com/spec/v1' -or
                [string]::IsNullOrWhiteSpace($name) -or $name -match '(^/|\\|:|[\x00-\x1f]|(^|/)\.\.?(/|$)|(^|/)\.git(/|$))') {
                throw 'LFS manifest contains an invalid object identity or repository path.'
            }
            if ($objects.ContainsKey($oid) -and $objects[$oid] -ne $size) { throw 'LFS manifest repeats an OID with a different size.' }
            if ($name.Length -gt 1024 -or -not $names.Add($name)) { throw 'LFS manifest path is too long or duplicated.' }
            if (-not $objects.ContainsKey($oid)) {
                if ($size -gt [long]::MaxValue-$totalBytes) { throw 'LFS manifest total size overflows Int64.' }
                $totalBytes += $size
            }
            $objects[$oid] = $size
            $entries.Add([pscustomobject]@{ Name=$name; Oid=$oid; Size=$size })
        }
        return [pscustomobject]@{ Objects=$objects; Files=$entries.ToArray() }
    }
    finally { $document.Dispose() }
}

function Test-SashimiLfsObjectBytes {
    param([Parameter(Mandatory)][IO.Stream]$Stream, [string]$Oid, [long]$Size)
    if ($Stream.Length -ne $Size) { return $false }
    $Stream.Position = 0
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Stream)).ToLowerInvariant()
    $Stream.Position = 0
    return $hash -ceq $Oid
}

function Copy-SashimiVerifiedLfsObject {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Oid, [Parameter(Mandatory)][long]$Size)
    # Cache data is untrusted. Neither metadata nor a filename establishes a hit.
    Assert-SashimiNoReparsePoint -Path $Source
    Assert-SashimiNoReparsePoint -Path $Destination
    $result=[pscustomobject]@{ Status='Missing'; CleanupWarning=$false }
    if (-not [IO.File]::Exists($Source)) { return $result }
    $inputStream=$null; $outputStream=$null; $temporary=''; $createdTemporary=$false
    try {
        $inputStream=[IO.FileStream]::new($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        Assert-SashimiNoReparsePoint -Path $Source
        if (-not (Test-SashimiLfsObjectBytes -Stream $inputStream -Oid $Oid -Size $Size)) { $result.Status='Invalid'; return $result }
        if ([IO.File]::Exists($Destination)) {
            Assert-SashimiNoReparsePoint -Path $Destination
            $existing=[IO.FileStream]::new($Destination,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try {
                Assert-SashimiNoReparsePoint -Path $Destination
                if (Test-SashimiLfsObjectBytes -Stream $existing -Oid $Oid -Size $Size) { $result.Status='Existing'; return $result }
                $result.Status='InvalidDestination'; return $result
            } finally { $existing.Dispose() }
        }
        $parent=[IO.Path]::GetDirectoryName($Destination)
        [void][IO.Directory]::CreateDirectory($parent)
        Assert-SashimiNoReparsePoint -Path $Destination
        $temporary=Join-Path $parent ('.lfs-cache-copy-'+[Guid]::NewGuid().ToString('N')+'.tmp')
        $outputStream=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $createdTemporary=$true
        $inputStream.CopyTo($outputStream)
        $outputStream.Flush($true)
        if (-not (Test-SashimiLfsObjectBytes -Stream $outputStream -Oid $Oid -Size $Size)) { throw 'LFS copy failed its byte identity check.' }
        $outputStream.Dispose(); $outputStream=$null
        Assert-SashimiNoReparsePoint -Path $temporary
        Assert-SashimiNoReparsePoint -Path $Destination
        try { [IO.File]::Move($temporary,$Destination,$false); $temporary='' }
        catch [IO.IOException] {
            if (-not [IO.File]::Exists($Destination)) { throw }
            Assert-SashimiNoReparsePoint -Path $Destination
            $existing=[IO.FileStream]::new($Destination,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try {
                Assert-SashimiNoReparsePoint -Path $Destination
                if (-not (Test-SashimiLfsObjectBytes -Stream $existing -Oid $Oid -Size $Size)) { $result.Status='InvalidDestination'; return $result }
            } finally { $existing.Dispose() }
        }
        $result.Status='Copied'; return $result
    }
    finally {
        if ($null -ne $inputStream) { $inputStream.Dispose() }
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($createdTemporary -and $temporary -and [IO.File]::Exists($temporary)) {
            # Only this invocation's CreateNew temporary file is eligible.
            try {
                Assert-SashimiNoReparsePoint -Path $temporary
                [IO.File]::Delete($temporary)
            }
            catch [IO.IOException] { $result.CleanupWarning=$true }
            catch [UnauthorizedAccessException] { $result.CleanupWarning=$true }
        }
    }
}

function Get-SashimiLfsCacheUsage {
    param([Parameter(Mandatory)][string]$Path)
    $bytes=0L; $count=0
    if (-not [IO.Directory]::Exists($Path)) { return [pscustomobject]@{ Bytes=0L; Files=0; Full=$false } }
    $pending=[Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    $visited=0
    while ($pending.Count -gt 0) {
        $directory=$pending.Pop()
        Assert-SashimiNoReparsePoint -Path $directory
        foreach ($entry in [IO.DirectoryInfo]::new($directory).EnumerateFileSystemInfos()) {
            $visited++
            if ($visited -gt 30000) { return [pscustomobject]@{ Bytes=$bytes; Files=$count; Full=$true } }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point in LFS cache.' }
            if (($entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0) { $pending.Push($entry.FullName) }
            else {
                $bytes+=$entry.Length; $count++
                if ($bytes -ge 32GB -or $count -ge 10000) { return [pscustomobject]@{ Bytes=$bytes; Files=$count; Full=$true } }
            }
        }
    }
    return [pscustomobject]@{ Bytes=$bytes; Files=$count; Full=$false }
}

function Sync-SashimiLfsObjectCache {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryPath, [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$ManifestJson, [Parameter(Mandatory)][ValidateSet('Restore','Store')][string]$Mode,
        [switch]$DryRun)
    $manifest=ConvertFrom-SashimiLfsObjectManifest -Json $ManifestJson
    $root=ConvertTo-SashimiPath -Path $RunRoot -AllowMissing -Lexical
    $repository=ConvertTo-SashimiPath -Path $RepositoryPath -AllowMissing -Lexical
    if (-not (Test-SashimiPathWithin -Path $repository -Root $root) -or (Split-Path -Leaf $repository) -cne 'Repository') {
        throw 'LFS cache may access only a standalone run Repository.'
    }
    $expected=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SashimiBoyAutomation\Runs'
    if (-not (Test-SashimiHarnessMode) -and -not (Test-SashimiPathEqual $root $expected)) { throw 'LFS cache RunRoot is not the fixed Host root.' }
    $cache=Join-Path (Split-Path -Parent $root) 'LfsCache\objects'
    $local=Join-Path $repository '.git\lfs\objects'
    Assert-SashimiNoReparsePoint -Path $repository
    $result=[ordered]@{ Mode=$Mode; DryRun=[bool]$DryRun; Objects=$manifest.Objects.Count; Hits=0; Available=0; Misses=0; Stored=0; Skipped=0; Warnings=@(); AllAvailable=$false }
    if ($DryRun -or $manifest.Objects.Count -eq 0) { return [pscustomobject]$result }
    [void](Get-SashimiOwnedRun -RunPath (Split-Path -Parent $repository) -RunRoot $root)
    $mutex=$null
    try {
        if ($Mode -ceq 'Store') {
            Assert-SashimiNoReparsePoint -Path $cache
            $mutex=Enter-SashimiHostMutex -Name ('Global\SashimiBoyLfsCache-'+(Get-SashimiTextSha256 $cache.ToLowerInvariant())) -TimeoutMilliseconds 0
            if (-not $mutex.Acquired) { $result.Skipped=$manifest.Objects.Count; $result.Warnings=@('CacheWriterBusy'); return [pscustomobject]$result }
        }
        $usage=if ($Mode -ceq 'Store') { Get-SashimiLfsCacheUsage $cache } else { $null }
        foreach ($oid in $manifest.Objects.Keys) {
            $size=$manifest.Objects[$oid]
            $relative=Join-Path $oid.Substring(0,2) (Join-Path $oid.Substring(2,2) $oid)
            $cacheObject=Join-Path $cache $relative
            $localObject=Join-Path $local $relative
            try {
                if ($Mode -ceq 'Restore') {
                    Assert-SashimiNoReparsePoint -Path $localObject
                    if ([IO.File]::Exists($localObject)) {
                        $stream=[IO.FileStream]::new($localObject,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                        try { Assert-SashimiNoReparsePoint -Path $localObject; $valid=Test-SashimiLfsObjectBytes $stream $oid $size } finally { $stream.Dispose() }
                        if ($valid) { $result.Available++; continue }
                    }
                    if ($size -gt 5GB) { $result.Skipped++; $result.Misses++; continue }
                    $copy=Copy-SashimiVerifiedLfsObject $cacheObject $localObject $oid $size
                    $status=$copy.Status
                    if ($copy.CleanupWarning) { $result.Warnings+='CacheTemporaryCleanupUnavailable' }
                    if ($status -in @('Copied','Existing')) { $result.Hits++; $result.Available++ }
                    else { $result.Misses++; if ($status -ne 'Missing') { $result.Warnings+=('CacheObject'+$status) } }
                }
                else {
                    if ($size -gt 5GB -or $usage.Full -or $usage.Bytes+$size -gt 32GB -or $usage.Files -ge 10000) { $result.Skipped++; continue }
                    $copy=Copy-SashimiVerifiedLfsObject $localObject $cacheObject $oid $size
                    $status=$copy.Status
                    if ($copy.CleanupWarning) { $result.Warnings+='CacheTemporaryCleanupUnavailable' }
                    if ($status -ceq 'Copied') { $result.Stored++; $usage.Bytes+=$size; $usage.Files++ }
                    elseif ($status -cne 'Existing') { $result.Skipped++; $result.Warnings+=('CacheObject'+$status) }
                }
            }
            catch [IO.IOException] { $result.Skipped++; $result.Warnings+=('CacheIoUnavailable'); if($Mode -ceq 'Restore'){$result.Misses++} }
            catch [UnauthorizedAccessException] { $result.Skipped++; $result.Warnings+=('CacheAccessUnavailable'); if($Mode -ceq 'Restore'){$result.Misses++} }
        }
    }
    catch [IO.IOException] { $result.Skipped=$manifest.Objects.Count; $result.Warnings+=('CacheIoUnavailable'); if($Mode -ceq 'Restore'){$result.Misses=$manifest.Objects.Count-$result.Available} }
    catch [UnauthorizedAccessException] { $result.Skipped=$manifest.Objects.Count; $result.Warnings+=('CacheAccessUnavailable'); if($Mode -ceq 'Restore'){$result.Misses=$manifest.Objects.Count-$result.Available} }
    finally { if($null -ne $mutex){Exit-SashimiHostMutex -Lease $mutex} }
    $result.Warnings=@($result.Warnings|Sort-Object -Unique)
    $result.AllAvailable=($result.Available -eq $manifest.Objects.Count)
    return [pscustomobject]$result
}

function Assert-SashimiLfsMaterializedBytes {
    param([Parameter(Mandatory)][string]$RepositoryPath, [Parameter(Mandatory)][string]$ManifestJson)
    $manifest=ConvertFrom-SashimiLfsObjectManifest $ManifestJson
    foreach($entry in $manifest.Files) {
        $path=Join-Path $RepositoryPath $entry.Name
        if(-not (Test-SashimiPathWithin -Path $path -Root $RepositoryPath)){throw 'LFS working file escaped Repository.'}
        Assert-SashimiNoReparsePoint -Path $path
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try { if(-not(Test-SashimiLfsObjectBytes $stream $entry.Oid $entry.Size)){throw 'LFS working file does not match the pinned manifest.'} }
        finally {$stream.Dispose()}
    }
}

function Initialize-SashimiLfsWorkspace {
    param([Parameter(Mandatory)][string]$RepositoryPath, [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CommitSha,
        [Parameter(Mandatory)][ValidateSet('origin','sashimi-canonical')][string]$Remote,
        [Parameter(Mandatory)][scriptblock]$InvokeLfs, [switch]$DryRun)
    $native=& $InvokeLfs 'Read pinned LFS object manifest' @('ls-files','--json',$CommitSha)
    if ($DryRun) {
        [void](& $InvokeLfs 'Materialize Git LFS content' @('pull',$Remote))
        return [pscustomobject]@{ DryRun=$true; CommitSha=$CommitSha; Strategy='Preview' }
    }
    $manifestJson=[string]$native.StdOut
    $restore=Sync-SashimiLfsObjectCache -RepositoryPath $RepositoryPath -RunRoot $RunRoot -ManifestJson $manifestJson -Mode Restore
    # A complete verified local set needs checkout, since clones skip smudge.
    # A partial set retains the endpoint-pinned native pull for missing objects.
    $strategy=if ($restore.Objects -gt 0 -and $restore.AllAvailable) { 'checkout' } else { 'pull' }
    [string[]]$arguments=if ($strategy -ceq 'checkout') { @('checkout') } else { @('pull',$Remote) }
    [void](& $InvokeLfs 'Materialize Git LFS content' $arguments)
    Assert-SashimiLfsMaterializedBytes -RepositoryPath $RepositoryPath -ManifestJson $manifestJson
    [void](& $InvokeLfs 'Validate materialized LFS objects' @('fsck'))
    $stored=Sync-SashimiLfsObjectCache -RepositoryPath $RepositoryPath -RunRoot $RunRoot -ManifestJson $manifestJson -Mode Store
    return [pscustomobject]@{ DryRun=$false; CommitSha=$CommitSha; Strategy=$strategy; Restore=$restore; Store=$stored }
}
