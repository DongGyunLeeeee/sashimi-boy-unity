#requires -Version 7.5

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ConfigPath,
    [ValidateNotNullOrEmpty()][string]$OrchestratorPath = ([IO.Path]::Combine($PSScriptRoot, 'Invoke-SashimiHostOrchestrator.ps1')),
    [DateTime]$StartBoundary = [DateTime]::MinValue,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TaskName = 'SASHIMI BOY Host Orchestrator'
$script:RequiredUserName = '02031'
$script:PowerShellPath = 'C:\Program Files\PowerShell\7\pwsh.exe'
$script:MinimumPowerShellVersion = [Version]'7.5.0'
$script:PowerShellHome = [IO.Path]::GetDirectoryName($script:PowerShellPath)
$script:PowerShellModuleRoot = [IO.Path]::Combine($script:PowerShellHome, 'Modules')
$script:WindowsModuleRoot = [IO.Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows), 'System32', 'WindowsPowerShell', 'v1.0', 'Modules')
$script:SecurityModuleManifest = [IO.Path]::Combine($script:PowerShellModuleRoot, 'Microsoft.PowerShell.Security', 'Microsoft.PowerShell.Security.psd1')
$script:ScheduledTasksModuleManifest = [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'ScheduledTasks.psd1')
$script:TrustedModuleFiles = @(
    $script:SecurityModuleManifest,
    [IO.Path]::Combine($script:PowerShellHome, 'Microsoft.PowerShell.Security.dll'),
    [IO.Path]::Combine($script:PowerShellModuleRoot, 'Microsoft.PowerShell.Security', 'Security.types.ps1xml'),
    $script:ScheduledTasksModuleManifest,
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'MSFT_ScheduledTask_v1.0.cdxml'),
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'PS_ScheduledTask_v1.0.cdxml'),
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'PS_ClusteredScheduledTask_v1.0.cdxml'),
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'MSFT_ScheduledTask.types.ps1xml'),
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'PS_ScheduledTask.types.ps1xml'),
    [IO.Path]::Combine($script:WindowsModuleRoot, 'ScheduledTasks', 'MSFT_ScheduledTask.format.ps1xml')
)
$script:TrustedPowerShellFileHashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$script:InstallRoot = [IO.Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles), 'SashimiBoyAutomation')
$script:BundlesRoot = [IO.Path]::Combine($script:InstallRoot, 'Bundles')
$script:ManifestName = 'HostIntegrity.json'
$script:ExecutableIdentityName = 'ExecutableIdentity.json'
$script:ExpectedRepository = 'DongGyunLeeeee/sashimi-boy-unity'
$script:ExpectedRemoteUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity.git'
$script:ExpectedProjectOwner = 'DongGyunLeeeee'
$script:ExpectedProjectNumber = 1
$script:ExpectedMutexName = 'Global\SashimiBoyHostOrchestrator'
$script:ExecutableProperties = @('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','PowerShellExecutable','UnityExecutable')
$script:RequiredBundleFiles = @(
    'HostAutomation.Common.ps1',
    'Invoke-SashimiHostOrchestrator.ps1',
    'Get-SashimiProjectQueue.ps1',
    'Invoke-SashimiCodexExec.ps1',
    'Invoke-SashimiDeveloperRun.ps1',
    'Invoke-SashimiReviewerRun.ps1',
    'Invoke-SashimiUnityValidation.ps1',
    'Publish-SashimiRunResult.ps1'
)

# Prevent module auto-loading from the current user's profile or another
# process-supplied path before any non-core module command is resolved.
$env:PSModulePath = [string]::Join([IO.Path]::PathSeparator, @($script:PowerShellModuleRoot, $script:WindowsModuleRoot))

function Assert-InstallerTrustedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$TrustedRoot
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($TrustedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($fullPath)) {
        throw 'A required PowerShell component is missing from its exact protected system root.'
    }
    $cursor = $fullPath
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'A required PowerShell component traverses a reparse point.'
        }
        if ([string]::Equals($cursor, $TrustedRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrWhiteSpace($parent) -or [string]::Equals($parent, $cursor, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = $parent
    }
}

function Get-InstallerNativeSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))).ToLowerInvariant()
}

function Assert-InstallerMicrosoftSignature {
    param([Parameter(Mandatory = $true)][string]$Path)

    $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
    if ([string]$signature.Status -cne 'Valid' -or $null -eq $signature.SignerCertificate -or
        [string]$signature.SignerCertificate.Subject -cnotmatch '(?:^|, )O=Microsoft Corporation(?:,|$)') {
        throw 'A required PowerShell component does not have a valid Microsoft Authenticode signature.'
    }
    $hasCodeSigningEku = $false
    foreach ($extension in $signature.SignerCertificate.Extensions) {
        if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ([string]$usage.Value -ceq '1.3.6.1.5.5.7.3.3') { $hasCodeSigningEku = $true }
            }
        }
    }
    if (-not $hasCodeSigningEku) { throw 'A required PowerShell component signer is not authorized for code signing.' }
}

