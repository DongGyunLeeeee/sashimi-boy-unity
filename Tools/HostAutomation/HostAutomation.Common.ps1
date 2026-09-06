#requires -Version 7.5

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    if ([int](Get-SashimiPropertyValue $identity 'SchemaVersion' 0) -ne 1) {
        throw 'Executable identity SchemaVersion must be 1.'
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
        $verified.Add([pscustomobject][ordered]@{
                Name = $name
                Path = $entryPath
                Length = [int64]$entry.Length
                Sha256 = [string]$entry.Sha256
            })
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
    $expectedDistributionRoot = ConvertTo-SashimiPath -Path (Join-Path $protectedRoot ([string]$identityEntries[0].Sha256)) -AllowMissing -Lexical
    $expectedCodexPath = ConvertTo-SashimiPath -Path (Join-Path $expectedDistributionRoot 'codex.exe') -AllowMissing -Lexical
    if (-not (Test-SashimiPathEqual -Left $candidate -Right $expectedCodexPath)) {
        throw "CodexExecutable must be the exact content-addressed path '$expectedCodexPath'."
    }
    Assert-SashimiNoReparsePoint -Path $candidate

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
    $cursor = $candidate
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
    return $candidate
}

function Open-SashimiExecutableLaunchLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][ValidateSet('Generic','Git','GitHub','Codex','Unity')][string]$Kind
    )

    $candidate = ConvertTo-SashimiExecutablePath -Name 'Process FilePath' -Path $FilePath -RequireFile
    if ($Kind -ceq 'Codex') { [void](Assert-SashimiProtectedCodexExecutable -FilePath $candidate) }
    Assert-SashimiBoundExecutableIdentity -FilePath $candidate

    $stream = $null
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
        Assert-SashimiNoReparsePoint -Path $candidate
        if ($Kind -ceq 'Codex') { [void](Assert-SashimiProtectedCodexExecutable -FilePath $candidate) }
        return [pscustomobject][ordered]@{ Path = $candidate; Stream = $stream }
    }
    catch {
        if ($null -ne $stream) { $stream.Dispose() }
        throw
    }
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
        $document = [Text.Json.JsonDocument]::Parse($JsonText, $options)
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
            string standardInput, IDictionary<string,string> environment, int timeoutSeconds, string cancellationMarkerPath)
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
                while (!mainExited)
                {
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
                    if (processCreated) TerminateJobObject(job, FORCED_TERMINATION_EXIT_CODE);
                    CloseNativeHandle(ref job);
                    if (processCreated) WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_MILLISECONDS);
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
        [switch]$ClearEnvironment,
        [switch]$PreserveRawOutputInMemory,
        [switch]$RequireKillOnCloseJob,
        [switch]$DryRun
    )

    [void](Assert-SashimiSafeCommand -FilePath $FilePath -ArgumentList $ArgumentList -Kind $Kind)
    if ($PreserveRawOutputInMemory -and ($Kind -cne 'Codex' -or -not [string]::IsNullOrWhiteSpace($InvocationRecordPath))) {
        throw 'Unredacted in-memory output is allowed only for Codex without an invocation-record path.'
    }
    if ($RequireKillOnCloseJob -and $Kind -cne 'Unity') {
        throw 'The kill-on-close suspended process boundary is supported only for Unity.'
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
            $launchLease = Open-SashimiExecutableLaunchLease -FilePath $FilePath -Kind $Kind
            $native = [SashimiBoyAutomation.KillOnCloseProcess]::Run(
                [string]$FilePath,
                [string[]]@($ArgumentList),
                [string]$startInfo.WorkingDirectory,
                [string]$StandardInput,
                [Collections.Generic.IDictionary[string,string]]$startInfo.Environment,
                [int]$TimeoutSeconds,
                [string]$CancellationMarkerPath)
        }
        catch { $nativeException = $_.Exception }
        finally {
            if ($null -ne $launchLease) { try { $launchLease.Stream.Dispose() } catch { } }
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
        $launchLease = Open-SashimiExecutableLaunchLease -FilePath $FilePath -Kind $Kind
        $started = [bool]$process.Start()
        if (-not $started) { throw "Unable to start process: $commandText" }
        $launchLease.Stream.Dispose()
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
            try { $launchLease.Stream.Dispose() } catch { }
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

function Remove-SashimiRunRepository {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RunPath, [Parameter(Mandatory = $true)][string]$RunRoot, [switch]$DryRun)

    try {
        $owned = Get-SashimiOwnedRun -RunPath $RunPath -RunRoot $RunRoot
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
