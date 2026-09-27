function Invoke-HostLfsCacheRegression {
    function New-CacheCase([string]$Name) {
        $root=Join-Path $script:temporaryRoot ("lfs-cache-"+$Name)
        $runs=Join-Path $root 'Runs'
        $run=New-SashimiRunWorkspace -RunRoot $runs
        [pscustomobject]@{ Root=$root; Runs=$runs; Run=$run; Cache=(Join-Path $root 'LfsCache\objects') }
    }
    function New-CacheEntry([string]$Name,[string]$Text) {
        [pscustomobject]@{name=$Name; size=[long][Text.Encoding]::UTF8.GetByteCount($Text);
            oid=(Get-SashimiTextSha256 $Text); oid_type='sha256'; version='https://git-lfs.github.com/spec/v1'}
    }
    function Get-ObjectPath([string]$Root,[string]$Oid) {
        Join-Path $Root (Join-Path $Oid.Substring(0,2) (Join-Path $Oid.Substring(2,2) $Oid))
    }
    function Put-LocalObject($Run,$Entry,[string]$Text) {
        $path=Get-ObjectPath (Join-Path $Run.RepositoryPath '.git\lfs\objects') $Entry.oid
        Write-SashimiUtf8File $path $Text
        $path
    }
    $textA='fixture-lfs-alpha'; $textB='fixture-lfs-bravo'
    $a=New-CacheEntry 'Assets/A.bin' $textA
    $b=New-CacheEntry 'Assets/B.bin' $textB
    $one=ConvertTo-SashimiJson @{files=@($a)}
    $two=ConvertTo-SashimiJson @{files=@($a,$b)}
    $case=New-CacheCase 'warm'
    [void](Put-LocalObject $case.Run $a $textA)
    $config=Join-Path $case.Run.RepositoryPath '.git\config'
    Write-SashimiUtf8File $config 'fixture config sentinel'
    $store=Sync-SashimiLfsObjectCache $case.Run.RepositoryPath $case.Runs $one Store
    Assert-HostTest ($store.Stored -eq 1) 'Cold cache did not store a verified object.'
    $warm=New-SashimiRunWorkspace $case.Runs
    $restored=Sync-SashimiLfsObjectCache $warm.RepositoryPath $case.Runs $one Restore
    Assert-HostTest ($restored.Hits -eq 1 -and $restored.AllAvailable) 'Warm clone did not restore verified bytes.'
    $again=Sync-SashimiLfsObjectCache $warm.RepositoryPath $case.Runs $one Restore
    Assert-HostTest ($again.Hits -eq 0 -and $again.Available -eq 1 -and $again.AllAvailable) 'Valid local object was not reused.'
    Assert-HostTest ([IO.File]::ReadAllText($config) -ceq 'fixture config sentinel') 'Cache changed Git configuration.'
    Assert-HostTest (-not (Test-Path (Join-Path $warm.RepositoryPath '.git\config'))) 'Cache copied Git control data.'
    $partial=Sync-SashimiLfsObjectCache $warm.RepositoryPath $case.Runs $two Restore
    Assert-HostTest (-not $partial.AllAvailable -and $partial.Misses -eq 1 -and $partial.Available -eq 1) 'Partial cache was treated as complete.'
    $bad=New-CacheCase 'corrupt'
    Write-SashimiUtf8File (Get-ObjectPath $bad.Cache $a.oid) ('x'*$a.size)
    $miss=Sync-SashimiLfsObjectCache $bad.Run.RepositoryPath $bad.Runs $one Restore
    Assert-HostTest (-not $miss.AllAvailable -and $miss.Misses -eq 1) 'Same-size corrupt cache passed SHA validation.'
    Assert-HostTest (-not (Test-Path (Get-ObjectPath (Join-Path $bad.Run.RepositoryPath '.git\lfs\objects') $a.oid))) 'Corrupt bytes reached a fresh clone.'
    $badLocal=Put-LocalObject $warm $a ('x'*$a.size)
    $refused=Sync-SashimiLfsObjectCache $warm.RepositoryPath $case.Runs $one Restore
    Assert-HostTest (-not $refused.AllAvailable -and [IO.File]::ReadAllText($badLocal) -ceq ('x'*$a.size)) 'Cache overwrote a conflicting local object.'
    foreach ($invalid in @(
        '{"files":[{"oid":"../escape","size":1,"name":"x","oid_type":"sha256","version":"https://git-lfs.github.com/spec/v1"}]}',
        $one.Replace('Assets/A.bin','../escape'),
        (ConvertTo-SashimiJson @{files=@($a,$a)}),
        (ConvertTo-SashimiJson @{files=@($a,($a|Select-Object *,@{n='unused';e={1}}))})
    )) { Assert-HostThrows { ConvertFrom-SashimiLfsObjectManifest $invalid } }
    $differentSize=$a|Select-Object *
    $differentSize.name='Assets/C.bin'; $differentSize.size++
    Assert-HostThrows { ConvertFrom-SashimiLfsObjectManifest (ConvertTo-SashimiJson @{files=@($a,$differentSize)}) } 'different size'
    $overflowA=$a|Select-Object *; $overflowB=$b|Select-Object *
    $overflowA.size=[long]::MaxValue; $overflowB.size=1L
    Assert-HostThrows { ConvertFrom-SashimiLfsObjectManifest (ConvertTo-SashimiJson @{files=@($overflowA,$overflowB)}) } 'overflows'
    Assert-HostThrows { ConvertFrom-SashimiLfsObjectManifest (' '* (8MB+1)) } 'size limit'
    $dry=New-CacheCase 'dry'
    [void](Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Store -DryRun)
    [void](Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Restore -DryRun)
    Assert-HostTest (-not (Test-Path $dry.Cache)) 'DryRun created cache storage.'
    Write-SashimiUtf8File (Join-Path $dry.Run.RepositoryPath $a.name) 'version https://git-lfs.github.com/spec/v1'
    Assert-HostThrows { Assert-SashimiLfsMaterializedBytes $dry.Run.RepositoryPath $one } 'pinned manifest'
    Write-SashimiUtf8File (Join-Path $dry.Run.RepositoryPath $a.name) $textA
    Assert-SashimiLfsMaterializedBytes $dry.Run.RepositoryPath $one
    & {
        # Model an unavailable optional cache without weakening run ownership.
        $baseAssert=(Get-Item Function:Assert-SashimiNoReparsePoint).ScriptBlock
        function Assert-SashimiNoReparsePoint {
            param($Path,[switch]$Recurse)
            if ($Path -like '*\LfsCache\*') { throw [UnauthorizedAccessException]::new('fixture cache denied') }
            & $baseAssert -Path $Path -Recurse:$Recurse
        }
        $denied=Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Restore
        Assert-HostTest (-not $denied.AllAvailable -and $denied.Warnings -contains 'CacheAccessUnavailable') 'Cache access error escaped optional fallback.'
        [void](Put-LocalObject $dry.Run $a $textA)
        $local=Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Restore
        Assert-HostTest $local.AllAvailable 'Unavailable cache blocked valid local content.'
        $storeDenied=Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Store
        Assert-HostTest ($storeDenied.Skipped -eq 1) 'Cache store permission error blocked the caller.'
    }
    & {
        function Get-SashimiLfsCacheUsage { param($Path) [pscustomobject]@{Bytes=32GB; Files=10000; Full=$true} }
        $full=Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Store
        Assert-HostTest ($full.Skipped -eq 1 -and -not (Test-Path $dry.Cache)) 'Cache capacity policy created or blocked content.'
    }
    & {
        function Enter-SashimiHostMutex { param($Name,$TimeoutMilliseconds) [pscustomobject]@{Acquired=$false; Name=$Name} }
        function Exit-SashimiHostMutex { param($Lease) }
        $busy=Sync-SashimiLfsObjectCache $dry.Run.RepositoryPath $dry.Runs $one Store
        Assert-HostTest ($busy.Skipped -eq 1 -and $busy.Warnings -contains 'CacheWriterBusy') 'Busy cache writer blocked a run.'
    }
    & {
        # Deterministic nonce collision; never claim naturally occurring races.
        $copyBody=(Get-HostTestFunctionScriptBlock $commonPath 'Copy-SashimiVerifiedLfsObject').ToString()
        Set-Item Function:Copy-SashimiVerifiedLfsObject ([scriptblock]::Create($copyBody.Replace('[Guid]::NewGuid()', "[Guid]::Parse('11111111-1111-1111-1111-111111111111')")))
        $source=Put-LocalObject $dry.Run $a $textA
        $target=Join-Path $dry.Root 'collision\object'
        $sentinel=Join-Path (Split-Path $target) '.lfs-cache-copy-11111111111111111111111111111111.tmp'
        Write-SashimiUtf8File $sentinel 'non-owned sentinel'
        Assert-HostThrows { Copy-SashimiVerifiedLfsObject $source $target $a.oid $a.size }
        Assert-HostTest ([IO.File]::ReadAllText($sentinel) -ceq 'non-owned sentinel') 'Failed CreateNew deleted an existing file.'
    }
    # Drive the shared production role glue: checkout on all hits, canonical
    # pull on a miss, verify the working bytes before admitting the workspace.
    $flow=New-CacheCase 'flow'
    [void](Put-LocalObject $flow.Run $a $textA)
    [void](Sync-SashimiLfsObjectCache $flow.Run.RepositoryPath $flow.Runs $one Store)
    foreach ($mode in @('warm','partial','broken')) {
        $flowRun=New-SashimiRunWorkspace $flow.Runs
        $calls=[Collections.Generic.List[object]]::new()
        $manifest=if($mode -eq 'partial'){$two}else{$one}
        $invoke={
            param([string]$Stage,[string[]]$Arguments)
            $calls.Add([pscustomobject]@{Stage=$Stage; Arguments=$Arguments})
            if($Arguments[0] -eq 'ls-files'){return [pscustomobject]@{StdOut=$manifest}}
            if($Arguments[0] -in @('checkout','pull') -and $mode -ne 'broken'){
                Write-SashimiUtf8File (Join-Path $flowRun.RepositoryPath $a.name) $textA
                if($mode -eq 'partial'){
                    [void](Put-LocalObject $flowRun $b $textB)
                    Write-SashimiUtf8File (Join-Path $flowRun.RepositoryPath $b.name) $textB
                }
            }
            [pscustomobject]@{StdOut=''}
        }
        $callArgs=@{RepositoryPath=$flowRun.RepositoryPath;RunRoot=$flow.Runs;CommitSha=('3'*40);Remote='sashimi-canonical';InvokeLfs=$invoke}
        if($mode -eq 'broken') { Assert-HostThrows { Initialize-SashimiLfsWorkspace @callArgs }; continue }
        $initialized=Initialize-SashimiLfsWorkspace @callArgs
        $materialize=@($calls|Where-Object {$_.Arguments[0] -in @('checkout','pull')})
        Assert-HostTest ($materialize.Count -eq 1 -and $calls[0].Arguments[-1] -ceq ('3'*40)) 'Role glue did not bind the manifest to the exact commit.'
        if($mode -eq 'warm') {
            Assert-HostTest ($initialized.Strategy -ceq 'checkout' -and $materialize[0].Arguments.Count -eq 1) 'Warm role queried a remote instead of checkout.'
        } else {
            Assert-HostTest ($initialized.Strategy -ceq 'pull' -and $materialize[0].Arguments[1] -ceq 'sashimi-canonical') 'Partial role lost canonical pull routing.'
        }
    }
}

