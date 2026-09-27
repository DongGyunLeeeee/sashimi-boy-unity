# Loaded only by the owned, fake-executable Host test harness.
function Assert-HostResumeEvidence {
    param($Bundle, [object[]]$Calls)
    $edits = @($Calls | Where-Object { $_.Tool -ceq 'gh' -and $_.Arguments[0] -ceq 'pr' -and $_.Arguments[1] -ceq 'edit' })
    $transitions = @($Calls | Where-Object { $_.Tool -ceq 'gh' -and (@($_.Arguments) -join ' ') -match 'mutation' })
    Assert-HostTest ($edits.Count -eq 1 -and $edits[0].Arguments[2] -ceq [string]$Bundle.Selection.PullRequestNumber) 'Resume did not update exactly the existing PR.'
    Assert-HostTest ($transitions.Count -eq 1 -and [array]::IndexOf($Calls,$edits[0]) -lt [array]::IndexOf($Calls,$transitions[0])) 'Review transition preceded evidence publication/read-back.'
    $body = [IO.File]::ReadAllText((Join-Path $Bundle.ScenarioRoot 'pr-body.txt'))
    Assert-HostTest ($body.StartsWith('<!-- sashimi-boy-host-validation:start:v1 -->') -and $body.EndsWith('Synthetic existing PR body.')) 'Resume lost the original PR description.'
    Assert-HostTest ($body.Contains("Validated delivery head: $($Bundle.DeliverySha)") -and $body.Contains("Host run: $($Bundle.Run.RunId)")) 'Resume evidence lacks exact head/run provenance.'
    Assert-HostTest ($body.Contains('EditMode: PASS (native exit 0); tests ') -and $body.Contains('PlayMode: PASS (native exit 0); tests ') -and -not $body.Contains('exit planned')) 'Resume evidence did not use executed Unity stage results.'
}

function Invoke-HostResumeEvidenceFailureRegression {
    $index = 0
    foreach ($mode in @('Fail','StaleReadBack','ConcurrentHead')) {
        $index++; $head='a' * 40
        $bundle=New-HostResumeFixtureBundle -Mode DeliveryResume -IssueNumber (5410+$index) -PinnedSha $head -DeliverySha $head -StaleSha ('f'*40)
        $process=Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath=$script:fakeConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            CodexFixturePath=$bundle.CodexFixture; UnityFixturePath=$bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='developer-current'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot; SASHIMI_FAKE_STATUS_STATE=$bundle.StatusState
            SASHIMI_FAKE_GIT_PINNED_SHA=$head; SASHIMI_FAKE_GIT_HEAD_SHA=$head; SASHIMI_FAKE_GIT_BRANCH=$bundle.Branch
            SASHIMI_FAKE_GIT_STATUS=''; SASHIMI_FAKE_PR_EDIT_MODE=$mode
        } -TimeoutSeconds 120
        $result=ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ($process.ExitCode -ne 0 -and -not $result.Success -and -not (Test-Path $bundle.StatusState)) "$mode evidence failure reached Review: $($result.Error)"
        Assert-HostTest (-not (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'HandoffCompletion.md'))) "$mode evidence failure produced a completion marker."
    }
}

