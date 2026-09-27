function New-HostHeadProbeFixture {
    param([string]$Text='', [int]$ExitCode=1)
    [pscustomobject]@{ StdOut=$Text; StdErr=''; ExitCode=$ExitCode; Succeeded=($ExitCode -eq 0);
        TimedOut=$false; Crashed=$false; Cancelled=$false; TerminationConfirmed=$true }
}

function Invoke-HostReviewerDetachedHeadRegression {
    $validator=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    Set-Item Function:Get-SashimiValidationHeadMode (Get-HostTestFunctionScriptBlock $validator 'Get-SashimiValidationHeadMode')
    $runRoot=Join-Path $script:temporaryRoot 'review-detached-head'
    $run=New-SashimiRunWorkspace -RunRoot $runRoot
    $marker=[IO.File]::ReadAllText($run.MarkerPath)
    $context=[pscustomobject]@{ RunId=$run.RunId; Status=''; MarkerSha256=(Get-FileHash $run.MarkerPath -Algorithm SHA256).Hash }
    $arguments=@{ ProjectRoot=$run.RepositoryPath; Head=('a'*40); SymbolicResult=(New-HostHeadProbeFixture);
        BranchResult=(New-HostHeadProbeFixture); ReviewContext=$context; RunId=$run.RunId; RunRoot=$runRoot }
    Assert-HostTest ((Get-SashimiValidationHeadMode @arguments) -ceq 'DetachedReview') 'Owned clean Reviewer detached HEAD was refused.'
    foreach ($case in @('developer','missing-context','wrong-run','wrong-context','wrong-root','wrong-project','changed-marker',
            'dirty-context','invalid-head','native-error','native-zero','partial-head','stderr','timeout','crash','cancelled','unconfirmed')) {
        $candidate=$arguments.Clone()
        $candidate.SymbolicResult=New-HostHeadProbeFixture
        $candidate.BranchResult=New-HostHeadProbeFixture
        $candidate.ReviewContext=$context | ConvertTo-Json | ConvertFrom-Json
        switch ($case) {
            developer { $candidate.RunId=''; $candidate.ReviewContext=$null }
            missing-context { $candidate.ReviewContext=$null }
            wrong-run { $candidate.RunId='20260927T000000Z-'+('b'*32) }
            wrong-context { $candidate.ReviewContext.RunId='20260927T000000Z-'+('b'*32) }
            wrong-root { $candidate.RunRoot=Join-Path $runRoot 'other' }
            wrong-project { $candidate.ProjectRoot=Join-Path $run.RepositoryPath 'nested' }
            changed-marker { Write-SashimiUtf8File $run.MarkerPath ($marker+' ') }
            dirty-context { $candidate.ReviewContext.Status=' M Source.cs' }
            invalid-head { $candidate.Head='invalid' }
            native-error { $candidate.SymbolicResult.ExitCode=128 }
            native-zero { $candidate.SymbolicResult.ExitCode=0; $candidate.SymbolicResult.Succeeded=$true }
            partial-head { $candidate.SymbolicResult=New-HostHeadProbeFixture 'refs/heads/main' 0 }
            stderr { $candidate.SymbolicResult.StdErr='fatal: fixture probe error' }
            timeout { $candidate.SymbolicResult.TimedOut=$true }
            crash { $candidate.BranchResult.Crashed=$true }
            cancelled { $candidate.BranchResult.Cancelled=$true }
            unconfirmed { $candidate.BranchResult.TerminationConfirmed=$false }
        }
        try { Assert-HostThrows { Get-SashimiValidationHeadMode @candidate | Out-Null } }
        finally { Write-SashimiUtf8File $run.MarkerPath $marker }
    }
    $attached=$arguments.Clone()
    $attached.RunId=''; $attached.ReviewContext=$null
    $attached.SymbolicResult=New-HostHeadProbeFixture 'refs/heads/codex/fixture' 0
    $attached.BranchResult=New-HostHeadProbeFixture 'codex/fixture' 0
    Assert-HostTest ((Get-SashimiValidationHeadMode @attached) -ceq 'Branch') 'Developer branch validation regressed.'
    $attached.BranchResult.StdOut='other'
    Assert-HostThrows { Get-SashimiValidationHeadMode @attached | Out-Null } 'inconsistent branch'
}