function Assert-InstallerTrustedPowerShellState {
    param([switch]$ScheduledTasks)

    $expected = if ($ScheduledTasks) { 'ScheduledTasks' } else { 'Microsoft.PowerShell.Security' }
    $expectedManifest = if ($ScheduledTasks) { $script:ScheduledTasksModuleManifest } else { $script:SecurityModuleManifest }
    $loaded = @(Microsoft.PowerShell.Core\Get-Module -Name $expected)
    if ($loaded.Count -ne 1 -or -not [string]::Equals([string]$loaded[0].Path, $expectedManifest, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The exact protected $expected module is not loaded."
    }
    foreach ($path in $script:TrustedModuleFiles) {
        if (-not $script:TrustedPowerShellFileHashes.ContainsKey($path) -or
            (Get-InstallerNativeSha256 -Path $path) -cne $script:TrustedPowerShellFileHashes[$path]) {
            throw 'A trusted PowerShell component changed after provenance verification.'
        }
    }
}

function Initialize-InstallerTrustedPowerShell {
    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion -lt $script:MinimumPowerShellVersion) {
        throw "Installer requires PowerShell $script:MinimumPowerShellVersion or newer (Core edition)."
    }
    $processPath = [Environment]::ProcessPath
    $mainModulePath = $null
    $currentProcess = [Diagnostics.Process]::GetCurrentProcess()
    try { $mainModulePath = $currentProcess.MainModule.FileName } finally { $currentProcess.Dispose() }
    foreach ($actualPath in @($processPath, $mainModulePath)) {
        if ([string]::IsNullOrWhiteSpace($actualPath) -or
            -not [string]::Equals([IO.Path]::GetFullPath($actualPath), $script:PowerShellPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer must run in the stable host '$($script:PowerShellPath)'."
        }
    }
    if (-not [string]::Equals([IO.Path]::GetFullPath($PSHOME), $script:PowerShellHome, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installer PSHOME does not match the stable PowerShell host.'
    }
    Assert-InstallerTrustedPath -Path $script:PowerShellPath -TrustedRoot $script:PowerShellHome
    Assert-InstallerTrustedPath -Path $script:SecurityModuleManifest -TrustedRoot $script:PowerShellModuleRoot
    Assert-InstallerTrustedPath -Path $script:ScheduledTasksModuleManifest -TrustedRoot $script:WindowsModuleRoot

    foreach ($moduleName in @('Microsoft.PowerShell.Security', 'ScheduledTasks')) {
        foreach ($module in @(Microsoft.PowerShell.Core\Get-Module -Name $moduleName)) {
            $expectedManifest = if ($moduleName -ceq 'ScheduledTasks') { $script:ScheduledTasksModuleManifest } else { $script:SecurityModuleManifest }
            if (-not [string]::Equals([string]$module.Path, $expectedManifest, [StringComparison]::OrdinalIgnoreCase)) {
                throw "An untrusted preloaded $moduleName module is present in the elevated process."
            }
        }
    }

    Microsoft.PowerShell.Core\Import-Module -Name $script:SecurityModuleManifest -Scope Local -Force -ErrorAction Stop
    Assert-InstallerMicrosoftSignature -Path $script:PowerShellPath
    foreach ($path in $script:TrustedModuleFiles) {
        $trustedRoot = if ($path.StartsWith($script:WindowsModuleRoot, [StringComparison]::OrdinalIgnoreCase)) { $script:WindowsModuleRoot } else { $script:PowerShellHome }
        Assert-InstallerTrustedPath -Path $path -TrustedRoot $trustedRoot
        Assert-InstallerMicrosoftSignature -Path $path
        $script:TrustedPowerShellFileHashes[$path] = Get-InstallerNativeSha256 -Path $path
    }
    Microsoft.PowerShell.Core\Import-Module -Name $script:ScheduledTasksModuleManifest -Scope Local -Force -ErrorAction Stop
    Assert-InstallerTrustedPowerShellState
    Assert-InstallerTrustedPowerShellState -ScheduledTasks
    foreach ($qualifiedCommand in @(
        'Microsoft.PowerShell.Security\Get-Acl',
        'Microsoft.PowerShell.Security\Set-Acl',
        'ScheduledTasks\Get-ScheduledTask',
        'ScheduledTasks\Register-ScheduledTask',
        'ScheduledTasks\Unregister-ScheduledTask'
    )) {
        $command = Microsoft.PowerShell.Core\Get-Command $qualifiedCommand -ErrorAction Stop
        if ($command.CommandType -notin @([Management.Automation.CommandTypes]::Cmdlet, [Management.Automation.CommandTypes]::Function)) {
            throw 'A required protected PowerShell command did not resolve to a cmdlet or module function.'
        }
    }
}

function ConvertTo-InstallerJson {
    param([Parameter(Mandatory = $true)][AllowNull()][object]$InputObject)
    return ($InputObject | ConvertTo-Json -Depth 32 -Compress)
}

function Import-InstallerConfig {
    param([Parameter(Mandatory = $true)][string]$Path)

    # The installer may be elevated. Configuration is parsed and validated as
    # data using installer-local code; no writable source-tree module is ever
    # dot-sourced into the elevated process.
    $normalizedPath = ConvertTo-InstallerPath -Path $Path
    Assert-InstallerNoReparsePoint $normalizedPath
    $config = Read-InstallerJsonFile -Path $normalizedPath
    if ([int](Get-InstallerPropertyValue $config 'SchemaVersion' 0) -ne 1) { throw 'Config SchemaVersion must be 1.' }
    if ([string](Get-InstallerPropertyValue $config 'Repository' '') -cne $script:ExpectedRepository) { throw "Config Repository must be exactly '$($script:ExpectedRepository)'." }
    if ([string](Get-InstallerPropertyValue $config 'ProjectOwner' '') -cne $script:ExpectedProjectOwner -or
        [int](Get-InstallerPropertyValue $config 'ProjectNumber' 0) -ne $script:ExpectedProjectNumber) { throw "Config must target Project '$($script:ExpectedProjectOwner)/$($script:ExpectedProjectNumber)'." }
    if ([string](Get-InstallerPropertyValue $config 'DefaultBranch' '') -cne 'main') { throw "Config DefaultBranch must be exactly 'main'." }
    if ([string](Get-InstallerPropertyValue $config 'RemoteUrl' '') -cne $script:ExpectedRemoteUrl) { throw "Config RemoteUrl must be exactly '$($script:ExpectedRemoteUrl)'." }
    if ([string](Get-InstallerPropertyValue $config 'MutexName' '') -cne $script:ExpectedMutexName) { throw "Config MutexName must be exactly '$($script:ExpectedMutexName)'." }
    $retention = [int](Get-InstallerPropertyValue $config 'ArtifactRetentionDays' 0)
    if ($retention -lt 1 -or $retention -gt 365) { throw 'ArtifactRetentionDays must be between 1 and 365.' }

    foreach ($required in @('GitExecutable','GitLfsExecutable','GitHubCli','CodexExecutable','PowerShellExecutable','UnityExecutable','GitAuthorName','GitAuthorEmail','Task','Timeouts','Retry','Security')) {
        if ($null -eq $config.PSObject.Properties[$required]) { throw "Config is missing required property '$required'." }
    }
    $runRoot = [string](Get-InstallerPropertyValue $config 'RunRoot' '')
    if ([string]::IsNullOrWhiteSpace($runRoot)) { throw 'Config RunRoot is required.' }
    $expandedRunRoot = ConvertTo-InstallerPath -Path $runRoot -AllowMissing
    $expectedRunRoot = ConvertTo-InstallerPath -Path (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'SashimiBoyAutomation\Runs') -AllowMissing
    if (-not (Test-InstallerPathEqual -Left $expandedRunRoot -Right $expectedRunRoot) -and -not (Test-InstallerHarnessMode)) { throw "RunRoot must be exactly '$expectedRunRoot' outside the test harness." }
    foreach ($name in $script:ExecutableProperties) { $config.$name = ConvertTo-InstallerExecutablePath -Name $name -Path ([string]$config.$name) }
    if ([string]$config.PowerShellExecutable -cne $script:PowerShellPath) { throw "PowerShellExecutable must be '$($script:PowerShellPath)'." }
    if ([string]::IsNullOrWhiteSpace([string]$config.GitAuthorName) -or [string]$config.GitAuthorName -notmatch '^[^\x00-\x1f\x7f]{1,128}$' -or
        [string]$config.GitAuthorEmail -notmatch '^[A-Za-z0-9.!#$%&''*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$') {
        throw 'GitAuthorName and GitAuthorEmail must be explicit, bounded, single-line Git identities.'
    }
    if ([int]$config.Task.IntervalMinutes -ne 15 -or [string]$config.Task.Name -cne $script:TaskName) { throw 'Task configuration must retain the exact name and 15-minute interval contract.' }
    if ([int]$config.Retry.MaximumAttempts -lt 1 -or [int]$config.Retry.MaximumAttempts -gt 10 -or [int]$config.Retry.CooldownSeconds -lt 0 -or [int]$config.Retry.CooldownSeconds -gt 3600) { throw 'Retry settings are outside the supported bounds.' }
    $authorizedAuthors = @($config.Security.AuthorizedPrAuthors | ForEach-Object { [string]$_ })
    if ($authorizedAuthors.Count -lt 1 -or @($authorizedAuthors | Where-Object { $_ -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$' }).Count -gt 0) { throw 'Security.AuthorizedPrAuthors must contain at least one valid GitHub login.' }
    $mandatoryProtectedPatterns = @('Assets/_SashimiBoy/Art/Source/**','Assets/**/*.unity','Assets/**/*.prefab','Assets/**/*.fbx','Assets/**/*.wav','Assets/**/*.mp3','Packages/**','ProjectSettings/**')
    $configuredProtectedPatterns = @($config.Security.ProtectedPathPatterns | ForEach-Object { [string]$_ })
    if (@($mandatoryProtectedPatterns | Where-Object { $configuredProtectedPatterns -cnotcontains $_ }).Count -gt 0) { throw 'Security.ProtectedPathPatterns is missing mandatory production protection.' }
    $artifactExclusions = @($config.Security.ArtifactExclusionPatterns | ForEach-Object { [string]$_ })
    foreach ($requiredExclusion in @('**/.git/**','**/.codex/**','**/*Save*/**')) {
        if ($artifactExclusions -cnotcontains $requiredExclusion) { throw "Security.ArtifactExclusionPatterns must include '$requiredExclusion'." }
    }
    $config.RunRoot = $expandedRunRoot
    return $config
}

function Get-InstallerPropertyValue {
    param([AllowNull()][object]$Object, [Parameter(Mandatory = $true)][string]$Name, [AllowNull()][object]$DefaultValue = $null)
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) { return $DefaultValue }
    return $Object.PSObject.Properties[$Name].Value
}

function ConvertTo-InstallerPath {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$AllowMissing)
    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not [IO.Path]::IsPathRooted($expanded)) { $expanded = Join-Path (Get-Location).ProviderPath $expanded }
    if (-not $AllowMissing -and -not (Test-Path -LiteralPath $expanded)) { throw "Path does not exist: $Path" }
    $full = [IO.Path]::GetFullPath($expanded)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)) }
    return $full
}

