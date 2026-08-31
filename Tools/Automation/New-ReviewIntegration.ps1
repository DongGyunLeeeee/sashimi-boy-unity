#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$PullRequestNumber,

    [ValidatePattern('^[^/\s]+/[^/\s]+$')]
    [string]$Repository = 'DongGyunLeeeee/sashimi-boy-unity',

    [string]$RepositoryUrl,

    [ValidateNotNullOrEmpty()]
    [string]$BaseBranch = 'main',

    [string]$TempRoot = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        'SashimiBoyAutomation'),

    [ValidateNotNullOrEmpty()]
    [string]$GitExecutable = 'git',

    [string[]]$ProtectedPath = @(
        'C:\Dev\sashimi-boy-unity',
        'C:\Dev\sashimi-boy-unity-developer',
        'C:\Dev\sashimi-boy-unity-reviewer'
    ),

    [switch]$KeepWorkspace,

    [switch]$DryRun,

    [Parameter(DontShow = $true)]
    [ValidatePattern('^ReviewIntegration-\d{8}T\d{6}Z-[0-9a-f]{32}$')]
    [string]$InternalWorkspaceLeaf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    Write-Error "Required helper was not found: $commonPath"
    exit 10
}
. $commonPath

function Add-PlannedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$List,

        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    [void]$List.Add([ordered]@{
        Stage     = $Stage
        FilePath  = $FilePath
        Arguments = @($ArgumentList)
    })
}

function Get-RemoteRefSha {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Remote,

        [Parameter(Mandatory = $true)]
        [string]$RefName,

        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$Commands
    )

    $arguments = @('ls-remote', '--exit-code', $Remote, $RefName)
    Add-PlannedCommand -List $Commands -Stage $Stage -FilePath $GitExecutable -ArgumentList $arguments
    $command = Invoke-AutomationNativeCommand -FilePath $GitExecutable -ArgumentList $arguments
    if (-not $command.Succeeded) {
        throw "$Stage failed with exit code $($command.ExitCode): $($command.StdErr)"
    }

    $line = @($command.StdOut -split "`r?`n" | Where-Object { $_ -match '\S' })
    if ($line.Count -ne 1 -or $line[0] -notmatch '^([0-9a-fA-F]{40})\s+') {
        throw "$Stage returned an unexpected ref response for '$RefName': $($command.StdOut)"
    }

    return $Matches[1].ToLowerInvariant()
}

function Invoke-CheckedGit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$Commands,

        [switch]$AllowFailure
    )

    Add-PlannedCommand -List $Commands -Stage $Stage -FilePath $GitExecutable -ArgumentList $ArgumentList
    $command = Invoke-AutomationNativeCommand -FilePath $GitExecutable -ArgumentList $ArgumentList
    if (-not $AllowFailure -and -not $command.Succeeded) {
        throw "$Stage failed with exit code $($command.ExitCode): $($command.StdErr)"
    }

    return $command
}

function Assert-SafeAutomationRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string[]]$Protected
    )

    foreach ($path in $Protected) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $normalizedProtected = ConvertTo-AutomationPath -Path $path -AllowMissing
        if ((Test-AutomationPathEqual -Left $Root -Right $normalizedProtected) -or
            (Test-AutomationPathWithin -Path $Root -Root $normalizedProtected) -or
            (Test-AutomationPathWithin -Path $normalizedProtected -Root $Root)) {
            throw "TempRoot '$Root' overlaps protected path '$normalizedProtected'."
        }
    }
}

