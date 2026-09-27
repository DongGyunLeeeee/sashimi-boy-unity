# Loaded only by the marked Host harness. All Git/GitHub/model boundaries are fake.
function Invoke-HostRolloutQueueAuthorRegression {
    $caseIndex = 0
    foreach ($mode in @('NewWork','Review','ReviewFix','DeliveryResume')) {
        $caseIndex++
        $status = switch ($mode) { NewWork { 'Ready' }; Review { 'Review' }; default { 'In Progress' } }
        $handoff = if ($mode -in @('ReviewFix','DeliveryResume')) { $mode } else { 'None' }
        $number = 5500 + ($caseIndex * 10)
        $owner = New-HostQueueItem -IssueNumber $number -Status $status -Priority P3 -HandoffMode $handoff
        $outsider = New-HostQueueItem -IssueNumber ($number+1) -Status $status -Priority P0 -HandoffMode $handoff
        $outsider.IssueAuthorLogin = 'untrusted-outsider'
        $empty = New-HostQueueItem -IssueNumber ($number+2) -Status $status -Priority P0 -HandoffMode $handoff
        $empty.IssueAuthorLogin = ''
        $missing = New-HostQueueItem -IssueNumber ($number+3) -Status $status -Priority P0 -HandoffMode $handoff
        $missing.Remove('IssueAuthorLogin')
        $fixture = New-HostQueueFixtureFile -Name "rollout-author-$mode" -Items @($owner,$outsider,$empty,$missing)
        $native = Invoke-HostQueueFixture $fixture
        $result = ConvertFrom-LastHostJson $native.StdOut
        Assert-HostTest ($native.ExitCode -eq 0 -and $result.Selected -and $result.IssueNumber -eq $number) "$mode selected an unauthorized Issue or rejected the Owner."
        Assert-HostTest ($result.Mode -ceq $mode -and $result.IssueAuthorLogin -ceq 'DongGyunLeeeee') "$mode lost the verified Issue author."
        Assert-HostTest (@($result.ExcludedCandidates | Where-Object Reason -eq 'UnauthorizedIssueAuthor').Count -eq 3) "$mode accepted a missing/empty/unknown Issue author."
    }
}

