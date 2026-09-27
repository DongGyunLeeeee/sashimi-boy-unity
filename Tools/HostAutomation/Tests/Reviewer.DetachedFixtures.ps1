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
    param([switch]$IncludeGenerator)
    $validator=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Get-SashimiValidationHeadMode','Get-SashimiUnityGitControlSnapshot','Assert-SashimiUnityGitControlUnchanged',
            'Get-SashimiValidationControlFileState','Get-SashimiValidationControlTreeState','Get-SashimiValidationGitControlManifest',
            'Get-SashimiValidationAttributeControlState','Assert-SashimiValidationGitOperationStateAbsent',
            'New-SashimiGeneratorBaseline','Get-SashimiGeneratorSourceManifest','New-SashimiReviewerGeneratorContext')) {
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
    $script:reviewerGeneratorContext=$null
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
    if ($IncludeGenerator) {
        # Use the production copy/nonce/manifest functions, not the Unity fixture
        # shortcut that keeps GeneratorRun2 in the original Repository.
        $fixture=$null
        Write-SashimiUtf8File (Join-Path $run.RepositoryPath 'Assets/owned.txt') 'fixture input'
        $manifest=@(Get-SashimiGeneratorSourceManifest $run.RepositoryPath)
        $workspace=New-SashimiGeneratorBaseline $run.RepositoryPath $run.StatePath $manifest
        $script:reviewerGeneratorContext=New-SashimiReviewerGeneratorContext $run.RepositoryPath $workspace $script:gitControlBaseline
        $firstBaseline=$script:gitControlBaseline
        $generatorMarker=Join-Path $workspace.Parent '.generator-owner'
        $ownerMarker=[IO.File]::ReadAllText($run.MarkerPath)
        try {
            Assert-HostTest ([IO.File]::ReadAllText((Join-Path $workspace.Repository '.git/HEAD')) -ceq (('a'*40)+"`n")) 'The independent copy did not preserve detached HEAD.'
            $headState.Head='b'*40
            Assert-HostThrows { Get-SashimiUnityGitControlSnapshot $workspace.Repository 'wrong initial child commit'|Out-Null } 'Git.control'
            $headState.Head='a'*40; $headState.Attached=$true
            Assert-HostThrows { Get-SashimiUnityGitControlSnapshot $workspace.Repository 'wrong initial child mode'|Out-Null } 'Git.control'
            $headState.Attached=$false
            $script:gitControlBaseline=Get-SashimiUnityGitControlSnapshot $workspace.Repository 'independent generator baseline'
            Assert-SashimiUnityGitControlUnchanged $workspace.Repository 'after Unity stage GeneratorRun2'
            Assert-HostTest ($script:gitControlBaseline.HeadMode -ceq 'DetachedReview') 'The copied generator lost Reviewer HEAD mode.'
            $validContext=$script:reviewerGeneratorContext
            foreach($case in @('no-binding','other-run','other-state','other-parent','other-repository','wrong-nonce',
                    'missing-nonce','reused-nonce','changed-owner-marker','changed-commit','changed-mode','native-failure','control-drift')) {
                $script:reviewerGeneratorContext=$validContext|ConvertTo-Json|ConvertFrom-Json
                switch($case) {
                    no-binding { $script:reviewerGeneratorContext=$null }
                    other-run { $script:reviewerGeneratorContext.RunPath=Join-Path $runRoot ('20260927T000000Z-'+('b'*32)) }
                    other-state { $script:reviewerGeneratorContext.StateRoot=$run.RepositoryPath }
                    other-parent { $script:reviewerGeneratorContext.Parent=$run.StatePath }
                    other-repository { $script:reviewerGeneratorContext.Repository=$run.RepositoryPath }
                    wrong-nonce { $script:reviewerGeneratorContext.OwnerNonce='b'*32 }
                    missing-nonce { Remove-Item -LiteralPath $generatorMarker }
                    reused-nonce { Write-SashimiUtf8File $generatorMarker ('c'*32) }
                    changed-owner-marker { Write-SashimiUtf8File $run.MarkerPath ($ownerMarker+' ') }
                    changed-commit { $headState.Head='b'*40 }
                    changed-mode { $headState.Attached=$true }
                    native-failure { $headState.FailRead='GitControlIndexEntries' }
                    control-drift { Write-SashimiUtf8File (Join-Path $workspace.Repository '.git/extra-control') 'unexpected' }
                }
                try { Assert-HostThrows { Assert-SashimiUnityGitControlUnchanged $workspace.Repository "generator rejection $case" } 'Git.control' }
                finally {
                    $headState.Head='a'*40; $headState.Attached=$false; $headState.FailRead=''
                    Write-SashimiUtf8File $generatorMarker $workspace.OwnerNonce
                    Write-SashimiUtf8File $run.MarkerPath $ownerMarker
                    $extra=Join-Path $workspace.Repository '.git/extra-control'
                    if(Test-Path -LiteralPath $extra){Remove-Item -LiteralPath $extra}
                }
            }
            $script:reviewerGeneratorContext=$validContext
            Assert-SashimiUnityGitControlUnchanged $workspace.Repository 'restored independent generator'
            $held=Join-Path $workspace.Parent 'held-r'
            foreach($path in @($workspace.Repository,$held)){
                Assert-HostTest (Test-SashimiPathWithin ([IO.Path]::GetFullPath($path)) $run.StatePath) 'Fixture move escaped owned State.'
            }
            Move-Item -LiteralPath $workspace.Repository -Destination $held
            try {
                New-Item -ItemType Junction -Path $workspace.Repository -Target $held | Out-Null
                Assert-HostThrows { Assert-SashimiUnityGitControlUnchanged $workspace.Repository 'generator reparse' } 'Git.control'
            }
            finally {
                if(Test-Path -LiteralPath $workspace.Repository){
                    Assert-HostTest (((Get-Item -LiteralPath $workspace.Repository -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'Fixture cleanup target is not its junction.'
                    Remove-Item -LiteralPath $workspace.Repository
                }
                Move-Item -LiteralPath $held -Destination $workspace.Repository
            }
            Assert-SashimiUnityGitControlUnchanged $workspace.Repository 'generator after reparse rejection'
        }
        finally { $script:gitControlBaseline=$firstBaseline }
        Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'primary after generator finally'
        $script:reviewerGeneratorContext=$null
        $ReviewRunId=''; $reviewDriftContext=$null
        Assert-HostThrows { Get-SashimiUnityGitControlSnapshot $workspace.Repository 'detached Developer copy'|Out-Null } 'Git.control'
        $headState.Attached=$true
        foreach($root in @($run.RepositoryPath,$workspace.Repository)){
            $script:gitControlBaseline=Get-SashimiUnityGitControlSnapshot $root 'attached Developer baseline'
            Assert-SashimiUnityGitControlUnchanged $root 'attached Developer unchanged'
        }
        $headState.Attached=$false; $ReviewRunId=$run.RunId
        $reviewDriftContext=[pscustomobject]@{RunId=$run.RunId;Status='';MarkerSha256=(Get-FileHash $run.MarkerPath -Algorithm SHA256).Hash}
        $script:gitControlBaseline=$firstBaseline
    }
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

function Invoke-HostReviewerDetachedPipelineRegression {
    $validator=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach($name in @('Get-SashimiValidationHeadMode','Get-SashimiUnityGitControlSnapshot','Assert-SashimiUnityGitControlUnchanged',
            'Get-SashimiValidationControlFileState','Get-SashimiValidationControlTreeState','Get-SashimiValidationGitControlManifest',
            'Get-SashimiValidationAttributeControlState','Assert-SashimiValidationGitOperationStateAbsent',
            'New-SashimiGeneratorBaseline','Get-SashimiGeneratorSourceManifest','New-SashimiReviewerGeneratorContext',
            'ConvertTo-SashimiProjectRelativePath','Get-SashimiDeterminismSnapshot','Get-SashimiGeneratorDelta',
            'Invoke-SashimiUnityValidationStage','Get-SashimiUnityLogDiagnostics','Get-SashimiUnityNUnitSummaryFromText')) {
        Set-Item ("Function:$name") (Get-HostTestFunctionScriptBlock $validator $name)
    }
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($validator,[ref]$tokens,[ref]$errors)
    $blocks=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -ceq '$preUnityScopePassed'},$true))
    Assert-HostTest ($errors.Count -eq 0 -and $blocks.Count -eq 1) 'Cannot locate production Unity orchestration.'
    $pipeline=[scriptblock]::Create($blocks[0].Extent.Text)
    function Assert-SashimiValidationNotCancelled {}
    function Add-SashimiValidationCheck { param($Name,$Passed,$Detail,$Data) $checks.Add([string]$Name) }
    function Add-SashimiValidationFailure { param($Code,$Stage,$Message,$NativeExitCode) $failures.Add([string]$Code) }
    function Write-SashimiBoundedUnityTextArtifact {}
    function Read-SashimiComponentInventory { return [pscustomobject]@{Passed=$true} }
    function Protect-SashimiUnityOutputText { param($Text) return [string]$Text }
    function Publish-SashimiSanitizedTextArtifact {
        param($SourcePath,$DestinationPath,$MaximumSourceBytes)
        $text=[IO.File]::ReadAllText($SourcePath)
        Write-SashimiUtf8File $DestinationPath $text
        return [pscustomobject]@{Text=$text}
    }
    function Invoke-SashimiValidationProcess {
        param($Name,$Kind,$FilePath,$Arguments,$WorkingDirectory,$TimeoutSeconds,$Fixture,$FixtureGroup,$LogPath,$XmlPath,[switch]$DryRun)
        $events.Add([pscustomobject]@{Name=$Name;Kind=$Kind;Root=$WorkingDirectory})
        if($Kind -ceq 'Git') {
            if($Name -in @('GitControlSymbolicHead','GitControlBranch')) { return New-HostHeadProbeFixture }
            $text=switch($Name) {
                GitControlGitDirectory { '.git' }
                GitControlCommonDirectory { '.git' }
                GitControlHead { 'a'*40 }
                GitControlOriginUrls { $script:canonicalRepositoryUrl }
                GitControlPushUrls { $script:canonicalRepositoryUrl }
                GitControlHooksPath { 'NUL' }
                GitControlLocalConfig { "core.hookspath`nNUL`0remote.origin.url`n$($script:canonicalRepositoryUrl)`0" }
                GitControlRefs { "refs/heads/main`0$('c'*40)`0" }
                GitControlWorktreeIdentity { $WorkingDirectory }
                default { '' }
            }
            return New-HostHeadProbeFixture $text 0
        }
        Assert-HostTest ($Kind -ceq 'Unity' -and -not $DryRun) 'Unexpected native boundary in pipeline regression.'
        $expectedRoot=if($Name -ceq 'GeneratorRun2'){Join-Path $run.StatePath 'g/r'}else{$run.RepositoryPath}
        Assert-HostTest (Test-SashimiPathEqual $WorkingDirectory $expectedRoot) "Wrong project for $Name."
        Assert-HostTest ($Arguments.Count -eq 2 -and $Arguments[0] -ceq '-projectPath' -and (Test-SashimiPathEqual $Arguments[1] $expectedRoot)) "Wrong project argument for $Name."
        Assert-HostTest (Test-SashimiPathEqual $script:gitControlBaseline.CanonicalWorkTree $expectedRoot) "Wrong Git baseline during $Name."
        if($Name -ceq 'GeneratorRun1') {
            $child=Join-Path $run.StatePath 'g/r'
            Assert-HostTest (@($events|Where-Object {$_.Kind -ceq 'Git' -and $_.Name -ceq 'GitControlWorktreeIdentity' -and (Test-SashimiPathEqual $_.Root $child)}).Count -eq 1) 'The full child Git baseline was not pinned before Run1.'
            if($scenario -ceq 'run1-child-drift') { Write-SashimiUtf8File (Join-Path $child '.git/info/exclude') 'Run1 changed child control' }
        }
        if($Name -ceq 'GeneratorRun2' -and $scenario -ceq 'run2-child-drift') {
            Write-SashimiUtf8File (Join-Path $WorkingDirectory '.git/info/exclude') 'Run2 changed its control'
        }
        Write-SashimiUtf8File $LogPath 'Fixture Unity completed.'
        if($XmlPath) { Write-SashimiUtf8File $XmlPath '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-case name="fixture" result="Passed" /></test-run>' }
        return [pscustomobject]@{ProcessId=$null;ExitCode=0;Succeeded=$true;TimedOut=$false;Crashed=$false;
            TerminationConfirmed=$true;KillOnCloseJobAssigned=$true;RemainingDescendantProcessIds=@();DurationMilliseconds=1;StdOut='';StdErr=''}
    }
    foreach($scenario in @('valid','run1-child-drift','run2-child-drift')) {
        $runRoot=Join-Path $script:temporaryRoot "review-pipeline-$scenario"
        $run=New-SashimiRunWorkspace $runRoot
        $normalizedProjectPath=$run.RepositoryPath; $normalizedArtifactsPath=$run.ArtifactsPath
        $script:unityArtifactStateRoot=$run.StatePath
        Write-SashimiUtf8File (Join-Path $run.RepositoryPath '.git/HEAD') (('a'*40)+"`n")
        Write-SashimiUtf8File (Join-Path $run.RepositoryPath 'Assets/Generated/result.asset') 'unchanged generated result'
        $config=[pscustomobject]@{RunRoot=$runRoot}; $ReviewRunId=$run.RunId
        $reviewDriftContext=[pscustomobject]@{RunId=$run.RunId;Status='';MarkerSha256=(Get-FileHash $run.MarkerPath -Algorithm SHA256).Hash}
        $script:gitExecutable='fixture-git'; $script:gitTimeout=10; $script:gitControlFixture=$null
        $script:canonicalRepositoryUrl='https://github.com/DongGyunLeeeee/sashimi-boy-unity.git'
        $script:gitControlSnapshotSequence=0; $script:gitControlPassed=$false; $script:gitControlSecurityFailure=$false
        $script:gitControlBaseline=$null; $script:reviewerGeneratorContext=$null; $script:rawValidationCleanupSafe=$true
        $script:unityLogMaximumBytes=4096; $script:unityXmlMaximumBytes=4096
        $events=[Collections.Generic.List[object]]::new(); $checks=[Collections.Generic.List[string]]::new(); $failures=[Collections.Generic.List[string]]::new()
        $ownedUnityProcessIds=[Collections.Generic.List[int]]::new()
        $preUnityScopePassed=$true; $fixture=$null; $DryRun=$false; $validationDefinition=[pscustomobject]@{Id='detached-pipeline'}
        $IssueNumber=5281; $determinismPaths=@('Assets/Generated/result.asset'); $screenshotPaths=@(); $previewPaths=@()
        $unityExecutable='fixture-unity'; $unityTimeout=1; $generatorTimeout=1
        foreach($argumentName in @('compileArguments','generatorRun1Arguments','generatorRun2Arguments','inventoryArguments','editArguments','playArguments')) {
            Set-Variable $argumentName @('-projectPath',$normalizedProjectPath)
        }
        foreach($logName in @('compileLog','compileRawLog','generatorRun1Log','generatorRun1RawLog','generatorRun2Log','generatorRun2RawLog','inventoryLog','inventoryRawLog','editLog','editRawLog','editXml','editRawXml','playLog','playRawLog','playXml','playRawXml')) {
            Set-Variable $logName (Join-Path $run.ArtifactsPath $logName)
        }
        $stages=[ordered]@{CompileImport=$null;GeneratorRun1=$null;GeneratorRun2=$null;ComponentInventory=$null;EditMode=$null;PlayMode=$null}
        $result=[ordered]@{Determinism=[ordered]@{Run1Snapshot=@();Run2Snapshot=@();Passed=$null;Comparisons=@()};ComponentInventory=$null}
        if($scenario -ceq 'valid') {
            & $pipeline
            Assert-HostTest ($failures.Count -eq 0 -and $result.Determinism.Passed) 'Valid copied Reviewer pipeline did not pass.'
            Assert-HostTest (@($stages.Values|Where-Object {$null -eq $_ -or -not $_.Success}).Count -eq 0) 'Not all six real stage functions passed.'
            Assert-HostTest ($checks.Contains('GitControl:before Unity stage GeneratorRun2') -and $checks.Contains('GitControl:after Unity stage GeneratorRun2')) 'Run2 was not checked on both boundaries.'
            $expectedStages='CompileImport,GeneratorRun1,GeneratorRun2,ComponentInventory,EditMode,PlayMode'
        }
        else {
            Assert-HostThrows { & $pipeline } 'Git.control'
            Assert-HostTest ($failures.Contains('GitControlDrift')) 'Changed child control did not produce drift evidence.'
            Assert-HostTest ($null -eq $stages.GeneratorRun2 -and $null -eq $stages.ComponentInventory -and $null -eq $stages.EditMode -and $null -eq $stages.PlayMode) 'A failed Git boundary admitted later stages.'
            $expectedStages=if($scenario -ceq 'run1-child-drift'){'CompileImport,GeneratorRun1'}else{'CompileImport,GeneratorRun1,GeneratorRun2'}
        }
        $actualStages=(@($events|Where-Object Kind -CEQ 'Unity'|ForEach-Object Name) -join ',')
        Assert-HostTest ($actualStages -ceq $expectedStages) "Wrong native stage order for $scenario."
        Assert-HostTest (Test-SashimiPathEqual $script:gitControlBaseline.CanonicalWorkTree $run.RepositoryPath) 'Generator finally did not restore the primary baseline.'
        Assert-SashimiUnityGitControlUnchanged $run.RepositoryPath 'after pipeline'
    }
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