$commands = New-Object System.Collections.ArrayList
$effectiveDryRun = [bool]$DryRun
$exitCode = 0
if (-not [string]::IsNullOrWhiteSpace($InternalWorkspaceLeaf) -and
    $env:SASHIMI_BOY_AUTOMATION_TEST_HARNESS -cne '1') {
    throw 'Internal workspace-name injection is available only to the automation smoke harness.'
}
$runId = if ([string]::IsNullOrWhiteSpace($InternalWorkspaceLeaf)) {
    [Guid]::NewGuid().ToString('N')
}
else {
    $InternalWorkspaceLeaf.Substring($InternalWorkspaceLeaf.Length - 32)
}
$workspaceCreated = $false
$markerPath = $null
$result = $null
$workspaceRoot = $null
$normalizedTempRoot = $null

try {
    Assert-AutomationPathHasNoReparsePoint -Path $TempRoot
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
    Assert-SafeAutomationRoot -Root $normalizedTempRoot -Protected $ProtectedPath

    if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
        $RepositoryUrl = "https://github.com/$Repository.git"
    }

    $workspaceLeaf = if ([string]::IsNullOrWhiteSpace($InternalWorkspaceLeaf)) {
        "ReviewIntegration-{0}-{1}" -f ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')), $runId
    }
    else {
        $InternalWorkspaceLeaf
    }
    $workspaceRoot = Join-Path -Path $normalizedTempRoot -ChildPath $workspaceLeaf
    $integrationPath = Join-Path -Path $workspaceRoot -ChildPath 'Repository'
    $markerPath = Join-Path -Path $workspaceRoot -ChildPath '.sashimi-boy-automation-owned.json'
    $disabledHooksPath = Join-Path -Path $workspaceRoot -ChildPath 'DisabledHooks'
    $pullRef = "refs/pull/$PullRequestNumber/head"
    $mainRef = "refs/heads/$BaseBranch"

    $result = [ordered]@{
        Tool                  = 'New-ReviewIntegration'
        Success               = $false
        ExitCode              = 0
        DryRun                = $effectiveDryRun
        Repository            = $Repository
        RepositoryUrl         = $RepositoryUrl
        PullRequestNumber     = $PullRequestNumber
        PullRequestRef        = $pullRef
        BaseBranch            = $BaseBranch
        MainRef               = $mainRef
        PullRequestHead       = $null
        MainHead              = $null
        MergeExitCode         = $null
        MergeHead             = $null
        MergeTree             = $null
        AlreadyUpToDate       = $false
        WorkspaceRoot         = $workspaceRoot
        IntegrationPath       = $integrationPath
        DisabledHooksPath     = $disabledHooksPath
        KeepWorkspaceRequested = [bool]$KeepWorkspace
        WorkspaceKept         = $false
        WorkspaceCleaned      = $false
        Conflicts             = @()
        Commands              = $commands
        Error                 = $null
        PrimaryError          = $null
        CleanupError          = $null
    }

    if ($effectiveDryRun) {
        Add-PlannedCommand -List $commands -Stage 'DiscoverPullRequestHead' -FilePath $GitExecutable -ArgumentList @('ls-remote', '--exit-code', $RepositoryUrl, $pullRef)
        Add-PlannedCommand -List $commands -Stage 'DiscoverMainHead' -FilePath $GitExecutable -ArgumentList @('ls-remote', '--exit-code', $RepositoryUrl, $mainRef)
        Add-PlannedCommand -List $commands -Stage 'Clone' -FilePath $GitExecutable -ArgumentList @('-c', "core.hooksPath=$disabledHooksPath", 'clone', '--no-checkout', '--origin', 'origin', '--', $RepositoryUrl, $integrationPath)
        Add-PlannedCommand -List $commands -Stage 'FetchExactRefs' -FilePath $GitExecutable -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", 'fetch', '--no-tags', 'origin', "+$mainRef`:refs/remotes/origin/automation-main", "+$pullRef`:refs/remotes/origin/automation-pr-$PullRequestNumber")
        Add-PlannedCommand -List $commands -Stage 'CheckoutMainHead' -FilePath $GitExecutable -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", 'checkout', '--detach', '<latest-main-head>')
        Add-PlannedCommand -List $commands -Stage 'SyntheticMerge' -FilePath $GitExecutable -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", '-c', 'user.name=Sashimi Boy Automation', '-c', 'user.email=automation@local.invalid', '-c', 'commit.gpgSign=false', 'merge', '--no-ff', '--no-edit', '--no-verify', '<pull-request-head>')
        $result.Success = $true
    }
    else {
        # Discover both live refs before cloning. PR API baseRefOid/base.sha is not a live main ref.
        $result.PullRequestHead = Get-RemoteRefSha -Remote $RepositoryUrl -RefName $pullRef -Stage 'DiscoverPullRequestHead' -Commands $commands
        $result.MainHead = Get-RemoteRefSha -Remote $RepositoryUrl -RefName $mainRef -Stage 'DiscoverMainHead' -Commands $commands

        if (-not (Test-Path -LiteralPath $normalizedTempRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $normalizedTempRoot -Force -ErrorAction Stop | Out-Null
        }
        Assert-AutomationPathHasNoReparsePoint -Path $normalizedTempRoot

        # The create operation is the ownership boundary. Without -Force,
        # New-Item refuses to reuse or overwrite a pre-existing workspace.
        New-Item -ItemType Directory -Path $workspaceRoot -ErrorAction Stop | Out-Null
        $workspaceCreated = $true
        Assert-AutomationPathHasNoReparsePoint -Path $workspaceRoot
        $markerData = [ordered]@{
            SchemaVersion = 1
            RunId          = $runId
            WorkspaceRoot  = $workspaceRoot
            CreatedUtc     = [DateTime]::UtcNow.ToString('o')
            Script         = $PSCommandPath
        }
        Set-Content -LiteralPath $markerPath -Value (ConvertTo-AutomationJson -InputObject $markerData) -Encoding UTF8
        New-Item -ItemType Directory -Path $disabledHooksPath -Force | Out-Null
        Assert-AutomationTreeHasNoReparsePoint -Root $workspaceRoot

        Invoke-CheckedGit -Stage 'Clone' -ArgumentList @('-c', "core.hooksPath=$disabledHooksPath", 'clone', '--no-checkout', '--origin', 'origin', '--', $RepositoryUrl, $integrationPath) -Commands $commands | Out-Null

        $mainTrackingRef = 'refs/remotes/origin/automation-main'
        $pullTrackingRef = "refs/remotes/origin/automation-pr-$PullRequestNumber"
        Invoke-CheckedGit -Stage 'FetchExactRefs' -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", 'fetch', '--no-tags', 'origin', "+$mainRef`:$mainTrackingRef", "+$pullRef`:$pullTrackingRef") -Commands $commands | Out-Null

        $fetchedPull = (Invoke-CheckedGit -Stage 'VerifyPullRequestHead' -ArgumentList @('-C', $integrationPath, 'rev-parse', $pullTrackingRef) -Commands $commands).StdOut.Trim().ToLowerInvariant()
        $fetchedMain = (Invoke-CheckedGit -Stage 'VerifyMainHead' -ArgumentList @('-C', $integrationPath, 'rev-parse', $mainTrackingRef) -Commands $commands).StdOut.Trim().ToLowerInvariant()
        if ($fetchedPull -ne $result.PullRequestHead -or $fetchedMain -ne $result.MainHead) {
            throw "A remote ref changed during setup. Expected PR/main $($result.PullRequestHead)/$($result.MainHead), fetched $fetchedPull/$fetchedMain. Rerun to use a single consistent snapshot."
        }

        # Match GitHub's merge ref semantics: first parent is latest main, second parent is the exact PR head.
        Invoke-CheckedGit -Stage 'CheckoutMainHead' -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", 'checkout', '--detach', $result.MainHead) -Commands $commands | Out-Null
        $merge = Invoke-CheckedGit -Stage 'SyntheticMerge' -ArgumentList @('-C', $integrationPath, '-c', "core.hooksPath=$disabledHooksPath", '-c', 'user.name=Sashimi Boy Automation', '-c', 'user.email=automation@local.invalid', '-c', 'commit.gpgSign=false', 'merge', '--no-ff', '--no-edit', '--no-verify', $result.PullRequestHead) -Commands $commands -AllowFailure
        $result.MergeExitCode = [int]$merge.ExitCode
        if (-not $merge.Succeeded) {
            $conflictCommand = Invoke-CheckedGit -Stage 'ListConflicts' -ArgumentList @('-C', $integrationPath, 'diff', '--name-only', '--diff-filter=U') -Commands $commands -AllowFailure
            $result.Conflicts = @($conflictCommand.StdOut -split "`r?`n" | Where-Object { $_ -match '\S' })
            $result.Error = [ordered]@{
                Stage          = 'SyntheticMerge'
                NativeExitCode = $merge.ExitCode
                Message        = if ($result.Conflicts.Count -gt 0) { 'Synthetic merge has conflicts.' } else { 'Synthetic merge failed.' }
                StdErr         = $merge.StdErr
            }
            $result.PrimaryError = $result.Error
            $exitCode = if ($merge.ExitCode -ne 0) { [int]$merge.ExitCode } else { 1 }
        }
        else {
            $result.AlreadyUpToDate = ($merge.StdOut -match '(?i)already up[ -]to[ -]date')
            $result.MergeHead = (Invoke-CheckedGit -Stage 'ResolveMergeHead' -ArgumentList @('-C', $integrationPath, 'rev-parse', 'HEAD') -Commands $commands).StdOut.Trim().ToLowerInvariant()
            $result.MergeTree = (Invoke-CheckedGit -Stage 'ResolveMergeTree' -ArgumentList @('-C', $integrationPath, 'rev-parse', 'HEAD^{tree}') -Commands $commands).StdOut.Trim().ToLowerInvariant()
            $status = Invoke-CheckedGit -Stage 'VerifyCleanIntegration' -ArgumentList @('-C', $integrationPath, 'status', '--porcelain=v1', '--untracked-files=all') -Commands $commands
            if (-not [string]::IsNullOrWhiteSpace($status.StdOut)) {
                throw "Synthetic integration is not clean after merge: $($status.StdOut)"
            }
            $result.Success = $true
        }
    }
}
catch {
    if ($null -eq $result) {
        $result = [ordered]@{
            Tool             = 'New-ReviewIntegration'
            Success          = $false
            ExitCode         = 1
            DryRun           = $effectiveDryRun
            Repository       = $Repository
            PullRequestNumber = $PullRequestNumber
            Commands         = $commands
            Error            = $null
            PrimaryError     = $null
            CleanupError     = $null
        }
    }
    if ($null -eq $result.Error) {
        $result.Error = [ordered]@{
            Stage   = 'Unhandled'
            Message = $_.Exception.Message
        }
    }
    $result.PrimaryError = $result.Error
    if ($exitCode -eq 0) {
        $exitCode = 1
    }
}
finally {
    if ($workspaceCreated -and -not $KeepWorkspace) {
        try {
            Remove-AutomationOwnedWorkspace -WorkspaceRoot $workspaceRoot -AllowedRoot $normalizedTempRoot -ExpectedRunId $runId | Out-Null
            $result.WorkspaceCleaned = $true
        }
        catch {
            $result.Success = $false
            $result.CleanupError = [ordered]@{
                Stage   = 'Cleanup'
                Message = $_.Exception.Message
            }
            if ($null -eq $result.Error) {
                $result.Error = $result.CleanupError
            }
            $exitCode = 12
        }
    }
    elseif ($workspaceCreated -and $KeepWorkspace) {
        $result.WorkspaceKept = $true
    }
}

$result.ExitCode = $exitCode
[Console]::Out.WriteLine((ConvertTo-AutomationJson -InputObject $result))
exit $exitCode
