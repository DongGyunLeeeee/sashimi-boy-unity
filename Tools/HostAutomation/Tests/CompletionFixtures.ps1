# Loaded only by the marker-owned self-contained fixture harness.
Invoke-HostTestCase 'CompletionGraphQLFixtureRejectsNonexistentFieldType' {
    $process = Invoke-SashimiHostProcess -FilePath $script:fakeTools.GitHub -Kind GitHub -ArgumentList @(
        'api','graphql','-f','query=query HostProjectFields{node{id ... on ProjectV2RepositoryField{id name}}}'
    ) -Environment @{SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath} -TimeoutSeconds 10
    Assert-HostTest (-not $process.Succeeded -and $process.ExitCode -eq 1 -and $process.StdErr -match 'No such type') "The GitHub fixture did not reject an impossible fragment at its schema boundary: exit=$($process.ExitCode); error=$($process.StdErr)."
}

Invoke-HostTestCase 'CompletionPilotPinsOneEligibleIssueWithoutFallback' {
    $fixture = New-HostQueueFixtureFile -Name 'pinned-pilot' -Items @(
        (New-HostQueueItem -IssueNumber 5211 -Status Review -Priority P0),
        (New-HostQueueItem -IssueNumber 5212 -Status Ready -Priority P1),
        (New-HostQueueItem -IssueNumber 5213 -Status Verification -Priority P0)
    )
    foreach ($number in @(5212,5213,5214)) {
        $process = Invoke-HostTestScript (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1') @{
            ConfigPath=$script:configPath;QueueFixturePath=$fixture;DryRun=$true;Once=$true;IssueNumber=$number;
            MutexName=('Global\SashimiBoyPinnedPilot-' + $script:testRunId)
        }
        Assert-HostTest $process.Succeeded "Pinned preview failed: $($process.StdOut)"
        $result = ConvertFrom-LastHostJson $process.StdOut
        if ($number -eq 5212) {
            Assert-HostTest ($result.Selection.Selected -and $result.Selection.IssueNumber -eq $number -and $result.Selection.DispatchCount -eq 1) 'Pinned preview selected the higher-priority unrelated Review issue.'
        }
        else { Assert-HostTest (-not $result.Selection.Selected -and $result.State -ceq 'NoWork') 'Ineligible or absent pinned issue fell through to another issue.' }
    }
}

Invoke-HostTestCase 'CompletionArtifactFailureCannotRetainSuccessfulPublicResult' {
    $function = Get-HostTestFunctionScriptBlock (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1') 'Complete-OrchestratorArtifactOutput'
    Set-Item Function:Complete-OrchestratorArtifactOutput -Value $function
    $run = New-SashimiRunWorkspace -RunRoot (Join-Path $script:temporaryRoot 'final-artifact-runs')
    Write-HostTestFile (Join-Path $run.ArtifactsPath 'unexpected.txt') 'unvalidated'
    Write-HostTestFile (Join-Path $run.ArtifactsPath 'RunResult.json') '{"Success":true}'
    Write-HostTestFile (Join-Path $run.StatePath 'FinalResult.json') '{"Success":true}'
    $output = Complete-OrchestratorArtifactOutput -Workspace $run -Value ([ordered]@{Success=$true;ExitCode=0;State='Succeeded';Error=''})
    Assert-HostTest (-not $output.Success -and $output.ExitCode -eq 1 -and $output.Error -ceq 'ArtifactBoundaryFailed') 'Artifact boundary failure did not fail the run.'
    foreach ($path in @((Join-Path $run.ArtifactsPath 'RunResult.json'),(Join-Path $run.StatePath 'FinalResult.json'))) {
        Assert-HostTest (-not (Read-SashimiJsonFile $path).Success) 'A retained final result still claims success.'
    }
    Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $run.ArtifactsPath 'unexpected.txt'))) 'Unvalidated bytes remain in public Artifacts.'
    Assert-HostTest (@(Get-ChildItem -LiteralPath $run.StatePath -Directory -Filter '.unpublished-artifacts-*').Count -eq 1) 'Failed evidence was discarded instead of quarantined.'
    Assert-SashimiRunArtifactBoundary -RunPath $run.RunPath -RequireFinalSeal
}

