#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true)]
    [ValidateSet('block', 'unblock')]
    [string]$Queue,

    [Parameter(Mandatory = $true)]
    [ValidateSet('product-decision', 'source-asset-missing', 'external-blocker', 'resolved')]
    [string]$Reason
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $helperPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Handoff.ps1'
    if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
        throw "Automation handoff helper is missing: $helperPath"
    }
    . $helperPath

    $marker = New-AutomationOwnerQueueDecisionMarker `
        -IssueNumber $IssueNumber `
        -Queue $Queue `
        -Reason $Reason
    [Console]::Out.WriteLine($marker)
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

exit 0