function Invoke-HostReviewerCachedLfsRegression {
    $config=Import-SashimiHostConfig $script:fakeConfigPath
    $index=0
    foreach ($mode in @('Warm','Missing','Pointer','Corrupt')) {
        $index++; $head='a'*40
        $bundle=New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber (5420+$index) -PinnedSha $head -DeliverySha $head -StaleSha ('b'*40)
        $selection=$bundle.Selection
        $selection.Role='Reviewer'; $selection.Mode='Review'; $selection.Status='Review'
        Write-HostTestFile $bundle.SelectionPath ($selection | ConvertTo-Json -Depth 32)
        $project=Read-SashimiJsonFile (Join-Path $bundle.ScenarioRoot 'project.json')
        $project.data.node.statusValue.name='Review'
        Write-HostTestFile (Join-Path $bundle.ScenarioRoot 'project.json') ($project | ConvertTo-Json -Depth 32)
        $project.data.node.statusValue.name='Verification'
        Write-HostTestFile (Join-Path $bundle.ScenarioRoot 'project-after.json') ($project | ConvertTo-Json -Depth 32)
        $payload=New-HostCodexResult -RunId $bundle.Run.RunId -IssueNumber $selection.IssueNumber -HeadSha $head -Role Reviewer -Mode Review -PullRequestNumber $selection.PullRequestNumber
        $codexFixture=New-HostCodexFixtureFile -Name "review-cache-$mode" -Result $payload -Events @(
            [ordered]@{type='item.completed';item=[ordered]@{id='review-cache';type='agent_message';text=($payload | ConvertTo-Json -Depth 32 -Compress)}},
            [ordered]@{type='turn.completed'}
        )
        $bytes="non-empty LFS fixture $mode"; $oid=Get-SashimiTextSha256 $bytes
        $manifest=ConvertTo-SashimiJson @{files=@(@{name='Assets/FixtureLfs.bin';oid=$oid;size=[Text.Encoding]::UTF8.GetByteCount($bytes);oid_type='sha256';version='https://git-lfs.github.com/spec/v1'})}
        $manifestPath=Join-Path $bundle.ScenarioRoot 'lfs-manifest.json'
        Write-HostTestFile $manifestPath $manifest
        if ($mode -cne 'Missing') {
            $seed=New-SashimiRunWorkspace -RunRoot ([string]$config.RunRoot)
            Write-HostTestFile (Join-Path $seed.RepositoryPath ".git/lfs/objects/$($oid.Substring(0,2))/$($oid.Substring(2,2))/$oid") $bytes
            $stored=Sync-SashimiLfsObjectCache -RepositoryPath $seed.RepositoryPath -RunRoot ([string]$config.RunRoot) -ManifestJson $manifest -Mode Store
            Assert-HostTest ($stored.Stored -eq 1) 'Reviewer test did not seed a verified non-empty cache.'
        }
        $sentinel=Join-Path $bundle.ScenarioRoot 'early-smudge.txt'
        $envs=@{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='review-cache'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot; SASHIMI_FAKE_STATUS_STATE=$bundle.StatusState
            SASHIMI_FAKE_GIT_PINNED_SHA=$head; SASHIMI_FAKE_GIT_HEAD_SHA=$head; SASHIMI_FAKE_GIT_MAIN_SHA=('1'*40); SASHIMI_FAKE_GIT_STATUS=''
            SASHIMI_FAKE_LFS_MANIFEST=$manifestPath; SASHIMI_FAKE_LFS_OID=$oid; SASHIMI_FAKE_LFS_CHECKOUT=$mode; SASHIMI_FAKE_LFS_EARLY_SMUDGE=$sentinel
        }
        if ($mode -ceq 'Warm') {
            # Negative controls prove both native commands detect the original
            # bug even with canonical routing and no malicious .lfsconfig.
            foreach ($verb in @('switch','merge')) {
                $control=$envs.Clone()
                $control.SASHIMI_FAKE_LFS_EARLY_SMUDGE=Join-Path $bundle.ScenarioRoot "$verb-negative.txt"
                $gitArguments=if ($verb -ceq 'switch') { @('switch','--detach',$head) } else { @('merge','--no-ff','--no-edit',$head) }
                $native=Invoke-SashimiHostProcess -FilePath ([string]$config.GitExecutable) -ArgumentList (@('-c','core.hooksPath=NUL','-C',$bundle.Run.RunPath)+$gitArguments) -Environment $control -TimeoutSeconds 30 -Kind Git
                Assert-HostTest ($native.ExitCode -eq 128 -and (Test-Path $control.SASHIMI_FAKE_LFS_EARLY_SMUDGE)) "$verb negative control failed to detect early smudge."
            }
        }
        $auditBefore=@(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $process=Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath=$script:fakeConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            CodexFixturePath=$codexFixture; UnityFixturePath=$bundle.UnityFixture
        } -Environment $envs -TimeoutSeconds 120
        $result=ConvertFrom-LastHostJson $process.StdOut
        $calls=@((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
        Assert-HostTest (-not (Test-Path $sentinel)) "$mode Reviewer attempted smudge before cache restore."
        foreach ($verb in @('switch','merge')) {
            Assert-HostTest (@($calls | Where-Object { $_.Tool -ceq 'git' -and @($_.Arguments) -ccontains $verb }).Count -eq 1) "$mode did not reach the $verb preparation step."
        }
        $pulls=@($calls | Where-Object { $_.Tool -ceq 'lfs' -and @($_.Arguments) -ccontains 'pull' })
        $checkouts=@($calls | Where-Object { $_.Tool -ceq 'lfs' -and @($_.Arguments) -ccontains 'checkout' })
        if ($mode -ceq 'Warm') {
            Assert-HostTest ($process.ExitCode -eq 0 -and $result.Transition -ceq 'Review->Verification') "Warm Reviewer failed: $($result.Error)"
            $cache=Read-SashimiJsonFile (Join-Path $bundle.Run.StatePath 'LfsCache.Initial.json')
            Assert-HostTest ($cache.Restore.Hits -eq 1 -and $cache.Strategy -ceq 'checkout' -and $checkouts.Count -eq 1 -and $pulls.Count -eq 0) 'Warm Reviewer did not reuse the cache without pull.'
            Assert-HostTest (@($calls | Where-Object { $_.Tool -ceq 'lfs' -and @($_.Arguments) -ccontains 'fsck' }).Count -eq 1) 'Warm Reviewer omitted final fsck.'
        }
        else {
            Assert-HostTest ($process.ExitCode -ne 0 -and [string]::IsNullOrEmpty($result.Transition) -and -not (Test-Path $bundle.StatusState)) "$mode incorrectly completed review."
            Assert-HostTest (-not (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'Codex/CodexResult.json')) -and -not (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'Unity/UnityValidation.Summary.json'))) "$mode reached Codex/Unity with unavailable or corrupt LFS bytes."
            if ($mode -ceq 'Missing') {
                Assert-HostTest ($pulls.Count -eq 1 -and $checkouts.Count -eq 0 -and $result.Error -match 'budget exceeded') 'Cache miss did not fail at the canonical pull boundary.'
            }
            else {
                Assert-HostTest ($pulls.Count -eq 0 -and $checkouts.Count -eq 1 -and $result.Error -match 'pinned manifest') "$mode checkout exit zero bypassed materialized-byte verification."
            }
        }
    }
}