function Invoke-HostRolloutLiveAuthorRegression {
    $allowedConfig = Read-SashimiJsonFile $script:fakeConfigPath
    $allowedConfig.Security.AuthorizedPrAuthors = @('DongGyunLeeeee','allowed-maintainer')
    $allowedConfigPath = Join-Path $script:temporaryRoot 'rollout-allowed-author.config.json'
    Write-HostTestFile $allowedConfigPath ($allowedConfig | ConvertTo-Json -Depth 64)
    $caseIndex = 0
    foreach ($authorKind in @('Owner','Allowed','Unknown','Empty','Missing','Null')) {
        $caseIndex++
        $authorConfigPath = if ($authorKind -ceq 'Allowed') { $allowedConfigPath } else { $script:fakeConfigPath }
        $authorAllowed = $authorKind -in @('Owner','Allowed')
        $scenarioRoot = Join-Path $script:temporaryRoot "rollout-live-author-$authorKind"
        [void](New-HostLiveQueueScenario -Root $scenarioRoot)
        foreach ($page in @('items-1.json','items-2.json')) {
            $pagePath = Join-Path $scenarioRoot $page
            $data = Read-SashimiJsonFile $pagePath
            foreach ($node in $data.data.user.projectV2.items.nodes) {
                switch ($authorKind) {
                    Allowed { $node.content.author.login = 'allowed-maintainer' }
                    Unknown { $node.content.author.login = 'untrusted-outsider' }
                    Empty { $node.content.author.login = '' }
                    Missing { $node.content.PSObject.Properties.Remove('author') }
                    Null { $node.content.author = $null }
                }
            }
            Write-HostTestFile $pagePath ($data | ConvertTo-Json -Depth 64)
        }
        $queue = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Get-SashimiProjectQueue.ps1') -Parameters @{
            ConfigPath=$authorConfigPath
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='queue-pagination'
            SASHIMI_FAKE_SCENARIO_ROOT=$scenarioRoot
        }
        $selected = ConvertFrom-LastHostJson $queue.StdOut
        Assert-HostTest ($queue.ExitCode -eq 0 -and $selected.Success -and ([bool]$selected.Selected -eq $authorAllowed)) "Live-shaped queue mishandled $authorKind author: $(Get-SashimiPropertyValue $selected 'Error' '')"

        $issue = 5570 + $caseIndex
        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $issue -PinnedSha ('a'*40) -DeliverySha ('a'*40) -StaleSha ('b'*40)
        $projectPath = Join-Path $bundle.ScenarioRoot 'project.json'
        $project = Read-SashimiJsonFile $projectPath
        switch ($authorKind) {
            Allowed { $project.data.node.content.author.login = 'allowed-maintainer' }
            Unknown { $project.data.node.content.author.login = 'untrusted-outsider' }
            Empty { $project.data.node.content.author.login = '' }
            Missing { $project.data.node.content.PSObject.Properties.Remove('author') }
            Null { $project.data.node.content.author = $null }
        }
        Write-HostTestFile $projectPath ($project | ConvertTo-Json -Depth 64)
        $published = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$authorConfigPath; Action='RevalidateIssue'; Role='Developer'; IssueNumber=$issue
            ProjectItemId=$bundle.Selection.ProjectItemId; PullRequestNumber=$bundle.Selection.PullRequestNumber
            PinnedHeadSha=('a'*40); PinnedHeadRef=$bundle.Selection.PullRequestHeadRef
            PinnedIssueUpdatedAt=$bundle.Selection.IssueUpdatedAt; PinnedIssueBodySha256=$bundle.Selection.IssueBodySha256
            PinnedConversationSha256=$bundle.Selection.ConversationSha256
            PinnedPullRequestContentSha256=$bundle.Selection.PullRequestContentSha256; FromStatus='In Progress'
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='rollout-author'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot
        }
        $result = ConvertFrom-LastHostJson $published.StdOut
        Assert-HostTest (($published.ExitCode -eq 0) -eq $authorAllowed) "Live-shaped publication contract mishandled $authorKind author: $($result.Error)"
        Assert-HostTest (-not $result.MutationAttempted) 'Read-only Issue revalidation mutated GitHub.'
        if (-not $authorAllowed) { Assert-HostTest ($result.Error -match 'Issue author is missing or unauthorized') 'Author refusal stopped at the wrong boundary.' }
    }
}

