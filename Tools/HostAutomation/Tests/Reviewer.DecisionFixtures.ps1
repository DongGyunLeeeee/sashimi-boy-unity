# Loaded only by the marked, fake-executable host fixture harness.
function Invoke-HostReviewerDecisionRegression {
    $caseIndex=0
    foreach ($category in @('Defect','HumanCheck','Infrastructure','Unverified','UnityUnavailable')) {
        $caseIndex++; $issue=5390+$caseIndex; $head='a' * 40
        $bundle=New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $issue -PinnedSha $head -DeliverySha $head -StaleSha ('b' * 40)
        $selection=$bundle.Selection
        $selection.Role='Reviewer'; $selection.Mode='Review'; $selection.Status='Review'
        Write-HostTestFile $bundle.SelectionPath ($selection | ConvertTo-Json -Depth 32)
        $projectPath=Join-Path $bundle.ScenarioRoot 'project.json'
        $project=Read-SashimiJsonFile $projectPath
        $project.data.node.statusValue.name='Review'
        Write-HostTestFile $projectPath ($project | ConvertTo-Json -Depth 32)
        $project.data.node.statusValue.name=if ($category -ceq 'Defect') { 'In Progress' } else { 'Verification' }
        Write-HostTestFile (Join-Path $bundle.ScenarioRoot 'project-after.json') ($project | ConvertTo-Json -Depth 32)
        $payload=New-HostCodexResult -RunId $bundle.Run.RunId -IssueNumber $issue -HeadSha $head -Role Reviewer -Mode Review -PullRequestNumber $selection.PullRequestNumber
        $finding=[ordered]@{
            severity='Major'; category='Defect'; basis='CodePath'; title='Confirmed regression one'
            evidence='The second call reads a field cleared by the first call.'
            requirement='Repeated interaction must succeed.'; location='Example.cs:Interact'
            expected='Both calls succeed'; actual='Second call dereferences null'
            reproduction='Trace Clear followed by Interact in the same scene.'; recommendation='Keep a valid reference.'
        }
        if ($category -in @('HumanCheck','Infrastructure','Unverified')) {
            $finding.category=$category; $finding.basis='None'; $finding.title="$category observation"
            $finding.evidence='Owner must check camera feel in the playable scene.'
            foreach ($field in @('requirement','location','expected','actual','reproduction','recommendation')) { $finding[$field]='' }
        }
        $payload.findings=@($finding)
        if ($category -ceq 'Defect') {
            $second=($finding | ConvertTo-Json | ConvertFrom-Json -AsHashtable)
            $second.title='Confirmed regression two'; $second.location='Example.cs:Buy'
            $payload.findings+=@($second)
        }
        $codexFixture=New-HostCodexFixtureFile -Name "review-decision-$category" -Result $payload -Events @(
            [ordered]@{type='item.completed';item=[ordered]@{id='review-decision';type='agent_message';text=($payload | ConvertTo-Json -Depth 32 -Compress)}},
            [ordered]@{type='turn.completed'}
        )
        if ($category -ceq 'UnityUnavailable') {
            $unity=Read-SashimiJsonFile $bundle.UnityFixture
            $unity.Stages | Add-Member -NotePropertyName CompileImport -NotePropertyValue ([pscustomobject]@{TimedOut=$true}) -Force
            Write-HostTestFile $bundle.UnityFixture ($unity | ConvertTo-Json -Depth 32)
        }
        $process=Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath=$script:fakeConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            CodexFixturePath=$codexFixture; UnityFixturePath=$bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='review-decisions'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot; SASHIMI_FAKE_STATUS_STATE=$bundle.StatusState
            SASHIMI_FAKE_GIT_PINNED_SHA=$head; SASHIMI_FAKE_GIT_HEAD_SHA=$head
            SASHIMI_FAKE_GIT_MAIN_SHA=('1' * 40); SASHIMI_FAKE_GIT_STATUS=''
        } -TimeoutSeconds 120
        $result=ConvertFrom-LastHostJson $process.StdOut
        $shouldComplete=$category -in @('Defect','HumanCheck')
        Assert-HostTest (($process.ExitCode -eq 0) -eq $shouldComplete) "$category runner outcome was wrong: $($result.Error)"
        Assert-HostTest (-not $result.ReviewerPushAttempted) "$category review pushed code."
        if ($category -ceq 'Defect') {
            Assert-HostTest ($result.Transition -ceq 'Review->In Progress') 'Confirmed defects did not return to implementation.'
            $findingText=[IO.File]::ReadAllText((Join-Path $bundle.Run.ArtifactsPath 'ReviewFinding.md'))
            Assert-HostTest ($findingText.Contains('Confirmed regression one') -and $findingText.Contains('Confirmed regression two')) 'The real reviewer path dropped a blocking finding.'
            Assert-HostTest (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'ReviewFixHandoff.md')) 'Confirmed defects lack a handoff.'
        }
        elseif ($category -ceq 'HumanCheck') {
            Assert-HostTest ($result.Transition -ceq 'Review->Verification') 'Human-only check returned to implementation.'
            $checklist=[IO.File]::ReadAllText((Join-Path $bundle.Run.ArtifactsPath 'OwnerVerificationChecklist.md'))
            Assert-HostTest ($checklist.Contains('HumanCheck observation')) 'Human check was lost from the Owner checklist.'
            Assert-HostTest (-not (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'ReviewFixHandoff.md'))) 'Human check produced a repair handoff.'
        }
        else {
            Assert-HostTest ([string]::IsNullOrEmpty($result.Transition) -and -not (Test-Path $bundle.StatusState)) "$category changed Project state."
            foreach ($name in @('ReviewFixHandoff.md','ReviewFinding.md','OwnerVerificationChecklist.md')) {
                Assert-HostTest (-not (Test-Path (Join-Path $bundle.Run.ArtifactsPath $name))) "$category published a code-defect or PASS artifact."
            }
        }
    }
}
