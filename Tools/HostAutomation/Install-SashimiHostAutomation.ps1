#requires -Version 7.5

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ConfigPath,
    [ValidateNotNullOrEmpty()][string]$OrchestratorPath = ([IO.Path]::Combine($PSScriptRoot, 'Invoke-SashimiHostOrchestrator.ps1')),
    [DateTime]$StartBoundary = [DateTime]::MinValue,
    [ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedBundleId,
    [ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedInstallerSha256,
    [switch]$DryRun,
    [Parameter(DontShow = $true)][string]$InstallRootFixturePath,
    [Parameter(DontShow = $true)][string]$SchedulerFixturePath
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
$script:CodexDistributionsRoot = [IO.Path]::Combine($script:InstallRoot, 'CodexDistributions')
$script:InstallerSchedulerFixturePath = ''
$script:ManifestName = 'HostIntegrity.json'
$script:ExecutableIdentityName = 'ExecutableIdentity.json'
$script:StagingMarkerName = '.sashimi-installer-staging.json'
$script:InstallerBootstrapName = 'Install-SashimiHostAutomation.ps1'
$script:ExpectedRepository = 'DongGyunLeeeee/sashimi-boy-unity'
$script:ExpectedRemoteUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity.git'
$script:ExpectedGitAuthorName = 'DongGyunLeeeee'
$script:ExpectedGitAuthorEmail = '83210475+DongGyunLeeeee@users.noreply.github.com'
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

function Get-InstallerJsonObjectMap {
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
            throw "$Context contains duplicate or case-variant property '$name'."
        }
        if (-not $map.TryAdd($name, $property.Value.Clone())) {
            throw "$Context contains duplicate property '$name'."
        }
    }
    return ,$map
}

function Assert-InstallerExactJsonObject {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context,
        [Parameter(Mandatory = $true)][string[]]$PropertyNames
    )

    $map = Get-InstallerJsonObjectMap -Element $Element -Context $Context
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

function Assert-InstallerJsonKind {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][Text.Json.JsonValueKind]$Kind,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Element.ValueKind -ne $Kind) { throw "$Context must be JSON $($Kind.ToString().ToLowerInvariant())." }
}

function Assert-InstallerJsonInteger {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-InstallerJsonKind -Element $Element -Kind Number -Context $Context
    $integer = 0
    if (-not $Element.TryGetInt32([ref]$integer)) { throw "$Context must be a 32-bit JSON integer." }
}

function Assert-InstallerJsonStringArray {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-InstallerJsonKind -Element $Element -Kind Array -Context $Context
    $index = 0
    foreach ($value in $Element.EnumerateArray()) {
        Assert-InstallerJsonKind -Element $value -Kind String -Context "$Context[$index]"
        $index++
    }
}

