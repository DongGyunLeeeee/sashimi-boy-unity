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

function Import-SashimiHostConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    $normalizedConfigPath = ConvertTo-SashimiPath -Path $ConfigPath
    $config = Read-SashimiJsonFile -Path $normalizedConfigPath
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
    if ([string]::IsNullOrWhiteSpace([string]$config.GitAuthorName) -or [string]$config.GitAuthorName -notmatch '^[^\x00-\x1f\x7f]{1,128}$' -or
        [string]$config.GitAuthorEmail -notmatch '^[A-Za-z0-9.!#$%&''*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$') {
        throw 'GitAuthorName and GitAuthorEmail must be explicit, bounded, single-line Git identities.'
    }
    if ([int]$config.Task.IntervalMinutes -ne 15 -or [string]$config.Task.Name -cne $script:SashimiTaskName) {
        throw 'Task configuration must retain the exact name and 15-minute interval contract.'
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

    # Codex authentication is intentionally credential-store-only. Environment
    # API keys and Git/GitHub credentials are removed before the CLI starts.
    # Profile locator variables remain so the installed CLI can find its own
    # protected login store; command-event auditing forbids reading that content.
    $allowedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @(
            'SystemRoot','WINDIR','COMSPEC','PATH','PATHEXT','TEMP','TMP',
            'USERPROFILE','HOME','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','PROGRAMDATA',
            'ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432',
            'PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','NUMBER_OF_PROCESSORS','PSModulePath',
            'LANG','LC_ALL','SSL_CERT_FILE','SSL_CERT_DIR','CODEX_HOME','OPENAI_BASE_URL',
            'HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY'
        )) {
        [void]$allowedNames.Add($name)
    }

    $pathBearingNames = @(
        'USERPROFILE','HOME','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','PROGRAMDATA',
        'TEMP','TMP','PATH','PSModulePath','CODEX_HOME','SSL_CERT_FILE','SSL_CERT_DIR'
    )
    $removeNames = New-Object 'System.Collections.Generic.List[string]'
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        $remove = -not $allowedNames.Contains($name) -or (Test-SashimiSensitiveEnvironmentName -Name $name)
        if (-not $remove -and $pathBearingNames -notcontains $name -and
            (Test-SashimiRecognizableSensitiveText -Text $value)) {
            $remove = $true
        }
        if ($remove) { $removeNames.Add($name) }
    }

    return [pscustomobject][ordered]@{
        Mode = 'AllowList'
        Authentication = 'CredentialStoreOnly'
        RemoveNames = @($removeNames.ToArray() | Sort-Object -Unique)
        Overrides = @{
            GIT_TERMINAL_PROMPT = '0'
            GCM_INTERACTIVE = 'Never'
        }
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
        [pscustomobject]@{ Key='sequence.editor'; Value='false' },
        [pscustomobject]@{ Key='diff.external'; Value='' },
        [pscustomobject]@{ Key='commit.gpgSign'; Value='false' },
        [pscustomobject]@{ Key='tag.gpgSign'; Value='false' },
        [pscustomobject]@{ Key='credential.interactive'; Value='never' },
        [pscustomobject]@{ Key='credential.helper'; Value='' },
        [pscustomobject]@{ Key='credential.https://github.com.helper'; Value=('!' + $quotedGh + ' auth git-credential') },
        [pscustomobject]@{ Key='credential.https://github.com.useHttpPath'; Value='false' },
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
        [switch]$DryRun
    )

    [void](Assert-SashimiSafeCommand -FilePath $FilePath -ArgumentList $ArgumentList -Kind $Kind)
    $commandText = Format-SashimiCommand -FilePath $FilePath -ArgumentList $ArgumentList
    if ($DryRun) {
        return [pscustomobject][ordered]@{
            FilePath = Protect-SashimiText $FilePath; Arguments = @($ArgumentList | ForEach-Object { Protect-SashimiText $_ }); Command = $commandText
            ExitCode = 0; StdOut = ''; StdErr = ''; Succeeded = $true
            TimedOut = $false; Cancelled = $false; TerminationConfirmed = $true
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
    $startInfo.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
    $startInfo.Environment['NO_COLOR'] = '1'
    $startInfo.Environment['GH_FORCE_TTY'] = 'never'
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

    # The identity is deliberately re-read after all process preparation and
    # immediately before Process.Start, closing PATH lookup and stale-hash use.
    Assert-SashimiBoundExecutableIdentity -FilePath $FilePath
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
    try {
        if (-not $process.Start()) { throw "Unable to start process: $commandText" }
        $started = $true
        $pidValue = $process.Id
        if (-not [string]::IsNullOrWhiteSpace($OwnedProcessRecordPath)) {
            $processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
            Update-SashimiOwnedProcessLedger -Path $OwnedProcessRecordPath -Action Add -ProcessId $pidValue -StartTimeUtc $processStartTimeUtc
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
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
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
        }
        else {
            $stderr = 'The owned process could not be confirmed terminated; its PID record was preserved.'
        }
        if (-not $timedOut -and -not $cancelled -and $process.HasExited) { $exitCode = $process.ExitCode }
        elseif ($timedOut) { $exitCode = 124 }
        elseif ($cancelled) { $exitCode = 125 }
    }
    catch {
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
        if ($terminationConfirmed -and -not [string]::IsNullOrWhiteSpace($OwnedProcessRecordPath) -and
            $null -ne $pidValue -and -not [string]::IsNullOrWhiteSpace($processStartTimeUtc)) {
            try {
                Update-SashimiOwnedProcessLedger -Path $OwnedProcessRecordPath -Action Remove -ProcessId $pidValue -StartTimeUtc $processStartTimeUtc
            }
            catch { }
        }
        $process.Dispose()
    }

    $result = [pscustomobject][ordered]@{
        FilePath = Protect-SashimiText $FilePath
        Arguments = @($ArgumentList | ForEach-Object { Protect-SashimiText $_ })
        Command = $commandText
        ExitCode = [int]$exitCode
        StdOut = Protect-SashimiTextWithExactValues -Text $stdout.TrimEnd() -ExactValues $sensitiveOutputValues
        StdErr = Protect-SashimiTextWithExactValues -Text $stderr.TrimEnd() -ExactValues $sensitiveOutputValues
        Succeeded = ($exitCode -eq 0 -and -not $timedOut -and -not $cancelled -and $terminationConfirmed)
        TimedOut = $timedOut
        Cancelled = $cancelled
        TerminationConfirmed = $terminationConfirmed
        ProcessId = $pidValue
        DurationMilliseconds = [int64]$stopwatch.ElapsedMilliseconds
        DryRun = $false
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
