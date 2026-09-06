#requires -Version 7.5

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$DryRun,

    # Artifacts are deliberately always preserved. The explicit switch makes
    # that contract visible in owner commands and future-proofs the interface.
    [switch]$PreserveArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$taskName = 'SASHIMI BOY Host Orchestrator'
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
$env:PSModulePath = [string]::Join([IO.Path]::PathSeparator, @($script:PowerShellModuleRoot, $script:WindowsModuleRoot))

function Assert-UninstallerTrustedPath {
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

function Get-UninstallerNativeSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))).ToLowerInvariant()
}

function Assert-UninstallerMicrosoftSignature {
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

function Assert-UninstallerTrustedPowerShellState {
    param([switch]$ScheduledTasks)

    $expected = if ($ScheduledTasks) { 'ScheduledTasks' } else { 'Microsoft.PowerShell.Security' }
    $expectedManifest = if ($ScheduledTasks) { $script:ScheduledTasksModuleManifest } else { $script:SecurityModuleManifest }
    $loaded = @(Microsoft.PowerShell.Core\Get-Module -Name $expected)
    if ($loaded.Count -ne 1 -or -not [string]::Equals([string]$loaded[0].Path, $expectedManifest, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The exact protected $expected module is not loaded."
    }
    foreach ($path in $script:TrustedModuleFiles) {
        if (-not $script:TrustedPowerShellFileHashes.ContainsKey($path) -or
            (Get-UninstallerNativeSha256 -Path $path) -cne $script:TrustedPowerShellFileHashes[$path]) {
            throw 'A trusted PowerShell component changed after provenance verification.'
        }
    }
}

function Initialize-UninstallerTrustedPowerShell {
    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion -lt $script:MinimumPowerShellVersion) {
        throw "Uninstaller requires PowerShell $script:MinimumPowerShellVersion or newer (Core edition)."
    }
    $processPath = [Environment]::ProcessPath
    $mainModulePath = $null
    $currentProcess = [Diagnostics.Process]::GetCurrentProcess()
    try { $mainModulePath = $currentProcess.MainModule.FileName } finally { $currentProcess.Dispose() }
    foreach ($actualPath in @($processPath, $mainModulePath)) {
        if ([string]::IsNullOrWhiteSpace($actualPath) -or
            -not [string]::Equals([IO.Path]::GetFullPath($actualPath), $script:PowerShellPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Uninstaller must run in the stable host '$($script:PowerShellPath)'."
        }
    }
    if (-not [string]::Equals([IO.Path]::GetFullPath($PSHOME), $script:PowerShellHome, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Uninstaller PSHOME does not match the stable PowerShell host.'
    }
    Assert-UninstallerTrustedPath -Path $script:PowerShellPath -TrustedRoot $script:PowerShellHome
    Assert-UninstallerTrustedPath -Path $script:SecurityModuleManifest -TrustedRoot $script:PowerShellModuleRoot
    Assert-UninstallerTrustedPath -Path $script:ScheduledTasksModuleManifest -TrustedRoot $script:WindowsModuleRoot

    foreach ($moduleName in @('Microsoft.PowerShell.Security', 'ScheduledTasks')) {
        foreach ($module in @(Microsoft.PowerShell.Core\Get-Module -Name $moduleName)) {
            $expectedManifest = if ($moduleName -ceq 'ScheduledTasks') { $script:ScheduledTasksModuleManifest } else { $script:SecurityModuleManifest }
            if (-not [string]::Equals([string]$module.Path, $expectedManifest, [StringComparison]::OrdinalIgnoreCase)) {
                throw "An untrusted preloaded $moduleName module is present in the elevated process."
            }
        }
    }

    Microsoft.PowerShell.Core\Import-Module -Name $script:SecurityModuleManifest -Scope Local -Force -ErrorAction Stop
    Assert-UninstallerMicrosoftSignature -Path $script:PowerShellPath
    foreach ($path in $script:TrustedModuleFiles) {
        $trustedRoot = if ($path.StartsWith($script:WindowsModuleRoot, [StringComparison]::OrdinalIgnoreCase)) { $script:WindowsModuleRoot } else { $script:PowerShellHome }
        Assert-UninstallerTrustedPath -Path $path -TrustedRoot $trustedRoot
        Assert-UninstallerMicrosoftSignature -Path $path
        $script:TrustedPowerShellFileHashes[$path] = Get-UninstallerNativeSha256 -Path $path
    }
    Microsoft.PowerShell.Core\Import-Module -Name $script:ScheduledTasksModuleManifest -Scope Local -Force -ErrorAction Stop
    Assert-UninstallerTrustedPowerShellState
    Assert-UninstallerTrustedPowerShellState -ScheduledTasks
    foreach ($qualifiedCommand in @('ScheduledTasks\Get-ScheduledTask', 'ScheduledTasks\Unregister-ScheduledTask')) {
        $command = Microsoft.PowerShell.Core\Get-Command $qualifiedCommand -ErrorAction Stop
        if ($command.CommandType -notin @([Management.Automation.CommandTypes]::Cmdlet, [Management.Automation.CommandTypes]::Function)) {
            throw 'A required protected PowerShell command did not resolve to a cmdlet or module function.'
        }
    }
}

function ConvertTo-UninstallerJson {
    param([Parameter(Mandatory = $true)][AllowNull()][object]$InputObject)

    return ($InputObject | Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 12 -Compress)
}

$result = [ordered]@{
    Tool               = 'Uninstall-SashimiHostAutomation'
    Success            = $false
    ExitCode           = 1
    DryRun             = [bool]$DryRun
    TaskName           = $taskName
    TaskFound          = $null
    Changed            = $false
    ArtifactsPreserved = $true
    Error              = $null
}

try {
    Initialize-UninstallerTrustedPowerShell

    # There is intentionally no artifact deletion path in this uninstaller.
    # Run retention remains owned by the orchestrator's marker-guarded cleanup.
    $effectiveDryRun = [bool]$DryRun -or [bool]$WhatIfPreference
    $result.DryRun = $effectiveDryRun
    if ($effectiveDryRun) {
        # DryRun must be usable without Task Scheduler service access and must
        # never call a mutation-capable scheduler cmdlet.
        $result.TaskFound = $null
    }
    else {
        Assert-UninstallerTrustedPowerShellState -ScheduledTasks
        $task = ScheduledTasks\Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        $result.TaskFound = $null -ne $task
        if ($null -ne $task -and $PSCmdlet.ShouldProcess($taskName, 'Unregister scheduled task')) {
            Assert-UninstallerTrustedPowerShellState -ScheduledTasks
            ScheduledTasks\Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
            $result.Changed = $true
        }
    }

    $result.Success = $true
    $result.ExitCode = 0
}
catch {
    $result.Error = $_.Exception.Message
}

[Console]::Out.WriteLine((ConvertTo-UninstallerJson -InputObject $result))
exit ([int]$result.ExitCode)