function Assert-InstallerConfigJsonSchema {
    param([Parameter(Mandatory = $true)][string]$JsonText)

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
        $root = Assert-InstallerExactJsonObject -Element $document.RootElement -Context 'Config' -PropertyNames $rootNames
        foreach ($name in @('SchemaVersion','ProjectNumber','ArtifactRetentionDays')) { Assert-InstallerJsonInteger -Element $root[$name] -Context "Config.$name" }
        foreach ($name in @('Repository','ProjectOwner','DefaultBranch','RemoteUrl','RunRoot','GitExecutable','GitLfsExecutable','GitAuthorName','GitAuthorEmail','GitHubCli','CodexExecutable','PowerShellExecutable','UnityExecutable','ExpectedUnityVersion','MutexName')) {
            Assert-InstallerJsonKind -Element $root[$name] -Kind String -Context "Config.$name"
        }

        $task = Assert-InstallerExactJsonObject -Element $root['Task'] -Context 'Config.Task' -PropertyNames @('Name','User','IntervalMinutes','StartWhenAvailable','WakeToRun','MultipleInstances')
        foreach ($name in @('Name','User','MultipleInstances')) { Assert-InstallerJsonKind -Element $task[$name] -Kind String -Context "Config.Task.$name" }
        Assert-InstallerJsonInteger -Element $task['IntervalMinutes'] -Context 'Config.Task.IntervalMinutes'
        foreach ($name in @('StartWhenAvailable','WakeToRun')) {
            if ($task[$name].ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) { throw "Config.Task.$name must be JSON boolean." }
        }

        $timeouts = Assert-InstallerExactJsonObject -Element $root['Timeouts'] -Context 'Config.Timeouts' -PropertyNames @('CodexSeconds','GitSeconds','GitHubSeconds','UnityStageSeconds','GeneratorSeconds')
        foreach ($name in @($timeouts.Keys)) { Assert-InstallerJsonInteger -Element $timeouts[$name] -Context "Config.Timeouts.$name" }
        $retry = Assert-InstallerExactJsonObject -Element $root['Retry'] -Context 'Config.Retry' -PropertyNames @('MaximumAttempts','CooldownSeconds')
        foreach ($name in @($retry.Keys)) { Assert-InstallerJsonInteger -Element $retry[$name] -Context "Config.Retry.$name" }

        $security = Assert-InstallerExactJsonObject -Element $root['Security'] -Context 'Config.Security' -PropertyNames @('AuthorizedPrAuthors','CodexWorkspaceWriteNetworkAccess','ProtectedPathPatterns','ArtifactExclusionPatterns')
        foreach ($name in @('AuthorizedPrAuthors','ProtectedPathPatterns','ArtifactExclusionPatterns')) { Assert-InstallerJsonStringArray -Element $security[$name] -Context "Config.Security.$name" }
        if ($security['CodexWorkspaceWriteNetworkAccess'].ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) { throw 'Config.Security.CodexWorkspaceWriteNetworkAccess must be JSON boolean.' }

        $validationMap = Get-InstallerJsonObjectMap -Element $root['IssueValidations'] -Context 'Config.IssueValidations'
        foreach ($validationName in @($validationMap.Keys)) {
            if ($validationName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or
                $validationName -match '(?i)(?:token|secret|password|credential|proxy|endpoint|certificate|auth|codex.?home|base.?url)') {
                throw "Config.IssueValidations property '$validationName' has an invalid or secret-bearing identifier."
            }
            $definition = Assert-InstallerExactJsonObject -Element $validationMap[$validationName] -Context "Config.IssueValidations.$validationName" -PropertyNames @('IssueNumber','UnityExecuteMethod','Arguments','DeterminismPaths','ScreenshotPaths','PreviewPaths','AllowedProtectedPathPatterns')
            Assert-InstallerJsonInteger -Element $definition['IssueNumber'] -Context "Config.IssueValidations.$validationName.IssueNumber"
            Assert-InstallerJsonKind -Element $definition['UnityExecuteMethod'] -Kind String -Context "Config.IssueValidations.$validationName.UnityExecuteMethod"
            foreach ($name in @('Arguments','DeterminismPaths','ScreenshotPaths','PreviewPaths','AllowedProtectedPathPatterns')) {
                Assert-InstallerJsonStringArray -Element $definition[$name] -Context "Config.IssueValidations.$validationName.$name"
            }
        }
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function New-InstallerCanonicalConfigProjection {
    param([Parameter(Mandatory = $true)][object]$Config)

    $validations = [ordered]@{}
    $validationEntries = if ($Config.IssueValidations -is [Collections.IDictionary]) {
        @($Config.IssueValidations.Keys | Sort-Object | ForEach-Object {
                [pscustomobject]@{ Name=[string]$_; Value=$Config.IssueValidations[$_] }
            })
    }
    else { @($Config.IssueValidations.PSObject.Properties | Sort-Object Name) }
    foreach ($property in $validationEntries) {
        $definition = $property.Value
        $validations[[string]$property.Name] = [ordered]@{
            IssueNumber = [int]$definition.IssueNumber
            UnityExecuteMethod = [string]$definition.UnityExecuteMethod
            Arguments = @($definition.Arguments | ForEach-Object { [string]$_ })
            DeterminismPaths = @($definition.DeterminismPaths | ForEach-Object { [string]$_ })
            ScreenshotPaths = @($definition.ScreenshotPaths | ForEach-Object { [string]$_ })
            PreviewPaths = @($definition.PreviewPaths | ForEach-Object { [string]$_ })
            AllowedProtectedPathPatterns = @($definition.AllowedProtectedPathPatterns | ForEach-Object { [string]$_ })
        }
    }
    return [pscustomobject][ordered]@{
        SchemaVersion = [int]$Config.SchemaVersion
        Repository = [string]$Config.Repository
        ProjectOwner = [string]$Config.ProjectOwner
        ProjectNumber = [int]$Config.ProjectNumber
        DefaultBranch = [string]$Config.DefaultBranch
        RemoteUrl = [string]$Config.RemoteUrl
        RunRoot = [string]$Config.RunRoot
        ArtifactRetentionDays = [int]$Config.ArtifactRetentionDays
        GitExecutable = [string]$Config.GitExecutable
        GitLfsExecutable = [string]$Config.GitLfsExecutable
        GitAuthorName = [string]$Config.GitAuthorName
        GitAuthorEmail = [string]$Config.GitAuthorEmail
        GitHubCli = [string]$Config.GitHubCli
        CodexExecutable = [string]$Config.CodexExecutable
        PowerShellExecutable = [string]$Config.PowerShellExecutable
        UnityExecutable = [string]$Config.UnityExecutable
        ExpectedUnityVersion = [string]$Config.ExpectedUnityVersion
        MutexName = [string]$Config.MutexName
        Task = [ordered]@{
            Name = [string]$Config.Task.Name
            User = [string]$Config.Task.User
            IntervalMinutes = [int]$Config.Task.IntervalMinutes
            StartWhenAvailable = [bool]$Config.Task.StartWhenAvailable
            WakeToRun = [bool]$Config.Task.WakeToRun
            MultipleInstances = [string]$Config.Task.MultipleInstances
        }
        Timeouts = [ordered]@{
            CodexSeconds = [int]$Config.Timeouts.CodexSeconds
            GitSeconds = [int]$Config.Timeouts.GitSeconds
            GitHubSeconds = [int]$Config.Timeouts.GitHubSeconds
            UnityStageSeconds = [int]$Config.Timeouts.UnityStageSeconds
            GeneratorSeconds = [int]$Config.Timeouts.GeneratorSeconds
        }
        Retry = [ordered]@{
            MaximumAttempts = [int]$Config.Retry.MaximumAttempts
            CooldownSeconds = [int]$Config.Retry.CooldownSeconds
        }
        Security = [ordered]@{
            AuthorizedPrAuthors = @($Config.Security.AuthorizedPrAuthors | ForEach-Object { [string]$_ })
            CodexWorkspaceWriteNetworkAccess = [bool]$Config.Security.CodexWorkspaceWriteNetworkAccess
            ProtectedPathPatterns = @($Config.Security.ProtectedPathPatterns | ForEach-Object { [string]$_ })
            ArtifactExclusionPatterns = @($Config.Security.ArtifactExclusionPatterns | ForEach-Object { [string]$_ })
        }
        IssueValidations = $validations
    }
}

function Import-InstallerConfig {
    param([Parameter(Mandatory = $true)][string]$Path)

    # The installer may be elevated. Configuration is parsed and validated as
    # data using installer-local code; no writable source-tree module is ever
    # dot-sourced into the elevated process.
    $normalizedPath = ConvertTo-InstallerPath -Path $Path
    Assert-InstallerNoReparsePoint $normalizedPath
    try {
        $configBytes = [IO.File]::ReadAllBytes($normalizedPath)
        $configText = [Text.UTF8Encoding]::new($false,$true).GetString($configBytes)
        Assert-InstallerConfigJsonSchema -JsonText $configText
        $config = $configText | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch { throw "Invalid strict-schema UTF-8 configuration '$normalizedPath': $($_.Exception.Message)" }
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
    if ([string]$config.ExpectedUnityVersion -cne '6000.4.0f1') { throw "ExpectedUnityVersion must be exactly '6000.4.0f1'." }
    if ([string]$config.GitAuthorName -cne $script:ExpectedGitAuthorName -or
        [string]$config.GitAuthorEmail -cne $script:ExpectedGitAuthorEmail) {
        throw 'GitAuthorName and GitAuthorEmail must equal the immutable repository-owner identity.'
    }
    if ([int]$config.Task.IntervalMinutes -ne 15 -or [string]$config.Task.Name -cne $script:TaskName -or
        [string]$config.Task.User -cne $script:RequiredUserName -or -not [bool]$config.Task.StartWhenAvailable -or
        -not [bool]$config.Task.WakeToRun -or [string]$config.Task.MultipleInstances -cne 'IgnoreNew') {
        throw 'Task configuration must retain the exact identity, name, interval, availability, wake, and IgnoreNew contract.'
    }
    foreach ($timeoutName in @('CodexSeconds','GitSeconds','GitHubSeconds','UnityStageSeconds','GeneratorSeconds')) {
        $timeoutValue = [int]$config.Timeouts.$timeoutName
        if ($timeoutValue -lt 1 -or $timeoutValue -gt 86400) { throw "Timeouts.$timeoutName must be between 1 and 86400 seconds." }
    }
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
    if ([bool]$config.Security.CodexWorkspaceWriteNetworkAccess) { throw 'Security.CodexWorkspaceWriteNetworkAccess must remain false.' }
    foreach ($validationProperty in @($config.IssueValidations.PSObject.Properties)) {
        $definition = $validationProperty.Value
        if ([int]$definition.IssueNumber -lt 1 -or [string]$definition.UnityExecuteMethod -cnotmatch '^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+$') {
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
    return New-InstallerCanonicalConfigProjection -Config $config
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

function Get-InstallerHarnessFixtureLocation {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (-not (Test-InstallerHarnessMode)) { throw "$Purpose is available only to the owned installer test harness." }
    $fullPath = ConvertTo-InstallerPath -Path $Path -AllowMissing
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "$Purpose must remain below the Windows temporary root." }
    $relative = $fullPath.Substring($temporaryRoot.Length)
    $separatorIndex = $relative.IndexOf([IO.Path]::DirectorySeparatorChar)
    if ($separatorIndex -lt 1) { throw "$Purpose must be inside an owned host-test directory." }
    $testRootLeaf = $relative.Substring(0, $separatorIndex)
    if ($testRootLeaf -cnotmatch '^SashimiBoyHostTests-[0-9a-f]{32}$') { throw "$Purpose is outside an owned host-test directory." }
    $testRoot = Join-Path ($temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)) $testRootLeaf
    $markerPath = Join-Path $testRoot '.host-tests-owner.json'
    if (-not (Test-Path -LiteralPath $testRoot -PathType Container) -or -not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "$Purpose lacks the owned host-test root and marker."
    }
    Assert-InstallerNoReparsePoint $testRoot
    Assert-InstallerNoReparsePoint $markerPath
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "$Purpose parent must already exist inside the owned host-test root." }
    Assert-InstallerNoReparsePoint $parent
    return [pscustomobject][ordered]@{ Path=$fullPath; TestRoot=$testRoot }
}

function Initialize-InstallerHarnessBoundaries {
    if ([string]::IsNullOrWhiteSpace($InstallRootFixturePath) -and [string]::IsNullOrWhiteSpace($SchedulerFixturePath)) { return }
    if (-not (Test-InstallerHarnessMode)) { throw 'Installer boundary injection is forbidden outside the owned test harness.' }
    if ([string]::IsNullOrWhiteSpace($InstallRootFixturePath) -or [string]::IsNullOrWhiteSpace($SchedulerFixturePath)) {
        throw 'Installer harness injection requires both InstallRootFixturePath and SchedulerFixturePath.'
    }
    $installLocation = Get-InstallerHarnessFixtureLocation -Path $InstallRootFixturePath -Purpose 'InstallRootFixturePath'
    $schedulerLocation = Get-InstallerHarnessFixtureLocation -Path $SchedulerFixturePath -Purpose 'SchedulerFixturePath'
    if (-not [string]::Equals([string]$installLocation.TestRoot, [string]$schedulerLocation.TestRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installer harness boundaries must share one owned host-test root.'
    }
    if (Test-Path -LiteralPath $installLocation.Path) {
        $installItem = Get-Item -LiteralPath $installLocation.Path -Force -ErrorAction Stop
        if (-not $installItem.PSIsContainer -or ($installItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'InstallRootFixturePath must be absent or an existing plain directory.'
        }
        Assert-InstallerNoReparsePoint $installItem.FullName
    }
    $script:InstallRoot = [string]$installLocation.Path
    $script:BundlesRoot = Join-Path $script:InstallRoot 'Bundles'
    $script:CodexDistributionsRoot = Join-Path $script:InstallRoot 'CodexDistributions'
    $script:InstallerSchedulerFixturePath = [string]$schedulerLocation.Path
}

function Invoke-InstallerSchedulerBoundary {
    param(
        [Parameter(Mandatory = $true)][string]$TaskName,
        [Parameter(Mandatory = $true)][string]$Xml,
        [switch]$DryRun
    )

    if (-not [string]::IsNullOrWhiteSpace([string]$script:InstallerSchedulerFixturePath)) {
        $fixtureLocation = Get-InstallerHarnessFixtureLocation -Path ([string]$script:InstallerSchedulerFixturePath) -Purpose 'SchedulerFixturePath'
        $record = [ordered]@{
            SchemaVersion=1; Operation='Register-ScheduledTask'; DryRun=[bool]$DryRun; TaskName=$TaskName
            XmlSha256=Get-InstallerTextSha256 -Text $Xml
        }
        [IO.File]::AppendAllText($fixtureLocation.Path, ((ConvertTo-InstallerJson $record) + "`n"), [Text.UTF8Encoding]::new($false))
        return [pscustomobject][ordered]@{ Invoked=$true; Registered=(-not [bool]$DryRun); Fixture=$true }
    }
    if ($DryRun) { return [pscustomobject][ordered]@{ Invoked=$true; Registered=$false; Fixture=$false } }
    Assert-InstallerTrustedPowerShellState -ScheduledTasks
    ScheduledTasks\Register-ScheduledTask -TaskName $TaskName -Xml $Xml -Force -ErrorAction Stop | Out-Null
    return [pscustomobject][ordered]@{ Invoked=$true; Registered=$true; Fixture=$false }
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
    Assert-InstallerNoReparsePoint $item.FullName
    return [ordered]@{ Name=$Name; Path=$item.FullName; Length=[int64]$item.Length; Sha256=Get-InstallerFileSha256 $item.FullName }
}

function Get-InstallerSourceExecutableSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path) -or $Path -cnotmatch '^[A-Za-z]:\\') {
        throw "$Name source must be an absolute local Windows executable path."
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($Path, $fullPath, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($fullPath) -cne '.exe') {
        throw "$Name source must be a canonical .exe path."
    }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name source must resolve to a plain executable file."
    }
    $stream = [IO.File]::Open($fullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt [int]::MaxValue) { throw "$Name source length is outside the supported immutable snapshot bounds." }
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes)
        if ($stream.Position -ne $stream.Length) { throw "$Name source could not be captured as one complete immutable snapshot." }
    }
    finally { $stream.Dispose() }
    $sha256 = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
    return [pscustomobject][ordered]@{ Name=$Name; Path=$fullPath; Length=[int64]$bytes.LongLength; Sha256=$sha256; Bytes=$bytes }
}