function Test-InstallerPathEqual {
    param([Parameter(Mandatory = $true)][string]$Left, [Parameter(Mandatory = $true)][string]$Right)
    return [string]::Equals((ConvertTo-InstallerPath $Left -AllowMissing),(ConvertTo-InstallerPath $Right -AllowMissing),[StringComparison]::OrdinalIgnoreCase)
}

function Test-InstallerHarnessMode { return [string]::Equals($env:SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS,'1',[StringComparison]::Ordinal) }

function ConvertTo-InstallerExecutablePath {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path) -or $Path -cnotmatch '^[A-Za-z]:\\') { throw "$Name must be an absolute local Windows executable path." }
    $full = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($Path,$full,[StringComparison]::OrdinalIgnoreCase) -or -not [string]::Equals([IO.Path]::GetExtension($full),'.exe',[StringComparison]::OrdinalIgnoreCase)) { throw "$Name must be a canonical absolute .exe path." }
    return $full
}

function Read-InstallerJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = ConvertTo-InstallerPath -Path $Path
    Assert-InstallerNoReparsePoint $normalized
    try {
        $bytes = [IO.File]::ReadAllBytes($normalized)
        $text = [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
        return $text | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch { throw "Invalid strict UTF-8 JSON file '$normalized'." }
}

function Protect-InstallerText {
    param([AllowNull()][object]$Text)
    if ($null -eq $Text) { return '' }
    $value = [string]$Text
    foreach ($pattern in @('(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+','(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}','(?i)\bsk-[A-Za-z0-9_-]{8,}','(?i)(?:access[_-]?token|refresh[_-]?token|api[_-]?key|token|password|secret|credential)\s*[=:]\s*[^\s,;}]+')) { $value = [regex]::Replace($value,$pattern,'[REDACTED_SECRET]') }
    $profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($profile)) {
        $value = $value.Replace($profile,'[REDACTED_PROFILE]',[StringComparison]::OrdinalIgnoreCase)
        $value = $value.Replace($profile.Replace('\','\\'),'[REDACTED_PROFILE]',[StringComparison]::OrdinalIgnoreCase)
        $value = $value.Replace($profile.Replace('\','/'),'[REDACTED_PROFILE]',[StringComparison]::OrdinalIgnoreCase)
    }
    return [regex]::Replace($value,'(?i)[A-Z]:\\[^\r\n"'']*\\(?:Save|Saves|SaveData|LocalLow)\\[^\r\n"'']*','[REDACTED_SAVE_PATH]')
}

function ConvertTo-TaskArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value.IndexOfAny([char[]]@('"', "`r", "`n")) -ge 0) { throw 'Task action paths cannot contain quotes or line breaks.' }
    return '"' + $Value + '"'
}

function ConvertTo-XmlText {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-InstallerFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Get-InstallerTextSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Assert-InstallerNoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)
    $cursor=[IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Installer path traverses a reparse point: $($item.FullName)" }
        }
        $parent=Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor=$parent
    }
}

function Get-InstallerExecutableIdentityEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path) -or $Path -cnotmatch '^[A-Za-z]:\\') {
        throw "$Name must be an absolute local Windows executable path."
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($Path,$fullPath,[StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must be canonical and cannot contain relative path segments or trailing separators."
    }
    if ([IO.Path]::GetExtension($fullPath) -cne '.exe') { throw "$Name must identify an .exe file." }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must identify a plain, non-reparse executable file."
    }
    if (-not [string]::Equals($item.FullName,$fullPath,[StringComparison]::OrdinalIgnoreCase) -or [int64]$item.Length -lt 1) {
        throw "$Name did not resolve to its exact canonical non-empty file."
    }
    return [ordered]@{ Name=$Name; Path=$item.FullName; Length=[int64]$item.Length; Sha256=Get-InstallerFileSha256 $item.FullName }
}

function New-InstallerExecutableIdentity {
    param([Parameter(Mandatory = $true)][object]$Config)
    $entries = foreach ($name in $script:ExecutableProperties) {
        $property = $Config.PSObject.Properties[$name]
        if ($null -eq $property) { throw "Configuration is missing executable property '$name'." }
        Get-InstallerExecutableIdentityEntry -Name $name -Path ([string]$property.Value)
    }
    return [ordered]@{ SchemaVersion=1; Executables=@($entries) }
}

function Assert-InstallerExecutableIdentity {
    param([Parameter(Mandatory = $true)][object]$Identity)
    if ([int]$Identity.SchemaVersion -ne 1) { throw 'Executable identity SchemaVersion must be 1.' }
    $entries = @($Identity.Executables)
    if ($entries.Count -ne $script:ExecutableProperties.Count) { throw 'Executable identity must contain exactly the configured bound tools.' }
    for ($index=0; $index -lt $script:ExecutableProperties.Count; $index++) {
        $expectedName=$script:ExecutableProperties[$index]; $entry=$entries[$index]
        if ([string]$entry.Name -cne $expectedName -or [string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$entry.Length -lt 1) {
            throw "Executable identity entry is invalid at index $index."
        }
        $current=Get-InstallerExecutableIdentityEntry -Name $expectedName -Path ([string]$entry.Path)
        if ([int64]$current.Length -ne [int64]$entry.Length -or [string]$current.Sha256 -cne [string]$entry.Sha256) {
            throw "$expectedName changed after its executable identity was captured."
        }
    }
}

function Get-StablePowerShellVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][int64]$ExpectedLength,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-Command','[Console]::Out.Write($PSVersionTable.PSVersion.ToString())')) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
    try {
        $current=Get-InstallerExecutableIdentityEntry -Name 'PowerShellExecutable' -Path $Executable
        if ([int64]$current.Length -ne $ExpectedLength -or [string]$current.Sha256 -cne $ExpectedSha256) {
            throw 'PowerShellExecutable changed before the installer version probe.'
        }
        if (-not $process.Start()) { throw 'The stable PowerShell version probe could not start.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            try { $process.Kill($true) } catch { }
            [void]$process.WaitForExit(10000)
            throw 'The stable PowerShell version probe timed out.'
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim(); $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) { throw "The stable PowerShell version probe failed with exit $($process.ExitCode): $stderr" }
        $detected = $null
        if (-not [Version]::TryParse($stdout, [ref]$detected)) { throw "The stable PowerShell version probe returned '$stdout'." }
        return $detected
    }
    finally { $process.Dispose() }
}

