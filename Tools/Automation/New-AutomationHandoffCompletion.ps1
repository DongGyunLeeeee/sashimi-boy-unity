#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$PullRequestNumber,

    [Parameter(Mandatory = $true)]
    [string]$HeadSha,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Developer')]
    [string]$SourceRole,

    [Parameter(Mandatory = $true)]
    [string]$HandoffUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $helperPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Handoff.ps1'
    if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
        throw "Automation handoff helper is missing: $helperPath"
    }
    . $helperPath

    $marker = New-AutomationHandoffCompletionMarker `
        -IssueNumber $IssueNumber `
        -PullRequestNumber $PullRequestNumber `
        -HeadSha $HeadSha `
        -SourceRole $SourceRole `
        -HandoffUrl $HandoffUrl
    [Console]::Out.WriteLine($marker)
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

exit 0