function New-InstallerExecutableIdentity {
    param(
        [Parameter(Mandatory = $true)][object]$Config,
        [hashtable]$SourceEntries = @{}
    )
    $entries = foreach ($name in $script:ExecutableProperties) {
        $property = $Config.PSObject.Properties[$name]
        if ($null -eq $property) { throw "Configuration is missing executable property '$name'." }
        $identityPath = ConvertTo-InstallerExecutablePath -Name $name -Path ([string]$property.Value)
        $sourceEntry = if ($SourceEntries.ContainsKey($name)) { $SourceEntries[$name] } else { Get-InstallerExecutableIdentityEntry -Name $name -Path $identityPath }
        if ([string]$sourceEntry.Name -cne $name -or [int64]$sourceEntry.Length -lt 1 -or [string]$sourceEntry.Sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "$name source snapshot is invalid."
        }
        [ordered]@{ Name=$name; Path=$identityPath; Length=[int64]$sourceEntry.Length; Sha256=[string]$sourceEntry.Sha256 }
    }
    return [ordered]@{ SchemaVersion=1; Executables=@($entries) }
}

function Assert-InstallerExecutableIdentity {
    param(
        [Parameter(Mandatory = $true)][object]$Identity,
        [hashtable]$SourceEntries = @{}
    )
    if ([int]$Identity.SchemaVersion -ne 1) { throw 'Executable identity SchemaVersion must be 1.' }
    $entries = @($Identity.Executables)
    if ($entries.Count -ne $script:ExecutableProperties.Count) { throw 'Executable identity must contain exactly the configured bound tools.' }
    for ($index=0; $index -lt $script:ExecutableProperties.Count; $index++) {
        $expectedName=$script:ExecutableProperties[$index]; $entry=$entries[$index]
        if ([string]$entry.Name -cne $expectedName -or [string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$entry.Length -lt 1) {
            throw "Executable identity entry is invalid at index $index."
        }
        $identityPath=ConvertTo-InstallerExecutablePath -Name $expectedName -Path ([string]$entry.Path)
        $current=if ($SourceEntries.ContainsKey($expectedName)) { $SourceEntries[$expectedName] } else { Get-InstallerExecutableIdentityEntry -Name $expectedName -Path $identityPath }
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
    $rulesBySid = @{}
    $expectedInheritance = if ($item.PSIsContainer) {
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    }
    else { [Security.AccessControl.InheritanceFlags]::None }
    $expectedUserRights = [Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize
    $expectedFullControl = [Security.AccessControl.FileSystemRights]::FullControl
    foreach ($rule in @($acl.Access)) {
        $sid = Get-InstallerSidValue $rule.IdentityReference
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $allowedSids -cnotcontains $sid -or $rule.IsInherited) {
            throw "Protected host ACL contains an unexpected, denied, or inherited access rule for '$sid': $Path"
        }
        if ($rulesBySid.ContainsKey($sid)) { throw "Protected host ACL contains duplicate access rules for '$sid': $Path" }
        if ($rule.InheritanceFlags -ne $expectedInheritance -or $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) {
            throw "Protected host ACL contains incorrect inheritance flags for '$sid': $Path"
        }
        $rulesBySid[$sid] = $rule
    }
    if ($rulesBySid.Count -ne 3 -or @($allowedSids | Where-Object { -not $rulesBySid.ContainsKey($_) }).Count -ne 0) {
        throw "Protected host ACL is missing a required exact access rule: $Path"
    }
    if ($rulesBySid[$UserSid.Value].FileSystemRights -ne $expectedUserRights) {
        throw "Task user must have exactly ReadAndExecute (plus the Windows Synchronize bit) on protected host path: $Path"
    }
    foreach ($sid in @($administrators,$system)) {
        if ($rulesBySid[$sid].FileSystemRights -ne $expectedFullControl) { throw "Protected host ACL does not grant exact FullControl to '$sid': $Path" }
    }
}

function Assert-InstallerPlainDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Installer directory is not a plain directory: $Path"
    }
    Assert-InstallerNoReparsePoint $item.FullName
    return $item.FullName
}