function Get-InstallerSidValue {
    param([Parameter(Mandatory = $true)][object]$Identity)
    if ($Identity -is [Security.Principal.SecurityIdentifier]) { return $Identity.Value }
    if ($Identity -is [Security.Principal.IdentityReference]) {
        return $Identity.Translate([Security.Principal.SecurityIdentifier]).Value
    }
    return ([Security.Principal.NTAccount]::new([string]$Identity)).Translate([Security.Principal.SecurityIdentifier]).Value
}

function Set-InstallerProtectedAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid,
        [switch]$Container
    )
    $administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $acl = if ($Container) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $acl.SetAccessRuleProtection($true, $false); $acl.SetOwner($administrators)
    $inheritance = if ($Container) { [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit } else { [Security.AccessControl.InheritanceFlags]::None }
    foreach ($entry in @(
        [pscustomobject]@{ Sid=$administrators; Rights=[Security.AccessControl.FileSystemRights]::FullControl },
        [pscustomobject]@{ Sid=$system; Rights=[Security.AccessControl.FileSystemRights]::FullControl },
        [pscustomobject]@{ Sid=$UserSid; Rights=[Security.AccessControl.FileSystemRights]::ReadAndExecute }
    )) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($entry.Sid,$entry.Rights,$inheritance,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Assert-InstallerTrustedPowerShellState
    Microsoft.PowerShell.Security\Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop
}

function Assert-InstallerProtectedAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Protected host path is a reparse point: $Path" }
    Assert-InstallerTrustedPowerShellState
    $acl = Microsoft.PowerShell.Security\Get-Acl -LiteralPath $Path -ErrorAction Stop
    $administrators = 'S-1-5-32-544'; $system = 'S-1-5-18'
    if (-not $acl.AreAccessRulesProtected) { throw "Protected host ACL still inherits permissions: $Path" }
    if ((Get-InstallerSidValue $acl.Owner) -cne $administrators) { throw "Protected host path is not owned by Administrators: $Path" }
    $allowedSids = @($administrators,$system,$UserSid.Value)
    $fullControlSids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $userRead = $false
    $writeMask = [Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Modify -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($rule in @($acl.Access)) {
        $sid = Get-InstallerSidValue $rule.IdentityReference
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $allowedSids -cnotcontains $sid) { throw "Protected host ACL contains an unexpected access rule for '$sid': $Path" }
        if ($sid -ceq $UserSid.Value) {
            if (($rule.FileSystemRights -band $writeMask) -ne 0) { throw "Task user retains write access to protected host path: $Path" }
            if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadAndExecute) -ne 0) { $userRead = $true }
        }
        elseif (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl) { [void]$fullControlSids.Add($sid) }
    }
    if (-not $userRead -or -not $fullControlSids.Contains($administrators) -or -not $fullControlSids.Contains($system)) { throw "Protected host ACL is missing a required access rule: $Path" }
}