Invoke-HostTestCase 'CompletionSourceToolsReadWriteAndRejectUnauthorizedAccess' {
    $root = Join-Path $script:temporaryRoot 'source-tool-fixture'
    Write-HostTestFile (Join-Path $root 'Example.txt') "value=1`n"
    Write-HostTestFile (Join-Path $root 'Example.txt.meta') "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`n"
    Write-HostTestFile (Join-Path $root 'Docs/Automation/SPEC_VERSION') '1.0.4'
    $hash = (Get-FileHash (Join-Path $root 'Example.txt')).Hash.ToLowerInvariant()
    $metaHash = (Get-FileHash (Join-Path $root 'Example.txt.meta')).Hash.ToLowerInvariant()
    $calls = @(
        @{name='read_file';arguments=@{path='Example.txt';startLine=1;lineCount=10}},
        @{name='write_file';arguments=@{path='Example.txt';expectedSha256=$hash;content="value=2`n"}},
        @{name='write_file';arguments=@{path='Example.txt';expectedSha256=$hash;content='stale'}},
        @{name='write_file';arguments=@{path='Example.txt.meta';expectedSha256=$metaHash;content="guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb`n"}},
        @{name='write_file';arguments=@{path='Nested/New.cs';expectedSha256='missing';content='// created'}},
        @{name='list_files';arguments=@{prefix='Nested/';offset=0}}
    )
    foreach ($path in @('../outside.txt','.git/config','C:/outside.txt','Assets/../outside.txt','Art/Source/source.cs','Packages/manifest.json','Assets/Scene.unity')) {
        $calls += @{name='write_file';arguments=@{path=$path;expectedSha256='missing';content='forbidden'}}
    }
    $id=0; $input = (($calls | ForEach-Object { $id++; @{jsonrpc='2.0';id=$id;method='tools/call';params=$_} | ConvertTo-Json -Depth 8 -Compress }) -join "`n") + "`n"
    $process = Invoke-SashimiHostProcess -FilePath $PowerShellPath -WorkingDirectory $root -TimeoutSeconds 30 `
        -ArgumentList @('-NoProfile','-NonInteractive','-File',(Join-Path $hostRoot 'Invoke-SashimiSourceServer.ps1'),'-RepositoryPath',$root,'-Role','Developer') -StandardInput $input
    Assert-HostTest $process.Succeeded "Source server failed: $($process.StdErr)"
    $rows = @($process.StdOut.Trim() -split '\r?\n' | ForEach-Object { $_ | ConvertFrom-Json -Depth 16 })
    Assert-HostTest ($rows.Count -eq $calls.Count) 'Source response count differs from request count.'
    foreach ($index in @(0,1,4,5)) { Assert-HostTest (-not $rows[$index].result.isError) "Allowed source request $index failed: $($rows[$index].result.content.text)" }
    foreach ($index in @(2,3) + @(6..($rows.Count-1))) { Assert-HostTest $rows[$index].result.isError "Forbidden source request $index succeeded." }
    Assert-HostTest ([IO.File]::ReadAllText((Join-Path $root 'Example.txt')) -ceq "value=2`n") 'Exact source edit did not survive negative requests.'
    Assert-HostTest ((Get-FileHash (Join-Path $root 'Example.txt.meta')).Hash.ToLowerInvariant() -ceq $metaHash) 'Meta GUID was changed.'
    $review = Invoke-SashimiHostProcess -FilePath $PowerShellPath -WorkingDirectory $root -TimeoutSeconds 30 `
        -ArgumentList @('-NoProfile','-NonInteractive','-File',(Join-Path $hostRoot 'Invoke-SashimiSourceServer.ps1'),'-RepositoryPath',$root,'-Role','Reviewer') -StandardInput $input
    Assert-HostTest $review.Succeeded 'Reviewer source server failed.'
    $reviewRows = @($review.StdOut.Trim() -split '\r?\n' | ForEach-Object { $_ | ConvertFrom-Json -Depth 16 })
    Assert-HostTest (-not $reviewRows[0].result.isError -and $reviewRows[1].result.isError) 'Reviewer source access was not read only.'
    $current = (Get-FileHash (Join-Path $root 'Example.txt')).Hash.ToLowerInvariant()
    $extra = @(
        @{name='read_file';arguments=@{path='Docs/Automation/SPEC_VERSION';startLine=1;lineCount=1}},
        @{name='replace_text';arguments=@{path='Example.txt';expectedSha256=$current;oldText='value=2';newText='value=3'}},
        @{name='read_file';arguments=@{path='Example.txt';startLine='1';lineCount=1}}
    )
    $id=0; $input=(($extra | ForEach-Object { $id++; @{jsonrpc='2.0';id=$id;method='tools/call';params=$_} | ConvertTo-Json -Depth 8 -Compress }) -join "`n")+"`n"
    $patch = Invoke-SashimiHostProcess -FilePath $PowerShellPath -WorkingDirectory $root -TimeoutSeconds 30 `
        -ArgumentList @('-NoProfile','-NonInteractive','-File',(Join-Path $hostRoot 'Invoke-SashimiSourceServer.ps1'),'-RepositoryPath',$root,'-Role','Developer') -StandardInput $input
    Assert-HostTest $patch.Succeeded 'Source replacement server failed.'
    $patchRows=@($patch.StdOut.Trim() -split '\r?\n' | ForEach-Object { $_ | ConvertFrom-Json -Depth 16 })
    Assert-HostTest (-not $patchRows[0].result.isError -and -not $patchRows[1].result.isError -and $patchRows[2].result.isError) 'SPEC_VERSION, replacement, or strict argument contract failed.'
    Assert-HostTest ([IO.File]::ReadAllText((Join-Path $root 'Example.txt')) -ceq "value=3`n") 'Literal source replacement was not exact.'
    $outside = Join-Path $script:temporaryRoot 'source-tool-outside'
    Write-HostTestFile (Join-Path $outside 'private.txt') 'outside source root'
    [void](New-Item -ItemType HardLink -Path (Join-Path $root 'Linked.txt') -Target (Join-Path $outside 'private.txt'))
    [void](New-Item -ItemType Junction -Path (Join-Path $root 'Alias') -Target $outside)
    $id=0; $input=((@('Linked.txt','Alias/private.txt') | ForEach-Object {
        $id++; @{jsonrpc='2.0';id=$id;method='tools/call';params=@{name='read_file';arguments=@{path=$_;startLine=1;lineCount=1}}} | ConvertTo-Json -Depth 8 -Compress
    }) -join "`n")+"`n"
    try {
        $linked = Invoke-SashimiHostProcess -FilePath $PowerShellPath -WorkingDirectory $root -TimeoutSeconds 30 `
            -ArgumentList @('-NoProfile','-NonInteractive','-File',(Join-Path $hostRoot 'Invoke-SashimiSourceServer.ps1'),'-RepositoryPath',$root,'-Role','Developer') -StandardInput $input
        Assert-HostTest $linked.Succeeded 'Source link refusal server failed.'
        $linkRows=@($linked.StdOut.Trim() -split '\r?\n' | ForEach-Object { $_ | ConvertFrom-Json -Depth 16 })
        Assert-HostTest ($linkRows.Count -eq 2 -and $linkRows[0].result.isError -and $linkRows[1].result.isError) 'Hard link or junction escaped the scoped file boundary.'
        Assert-HostTest ($linked.StdOut -notmatch 'outside source root') 'Link target content was disclosed.'
    }
    finally {
        # Remove only the junction entry; never recurse into its target.
        [IO.Directory]::Delete((Join-Path $root 'Alias'))
    }
}


Invoke-HostTestCase 'SourceListingPrunesUnrelatedTreesAndPreservesLiteralPages' {
    $root = Join-Path $script:temporaryRoot 'source-listing-pages'
    $outside = Join-Path $script:temporaryRoot 'source-listing-outside'
    Write-HostTestFile (Join-Path $outside 'private.txt') 'outside source root'
    foreach ($number in 0..204) {
        Write-HostTestFile (Join-Path $root ('Docs/Entry{0:D3}.txt' -f $number)) 'text'
    }
    Write-HostTestFile (Join-Path $root 'Docs.meta') 'folder metadata'
    Write-HostTestFile (Join-Path $root 'DocsExtra/Other.txt') 'partial directory name'
    Write-HostTestFile (Join-Path $root '.git/hidden.txt') 'excluded'
    [void](New-Item -ItemType Junction -Path (Join-Path $root 'Unrelated') -Target $outside)
    $calls = @(
        @{name='list_files';arguments=@{prefix='Docs';offset=0}},
        @{name='list_files';arguments=@{prefix='Docs';offset=200}},
        @{name='list_files';arguments=@{prefix='dOcS/';offset=200}},
        @{name='list_files';arguments=@{prefix='Docs/Entry20';offset=0}},
        @{name='list_files';arguments=@{prefix='DoesNotExist';offset=0}},
        @{name='list_files';arguments=@{prefix='../';offset=0}},
        @{name='list_files';arguments=@{prefix='.git/';offset=0}},
        @{name='list_files';arguments=@{prefix='Unrelated';offset=0}},
        @{name='read_file';arguments=@{path='Docs/Entry204.txt';startLine=1;lineCount=1}},
        @{name='write_file';arguments=@{path='Docs/New.txt';expectedSha256='missing';content='new'}},
        @{name='list_files';arguments=@{prefix='Docs/';offset=200}}
    )
    $id=0
    $input=(($calls | ForEach-Object {
        $id++; @{jsonrpc='2.0';id=$id;method='tools/call';params=$_} | ConvertTo-Json -Depth 8 -Compress
    }) -join [Environment]::NewLine)+[Environment]::NewLine
    try {
        $args=@{FilePath=$PowerShellPath;WorkingDirectory=$root;TimeoutSeconds=30;StandardInput=$input;
            ArgumentList=@('-NoProfile','-NonInteractive','-File',(Join-Path $hostRoot 'Invoke-SashimiSourceServer.ps1'),'-RepositoryPath',$root,'-Role','Developer')}
        $process=Invoke-SashimiHostProcess @args
        Assert-HostTest $process.Succeeded "Paged source server failed: $($process.StdErr)"
        $rows=@($process.StdOut.Trim() -split '\r?\n' | ForEach-Object { $_ | ConvertFrom-Json -Depth 16 })
        Assert-HostTest ($rows.Count -eq $calls.Count) 'Source listing did not complete the following read/write requests.'
        foreach($index in @(0..6)+@(8..10)){Assert-HostTest (-not $rows[$index].result.isError) "Request $index failed: $($rows[$index].result.content.text)"}
        Assert-HostTest $rows[7].result.isError 'A requested junction was traversed.'
        Assert-HostTest ($process.StdOut -notmatch 'outside source root|Unrelated/private.txt') 'Unrelated tree content was disclosed.'
        $page0=$rows[0].result.content[0].text|ConvertFrom-Json
        $page1=$rows[1].result.content[0].text|ConvertFrom-Json
        $casePage=$rows[2].result.content[0].text|ConvertFrom-Json
        $partial=$rows[3].result.content[0].text|ConvertFrom-Json
        $expected=@(@('Docs.meta','DocsExtra/Other.txt')+@(0..204|ForEach-Object{'Docs/Entry{0:D3}.txt' -f $_})|Sort-Object -CaseSensitive)
        $actual=@($page0.files)+@($page1.files)
        Assert-HostTest ($page0.total -eq 207 -and $page0.nextOffset -eq 200 -and $page1.nextOffset -eq 207 -and
            [string]::Join('|',$actual) -ceq [string]::Join('|',$expected)) 'Literal prefix, ordering, or pagination changed.'
        Assert-HostTest ($casePage.total -eq 205 -and $casePage.files.Count -eq 5 -and $partial.total -eq 5) 'Case-insensitive or partial filename prefix changed.'
        foreach($index in 4..6){$empty=$rows[$index].result.content[0].text|ConvertFrom-Json;Assert-HostTest ($empty.total -eq 0 -and $empty.files.Count -eq 0) 'Excluded or nonexistent prefix returned source paths.'}
        $afterWrite=$rows[10].result.content[0].text|ConvertFrom-Json
        Assert-HostTest ($afterWrite.total -eq 206 -and $afterWrite.files -ccontains 'Docs/New.txt') 'A later list used a stale source cache.'
    }
    finally {
        # Delete the owned junction entry only, never its external target.
        [IO.Directory]::Delete((Join-Path $root 'Unrelated'))
    }
}


Invoke-HostTestCase 'DeveloperRejectsPartialCheckoutBeforeCodexOrUnity' {
    $pinned='4'*40
    $bundle=New-HostResumeFixtureBundle -Mode DeliveryResume -IssueNumber 5391 -PinnedSha $pinned -DeliverySha $pinned -StaleSha ('f'*40)
    $fixturePath=Join-Path $script:temporaryRoot 'partial-checkout.developer.json'
    $fixture=[ordered]@{SchemaVersion=1;FetchedHead=$pinned;MainSha=('1'*40);LocalHeads=@($pinned);
        StageResults=[ordered]@{'Verify complete tracked checkout'=[ordered]@{ExitCode=0;StdOut=('Assets/Long/Incomplete.txt'+[char]0);StdErr=''}}}
    Write-HostTestFile $fixturePath ($fixture|ConvertTo-Json -Depth 12)
    $result=Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
        ConfigPath=$script:fakeConfigPath;SelectionPath=$bundle.SelectionPath;RunPath=$bundle.Run.RunPath;
        CodexFixturePath=$bundle.CodexFixture;UnityFixturePath=$bundle.UnityFixture;ExecutionFixturePath=$fixturePath
    } -TimeoutSeconds 60
    $json=ConvertFrom-LastHostJson $result.StdOut
    Assert-HostTest (-not $result.Succeeded -and -not $json.Success -and $json.Error -match 'missing tracked files') 'A partial checkout reached delivery.'
    Assert-HostTest (-not $json.Pushed -and -not $json.CreatedPullRequest -and -not $json.TransitionedToReview) 'Partial checkout changed delivery state.'
    $later=@('Install Git LFS locally','Codex Developer structured edit','Host Unity and repository validation','Normal push exact existing PR branch','In Progress to Review')
    Assert-HostTest (@($json.Commands|Where-Object{$_.Stage -cin $later}).Count -eq 0) 'Partial checkout reached a later processing stage.'
    Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $bundle.Run.ArtifactsPath 'Codex'))) 'Codex ran before checkout completeness was established.'
}

Invoke-HostTestCase 'CompletionCodexTimeoutCancellationAndDescendantsUseOwnedJob' {
    $root = Join-Path $script:temporaryRoot 'codex-lifecycle'
    [void][IO.Directory]::CreateDirectory($root)
    $fake = (New-HostFakeCodexAdapter $root).Path
    $workspace = Join-Path $root 'Workspace'; [void][IO.Directory]::CreateDirectory($workspace)
    $policy = Get-SashimiCodexEnvironmentPolicy
    $ledger = Join-Path $root 'ledger.json'; $cancel = Join-Path $root 'cancel.requested'
    foreach ($mode in @('timeout','cancel','exit')) {
        Write-HostTestFile ([IO.Path]::ChangeExtension($fake,'.process-mode')) $mode
        Write-HostTestFile ([IO.Path]::ChangeExtension($fake,'.cancel-path')) $cancel
        if (Test-Path -LiteralPath $cancel) { Remove-Item -LiteralPath $cancel }
        $ready = [IO.Path]::ChangeExtension($fake,'.descendant-ready')
        if (Test-Path -LiteralPath $ready) { Remove-Item -LiteralPath $ready }
        Write-HostTestFile $ledger '{"SchemaVersion":1,"Processes":[],"ProcessIds":[]}'
        $process = Invoke-SashimiHostProcess -FilePath $fake -Kind Codex -WorkingDirectory $workspace -CodexWorkspacePath $workspace `
            -ArgumentList @('--disable','shell_tool','--disable','unified_exec','exec','--ignore-user-config','--strict-config') `
            -ClearEnvironment -Environment $policy.Overrides -RemoveEnvironmentVariables $policy.RemoveNames `
            -OwnedProcessRecordPath $ledger -CancellationMarkerPath $cancel -TimeoutSeconds $(if($mode -ceq 'timeout'){1}else{10}) `
            -StandardInput $(if($mode -ceq 'timeout'){'x' * 2MB}else{''}) -PreserveRawOutputInMemory
        Assert-HostTest ($process.KillOnCloseJobAssigned -and $process.TerminationConfirmed) "Unconfirmed Codex lifecycle: $mode"
        Assert-HostTest (Test-Path -LiteralPath $ready) "Codex fixture never started its later descendant: $mode"
        Assert-HostTest (@((Read-SashimiJsonFile $ledger).Processes).Count -eq 0) 'Confirmed Codex job left a stale ledger.'
        if ($mode -ceq 'timeout') { Assert-HostTest $process.TimedOut 'Blocked Codex stdin escaped timeout.' }
        if ($mode -ceq 'cancel') { Assert-HostTest $process.Cancelled 'Codex ignored cancellation.' }
        if ($mode -ceq 'exit') { Assert-HostTest $process.Succeeded 'A clean Codex parent exit did not complete successfully after job closure.' }
    }
    Start-Sleep -Milliseconds 2300
    Assert-HostTest (-not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($fake,'.escaped')))) 'Codex descendant escaped its owning job.'
}

Invoke-HostTestCase 'CompletionUnknownProcessLedgerPreventsCleanupAndRetention' {
    $runRoot = Join-Path $script:temporaryRoot 'unconfirmed-runs'
    $run = New-SashimiRunWorkspace -RunRoot $runRoot
    Write-HostTestFile (Join-Path $run.RepositoryPath 'source.txt') 'preserve'
    Write-HostTestFile (Join-Path $run.StatePath 'OwnedHostPids.json') '{"SchemaVersion":1,"Processes":[{"Id":999999,"StartTimeUtc":"2026-01-01T00:00:00Z"}]}'
    $cleanup = Remove-SashimiRunRepository -RunPath $run.RunPath -RunRoot $runRoot
    Assert-HostTest (-not $cleanup.Success -and $cleanup.Preserved) 'Missing root PID was treated as proven descendant termination.'
    (Get-Item -LiteralPath $run.RunPath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)
    $retention = @(Invoke-SashimiRetention -RunRoot $runRoot -RetentionDays 14)
    Assert-HostTest ($retention.Count -eq 1 -and $retention[0].Preserved) 'Retention removed an unconfirmed process ledger.'
}

Invoke-HostTestCase 'CompletionGeneratorUsesIndependentInputsAndCompleteDeltas' {
    $production = Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Get-SashimiGeneratorSourceManifest','New-SashimiGeneratorBaseline','Get-SashimiGeneratorDelta')) {
        Set-Item -Path ('Function:' + $name) -Value (Get-HostTestFunctionScriptBlock $production $name)
    }
    $generator = Join-Path $script:temporaryRoot 'fixture-generator.ps1'
    Write-HostTestFile $generator @'
param([string]$Project,[string]$Mode)
$output = Join-Path $Project 'output.txt'
if ($Mode -eq 'random-if-missing') {
    if (-not (Test-Path -LiteralPath $output)) { [IO.File]::WriteAllText($output,[Guid]::NewGuid().ToString('N')) }
} else { [IO.File]::WriteAllText($output,([IO.File]::ReadAllText((Join-Path $Project 'input.txt'))).ToUpperInvariant()) }
'@
    foreach ($mode in @('random-if-missing','deterministic')) {
        $root = Join-Path $script:temporaryRoot ('generator-' + $mode)
        Write-HostTestFile (Join-Path $root 'Original/input.txt') 'fixed input'
        $project = Join-Path $root 'Original'
        $before = @(Get-SashimiGeneratorSourceManifest $project)
        $baseline = New-SashimiGeneratorBaseline -ProjectRoot $project -StateRoot (Join-Path $root 'State') -ExpectedManifest $before
        $one = Invoke-HostTestScript $generator @{Project=$project;Mode=$mode}
        Assert-HostTest $one.Succeeded 'First executable generator failed.'
        $first = @(Get-SashimiGeneratorSourceManifest $project)
        $repeat = Invoke-HostTestScript $generator @{Project=$project;Mode=$mode}
        Assert-HostTest ($repeat.Succeeded -and (ConvertTo-SashimiJson $first) -ceq (ConvertTo-SashimiJson @(Get-SashimiGeneratorSourceManifest $project))) 'Same-directory fixture does not reproduce the old false positive.'
        $two = Invoke-HostTestScript $generator @{Project=$baseline.Repository;Mode=$mode}
        Assert-HostTest $two.Succeeded 'Second executable generator failed.'
        $delta1 = @(Get-SashimiGeneratorDelta -Before $before -After $first -AllowedPaths @('output.txt'))
        $delta2 = @(Get-SashimiGeneratorDelta -Before $before -After @(Get-SashimiGeneratorSourceManifest $baseline.Repository) -AllowedPaths @('output.txt'))
        $same = (ConvertTo-SashimiJson $delta1) -ceq (ConvertTo-SashimiJson $delta2)
        Assert-HostTest ($same -eq ($mode -ceq 'deterministic')) 'Independent baseline comparison missed the random-if-missing counterexample.'
        Write-HostTestFile (Join-Path $project 'undeclared.cs') '// unexpected'
        Assert-HostThrows { Get-SashimiGeneratorDelta -Before $before -After @(Get-SashimiGeneratorSourceManifest $project) -AllowedPaths @('output.txt') } 'undeclared'
    }
}

Invoke-HostTestCase 'CompletionGeneratorBaselineStaysShortAndCleansReadOnlyCopies' {
    $production = Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Get-SashimiGeneratorSourceManifest','New-SashimiGeneratorBaseline',
            'Remove-SashimiGeneratorBaseline','Remove-SashimiUnityTreeWithoutReparseTraversal')) {
        Set-Item -Path ('Function:' + $name) -Value (Get-HostTestFunctionScriptBlock $production $name)
    }
    $root = Join-Path $script:temporaryRoot 'generator-short-path'
    $project = Join-Path $root 'Repository'
    $state = Join-Path $root 'State'
    Write-HostTestFile (Join-Path $project 'input.txt') 'fixed input'
    $packRelative = '.git/objects/pack/pack-fixture.idx'
    $sourcePack = Join-Path $project $packRelative
    Write-HostTestFile $sourcePack 'read-only pack index'
    [IO.File]::SetAttributes($sourcePack, ([IO.File]::GetAttributes($sourcePack) -bor [IO.FileAttributes]::ReadOnly))
    $sourceAttributes = [IO.File]::GetAttributes($sourcePack)
    $sourceHash = (Get-FileHash -LiteralPath $sourcePack).Hash
    $before = @(Get-SashimiGeneratorSourceManifest $project)
    $baseline = New-SashimiGeneratorBaseline -ProjectRoot $project -StateRoot $state -ExpectedManifest $before
    Assert-HostTest ($baseline.Repository.Length -le $project.Length) 'Generator baseline reintroduced deeper Unity paths.'
    Assert-HostTest ((ConvertTo-SashimiJson @(Get-SashimiGeneratorSourceManifest $baseline.Repository)) -ceq
        (ConvertTo-SashimiJson $before)) 'Short baseline changed source bytes.'
    $copyPack = Join-Path $baseline.Repository $packRelative
    Assert-HostTest ((Get-FileHash -LiteralPath $copyPack).Hash -ceq $sourceHash) 'Copied Git pack bytes changed.'
    Assert-HostTest (([IO.File]::GetAttributes($copyPack) -band [IO.FileAttributes]::ReadOnly) -eq 0) 'Fresh copied pack remains undeletable.'
    Assert-HostTest ([IO.File]::GetAttributes($sourcePack) -eq $sourceAttributes) 'Copy preparation changed source attributes.'
    Assert-HostThrows { New-SashimiGeneratorBaseline -ProjectRoot $project -StateRoot $state -ExpectedManifest $before } 'already exists'
    Assert-HostThrows { Remove-SashimiGeneratorBaseline -Workspace $baseline } 'termination was not confirmed'
    Assert-HostTest (Test-Path -LiteralPath $copyPack) 'Unconfirmed termination removed the baseline.'
    $marker = Join-Path $baseline.Parent '.generator-owner'
    Write-HostTestFile $marker ('0' * 32)
    Assert-HostThrows { Remove-SashimiGeneratorBaseline -Workspace $baseline -TerminationConfirmed } 'ownership mismatch'
    Assert-HostTest (Test-Path -LiteralPath $copyPack) 'Wrong ownership marker removed the baseline.'
    Write-HostTestFile $marker $baseline.OwnerNonce
    $wrongWorkspace = [pscustomobject]@{ Parent=$project; Repository=(Join-Path $project 'r'); StateRoot=$state; OwnerNonce=$baseline.OwnerNonce }
    Assert-HostThrows { Remove-SashimiGeneratorBaseline -Workspace $wrongWorkspace -TerminationConfirmed } 'ownership mismatch'
    Remove-SashimiGeneratorBaseline -Workspace $baseline -TerminationConfirmed
    Assert-HostTest (-not (Test-Path -LiteralPath $baseline.Parent)) 'Owned copied Git pack prevented baseline cleanup.'
    $next = New-SashimiGeneratorBaseline -ProjectRoot $project -StateRoot $state -ExpectedManifest $before
    Assert-HostTest ($next.OwnerNonce -cne $baseline.OwnerNonce) 'A new baseline reused the old ownership nonce.'
    Assert-HostThrows { Remove-SashimiGeneratorBaseline -Workspace $baseline -TerminationConfirmed } 'ownership mismatch'
    Assert-HostTest (Test-Path -LiteralPath (Join-Path $next.Repository $packRelative)) 'A stale cleanup removed a new baseline.'
    Remove-SashimiGeneratorBaseline -Workspace $next -TerminationConfirmed
    Assert-HostTest ((Get-FileHash -LiteralPath $sourcePack).Hash -ceq $sourceHash -and
        [IO.File]::GetAttributes($sourcePack) -eq $sourceAttributes) 'Cleanup changed the original Git pack.'
}

Invoke-HostTestCase 'CompletionGeneratorCleanupDoesNotFollowReparseTargets' {
    $production = Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Get-SashimiGeneratorSourceManifest','New-SashimiGeneratorBaseline',
            'Remove-SashimiGeneratorBaseline','Remove-SashimiUnityTreeWithoutReparseTraversal')) {
        Set-Item -Path ('Function:' + $name) -Value (Get-HostTestFunctionScriptBlock $production $name)
    }
    $root = Join-Path $script:temporaryRoot 'generator-reparse-cleanup'
    $project = Join-Path $root 'Repository'
    Write-HostTestFile (Join-Path $project 'input.txt') 'fixed input'
    $baseline = New-SashimiGeneratorBaseline -ProjectRoot $project -StateRoot (Join-Path $root 'State') `
        -ExpectedManifest @(Get-SashimiGeneratorSourceManifest $project)
    $outside = Join-Path $root 'Outside'
    $sentinel = Join-Path $outside 'sentinel.txt'
    Write-HostTestFile $sentinel 'outside content'
    [void](New-Item -ItemType Junction -Path (Join-Path $baseline.Repository 'link') -Target $outside)
    Remove-SashimiGeneratorBaseline -Workspace $baseline -TerminationConfirmed
    Assert-HostTest (-not (Test-Path -LiteralPath $baseline.Parent)) 'Baseline junction was not unlinked.'
    Assert-HostTest ([IO.File]::ReadAllText($sentinel) -ceq 'outside content') 'Cleanup followed a junction outside the baseline.'
}

Invoke-HostTestCase 'CompletionInventoryRequiresAllAssetsAndConsistentActiveComponents' {
    $production = Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('Read-SashimiBoundedStableUtf8File','Read-SashimiComponentInventory')) {
        Set-Item -Path ('Function:' + $name) -Value (Get-HostTestFunctionScriptBlock $production $name)
    }
    $script:unityLogMaximumBytes = 8MB
    $root = Join-Path $script:temporaryRoot 'inventory'
    Write-HostTestFile (Join-Path $root 'Assets/Test.unity') 'scene'
    Write-HostTestFile (Join-Path $root 'Assets/Unloaded.prefab') 'prefab'
    $asset = @{path='Assets/Test.unity';kind='Scene';activeAudioListeners=1;activeEventSystems=0;missingScripts=0;missingReferences=0;components=@(@{path='Root[0]';type='AudioListener';active=$true},@{path='Inactive[1]';type='AudioListener';active=$false})}
    $prefab = @{path='Assets/Unloaded.prefab';kind='Prefab';activeAudioListeners=0;activeEventSystems=0;missingScripts=0;missingReferences=0;components=@()}
    $log = Join-Path $root 'inventory.log'
    function Write-Inventory([object[]]$Assets) { Write-HostTestFile $log ('SASHIMI_COMPONENT_INVENTORY=' + (ConvertTo-SashimiJson @{schemaVersion=1;passed=$true;assets=$Assets;errors=@()})) }
    Write-Inventory @($asset,$prefab)
    [void](Read-SashimiComponentInventory -LogPath $log -ProjectRoot $root)
    Write-Inventory @($asset)
    Assert-HostThrows { Read-SashimiComponentInventory -LogPath $log -ProjectRoot $root } 'cover every'
    $asset.components[1].active=$true; $asset.activeAudioListeners=2
    Write-Inventory @($asset,$prefab)
    Assert-HostThrows { Read-SashimiComponentInventory -LogPath $log -ProjectRoot $root } 'duplicate active'
    $asset.activeAudioListeners=1
    Write-Inventory @($asset,$prefab)
    Assert-HostThrows { Read-SashimiComponentInventory -LogPath $log -ProjectRoot $root } 'counts do not match'
    Write-HostTestFile $log 'no structured inventory'
    Assert-HostThrows { Read-SashimiComponentInventory -LogPath $log -ProjectRoot $root } 'missing'
}