function Assert-InstallerTreeHasNoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootPath = Assert-InstallerPlainDirectory $Root
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($rootPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Installer-owned staging content contains a reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}

function New-InstallerStagingWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$ParentRoot,
        [Parameter(Mandatory = $true)][ValidateSet('Bundle','CodexDistribution')][string]$Purpose,
        [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$Identity,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )

    $parentPath = Assert-InstallerPlainDirectory $ParentRoot
    $leaf = '.sashimi-stage-' + [Guid]::NewGuid().ToString('N')
    $workspace = Join-Path $parentPath $leaf
    $payload = Join-Path $workspace 'Payload'
    $markerPath = Join-Path $workspace $script:StagingMarkerName
    $markerWritten = $false
    try {
        [IO.Directory]::CreateDirectory($workspace) | Out-Null
        Set-InstallerProtectedAcl $workspace $UserSid -Container
        $marker = [ordered]@{
            SchemaVersion=1; Owner='SashimiBoyHostInstaller'; Purpose=$Purpose
            Identity=$Identity; WorkspaceLeaf=$leaf
        }
        [IO.File]::WriteAllText($markerPath, ((ConvertTo-InstallerJson $marker) + "`n"), [Text.UTF8Encoding]::new($false))
        $markerWritten = $true
        Set-InstallerProtectedAcl $markerPath $UserSid
        [IO.Directory]::CreateDirectory($payload) | Out-Null
        Set-InstallerProtectedAcl $payload $UserSid -Container
        return [pscustomobject][ordered]@{
            ParentRoot=$parentPath; Workspace=$workspace; Payload=$payload; MarkerPath=$markerPath
            Purpose=$Purpose; Identity=$Identity; WorkspaceLeaf=$leaf
        }
    }
    catch {
        $failure = $_
        if (Test-Path -LiteralPath $workspace -PathType Container) {
            if ($markerWritten) {
                try { Remove-InstallerStagingWorkspace -Workspace $workspace -ParentRoot $parentPath -Purpose $Purpose -Identity $Identity } catch { }
            }
            else {
                try {
                    $createdItem = Get-Item -LiteralPath $workspace -Force -ErrorAction Stop
                    if ($createdItem.PSIsContainer -and ($createdItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
                        @(Get-ChildItem -LiteralPath $workspace -Force -ErrorAction Stop).Count -eq 0) {
                        [IO.Directory]::Delete($workspace, $false)
                    }
                }
                catch { }
            }
        }
        throw $failure
    }
}

function Remove-InstallerStagingWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$Workspace,
        [Parameter(Mandatory = $true)][string]$ParentRoot,
        [Parameter(Mandatory = $true)][ValidateSet('Bundle','CodexDistribution')][string]$Purpose,
        [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$Identity
    )

    $workspacePath = [IO.Path]::GetFullPath($Workspace)
    $parentPath = [IO.Path]::GetFullPath($ParentRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($workspacePath), $parentPath, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($workspacePath) -cnotmatch '^\.sashimi-stage-[0-9a-f]{32}$') {
        throw 'Refusing to remove a staging workspace outside its exact protected sibling root.'
    }
    Assert-InstallerTreeHasNoReparsePoint $workspacePath
    $markerPath = Join-Path $workspacePath $script:StagingMarkerName
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Refusing to remove an unmarked staging workspace.' }
    $marker = Read-InstallerJsonFile $markerPath
    $markerProperties = @($marker.PSObject.Properties | ForEach-Object Name)
    $expectedProperties = @('SchemaVersion','Owner','Purpose','Identity','WorkspaceLeaf')
    if ($markerProperties.Count -ne $expectedProperties.Count -or @($markerProperties | Where-Object { $expectedProperties -cnotcontains $_ }).Count -ne 0 -or
        [int]$marker.SchemaVersion -ne 1 -or [string]$marker.Owner -cne 'SashimiBoyHostInstaller' -or
        [string]$marker.Purpose -cne $Purpose -or [string]$marker.Identity -cne $Identity -or
        [string]$marker.WorkspaceLeaf -cne [IO.Path]::GetFileName($workspacePath)) {
        throw 'Refusing to remove a staging workspace whose ownership marker does not match.'
    }
    [IO.Directory]::Delete($workspacePath, $true)
}