function New-InstallerBundlePlan {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$SourceConfigPath,
        [Parameter(Mandatory = $true)][object]$Config
    )
    $entries = New-Object 'System.Collections.Generic.List[object]'
    Assert-InstallerNoReparsePoint $SourceRoot
    foreach ($name in $script:RequiredBundleFiles) {
        $source = Join-Path $SourceRoot $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required runtime host file is missing: $source" }
        $item = Get-Item -LiteralPath $source -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Runtime host source cannot be a reparse point: $source" }
        Assert-InstallerNoReparsePoint $item.FullName
        $sourceBytes=[IO.File]::ReadAllBytes($item.FullName)
        $sourceHash=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($sourceBytes))).ToLowerInvariant()
        $entries.Add([pscustomobject][ordered]@{ RelativePath=$name; SourcePath=''; Content=$null; Bytes=$sourceBytes; Sha256=$sourceHash; Length=[int64]$sourceBytes.LongLength })
    }
    $configItem = Get-Item -LiteralPath $SourceConfigPath -Force -ErrorAction Stop
    if (($configItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Configuration source cannot be a reparse point: $SourceConfigPath" }
    Assert-InstallerNoReparsePoint $configItem.FullName
    $configBytes=[IO.File]::ReadAllBytes($configItem.FullName)
    try {
        $configText=[Text.UTF8Encoding]::new($false,$true).GetString($configBytes)
        $snapshotConfig=$configText | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch { throw 'Configuration snapshot is not valid strict UTF-8 JSON.' }
    $snapshotConfig.RunRoot=ConvertTo-InstallerPath -Path ([string]$snapshotConfig.RunRoot) -AllowMissing
    foreach ($name in $script:ExecutableProperties) {
        $snapshotConfig.$name=ConvertTo-InstallerExecutablePath -Name $name -Path ([string]$snapshotConfig.$name)
    }
    if ((ConvertTo-InstallerJson $snapshotConfig) -cne (ConvertTo-InstallerJson $Config)) {
        throw 'Configuration changed while the installer captured its immutable bundle snapshot.'
    }
    $configHash=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($configBytes))).ToLowerInvariant()
    $entries.Add([pscustomobject][ordered]@{ RelativePath='Config.json'; SourcePath=''; Content=$null; Bytes=$configBytes; Sha256=$configHash; Length=[int64]$configBytes.LongLength })
    $executableIdentity = New-InstallerExecutableIdentity -Config $Config
    Assert-InstallerExecutableIdentity -Identity $executableIdentity
    $identityContent = ($executableIdentity | ConvertTo-Json -Depth 8 -Compress) + "`n"
    $identityBytes = [Text.UTF8Encoding]::new($false).GetBytes($identityContent)
    $entries.Add([pscustomobject][ordered]@{
            RelativePath=$script:ExecutableIdentityName; SourcePath=''; Content=$identityContent; Bytes=$null
            Sha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identityBytes))).ToLowerInvariant()
            Length=[int64]$identityBytes.LongLength
        })
    $orderedEntries = @($entries | Sort-Object RelativePath)
    $identityText = [string]::Join("`n", @($orderedEntries | ForEach-Object { "$($_.RelativePath)`0$($_.Sha256)`0$($_.Length)" }))
    $bundleId = Get-InstallerTextSha256 $identityText
    $manifestFiles = @($orderedEntries | ForEach-Object { [ordered]@{ RelativePath=[string]$_.RelativePath; Sha256=[string]$_.Sha256; Length=[int64]$_.Length } })
    return [pscustomobject][ordered]@{
        BundleId=$bundleId; Entries=$orderedEntries; ExecutableIdentity=$executableIdentity
        Manifest=[ordered]@{
            SchemaVersion=1; BundleId=$bundleId; MinimumPowerShellVersion=$script:MinimumPowerShellVersion.ToString()
            EntryPoint='Invoke-SashimiHostOrchestrator.ps1'; ConfigFile='Config.json'; ExecutableIdentityFile=$script:ExecutableIdentityName
            Files=$manifestFiles
        }
    }
}

function Assert-InstallerBundle {
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][object]$ExpectedManifest,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid,
        [switch]$SkipAcl
    )
    $manifestPath = Join-Path $BundleRoot $script:ManifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Staged host manifest is missing: $manifestPath" }
    $actual = Read-InstallerJsonFile $manifestPath
    if ([int]$actual.SchemaVersion -ne 1 -or [string]$actual.BundleId -cne [string]$ExpectedManifest.BundleId -or [string]$actual.MinimumPowerShellVersion -cne $script:MinimumPowerShellVersion.ToString() -or [string]$actual.EntryPoint -cne 'Invoke-SashimiHostOrchestrator.ps1' -or [string]$actual.ConfigFile -cne 'Config.json' -or [string]$actual.ExecutableIdentityFile -cne $script:ExecutableIdentityName) { throw 'Staged host manifest identity does not match the planned immutable bundle.' }
    $expectedFiles = @($ExpectedManifest.Files | Sort-Object RelativePath); $actualFiles = @($actual.Files | Sort-Object RelativePath)
    if ($actualFiles.Count -ne $expectedFiles.Count) { throw 'Staged host manifest file count changed.' }
    $expectedNames = @($expectedFiles | ForEach-Object { [string]$_.RelativePath }) + @($script:ManifestName)
    $actualItems = @(Get-ChildItem -LiteralPath $BundleRoot -Force -ErrorAction Stop)
    if (@($actualItems | Where-Object { $_.PSIsContainer }).Count -ne 0 -or
        @($actualItems | Where-Object { -not $_.PSIsContainer }).Count -ne $expectedNames.Count -or
        @($actualItems | Where-Object { -not $_.PSIsContainer -and $expectedNames -cnotcontains $_.Name }).Count -ne 0) {
        throw 'Staged host bundle contains an unexpected or missing filesystem entry.'
    }
    for ($index=0; $index -lt $expectedFiles.Count; $index++) {
        $expected=$expectedFiles[$index]; $entry=$actualFiles[$index]
        if ([string]$entry.RelativePath -cne [string]$expected.RelativePath -or [string]$entry.Sha256 -cne [string]$expected.Sha256 -or [int64]$entry.Length -ne [int64]$expected.Length) { throw "Staged host manifest entry changed at index $index." }
        $path = Join-Path $BundleRoot ([string]$entry.RelativePath); $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [int64]$item.Length -ne [int64]$entry.Length -or (Get-InstallerFileSha256 $path) -cne [string]$entry.Sha256) { throw "Staged host file failed its exact hash/length check: $($entry.RelativePath)" }
        if (-not $SkipAcl) { Assert-InstallerProtectedAcl $path $UserSid }
    }
    $installedIdentity = Read-InstallerJsonFile (Join-Path $BundleRoot $script:ExecutableIdentityName)
    Assert-InstallerExecutableIdentity -Identity $installedIdentity
    $installedConfig = Read-InstallerJsonFile (Join-Path $BundleRoot 'Config.json')
    for ($index=0; $index -lt $script:ExecutableProperties.Count; $index++) {
        $name=$script:ExecutableProperties[$index]
        $configuredPath=ConvertTo-InstallerExecutablePath -Name $name -Path ([string]$installedConfig.$name)
        if (-not [string]::Equals($configuredPath,[string]$installedIdentity.Executables[$index].Path,[StringComparison]::OrdinalIgnoreCase)) {
            throw "$name differs between staged Config.json and ExecutableIdentity.json."
        }
    }
    if (-not $SkipAcl) {
        Assert-InstallerProtectedAcl $manifestPath $UserSid
        Assert-InstallerProtectedAcl $BundleRoot $UserSid
    }
}

