# Dot-sourced only by the marked, executable-isolated fixture suite.
# Real production declarations and file IO; only ACL results and fault checkpoints
# are replaced. The scheduler's existing fixture branch remains the implementation.
function Invoke-HostM2TransactionRegression {
    $installerPath = Join-Path $hostRoot 'Install-SashimiHostAutomation.ps1'
    $source = [IO.File]::ReadAllText($installerPath)
    $offset = $source.IndexOf('$result = [ordered]@{', [StringComparison]::Ordinal)
    Assert-HostTest ($offset -gt 0) 'Missing installer declaration boundary.'
    . ([scriptblock]::Create($source.Substring(0,$offset))) -ConfigPath $script:fakeConfigPath
    $script:m2Rows = [Collections.Generic.List[object]]::new()
    $state = @{ Trace=[Collections.Generic.List[string]]::new(); Counts=@{}; Fault=''; Reached=$false; Action=$null; CleanupFault=$false }
    $sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-21-1000-1000-1000-1000')

    function Invoke-InstallerTransactionCheckpoint {
        param([string]$Name,[string]$Path)
        [void](Get-SashimiMarkedFixtureRoot -Path $Path)
        Assert-InstallerNoReparsePoint $Path
        $relative = $Path.Replace($script:InstallRoot,'<install>') -replace '\.sashimi-stage-[0-9a-f]{32}', '<transaction>'
        $key = "$Name|$relative"
        if (-not $state.Counts.ContainsKey($key)) { $state.Counts[$key]=0 }
        $state.Counts[$key]++
        $key += '#' + $state.Counts[$key]
        $state.Trace.Add($key)
        if ($state.CleanupFault -and $Name -in @('Cleanup.Delete','Cleanup.EmptyDelete')) { throw 'SIMULATED_M2_CLEANUP_FAILURE' }
        if ($key -ceq $state.Fault) {
            $state.Reached=$true
            if ($null -ne $state.Action) { & $state.Action $Path; return }
            throw "SIMULATED_M2_FAULT: $key"
        }
    }
    function Set-InstallerProtectedAcl {
        param($Path,$UserSid,[switch]$Container)
        Invoke-InstallerTransactionCheckpoint 'ACL.Apply' $Path
    }
    function Assert-InstallerProtectedAcl {
        param($Path,$UserSid)
        Invoke-InstallerTransactionCheckpoint 'ACL.Verify' $Path
    }
    function New-M2Attempt {
        $state.Trace.Clear(); $state.Counts.Clear(); $state.Fault=''; $state.Reached=$false; $state.Action=$null; $state.CleanupFault=$false
        $InstallRootFixturePath=Join-Path $script:temporaryRoot ('m2-' + [Guid]::NewGuid().ToString('N'))
        $SchedulerFixturePath=Join-Path $script:temporaryRoot ([IO.Path]::GetFileName($InstallRootFixturePath) + '-scheduler.jsonl')
        Initialize-InstallerHarnessBoundaries
        $config=Import-InstallerConfig $script:fakeConfigPath
        $plan=New-InstallerBundlePlan $hostRoot $script:fakeConfigPath $config $installerPath
        $bundleRoot=Join-Path $script:BundlesRoot $plan.BundleId
        $unrelated=Join-Path $script:temporaryRoot ([IO.Path]::GetFileName($InstallRootFixturePath) + '-unrelated.txt')
        Write-HostTestFile $unrelated 'unrelated-owner-data'
        Write-HostTestFile $script:InstallerSchedulerFixturePath 'prior-task-definition'
        return [pscustomobject]@{Plan=$plan; Root=$bundleRoot; Unrelated=$unrelated; Task=$script:InstallerSchedulerFixturePath}
    }
    function Invoke-M2Attempt($Attempt) {
        Initialize-InstallerProtectedRoots $sid
        [void](Install-InstallerCodexDistribution $Attempt.Plan.CodexDistribution $sid)
        [void](Install-InstallerBundle $Attempt.Plan $Attempt.Root $sid)
        Invoke-InstallerBundleRegistration $Attempt.Plan $Attempt.Root $sid '<Task />' | Out-Null
    }
    function Assert-M2Complete($Attempt) {
        Assert-InstallerCodexDistribution $Attempt.Plan.CodexDistribution $sid
        Assert-InstallerBundle $Attempt.Root $Attempt.Plan.Manifest $Attempt.Plan.ManifestSha256 $sid
        Assert-HostTest ([IO.File]::ReadAllText($Attempt.Unrelated) -ceq 'unrelated-owner-data') 'Unrelated owner data changed.'
    }
    function Reset-M2Trace {
        $state.Trace.Clear(); $state.Counts.Clear(); $state.Fault=''; $state.Reached=$false; $state.Action=$null; $state.CleanupFault=$false
    }

    $initial=New-M2Attempt
    Invoke-M2Attempt $initial
    $boundaries=@($state.Trace)
    Assert-HostTest ($boundaries.Count -gt 0) 'M2 production path produced no boundaries.'
    Assert-M2Complete $initial
    $priorRoot=$initial.Root
    $priorDigest=Get-InstallerFileSha256 (Join-Path $priorRoot 'HostIntegrity.json')
    Reset-M2Trace
    Assert-HostTest (-not (Install-InstallerBundle $initial.Plan $initial.Root $sid)) 'Same bundle was not reused.'
    Assert-HostTest (@($state.Trace | Where-Object { $_ -match 'ACL.Apply|Write|Promote' }).Count -eq 0) 'Reuse rewrote a complete bundle.'
    $script:m2Rows.Add([pscustomobject]@{Case='CleanSuccessAndReadOnlyReuse'; Reached=$true; Failure='none'; Cleanup='removed'; Final='complete'; Retry='reuse'; ExternalMutations=0})

    # Count and names come from the actual successful production trace, including
    # every payload, config, identity, manifest, fake ACL and scheduler boundary.
    foreach ($boundary in $boundaries) {
        $attempt=New-M2Attempt
        # Normalize content-addressed paths too: each isolated root is a distinct
        # plan, but each fault and retry use the exact same approved plan object.
        $selected=$boundary.Replace($initial.Plan.BundleId,$attempt.Plan.BundleId)
        $selected=$selected.Replace($initial.Task,$attempt.Task)
        $state.Fault=$selected
        $failure=''
        try { Invoke-M2Attempt $attempt } catch { $failure=$_.Exception.Message }
        Assert-HostTest ($state.Reached -and $failure -match 'SIMULATED_M2|cleanup failed|registration failed') "Fault was not reached/observed: $selected; $failure"
        $observedTrace=@($state.Trace)
        $published=@($observedTrace | Where-Object { $_ -match '^Bundle.Published\|' }).Count -gt 0
        # A fault at Published itself occurs after Directory.Move completed.
        Assert-HostTest ((Test-Path -LiteralPath $attempt.Root) -eq $published) "Incorrect final visibility after $selected"
        $taskText=[IO.File]::ReadAllText($attempt.Task)
        $schedulerWritten=@($observedTrace | Where-Object { $_ -match '^Scheduler.Written\|' }).Count -gt 0
        Assert-HostTest (($taskText -ceq 'prior-task-definition') -eq (-not $schedulerWritten)) "Unexpected prior task change at $selected"
        if ($schedulerWritten) { Assert-HostTest ($failure -match 'task state is unconfirmed') 'Ambiguous registration failure was not reported.' }
        Assert-HostTest ([IO.File]::ReadAllText($attempt.Unrelated) -ceq 'unrelated-owner-data') 'Unrelated file changed during fault.'
        Assert-HostTest ((Get-InstallerFileSha256 (Join-Path $priorRoot 'HostIntegrity.json')) -ceq $priorDigest) 'Prior complete bundle changed.'
        $remainders=@(foreach ($parent in @($script:BundlesRoot,$script:CodexDistributionsRoot)) {
            if (Test-Path $parent) { Get-ChildItem $parent -Directory -Force | Where-Object Name -like '.sashimi-stage-*' }
        })
        Reset-M2Trace
        Invoke-M2Attempt $attempt
        Assert-M2Complete $attempt
        $script:m2Rows.Add([pscustomobject]@{Case=$boundary; Reached=$true; Failure=$failure; Cleanup=$(if($remainders.Count){'preserved; failure reported'}else{'removed/absent'}); Final=$(if($published){'complete'}else{'absent'}); Retry='PASS identical plan'; ExternalMutations=0})
    }

    # Corrupt real bytes immediately before the real verifier; no replacement
    # snapshot verifier or mocked importer can hide incomplete content.
    foreach ($tamper in @('manifest-missing','manifest-bytes','payload-missing','payload-length','payload-hash','extra-file','config-invalid')) {
        $attempt=New-M2Attempt
        $state.Fault='Bundle.Verify|' + '<install>\Bundles\<transaction>\Payload#1'
        $state.Action={
            param($path)
            switch ($tamper) {
                'manifest-missing' { [IO.File]::Delete((Join-Path $path 'HostIntegrity.json')) }
                'manifest-bytes' { [IO.File]::AppendAllText((Join-Path $path 'HostIntegrity.json'),' ') }
                'payload-missing' { [IO.File]::Delete((Join-Path $path 'HostAutomation.Common.ps1')) }
                'payload-length' { [IO.File]::AppendAllText((Join-Path $path 'HostAutomation.Common.ps1'),'changed') }
                'payload-hash' {
                    $target=Join-Path $path 'HostAutomation.Common.ps1'; $bytes=[IO.File]::ReadAllBytes($target); $bytes[0]=$bytes[0] -bxor 1; [IO.File]::WriteAllBytes($target,$bytes)
                }
                'extra-file' { [IO.File]::WriteAllText((Join-Path $path 'unknown.txt'),'foreign') }
                'config-invalid' { [IO.File]::WriteAllText((Join-Path $path 'Config.json'),'{}') }
            }
        }
        Assert-HostThrows { Invoke-M2Attempt $attempt } 'manifest|hash|length|unexpected|missing'
        Assert-HostTest ($state.Reached -and -not (Test-Path $attempt.Root)) "Tamper not contained: $tamper"
        Reset-M2Trace; Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
        $script:m2Rows.Add([pscustomobject]@{Case=$tamper; Reached=$true; Failure='real verifier rejection'; Cleanup='removed'; Final='absent'; Retry='PASS identical plan'; ExternalMutations=0})
    }

    # Reproduce the original poisoned-final failure mechanism with a real
    # earlier payload write, then force owned cleanup to fail as a second fault.
    $attempt=New-M2Attempt
    $state.Fault='Bundle.Write.After|<install>\Bundles\<transaction>\Payload\Config.json#1'
    $state.Action={ param($path); $state.CleanupFault=$true; throw 'SIMULATED_M2_PAYLOAD_INTERRUPTION' }
    Assert-HostThrows { Invoke-M2Attempt $attempt } 'SIMULATED_M2_CLEANUP_FAILURE'
    Assert-HostTest ($state.Reached -and -not (Test-Path $attempt.Root)) 'Interrupted staging poisoned final destination.'
    $preserved=@(Get-ChildItem $script:BundlesRoot -Directory -Force | Where-Object Name -like '.sashimi-stage-*')
    Assert-HostTest ($preserved.Count -eq 1) 'Owned staging evidence was not preserved.'
    Reset-M2Trace; Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
    Assert-HostTest (Test-Path $preserved[0].FullName) 'Retry deleted a different transaction.'
    Remove-InstallerStagingWorkspace $preserved[0].FullName $script:BundlesRoot Bundle $attempt.Plan.BundleId
    $script:m2Rows.Add([pscustomobject]@{Case='InterruptedPayloadAndCleanupFailure'; Reached=$true; Failure='simulated write + cleanup'; Cleanup='owned workspace preserved; later exact cleanup'; Final='absent'; Retry='PASS identical plan'; ExternalMutations=0})
    foreach ($kind in @('foreign','partial','altered','wrong-path')) {
        $attempt=New-M2Attempt
        Initialize-InstallerProtectedRoots $sid
        [void](Install-InstallerCodexDistribution $attempt.Plan.CodexDistribution $sid)
        if ($kind -ceq 'altered') {
            [void](Install-InstallerBundle $attempt.Plan $attempt.Root $sid)
            [IO.File]::AppendAllText((Join-Path $attempt.Root 'Config.json'),'changed')
        } else {
            if ($kind -ceq 'wrong-path') { $attempt.Root=Join-Path $script:BundlesRoot ('a' * 64) }
            [void][IO.Directory]::CreateDirectory($attempt.Root)
            [IO.File]::WriteAllText((Join-Path $attempt.Root 'owner-data.txt'),'preserve')
            if ($kind -ceq 'partial') { [IO.File]::WriteAllText((Join-Path $attempt.Root 'HostIntegrity.json'),'{}') }
        }
        $before=@(Get-ChildItem $attempt.Root -File | ForEach-Object { $_.Name + ':' + (Get-InstallerFileSha256 $_.FullName) }) -join ';'
        Reset-M2Trace
        Assert-HostThrows { Install-InstallerBundle $attempt.Plan $attempt.Root $sid } 'manifest|hash|canonical|content-addressed'
        Assert-HostThrows { Install-InstallerBundle $attempt.Plan $attempt.Root $sid } 'manifest|hash|canonical|content-addressed'
        $after=@(Get-ChildItem $attempt.Root -File | ForEach-Object { $_.Name + ':' + (Get-InstallerFileSha256 $_.FullName) }) -join ';'
        Assert-HostTest ($before -ceq $after -and [IO.File]::ReadAllText($attempt.Task) -ceq 'prior-task-definition') 'Invalid destination was modified or registered.'
        Assert-HostTest (@($state.Trace | Where-Object { $_ -match 'ACL.Apply' }).Count -eq 0) 'Invalid existing destination was ACL-repaired.'
        $script:m2Rows.Add([pscustomobject]@{Case="Destination-$kind"; Reached=$true; Failure='real verifier rejection'; Cleanup='foreign destination untouched'; Final='unchanged invalid'; Retry='fail closed (persistent invalid input)'; ExternalMutations=0})
    }

    foreach ($kind in @('wrong-marker','missing-marker','outside-root','reparse')) {
        $attempt=New-M2Attempt
        Initialize-InstallerProtectedRoots $sid
        $stage=New-InstallerStagingWorkspace $script:BundlesRoot Bundle $attempt.Plan.BundleId $sid
        $markerBytes=[IO.File]::ReadAllBytes($stage.MarkerPath)
        $junction=$null
        $cleanupParent=$script:BundlesRoot
        switch ($kind) {
            'wrong-marker' {
                $marker=Read-InstallerJsonFile $stage.MarkerPath; $marker.Identity='0' * 64
                [IO.File]::WriteAllText($stage.MarkerPath,(ConvertTo-InstallerJson $marker))
            }
            'missing-marker' { [IO.File]::Delete($stage.MarkerPath) }
            'outside-root' { $cleanupParent=$script:CodexDistributionsRoot }
            'reparse' {
                $junction=Join-Path $stage.Payload 'junction'
                [void](New-Item -ItemType Junction -Path $junction -Target $script:CodexDistributionsRoot)
            }
        }
        Assert-HostThrows { Remove-InstallerStagingWorkspace $stage.Workspace $cleanupParent Bundle $attempt.Plan.BundleId } 'marker|unmarked|outside|reparse'
        Assert-HostTest (Test-Path $stage.Workspace) 'Rejected ownership cleanup removed workspace.'
        if ($null -ne $junction) {
            # Remove only this fixture junction itself, never its target/tree.
            [void](Get-SashimiMarkedFixtureRoot $stage.Workspace)
            Assert-HostTest ([IO.Path]::GetDirectoryName($junction) -ceq $stage.Payload) 'Junction escaped fixture payload.'
            Assert-HostTest (((Get-Item $junction -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'Expected fixture junction.'
            [IO.Directory]::Delete($junction,$false)
        }
        [IO.File]::WriteAllBytes($stage.MarkerPath,$markerBytes)
        Remove-InstallerStagingWorkspace $stage.Workspace $script:BundlesRoot Bundle $attempt.Plan.BundleId
        Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
        $script:m2Rows.Add([pscustomobject]@{Case="Cleanup-$kind"; Reached=$true; Failure='real ownership rejection'; Cleanup='refused; fixture condition restored; exact cleanup'; Final='absent'; Retry='PASS'; ExternalMutations=0})
    }

    $attempt=New-M2Attempt
    Initialize-InstallerProtectedRoots $sid
    $foreign=Join-Path $script:InstallRoot 'foreign-owner'
    [void][IO.Directory]::CreateDirectory($foreign)
    [IO.File]::WriteAllText((Join-Path $foreign 'owner.txt'),'preserve')
    [void](New-Item -ItemType Junction -Path $attempt.Root -Target $foreign)
    Assert-HostThrows { Install-InstallerBundle $attempt.Plan $attempt.Root $sid } 'reparse'
    Assert-HostTest ([IO.File]::ReadAllText((Join-Path $foreign 'owner.txt')) -ceq 'preserve') 'Reparse destination touched target data.'
    Assert-HostTest ([IO.Path]::GetDirectoryName($attempt.Root) -ceq $script:BundlesRoot) 'Fixture destination escaped bundle root.'
    [void](Get-SashimiMarkedFixtureRoot $script:BundlesRoot)
    [IO.Directory]::Delete($attempt.Root,$false)
    Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
    $script:m2Rows.Add([pscustomobject]@{Case='DestinationReparse'; Reached=$true; Failure='real reparse rejection'; Cleanup='target untouched; fixture junction removed without recursion'; Final='unchanged junction'; Retry='PASS after fixture condition removed'; ExternalMutations=0})

    $attempt=New-M2Attempt
    $state.Fault='Bundle.Promote|<install>\Bundles\<transaction>\Payload#1'
    $state.Action={
        param($path)
        # Simulated concurrent complete winner at the rename boundary.
        [void][IO.Directory]::CreateDirectory($attempt.Root)
        foreach ($file in Get-ChildItem $path -File) { [IO.File]::Copy($file.FullName,(Join-Path $attempt.Root $file.Name),$false) }
    }
    Assert-HostThrows { Invoke-M2Attempt $attempt } 'appeared before atomic promotion'
    Assert-HostTest $state.Reached 'Collision boundary was not reached.'
    Assert-M2Complete $attempt
    Assert-HostTest ([IO.File]::ReadAllText($attempt.Task) -ceq 'prior-task-definition') 'Collision reached scheduler.'
    Reset-M2Trace; Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
    $script:m2Rows.Add([pscustomobject]@{Case='CompleteDestinationCollision'; Reached=$true; Failure='simulated concurrent winner; real collision rejection'; Cleanup='owned stage removed; winner retained'; Final='complete winner'; Retry='PASS read-only reuse'; ExternalMutations=0})
    foreach ($kind in @('partial-marker-write','empty-cleanup-failure','marker-changed-before-write')) {
        $attempt=New-M2Attempt
        switch ($kind) {
            'partial-marker-write' {
                $state.Fault='Marker.Write|<install>\CodexDistributions\<transaction>\.sashimi-installer-staging.json#1'
                $state.Action={ param($path); [IO.File]::WriteAllText($path,'{'); throw 'SIMULATED_M2_PARTIAL_MARKER_WRITE' }
            }
            'empty-cleanup-failure' {
                $state.Fault='Workspace.Created|<install>\CodexDistributions\<transaction>#1'
                $state.Action={ param($path); $state.CleanupFault=$true; throw 'SIMULATED_M2_PREPARATION_INTERRUPTION' }
            }
            'marker-changed-before-write' {
                $state.Fault='Bundle.Write.After|<install>\Bundles\<transaction>\Payload\Config.json#1'
                $state.Action={
                    param($path)
                    $markerPath=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($path))) $script:StagingMarkerName
                    $marker=Read-InstallerJsonFile $markerPath; $marker.Identity='0' * 64
                    [IO.File]::WriteAllText($markerPath,(ConvertTo-InstallerJson $marker))
                }
            }
        }
        Assert-HostThrows { Invoke-M2Attempt $attempt } 'cleanup failed|ownership marker'
        Assert-HostTest ($state.Reached -and -not (Test-Path $attempt.Root)) "Preparation fault not observed: $kind"
        Assert-HostTest ([IO.File]::ReadAllText($attempt.Task) -ceq 'prior-task-definition') 'Preparation failure changed task state.'
        Reset-M2Trace; Invoke-M2Attempt $attempt; Assert-M2Complete $attempt
        $script:m2Rows.Add([pscustomobject]@{Case=$kind; Reached=$true; Failure='simulated interruption/marker mutation; real cleanup refusal'; Cleanup='preserved; failure reported'; Final='absent'; Retry='PASS identical plan'; ExternalMutations=0})
    }
    # Recompute the same approved source after many unique transactions: random
    # staging names must never enter immutable BundleId or manifest identity.
    $repeatPlan=New-InstallerBundlePlan $hostRoot $script:fakeConfigPath (Import-InstallerConfig $script:fakeConfigPath) $installerPath
    Assert-HostTest ($repeatPlan.BundleId -ceq $attempt.Plan.BundleId -and $repeatPlan.ManifestSha256 -ceq $attempt.Plan.ManifestSha256) 'Transaction paths changed immutable content identity.'
    Assert-M2Complete $initial
    Import-SashimiHostConfig $script:fakeConfigPath | Out-Null
}