function Assert-InstallerCodexDistribution {
    param(
        [Parameter(Mandatory = $true)][object]$Distribution,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid,
        [switch]$SkipAcl
    )

    $distributionRoot = Assert-InstallerPlainDirectory ([string]$Distribution.Root)
    $expectedPath = [IO.Path]::GetFullPath([string]$Distribution.Path)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($expectedPath), $distributionRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($expectedPath) -cne 'codex.exe') {
        throw 'Protected Codex distribution path is not its exact content-addressed location.'
    }
    $items = @(Get-ChildItem -LiteralPath $distributionRoot -Force -ErrorAction Stop)
    if ($items.Count -ne 1 -or $items[0].PSIsContainer -or $items[0].Name -cne 'codex.exe' -or
        ($items[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        [int64]$items[0].Length -ne [int64]$Distribution.Length -or
        (Get-InstallerFileSha256 $items[0].FullName) -cne [string]$Distribution.Sha256) {
        throw 'Protected Codex distribution failed its exact path, entry, hash, or length check.'
    }
    Assert-InstallerNoReparsePoint $items[0].FullName
    if (-not $SkipAcl) {
        Assert-InstallerProtectedAcl $items[0].FullName $UserSid
        Assert-InstallerProtectedAcl $distributionRoot $UserSid
    }
}

function Install-InstallerCodexDistribution {
    param(
        [Parameter(Mandatory = $true)][object]$Distribution,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )

    if (Test-Path -LiteralPath ([string]$Distribution.Root)) {
        Assert-InstallerCodexDistribution $Distribution $UserSid -SkipAcl
        Set-InstallerProtectedAcl ([string]$Distribution.Path) $UserSid
        Set-InstallerProtectedAcl ([string]$Distribution.Root) $UserSid -Container
        Assert-InstallerCodexDistribution $Distribution $UserSid
        return $false
    }

    $stage = New-InstallerStagingWorkspace -ParentRoot $script:CodexDistributionsRoot -Purpose CodexDistribution -Identity ([string]$Distribution.Sha256) -UserSid $UserSid
    $promoted = $false
    try {
        $stagedExecutable = Join-Path $stage.Payload 'codex.exe'
        [IO.File]::WriteAllBytes($stagedExecutable, [byte[]]$Distribution.Bytes)
        if ([int64](Get-Item -LiteralPath $stagedExecutable -Force -ErrorAction Stop).Length -ne [int64]$Distribution.Length -or
            (Get-InstallerFileSha256 $stagedExecutable) -cne [string]$Distribution.Sha256) {
            throw 'Staged Codex distribution bytes changed before promotion.'
        }
        Set-InstallerProtectedAcl $stagedExecutable $UserSid
        Set-InstallerProtectedAcl $stage.Payload $UserSid -Container
        $stagedDistribution = [pscustomobject][ordered]@{
            Root=$stage.Payload; Path=$stagedExecutable; Sha256=[string]$Distribution.Sha256; Length=[int64]$Distribution.Length
        }
        Assert-InstallerCodexDistribution $stagedDistribution $UserSid
        if (Test-Path -LiteralPath ([string]$Distribution.Root)) { throw 'Protected Codex distribution target appeared before atomic promotion.' }
        [IO.Directory]::Move($stage.Payload, [string]$Distribution.Root)
        $promoted = $true
        Assert-InstallerCodexDistribution $Distribution $UserSid
        return $true
    }
    catch {
        $failure = $_
        if ($promoted -and (Test-Path -LiteralPath ([string]$Distribution.Root))) {
            try {
                Assert-InstallerCodexDistribution $Distribution $UserSid -SkipAcl
                if (-not (Test-Path -LiteralPath $stage.Payload)) {
                    [IO.Directory]::Move([string]$Distribution.Root, $stage.Payload)
                    $promoted = $false
                }
            }
            catch { }
        }
        throw $failure
    }
    finally {
        if (Test-Path -LiteralPath $stage.Workspace -PathType Container) {
            Remove-InstallerStagingWorkspace -Workspace $stage.Workspace -ParentRoot $stage.ParentRoot -Purpose CodexDistribution -Identity ([string]$Distribution.Sha256)
        }
    }
}

function New-InstallerBundlePlan {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$SourceConfigPath,
        [Parameter(Mandatory = $true)][object]$Config,
        [Parameter(Mandatory = $true)][string]$InstallerBootstrapPath
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
        Assert-InstallerConfigJsonSchema -JsonText $configText
        $snapshotConfig=$configText | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch { throw "Configuration snapshot is not valid strict-schema UTF-8 JSON: $($_.Exception.Message)" }
    $snapshotConfig.RunRoot=ConvertTo-InstallerPath -Path ([string]$snapshotConfig.RunRoot) -AllowMissing
    foreach ($name in $script:ExecutableProperties) {
        $snapshotConfig.$name=ConvertTo-InstallerExecutablePath -Name $name -Path ([string]$snapshotConfig.$name)
    }
    $snapshotConfig = New-InstallerCanonicalConfigProjection -Config $snapshotConfig
    if ((ConvertTo-InstallerJson $snapshotConfig) -cne (ConvertTo-InstallerJson $Config)) {
        throw 'Configuration changed while the installer captured its immutable bundle snapshot.'
    }

    $sourceConfigHash=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($configBytes))).ToLowerInvariant()
    $sourceCodexPath = [string]$Config.CodexExecutable
    $sourceCodexEntry = Get-InstallerSourceExecutableSnapshot -Name 'CodexExecutable' -Path $sourceCodexPath
    $sourceCodexBytes = [byte[]]$sourceCodexEntry.Bytes
    $sourceCodexHash = [string]$sourceCodexEntry.Sha256
    $codexDistributionRoot = Join-Path $script:CodexDistributionsRoot $sourceCodexHash
    $codexDistributionPath = Join-Path $codexDistributionRoot 'codex.exe'
    $projectedConfig = New-InstallerCanonicalConfigProjection -Config $Config
    $projectedConfig.CodexExecutable = $codexDistributionPath
    $configContent = (ConvertTo-InstallerJson $projectedConfig) + "`n"
    $projectedConfigBytes = [Text.UTF8Encoding]::new($false).GetBytes($configContent)
    $projectedConfigHash = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectedConfigBytes))).ToLowerInvariant()
    $entries.Add([pscustomobject][ordered]@{
            RelativePath='Config.json'; SourcePath=''; Content=$configContent; Bytes=$null
            Sha256=$projectedConfigHash; Length=[int64]$projectedConfigBytes.LongLength
        })

    $identitySourceEntries = @{ CodexExecutable=$sourceCodexEntry }
    $executableIdentity = New-InstallerExecutableIdentity -Config $projectedConfig -SourceEntries $identitySourceEntries
    Assert-InstallerExecutableIdentity -Identity $executableIdentity -SourceEntries $identitySourceEntries
    $identityContent = ($executableIdentity | ConvertTo-Json -Depth 8 -Compress) + "`n"
    $identityBytes = [Text.UTF8Encoding]::new($false).GetBytes($identityContent)
    $entries.Add([pscustomobject][ordered]@{
            RelativePath=$script:ExecutableIdentityName; SourcePath=''; Content=$identityContent; Bytes=$null
            Sha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identityBytes))).ToLowerInvariant()
            Length=[int64]$identityBytes.LongLength
        })

    $bootstrapItem = Get-Item -LiteralPath $InstallerBootstrapPath -Force -ErrorAction Stop
    if ($bootstrapItem.PSIsContainer -or $bootstrapItem.Name -cne $script:InstallerBootstrapName -or
        ($bootstrapItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Installer bootstrap must be the exact non-reparse Install-SashimiHostAutomation.ps1 file.'
    }
    Assert-InstallerNoReparsePoint $bootstrapItem.FullName
    $bootstrapBytes = [IO.File]::ReadAllBytes($bootstrapItem.FullName)
    $bootstrapHash = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bootstrapBytes))).ToLowerInvariant()
    $orderedEntries = @($entries | Sort-Object RelativePath)
    $identityLines = @(
        "installer-bootstrap`0$bootstrapHash`0$($bootstrapBytes.LongLength)",
        "source-config`0$sourceConfigHash`0$($configBytes.LongLength)",
        "source-codex`0$sourceCodexHash`0$($sourceCodexBytes.LongLength)"
    ) + @($orderedEntries | ForEach-Object { "$($_.RelativePath)`0$($_.Sha256)`0$($_.Length)" })
    $identityText = [string]::Join("`n", $identityLines)
    $bundleId = Get-InstallerTextSha256 $identityText
    $manifestFiles = @($orderedEntries | ForEach-Object { [ordered]@{ RelativePath=[string]$_.RelativePath; Sha256=[string]$_.Sha256; Length=[int64]$_.Length } })
    $manifest = [ordered]@{
        SchemaVersion=1; BundleId=$bundleId; MinimumPowerShellVersion=$script:MinimumPowerShellVersion.ToString()
        EntryPoint='Invoke-SashimiHostOrchestrator.ps1'; ConfigFile='Config.json'; ExecutableIdentityFile=$script:ExecutableIdentityName
        InstallerBootstrap=[ordered]@{ Sha256=$bootstrapHash; Length=[int64]$bootstrapBytes.LongLength }
        SourceConfig=[ordered]@{ Sha256=$sourceConfigHash; Length=[int64]$configBytes.LongLength }
        CodexDistribution=[ordered]@{ Sha256=$sourceCodexHash; Length=[int64]$sourceCodexBytes.LongLength; FileName='codex.exe' }
        Files=$manifestFiles
    }
    $manifestContent = (($manifest | ConvertTo-Json -Depth 16 -Compress) + "`n")
    $manifestSha256 = Get-InstallerTextSha256 -Text $manifestContent
    return [pscustomobject][ordered]@{
        BundleId=$bundleId; Entries=$orderedEntries; ExecutableIdentity=$executableIdentity; ExecutableIdentitySourceEntries=$identitySourceEntries
        Config=$projectedConfig; Manifest=$manifest; ManifestContent=$manifestContent; ManifestSha256=$manifestSha256
        InstallerBootstrap=[pscustomobject][ordered]@{ Sha256=$bootstrapHash; Length=[int64]$bootstrapBytes.LongLength }
        SourceConfig=[pscustomobject][ordered]@{ Sha256=$sourceConfigHash; Length=[int64]$configBytes.LongLength }
        CodexDistribution=[pscustomobject][ordered]@{
            Root=$codexDistributionRoot; Path=$codexDistributionPath; Bytes=$sourceCodexBytes
            Sha256=$sourceCodexHash; Length=[int64]$sourceCodexBytes.LongLength
        }
    }
}