function New-SashimiScheduledTaskXml {
    param(
        [Parameter(Mandatory = $true)][string]$UserId,
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$ConfigurationPath,
        [Parameter(Mandatory = $true)][string]$IntegrityManifestPath,
        [Parameter(Mandatory = $true)][DateTime]$Boundary
    )
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(ConvertTo-TaskArgument $ScriptPath),'-ConfigPath',(ConvertTo-TaskArgument $ConfigurationPath),'-IntegrityManifestPath',(ConvertTo-TaskArgument $IntegrityManifestPath)) -join ' '
    $boundaryText = $Boundary.ToString('yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture); $workingDirectory=Split-Path -Parent $ScriptPath; $author="$UserId via SASHIMI BOY Host Automation"
    $escapedUser=ConvertTo-XmlText $UserId; $escapedAuthor=ConvertTo-XmlText $author; $escapedExecutable=ConvertTo-XmlText $Executable; $escapedArguments=ConvertTo-XmlText $arguments; $escapedWorkingDirectory=ConvertTo-XmlText $workingDirectory
    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Author>$escapedAuthor</Author><Description>Runs one unattended SASHIMI BOY Developer or Reviewer host pipeline from an integrity-verified bundle.</Description></RegistrationInfo>
  <Triggers><CalendarTrigger><Repetition><Interval>PT15M</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>$boundaryText</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>
  <Principals><Principal id="Author"><UserId>$escapedUser</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>true</WakeToRun><ExecutionTimeLimit>PT12H</ExecutionTimeLimit><Priority>7</Priority>
  </Settings>
  <Actions Context="Author"><Exec><Command>$escapedExecutable</Command><Arguments>$escapedArguments</Arguments><WorkingDirectory>$escapedWorkingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@
}

$result = [ordered]@{
    Tool='Install-SashimiHostAutomation'; Success=$false; ExitCode=1; DryRun=[bool]$DryRun; Changed=$false; Staged=$false
    TaskName=$script:TaskName; UserId=$null; LogonType='InteractiveToken'; RunLevel='HighestAvailable'; MultipleInstances='IgnoreNew'; RepetitionInterval='PT15M'
    PowerShellPath=$script:PowerShellPath; MinimumPowerShellVersion=$script:MinimumPowerShellVersion.ToString(); DetectedPowerShellVersion=$null
    InstallRoot=$script:InstallRoot; BundleId=$null; BundleRoot=$null; IntegrityManifestPath=$null; ExecutableIdentityPath=$null
    SourceOrchestratorPath=$null; SourceConfigPath=$null; OrchestratorPath=$null; ConfigPath=$null
    BundleFiles=@(); BoundExecutableCount=0; AclPlan=$null; SourceHashesVerified=$false; AclVerified=$false; HashesVerified=$false; TaskXml=$null; Error=$null
}