function Invoke-HostLfsIndexRefreshRegression {
    $developerPath=Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1'
    Set-Item Function:ConvertTo-DeveloperLfsControlComparison (Get-HostTestFunctionScriptBlock $developerPath 'ConvertTo-DeveloperLfsControlComparison')
    Set-Item Function:Assert-GitOwnershipUnchanged (Get-HostTestFunctionScriptBlock $developerPath 'Assert-GitOwnershipUnchanged')
    $script:gitControlGuardSnapshot=$null
    $events=[Collections.Generic.List[object]]::new()
    $before=[pscustomobject]@{
        Head=('1'*40); RefsSha256='refs'; LocalConfigSha256='config';
        IndexEntriesSha256='entries'; IndexFlagsSha256='flags'; StagedTreeSha256='tree'
        ControlFiles=@(
            [pscustomobject]@{Path='.git/index';Exists=$true;Length=393L;Sha256='old'},
            [pscustomobject]@{Path='.git/config';Exists=$true;Length=308L;Sha256='config'})
        GitControlManifest=@(
            [pscustomobject]@{Kind='File';Path='.git/index';Length=393L;Sha256='old'},
            [pscustomobject]@{Kind='File';Path='.git/config';Length=308L;Sha256='config'})
    }
    function New-IndexRefresh {
        $copy=ConvertTo-SashimiJson $before | ConvertFrom-Json
        $copy.ControlFiles[0].Length=374L; $copy.ControlFiles[0].Sha256='new'
        $copy.GitControlManifest[0].Length=374L; $copy.GitControlManifest[0].Sha256='new'
        $copy
    }
    function Get-GitOwnershipSnapshot { param($Boundary) $after }
    $after=New-IndexRefresh
    $boundary='immediately after Git LFS materialization'
    $accepted=Assert-GitOwnershipUnchanged $before $boundary -AllowHostLfsIndexRefresh
    Assert-HostTest ($accepted.ControlFiles[0].Sha256 -ceq 'new' -and $before.ControlFiles[0].Sha256 -ceq 'old') 'LFS exception mutated the evidence snapshots.'
    Assert-HostThrows { Assert-GitOwnershipUnchanged $before $boundary } 'immutable field'
    Assert-HostThrows { Assert-GitOwnershipUnchanged $before 'after Codex' -AllowHostLfsIndexRefresh } 'initial Host'
    $script:gitControlGuardSnapshot=$before
    Assert-HostThrows { Assert-GitOwnershipUnchanged $before $boundary -AllowHostLfsIndexRefresh } 'initial Host'
    $script:gitControlGuardSnapshot=$null
    foreach($case in @('Head','RefsSha256','LocalConfigSha256','IndexEntriesSha256','IndexFlagsSha256','StagedTreeSha256','config-bytes','index-absent','index-kind','duplicate-index','missing-index','index-path')){
        $after=New-IndexRefresh
        switch($case){
            'config-bytes' {$after.GitControlManifest[1].Sha256='changed'}
            'index-absent' {$after.ControlFiles[0].Exists=$false}
            'index-kind' {$after.GitControlManifest[0].Kind='Directory'}
            'duplicate-index' {$after.GitControlManifest+=($after.GitControlManifest[0]|Select-Object *)}
            'missing-index' {$after.GitControlManifest=@($after.GitControlManifest[1])}
            'index-path' {$after.GitControlManifest[0].Path='.git/other-index'}
            default {$after.$case='changed'}
        }
        Assert-HostThrows { Assert-GitOwnershipUnchanged $before $boundary -AllowHostLfsIndexRefresh } 'immutable field'
    }
    $script:gitControlSecurityFailure=$false
}