function Assert-InstallerBundle {
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][object]$ExpectedManifest,
        [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedManifestSha256,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid,
        [switch]$SkipAcl
    )
    [void](Assert-InstallerPlainDirectory $BundleRoot)
    $manifestPath = Join-Path $BundleRoot $script:ManifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Staged host manifest is missing: $manifestPath" }
    if ((Get-InstallerFileSha256 $manifestPath) -cne $ExpectedManifestSha256) {
        throw 'Staged host manifest bytes do not match the exact reviewed manifest digest.'
    }
    $actual = Read-InstallerJsonFile $manifestPath
    if ([int]$actual.SchemaVersion -ne 1 -or [string]$actual.BundleId -cne [string]$ExpectedManifest.BundleId -or [string]$actual.MinimumPowerShellVersion -cne $script:MinimumPowerShellVersion.ToString() -or [string]$actual.EntryPoint -cne 'Invoke-SashimiHostOrchestrator.ps1' -or [string]$actual.ConfigFile -cne 'Config.json' -or [string]$actual.ExecutableIdentityFile -cne $script:ExecutableIdentityName) { throw 'Staged host manifest identity does not match the planned immutable bundle.' }
    foreach ($metadataName in @('InstallerBootstrap','SourceConfig','CodexDistribution')) {
        $expectedMetadata = $ExpectedManifest.$metadataName
        $actualMetadata = $actual.$metadataName
        if ($null -eq $actualMetadata -or [string]$actualMetadata.Sha256 -cne [string]$expectedMetadata.Sha256 -or
            [int64]$actualMetadata.Length -ne [int64]$expectedMetadata.Length) {
            throw "Staged host manifest provenance changed for '$metadataName'."
        }
    }
    if ([string]$actual.CodexDistribution.FileName -cne 'codex.exe') { throw 'Staged host manifest has an invalid Codex distribution filename.' }
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