try {
    Initialize-InstallerTrustedPowerShell
    $config=Import-InstallerConfig $ConfigPath
    $normalizedConfigPath=(Resolve-Path -LiteralPath $ConfigPath -ErrorAction Stop).ProviderPath; $normalizedOrchestratorPath=(Resolve-Path -LiteralPath $OrchestratorPath -ErrorAction Stop).ProviderPath
    if ((Split-Path -Leaf $normalizedOrchestratorPath) -cne 'Invoke-SashimiHostOrchestrator.ps1') { throw 'OrchestratorPath must name Invoke-SashimiHostOrchestrator.ps1 exactly.' }
    $sourceRoot=Split-Path -Parent $normalizedOrchestratorPath; $taskConfig=$config.Task
    if ([string]$taskConfig.Name -cne $script:TaskName -or [string]$taskConfig.User -cne $script:RequiredUserName -or [int]$taskConfig.IntervalMinutes -ne 15 -or -not [bool]$taskConfig.StartWhenAvailable -or -not [bool]$taskConfig.WakeToRun -or [string]$taskConfig.MultipleInstances -cne 'IgnoreNew') { throw 'Task configuration must retain the exact user, interval, availability, wake, and IgnoreNew contract.' }
    if ([string]$config.PowerShellExecutable -cne $script:PowerShellPath -or -not (Test-Path -LiteralPath $script:PowerShellPath -PathType Leaf)) { throw "PowerShellExecutable must exist at the stable path '$script:PowerShellPath'." }
    $plan=New-InstallerBundlePlan -SourceRoot $sourceRoot -SourceConfigPath $normalizedConfigPath -Config $config
    Assert-InstallerExecutableIdentity -Identity $plan.ExecutableIdentity
    $powerShellIdentity=@($plan.ExecutableIdentity.Executables | Where-Object { [string]$_.Name -ceq 'PowerShellExecutable' })[0]
    $detectedPowerShell=Get-StablePowerShellVersion -Executable $script:PowerShellPath -ExpectedLength ([int64]$powerShellIdentity.Length) -ExpectedSha256 ([string]$powerShellIdentity.Sha256); if ($detectedPowerShell -lt $script:MinimumPowerShellVersion) { throw "Stable PowerShell is $detectedPowerShell; version $script:MinimumPowerShellVersion or newer is required." }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent(); $userId=[string]$identity.Name; $accountName=($userId -split '\\')[-1]
    if ($accountName -cne $script:RequiredUserName) { throw "The task must be installed by Windows user '$script:RequiredUserName'; current identity is '$userId'." }; $userSid=$identity.User
    $bundleRoot=Join-Path $script:BundlesRoot $plan.BundleId; $stagedOrchestrator=Join-Path $bundleRoot 'Invoke-SashimiHostOrchestrator.ps1'; $stagedConfig=Join-Path $bundleRoot 'Config.json'; $manifestPath=Join-Path $bundleRoot $script:ManifestName; $executableIdentityPath=Join-Path $bundleRoot $script:ExecutableIdentityName
    if ($StartBoundary -eq [DateTime]::MinValue) { $StartBoundary=[DateTime]::Now.AddMinutes(1) }; if ($StartBoundary.Kind -eq [DateTimeKind]::Utc) { $StartBoundary=$StartBoundary.ToLocalTime() }
    $taskXml=New-SashimiScheduledTaskXml $userId $script:PowerShellPath $stagedOrchestrator $stagedConfig $manifestPath $StartBoundary
    try { [xml]$parsedTask=$taskXml; if ($null -eq $parsedTask.Task) { throw 'Task XML has no Task root.' } } catch { throw "Generated Task Scheduler XML is invalid: $($_.Exception.Message)" }
    $result.UserId=$userId; $result.DetectedPowerShellVersion=$detectedPowerShell.ToString(); $result.BundleId=$plan.BundleId; $result.BundleRoot=$bundleRoot; $result.IntegrityManifestPath=$manifestPath; $result.ExecutableIdentityPath=$executableIdentityPath; $result.BoundExecutableCount=@($plan.ExecutableIdentity.Executables).Count; $result.SourceHashesVerified=$true
    $result.SourceOrchestratorPath=Protect-InstallerText $normalizedOrchestratorPath; $result.SourceConfigPath=Protect-InstallerText $normalizedConfigPath; $result.OrchestratorPath=$stagedOrchestrator; $result.ConfigPath=$stagedConfig; $result.TaskXml=$taskXml; $result.BundleFiles=@($plan.Manifest.Files)
    $result.AclPlan=[ordered]@{ Inheritance='Disabled'; Owner='BUILTIN\Administrators'; Administrators='FullControl'; System='FullControl'; TaskUser="$userId ReadAndExecute" }
    $effectiveDryRun=[bool]$DryRun -or [bool]$WhatIfPreference; $result.DryRun=$effectiveDryRun
    $approved=-not $effectiveDryRun -and $PSCmdlet.ShouldProcess($script:TaskName,"Stage immutable host bundle $($plan.BundleId) and register or replace scheduled task")
    if ($approved) {
        foreach ($directory in @($script:InstallRoot,$script:BundlesRoot)) {
            if (Test-Path -LiteralPath $directory) {
                $directoryItem=Get-Item -LiteralPath $directory -Force -ErrorAction Stop
                if (-not $directoryItem.PSIsContainer -or ($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Protected install directory is not a plain directory: $directory" }
            }
            else { [IO.Directory]::CreateDirectory($directory)|Out-Null }
            Set-InstallerProtectedAcl $directory $userSid -Container; Assert-InstallerProtectedAcl $directory $userSid
        }
        $bundleAlreadyExists=Test-Path -LiteralPath $bundleRoot
        if ($bundleAlreadyExists) {
            $bundleItem=Get-Item -LiteralPath $bundleRoot -Force -ErrorAction Stop
            if (-not $bundleItem.PSIsContainer -or ($bundleItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Existing content-addressed bundle path is not a plain directory.' }
            Assert-InstallerBundle $bundleRoot $plan.Manifest $userSid -SkipAcl
        }
        else {
            [IO.Directory]::CreateDirectory($bundleRoot)|Out-Null
            foreach ($entry in $plan.Entries) {
                $destination=Join-Path $bundleRoot $entry.RelativePath
                if (-not [string]::IsNullOrWhiteSpace([string]$entry.SourcePath)) { Copy-Item -LiteralPath $entry.SourcePath -Destination $destination -ErrorAction Stop }
                elseif ($null -ne $entry.Bytes) { [IO.File]::WriteAllBytes($destination,[byte[]]$entry.Bytes) }
                else { [IO.File]::WriteAllText($destination,[string]$entry.Content,[Text.UTF8Encoding]::new($false)) }
            }
            [IO.File]::WriteAllText($manifestPath,(($plan.Manifest|ConvertTo-Json -Depth 16)+"`n"),[Text.UTF8Encoding]::new($false))
        }
        foreach ($file in @($plan.Manifest.Files|ForEach-Object{Join-Path $bundleRoot $_.RelativePath})+@($manifestPath)) { Set-InstallerProtectedAcl $file $userSid }
        Set-InstallerProtectedAcl $bundleRoot $userSid -Container; Assert-InstallerBundle $bundleRoot $plan.Manifest $userSid
        Assert-InstallerExecutableIdentity -Identity $plan.ExecutableIdentity
        $result.Staged=$true; $result.AclVerified=$true; $result.HashesVerified=$true
        Assert-InstallerTrustedPowerShellState -ScheduledTasks
        ScheduledTasks\Register-ScheduledTask -TaskName $script:TaskName -Xml $taskXml -Force -ErrorAction Stop|Out-Null; $result.Changed=$true
    }
    $result.Success=$true; $result.ExitCode=0
}
catch { $result.Error=Protect-InstallerText $_.Exception.Message }

[Console]::Out.WriteLine((ConvertTo-InstallerJson $result))
exit ([int]$result.ExitCode)
