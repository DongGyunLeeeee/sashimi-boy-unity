#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WorkspaceRoot,

    [string]$TempRoot = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        'SashimiBoyAutomation'),

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    Write-Error "Required helper was not found: $commonPath"
    exit 10
}
. $commonPath

$effectiveDryRun = [bool]$DryRun -or [bool]$WhatIfPreference
$exitCode = 0
$result = [ordered]@{
    Tool          = 'Remove-ReviewIntegration'
    Success       = $false
    ExitCode      = 0
    DryRun        = $effectiveDryRun
    WorkspaceRoot = $WorkspaceRoot
    TempRoot      = $TempRoot
    RunId         = $null
    WouldRemove   = $false
    Removed       = $false
    Error         = $null
}

try {
    Assert-AutomationPathHasNoReparsePoint -Path $TempRoot
    Assert-AutomationPathHasNoReparsePoint -Path $WorkspaceRoot
    $normalizedTempRoot = ConvertTo-AutomationPath -Path $TempRoot -AllowMissing
    $canonicalAutomationRootInput = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        'SashimiBoyAutomation')
    Assert-AutomationPathHasNoReparsePoint -Path $canonicalAutomationRootInput
    $canonicalAutomationRoot = ConvertTo-AutomationPath -Path $canonicalAutomationRootInput -AllowMissing
    if (-not (Test-AutomationPathEqual -Left $normalizedTempRoot -Right $canonicalAutomationRoot) -and
        -not (Test-AutomationPathWithin -Path $normalizedTempRoot -Root $canonicalAutomationRoot)) {
        throw "TempRoot must be '%TEMP%\SashimiBoyAutomation' or one of its descendants. Actual: '$normalizedTempRoot'."
    }

    $ownedWorkspace = Get-AutomationOwnedWorkspace `
        -WorkspaceRoot $WorkspaceRoot `
        -AllowedRoot $normalizedTempRoot
    $result.WorkspaceRoot = $ownedWorkspace.WorkspaceRoot
    $result.TempRoot = $ownedWorkspace.AllowedRoot
    $result.RunId = $ownedWorkspace.RunId
    $result.WouldRemove = $true

    if ($effectiveDryRun) {
        $result.Success = $true
    }
    elseif ($PSCmdlet.ShouldProcess($ownedWorkspace.WorkspaceRoot, 'Remove owned review-integration workspace')) {
        Remove-AutomationOwnedWorkspace `
            -WorkspaceRoot $ownedWorkspace.WorkspaceRoot `
            -AllowedRoot $ownedWorkspace.AllowedRoot `
            -ExpectedRunId $ownedWorkspace.RunId | Out-Null
        $result.Removed = $true
        $result.Success = $true
    }
    else {
        $result.DryRun = $true
        $result.Success = $true
    }
}
catch {
    $exitCode = 12
    $result.Error = [ordered]@{
        Stage   = 'CleanupValidationOrRemoval'
        Message = $_.Exception.Message
    }
}

$result.ExitCode = $exitCode
[Console]::Out.WriteLine((ConvertTo-AutomationJson -InputObject $result))
exit $exitCode