function Install-InstallerBundle {
    param(
        [Parameter(Mandatory = $true)][object]$Plan,
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )

    if (Test-Path -LiteralPath $BundleRoot) {
        Assert-InstallerBundle $BundleRoot $Plan.Manifest $Plan.ManifestSha256 $UserSid -SkipAcl
        foreach ($file in @($Plan.Manifest.Files | ForEach-Object { Join-Path $BundleRoot $_.RelativePath }) + @(Join-Path $BundleRoot $script:ManifestName)) {
            Set-InstallerProtectedAcl $file $UserSid
        }
        Set-InstallerProtectedAcl $BundleRoot $UserSid -Container
        Assert-InstallerBundle $BundleRoot $Plan.Manifest $Plan.ManifestSha256 $UserSid
        return $false
    }

    $stage = New-InstallerStagingWorkspace -ParentRoot $script:BundlesRoot -Purpose Bundle -Identity ([string]$Plan.BundleId) -UserSid $UserSid
    $promoted = $false
    try {
        foreach ($entry in $Plan.Entries) {
            $destination = Join-Path $stage.Payload ([string]$entry.RelativePath)
            if ($null -ne $entry.Bytes) {
                [IO.File]::WriteAllBytes($destination, [byte[]]$entry.Bytes)
            }
            else {
                [IO.File]::WriteAllText($destination, [string]$entry.Content, [Text.UTF8Encoding]::new($false))
            }
        }
        $stagedManifestPath = Join-Path $stage.Payload $script:ManifestName
        [IO.File]::WriteAllText($stagedManifestPath, [string]$Plan.ManifestContent, [Text.UTF8Encoding]::new($false))
        foreach ($file in @($Plan.Manifest.Files | ForEach-Object { Join-Path $stage.Payload $_.RelativePath }) + @($stagedManifestPath)) {
            Set-InstallerProtectedAcl $file $UserSid
        }
        Set-InstallerProtectedAcl $stage.Payload $UserSid -Container
        Assert-InstallerBundle $stage.Payload $Plan.Manifest $Plan.ManifestSha256 $UserSid
        if (Test-Path -LiteralPath $BundleRoot) { throw 'Content-addressed host bundle target appeared before atomic promotion.' }
        [IO.Directory]::Move($stage.Payload, $BundleRoot)
        $promoted = $true
        Assert-InstallerBundle $BundleRoot $Plan.Manifest $Plan.ManifestSha256 $UserSid
        return $true
    }
    catch {
        $failure = $_
        if ($promoted -and (Test-Path -LiteralPath $BundleRoot)) {
            try {
                Assert-InstallerBundle $BundleRoot $Plan.Manifest $Plan.ManifestSha256 $UserSid -SkipAcl
                if (-not (Test-Path -LiteralPath $stage.Payload)) {
                    [IO.Directory]::Move($BundleRoot, $stage.Payload)
                    $promoted = $false
                }
            }
            catch { }
        }
        throw $failure
    }
    finally {
        if (Test-Path -LiteralPath $stage.Workspace -PathType Container) {
            Remove-InstallerStagingWorkspace -Workspace $stage.Workspace -ParentRoot $stage.ParentRoot -Purpose Bundle -Identity ([string]$Plan.BundleId)
        }
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
    InstallRoot=$script:InstallRoot; BundleId=$null; ExpectedBundleId=$ExpectedBundleId; BundleAuthorizationRequired=$null; BundleAuthorizationMatched=$false
    ManifestSha256=$null; InstallerBootstrapSha256=$null; ExpectedInstallerSha256=$ExpectedInstallerSha256; InstallerAuthorizationMatched=$false
    SourceConfigSha256=$null; BundleRoot=$null; IntegrityManifestPath=$null; ExecutableIdentityPath=$null
    CodexDistributionPath=$null; CodexDistributionSha256=$null; CodexDistributionStaged=$false
    SourceOrchestratorPath=$null; SourceConfigPath=$null; OrchestratorPath=$null; ConfigPath=$null
    BundleFiles=@(); BoundExecutableCount=0; AclPlan=$null; SourceHashesVerified=$false; AclVerified=$false; HashesVerified=$false
    SchedulerBoundaryInvoked=$false; SchedulerFixture=$false; TaskXml=$null; Error=$null
}

try {
    $effectiveDryRun=[bool]$DryRun -or [bool]$WhatIfPreference
    $result.DryRun=$effectiveDryRun
    $result.BundleAuthorizationRequired=-not $effectiveDryRun
    if (-not $effectiveDryRun) {
        if ([string]::IsNullOrWhiteSpace($ExpectedInstallerSha256)) {
            throw 'Non-DryRun installation requires the independently retained Owner-supplied -ExpectedInstallerSha256 for the reviewed installer bootstrap.'
        }
        $bootstrapPath = [IO.Path]::GetFullPath([string]$PSCommandPath)
        if (-not (Test-Path -LiteralPath $bootstrapPath -PathType Leaf)) {
            throw 'Installer bootstrap path is not an existing file.'
        }
        Assert-InstallerNoReparsePoint $bootstrapPath
        $bootstrapBytesAtEntry = [IO.File]::ReadAllBytes($bootstrapPath)
        $bootstrapHashAtEntry = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bootstrapBytesAtEntry))).ToLowerInvariant()
        $result.InstallerBootstrapSha256=$bootstrapHashAtEntry
        if ($ExpectedInstallerSha256 -cne $bootstrapHashAtEntry) {
            throw 'ExpectedInstallerSha256 does not match the executing reviewed installer bootstrap; installation stopped before any privileged boundary.'
        }
        $result.InstallerAuthorizationMatched=$true
    }
    Initialize-InstallerTrustedPowerShell
    Initialize-InstallerHarnessBoundaries
    $result.InstallRoot=$script:InstallRoot
    $config=Import-InstallerConfig $ConfigPath
    $normalizedConfigPath=(Resolve-Path -LiteralPath $ConfigPath -ErrorAction Stop).ProviderPath; $normalizedOrchestratorPath=(Resolve-Path -LiteralPath $OrchestratorPath -ErrorAction Stop).ProviderPath
    if ((Split-Path -Leaf $normalizedOrchestratorPath) -cne 'Invoke-SashimiHostOrchestrator.ps1') { throw 'OrchestratorPath must name Invoke-SashimiHostOrchestrator.ps1 exactly.' }
    $sourceRoot=Split-Path -Parent $normalizedOrchestratorPath; $taskConfig=$config.Task
    if ([string]$taskConfig.Name -cne $script:TaskName -or [string]$taskConfig.User -cne $script:RequiredUserName -or [int]$taskConfig.IntervalMinutes -ne 15 -or -not [bool]$taskConfig.StartWhenAvailable -or -not [bool]$taskConfig.WakeToRun -or [string]$taskConfig.MultipleInstances -cne 'IgnoreNew') { throw 'Task configuration must retain the exact user, interval, availability, wake, and IgnoreNew contract.' }
    if ([string]$config.PowerShellExecutable -cne $script:PowerShellPath -or -not (Test-Path -LiteralPath $script:PowerShellPath -PathType Leaf)) { throw "PowerShellExecutable must exist at the stable path '$script:PowerShellPath'." }
    $plan=New-InstallerBundlePlan -SourceRoot $sourceRoot -SourceConfigPath $normalizedConfigPath -Config $config -InstallerBootstrapPath $PSCommandPath
    $result.BundleId=$plan.BundleId; $result.ManifestSha256=$plan.ManifestSha256
    $result.InstallerBootstrapSha256=$plan.InstallerBootstrap.Sha256; $result.SourceConfigSha256=$plan.SourceConfig.Sha256
    $result.CodexDistributionPath=$plan.CodexDistribution.Path; $result.CodexDistributionSha256=$plan.CodexDistribution.Sha256
    if (-not $effectiveDryRun) {
        if ($ExpectedInstallerSha256 -cne [string]$plan.InstallerBootstrap.Sha256) {
            throw 'Installer bootstrap changed between entry authorization and immutable bundle capture.'
        }
        if ([string]::IsNullOrWhiteSpace($ExpectedBundleId)) {
            throw 'Non-DryRun installation requires the Owner-supplied -ExpectedBundleId from an immediately preceding reviewed DryRun.'
        }
        if ($ExpectedBundleId -cne [string]$plan.BundleId) {
            throw "ExpectedBundleId does not match the current reviewed source snapshot. Expected '$ExpectedBundleId'; current '$($plan.BundleId)'."
        }
        $result.BundleAuthorizationMatched=$true
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedBundleId)) {
        $result.BundleAuthorizationMatched=($ExpectedBundleId -ceq [string]$plan.BundleId)
    }
    if ($effectiveDryRun -and -not [string]::IsNullOrWhiteSpace($ExpectedInstallerSha256)) {
        $result.InstallerAuthorizationMatched=($ExpectedInstallerSha256 -ceq [string]$plan.InstallerBootstrap.Sha256)
    }
    Assert-InstallerExecutableIdentity -Identity $plan.ExecutableIdentity -SourceEntries $plan.ExecutableIdentitySourceEntries
    $powerShellIdentity=@($plan.ExecutableIdentity.Executables | Where-Object { [string]$_.Name -ceq 'PowerShellExecutable' })[0]
    $detectedPowerShell=Get-StablePowerShellVersion -Executable $script:PowerShellPath -ExpectedLength ([int64]$powerShellIdentity.Length) -ExpectedSha256 ([string]$powerShellIdentity.Sha256); if ($detectedPowerShell -lt $script:MinimumPowerShellVersion) { throw "Stable PowerShell is $detectedPowerShell; version $script:MinimumPowerShellVersion or newer is required." }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent(); $userId=[string]$identity.Name; $accountName=($userId -split '\\')[-1]
    if ($accountName -cne $script:RequiredUserName) { throw "The task must be installed by Windows user '$script:RequiredUserName'; current identity is '$userId'." }; $userSid=$identity.User
    $bundleRoot=Join-Path $script:BundlesRoot $plan.BundleId; $stagedOrchestrator=Join-Path $bundleRoot 'Invoke-SashimiHostOrchestrator.ps1'; $stagedConfig=Join-Path $bundleRoot 'Config.json'; $manifestPath=Join-Path $bundleRoot $script:ManifestName; $executableIdentityPath=Join-Path $bundleRoot $script:ExecutableIdentityName
    if ($StartBoundary -eq [DateTime]::MinValue) { $StartBoundary=[DateTime]::Now.AddMinutes(1) }; if ($StartBoundary.Kind -eq [DateTimeKind]::Utc) { $StartBoundary=$StartBoundary.ToLocalTime() }
    $taskXml=New-SashimiScheduledTaskXml $userId $script:PowerShellPath $stagedOrchestrator $stagedConfig $manifestPath $StartBoundary
    try { [xml]$parsedTask=$taskXml; if ($null -eq $parsedTask.Task) { throw 'Task XML has no Task root.' } } catch { throw "Generated Task Scheduler XML is invalid: $($_.Exception.Message)" }
    $result.UserId=$userId; $result.DetectedPowerShellVersion=$detectedPowerShell.ToString(); $result.BundleRoot=$bundleRoot; $result.IntegrityManifestPath=$manifestPath; $result.ExecutableIdentityPath=$executableIdentityPath; $result.BoundExecutableCount=@($plan.ExecutableIdentity.Executables).Count; $result.SourceHashesVerified=$true
    $result.SourceOrchestratorPath=Protect-InstallerText $normalizedOrchestratorPath; $result.SourceConfigPath=Protect-InstallerText $normalizedConfigPath; $result.OrchestratorPath=$stagedOrchestrator; $result.ConfigPath=$stagedConfig; $result.TaskXml=$taskXml; $result.BundleFiles=@($plan.Manifest.Files)
    $result.AclPlan=[ordered]@{ Inheritance='Disabled'; Owner='BUILTIN\Administrators'; Administrators='FullControl'; System='FullControl'; TaskUser="$userId ReadAndExecute" }
    $approved=-not $effectiveDryRun -and $PSCmdlet.ShouldProcess($script:TaskName,"Stage immutable host bundle $($plan.BundleId) and register or replace scheduled task")
    if ($approved) {
        foreach ($directory in @($script:InstallRoot,$script:BundlesRoot,$script:CodexDistributionsRoot)) {
            Assert-InstallerNoReparsePoint $directory
            if (Test-Path -LiteralPath $directory) {
                $directoryItem=Get-Item -LiteralPath $directory -Force -ErrorAction Stop
                if (-not $directoryItem.PSIsContainer -or ($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Protected install directory is not a plain directory: $directory" }
            }
            else { [IO.Directory]::CreateDirectory($directory)|Out-Null }
            Set-InstallerProtectedAcl $directory $userSid -Container; Assert-InstallerProtectedAcl $directory $userSid
        }
        $result.CodexDistributionStaged=[bool](Install-InstallerCodexDistribution $plan.CodexDistribution $userSid)
        [void](Install-InstallerBundle $plan $bundleRoot $userSid)
        Assert-InstallerExecutableIdentity -Identity $plan.ExecutableIdentity
        $result.Staged=$true; $result.AclVerified=$true; $result.HashesVerified=$true
        $schedulerResult=Invoke-InstallerSchedulerBoundary -TaskName $script:TaskName -Xml $taskXml
        $result.SchedulerBoundaryInvoked=[bool]$schedulerResult.Invoked; $result.SchedulerFixture=[bool]$schedulerResult.Fixture
        $result.Changed=[bool]$schedulerResult.Registered
    }
    elseif ($effectiveDryRun) {
        $schedulerResult=Invoke-InstallerSchedulerBoundary -TaskName $script:TaskName -Xml $taskXml -DryRun
        $result.SchedulerBoundaryInvoked=[bool]$schedulerResult.Invoked; $result.SchedulerFixture=[bool]$schedulerResult.Fixture
    }
    $result.Success=$true; $result.ExitCode=0
}
catch { $result.Error=Protect-InstallerText $_.Exception.Message }

[Console]::Out.WriteLine((ConvertTo-InstallerJson $result))
exit ([int]$result.ExitCode)