function Invoke-HostReviewerDetachedSnapshotRegression {
    $validator=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Get-SashimiValidationHeadMode','Get-SashimiUnityGitControlSnapshot','Assert-SashimiUnityGitControlUnchanged',
            'Get-SashimiValidationControlFileState','Get-SashimiValidationControlTreeState','Get-SashimiValidationGitControlManifest',
            'Get-SashimiValidationAttributeControlState','Assert-SashimiValidationGitOperationStateAbsent')) {
        Set-Item ("Function:$name") (Get-HostTestFunctionScriptBlock $validator $name)
    }
    $runRoot=Join-Path $script:temporaryRoot 'review-detached-snapshot'
    $run=New-SashimiRunWorkspace -RunRoot $runRoot
    Write-SashimiUtf8File (Join-Path $run.RepositoryPath '.git/HEAD') (('a'*40)+"`n")
    $config=[pscustomobject]@{RunRoot=$runRoot}
    $ReviewRunId=$run.RunId
    $reviewDriftContext=[pscustomobject]@{RunId=$run.RunId;Status='';MarkerSha256=(Get-FileHash $run.MarkerPath -Algorithm SHA256).Hash}
    $script:gitExecutable='fixture-git'; $script:gitTimeout=10; $script:gitControlFixture=$null
    $script:canonicalRepositoryUrl='https://github.com/DongGyunLeeeee/sashimi-boy-unity.git'
    $script:gitControlSnapshotSequence=0; $script:gitControlPassed=$true; $script:gitControlSecurityFailure=$false
    $headState=@{Head=('a'*40);Attached=$false;FailRead=''}
    $reads=[Collections.Generic.List[string]]::new()
    $failures=[Collections.Generic.List[object]]::new()
    function Add-SashimiValidationFailure { param($Code,$Stage,$Message) $failures.Add([pscustomobject]@{Code=$Code;Stage=$Stage;Message=$Message}) }
    function Add-SashimiValidationCheck { param($Name,$Passed,$Detail,$Data) }
    function Invoke-SashimiValidationProcess {
        param($Name,$Kind,$FilePath,$Arguments,$WorkingDirectory,$TimeoutSeconds,$Fixture,$FixtureGroup,[switch]$DryRun)
        $reads.Add($Name)
        if ($Name -ceq $headState.FailRead) { return New-HostHeadProbeFixture '' 128 }
        if ($Name -in @('GitControlSymbolicHead','GitControlBranch') -and -not $headState.Attached) { return New-HostHeadProbeFixture }
        $text=switch ($Name) {
            GitControlGitDirectory { '.git' }
            GitControlCommonDirectory { '.git' }
            GitControlHead { $headState.Head }
            GitControlSymbolicHead { 'refs/heads/fixture' }
            GitControlBranch { 'fixture' }
            GitControlOriginUrls { $script:canonicalRepositoryUrl }
            GitControlPushUrls { $script:canonicalRepositoryUrl }
            GitControlHooksPath { 'NUL' }
            GitControlLocalConfig { "core.hookspath`nNUL`0remote.origin.url`n$($script:canonicalRepositoryUrl)`0" }
            GitControlRefs { "refs/heads/main`0$('c'*40)`0" }
            GitControlWorktreeIdentity { 'owned-fixture' }
            default { '' }
        }
        New-HostHeadProbeFixture $text 0
    }
    $script:gitControlBaseline=Get-SashimiUnityGitControlSnapshot $run.RepositoryPath 'before Unity'
    Assert-HostTest ($script:gitControlBaseline.HeadMode -ceq 'DetachedReview' -and $script:gitControlBaseline.Upstream -ceq '') 'Detached snapshot has the wrong HEAD mode or upstream.'
    Assert-HostTest (-not $reads.Contains('GitControlUpstream')) 'Detached validation queried upstreams for all local branches.'
    Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'unchanged Reviewer'
    $headState.Head='b'*40
    Assert-HostThrows { Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'changed detached commit' } 'Git.control'
    $headState.Head='a'*40; $headState.Attached=$true
    Assert-HostThrows { Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'changed HEAD mode' } 'Git.control'
    $headState.Attached=$false; $headState.FailRead='GitControlIndexEntries'
    Assert-HostThrows { Get-SashimiUnityGitControlSnapshot $run.RepositoryPath 'failed read' | Out-Null } 'Git.control'
    $headState.FailRead=''
    Write-SashimiUtf8File (Join-Path $run.RepositoryPath '.git/extra-control') 'unexpected'
    Assert-HostThrows { Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'changed control files' } 'Git.control'
    Assert-HostTest (@($failures | Where-Object Code -eq 'GitControlDrift').Count -ge 3) 'HEAD, mode or control-file drift lost its blocking evidence.'
}

function Invoke-HostReviewerStructuredFailureRegression {
    $reviewer=Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1'
    Set-Item Function:Invoke-ReviewerScriptJson (Get-HostTestFunctionScriptBlock $reviewer 'Invoke-ReviewerScriptJson')
    $DryRun=$false
    $script:reviewerConfig=[pscustomobject]@{PowerShellExecutable='fixture-pwsh'}
    $script:ownedHostPidPath=''; $script:cancellationMarkerPath=''
    function Add-ReviewerPlan { param($Stage,$FilePath,$Arguments) }
    function Assert-ReviewerNotCancelled { }
    function Invoke-SashimiHostProcess {
        param($FilePath,$ArgumentList,$WorkingDirectory,$TimeoutSeconds,$OwnedProcessRecordPath,$CancellationMarkerPath)
        [pscustomobject]@{ Succeeded=$false; ExitCode=1; StdErr=''; StdOut='{"Success":false,"Failures":[{"Code":"GitControlSecurityFailure"},{"Code":"UnhandledValidationFailure"},{"Code":"not a safe diagnostic code"}]}' }
    }
    Assert-HostThrows { Invoke-ReviewerScriptJson 'Unity validation' 'fixture.ps1' @() 10 } 'exit=1; codes=GitControlSecurityFailure,UnhandledValidationFailure; error=$'
}