Invoke-HostTestCase 'CompletionCaptureQuotaKillsWriterAndArtifactSealRejectsChanges' {
    $root = Join-Path $script:temporaryRoot 'capture-quota'; [void][IO.Directory]::CreateDirectory($root)
    $writer = Join-Path $script:temporaryRoot 'quota-writer.ps1'
    Write-HostTestFile $writer @'
param([string]$Root)
$stream=[IO.File]::OpenWrite((Join-Path $Root 'raw.log'))
try { $stream.SetLength(9MB); $stream.Flush(); Start-Sleep -Seconds 20 } finally { $stream.Dispose() }
'@
    $process = Invoke-SashimiHostProcess -FilePath $PowerShellPath -WorkingDirectory $script:temporaryRoot -TimeoutSeconds 15 `
        -CaptureRoots @($root) -ArgumentList @('-NoProfile','-NonInteractive','-File',$writer,'-Root',$root)
    Assert-HostTest (-not $process.Succeeded -and $process.TerminationConfirmed -and -not $process.TimedOut) 'Oversized raw file did not terminate the owning process tree.'
    Remove-Item -LiteralPath (Join-Path $root 'raw.log')
    Write-HostTestFile (Join-Path $root 'result.json') '{"safe":true}'
    $seal = Join-Path $script:temporaryRoot 'artifact.seal.json'
    Write-SashimiArtifactSeal -Root $root -SealPath $seal
    Assert-SashimiArtifactSeal -Root $root -SealPath $seal
    [void][IO.Directory]::CreateDirectory((Join-Path $root 'unexpected'))
    Assert-HostThrows { Assert-SashimiArtifactSeal -Root $root -SealPath $seal } 'changed after'
    [IO.Directory]::Delete((Join-Path $root 'unexpected'))
    Write-HostTestFile (Join-Path $root 'unexpected.txt') 'not in manifest'
    Assert-HostThrows { Assert-SashimiArtifactSeal -Root $root -SealPath $seal } 'changed after'
    Remove-Item -LiteralPath (Join-Path $root 'unexpected.txt')
    Write-HostTestFile (Join-Path $root 'result.json') '{"safe":false}'
    Assert-HostThrows { Assert-SashimiArtifactSeal -Root $root -SealPath $seal } 'changed after'
}

Invoke-HostTestCase 'CompletionNonCodexOutputIsAuditedBeforeRedaction' {
    $writer = Join-Path $script:temporaryRoot 'sensitive-output.ps1'
    Write-HostTestFile $writer "[Console]::Out.WriteLine('Bearer ' + ('z' * 40))"
    $process = Invoke-HostTestScript $writer
    Assert-HostTest (-not $process.Succeeded -and $process.StdOut -ceq '' -and $process.StdErr -notmatch ('z' * 40)) 'Sensitive original output was redacted into a successful retained result.'
}