function Invoke-HostRolloutReviewerDiffRegression {
    $codexRoot = Join-Path $script:temporaryRoot 'rollout-reviewer-codex'
    [void](New-Item -ItemType Directory -Path $codexRoot -Force)
    $diffCodex = New-HostFakeCodexAdapter -Root $codexRoot
    $diffConfigPath = New-HostTestConfig -GitExecutable $script:fakeTools.Git -GitLfsExecutable $script:fakeTools.GitLfs -GitHubCli $script:fakeTools.GitHub -CodexExecutable $diffCodex.Path -UnityExecutable $script:fakeTools.Git
    $changedPaths = "D`tAssets/Removed.cs`nM`tAssets/Example.cs`nA`tAssets/Renamed.cs`nM`tAssets/Image.png"
    $patchText = @'
diff --git a/Assets/Removed.cs b/Assets/Removed.cs
deleted file mode 100644
--- a/Assets/Removed.cs
+++ /dev/null
@@ -1 +0,0 @@
-class Removed { const int PreviousValue = 17; }
diff --git a/Assets/Example.cs b/Assets/Example.cs
--- a/Assets/Example.cs
+++ b/Assets/Example.cs
@@ -1 +1 @@
-const int BeforeValue = 10;
+const int AfterValue = 20;
diff --git a/Assets/Renamed.cs b/Assets/Renamed.cs
new file mode 100644
--- /dev/null
+++ b/Assets/Renamed.cs
@@ -0,0 +1 @@
+class Renamed { const int PreviousValue = 17; }
diff --git a/Assets/Image.png b/Assets/Image.png
Binary files a/Assets/Image.png and b/Assets/Image.png differ
'@
    $caseIndex=0
    foreach ($case in @('Complete','Empty','Mismatch','Oversized','PromptOversized')) {
        $caseIndex++; $issue=5590+$caseIndex; $head='a'*40
        $bundle=New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $issue -PinnedSha $head -DeliverySha $head -StaleSha ('b'*40)
        $selection=$bundle.Selection; $selection.Role='Reviewer'; $selection.Mode='Review'; $selection.Status='Review'
        if ($case -ceq 'PromptOversized') {
            $selection.Conversation=@([ordered]@{Kind='IssueComment'; Body=('x'*2MB)})
        }
        Write-HostTestFile $bundle.SelectionPath ($selection | ConvertTo-Json -Depth 32)
        $projectPath=Join-Path $bundle.ScenarioRoot 'project.json'
        $project=Read-SashimiJsonFile $projectPath; $project.data.node.statusValue.name='Review'
        Write-HostTestFile $projectPath ($project | ConvertTo-Json -Depth 32)
        $project.data.node.statusValue.name='Verification'
        Write-HostTestFile (Join-Path $bundle.ScenarioRoot 'project-after.json') ($project | ConvertTo-Json -Depth 32)
        $payload=New-HostCodexResult -RunId $bundle.Run.RunId -IssueNumber $issue -HeadSha $head -Role Reviewer -Mode Review -PullRequestNumber $selection.PullRequestNumber
        Write-HostTestFile $diffCodex.ResultPath ($payload | ConvertTo-Json -Depth 32 -Compress)
        if (Test-Path -LiteralPath $diffCodex.InputPath) { Remove-Item -LiteralPath $diffCodex.InputPath -Force }
        $patchPath=Join-Path $bundle.ScenarioRoot 'review.patch'
        $patch=if ($case -ceq 'Oversized') { $patchText + ('x'*1MB) } elseif ($case -in @('Empty','Mismatch')) { '' } else { $patchText }
        Write-HostTestFile $patchPath $patch
        $native=Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath=$diffConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            UnityFixturePath=$bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='review-decisions'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot; SASHIMI_FAKE_STATUS_STATE=$bundle.StatusState
            SASHIMI_FAKE_GIT_PINNED_SHA=$head; SASHIMI_FAKE_GIT_HEAD_SHA=$head
            SASHIMI_FAKE_GIT_MAIN_SHA=('1'*40); SASHIMI_FAKE_GIT_STATUS=''
            SASHIMI_FAKE_GIT_REVIEW_PATHS=$(if ($case -ceq 'Empty') { '' } else { $changedPaths })
            SASHIMI_FAKE_GIT_REVIEW_PATCH_FILE=$patchPath
        } -TimeoutSeconds 120
        $result=ConvertFrom-LastHostJson $native.StdOut
        $shouldPass=$case -in @('Complete','Empty')
        Assert-HostTest (($native.ExitCode -eq 0) -eq $shouldPass) "$case diff handling failed: $($result.Error)"
        if ($shouldPass) {
            Assert-HostTest ($result.Transition -ceq 'Review->Verification') 'Complete review lost its valid transition.'
            $prompt=[IO.File]::ReadAllText($diffCodex.InputPath)
            Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $bundle.Run.StatePath 'ReviewerCodexPrompt.txt'))) 'Temporary review prompt was retained after model execution.'
            Assert-HostTest ($prompt.Contains('"BaselineSha": "' + ('1'*40) + '"') -and $prompt.Contains('"SyntheticCommitSha": "' + $head + '"')) 'Review context lost exact commit pins.'
            if ($case -ceq 'Complete') {
                foreach ($text in @('PreviousValue = 17','BeforeValue = 10','AfterValue = 20','Assets/Removed.cs','Assets/Renamed.cs','Binary files a/Assets/Image.png')) {
                    Assert-HostTest ($prompt.Contains($text)) "Model prompt omitted changed source data: $text"
                }
            } else { Assert-HostTest ($prompt.Contains('"NoChanges": true')) 'An empty exact diff was not explicitly represented.' }
        } else {
            Assert-HostTest ([string]::IsNullOrEmpty($result.Transition) -and -not (Test-Path $bundle.StatusState)) 'Incomplete diff reached a Project transition.'
            Assert-HostTest (-not (Test-Path (Join-Path $bundle.Run.ArtifactsPath 'Codex'))) 'Incomplete diff started model analysis.'
            Assert-HostTest (-not (Test-Path -LiteralPath $diffCodex.InputPath)) 'Incomplete diff reached the model input transport.'
            $expectedError=switch ($case) { Mismatch { 'manifest and patch disagree' }; Oversized { '1 MiB context limit' }; PromptOversized { '2 MiB limit' } }
            Assert-HostTest ($result.Error -match $expectedError) "$case failed at an unrelated boundary: $($result.Error)"
        }
    }
}
