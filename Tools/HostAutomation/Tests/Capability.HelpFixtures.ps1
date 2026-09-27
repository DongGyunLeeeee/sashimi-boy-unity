# Loaded only by the owned offline fixture harness.
function Invoke-HostCapabilityHelpContextRegression {
    $specimen = Read-SashimiJsonFile (Join-Path $PSScriptRoot 'Fixtures/Codex.SecureExecHelp.0.153.2.json')
    $adapterPath = Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1'
    $previousSecrets = Get-Variable sensitiveEnvironmentValues -Scope Script -ErrorAction SilentlyContinue
    $previousLedger = Get-Variable adapterLedgerPath -Scope Script -ErrorAction SilentlyContinue
    try {
        $script:sensitiveEnvironmentValues = @()
        $script:adapterLedgerPath = Join-Path $script:temporaryRoot 'help-context-ledger.json'
        foreach ($name in @('Get-AdapterProperty','Get-AdapterTextSha256','New-CodexContentFreeDiagnostic',
            'Assert-CodexOriginalProcessOutputSafe','Assert-AdapterCommand','Invoke-AdapterProcess','Get-CodexSecureHelpArguments')) {
            Set-Item -Path ('Function:' + $name) -Value (Get-HostTestFunctionScriptBlock -ScriptPath $adapterPath -FunctionName $name)
        }
        # Only the native return is simulated here. The actual production argument
        # routing, raw-byte audit and exception selection are executed unchanged.
        function Invoke-SashimiHostProcess {
            param($FilePath,$ArgumentList,$WorkingDirectory,$TimeoutSeconds,$StandardInputText,
                $CancellationMarkerPath,$Environment,$RemoveEnvironmentVariables,[switch]$ClearEnvironment,
                [switch]$PreserveRawOutputInMemory,$CodexWorkspacePath,$OwnedProcessRecordPath,
                [switch]$RequireKillOnCloseJob,$CaptureRoots,$Kind)
            return $fixtureResponse
        }
        $normalizedArtifacts = Join-Path $script:temporaryRoot 'help-context-artifacts'
        $baseParameters = @{
            FilePath=$script:fakeCodex.Path; ArgumentList=@(Get-CodexSecureHelpArguments)
            WorkingDirectory=$script:fakeRepository; TimeoutSeconds=30; CapabilityHelpProbe=$true
        }
        $baseline = @{
            ExitCode=0; TimedOut=$false; Cancelled=$false
            TerminationConfirmed=$true; KillOnCloseJobAssigned=$true
            UnredactedStdOut=[string]$specimen.StdOut; UnredactedStdErr=''
        }
        $fixtureResponse = [pscustomobject]$baseline.Clone()
        $accepted = Invoke-AdapterProcess @baseParameters
        Assert-HostTest ($accepted.StdOut -ceq $specimen.StdOut) 'Reviewed help was trimmed or rewritten in transport.'
        Assert-HostTest ((Get-AdapterTextSha256 $accepted.StdOut) -ceq $specimen.StdOutSha256) 'Reviewed help fixture changed.'
        $cases = @(
            @{ Name='model-context'; Parameters=@{ CapabilityHelpProbe=$false } },
            @{ Name='empty-stdin-parameter'; Parameters=@{ StandardInputText='' } },
            @{ Name='stdin-required'; Parameters=@{ RequireStandardInput=$true; StandardInputText='input' } },
            @{ Name='argv-extra'; Parameters=@{ ArgumentList=@(Get-CodexSecureHelpArguments) + @('-c','test=false') } },
            @{ Name='argv-case'; Parameters=@{ ArgumentList=@(Get-CodexSecureHelpArguments | ForEach-Object { if ($_ -ceq '--help') { '--HELP' } else { $_ } }) } },
            @{ Name='nonzero'; Response=@{ ExitCode=1 } },
            @{ Name='timeout'; Response=@{ TimedOut=$true } },
            @{ Name='cancelled'; Response=@{ Cancelled=$true } },
            @{ Name='stderr'; Response=@{ UnredactedStdErr='warning' } },
            @{ Name='appended-byte'; Response=@{ UnredactedStdOut=([string]$specimen.StdOut + 'x') } },
            @{ Name='changed-byte'; Response=@{ UnredactedStdOut=([string]$specimen.StdOut).Replace('Run Codex','Run Codey') } },
            @{ Name='missing-final-lf'; Response=@{ UnredactedStdOut=([string]$specimen.StdOut).TrimEnd() } },
            @{ Name='credential-path'; Response=@{ UnredactedStdOut=([string]$specimen.StdOut).Replace('~/.codex/config.toml','~/.codex/auth.json') } },
            @{ Name='profile'; Response=@{ UnredactedStdOut=([string]$specimen.StdOut + [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) } },
            @{ Name='inherited-documentation-secret'; Secret='~/.codex/config.toml' },
            @{ Name='inherited-other-secret'; Secret='workspace-write' }
        )
        foreach ($case in $cases) {
            $parameters = $baseParameters.Clone()
            $response = $baseline.Clone()
            if ($case.ContainsKey('Parameters')) { foreach ($key in $case.Parameters.Keys) { $parameters[$key]=$case.Parameters[$key] } }
            if ($case.ContainsKey('Response')) { foreach ($key in $case.Response.Keys) { $response[$key]=$case.Response[$key] } }
            $fixtureResponse = [pscustomobject]$response
            $script:sensitiveEnvironmentValues = @(if ($case.ContainsKey('Secret')) { $case.Secret })
            Assert-HostThrows -Body { Invoke-AdapterProcess @parameters } -Pattern 'CODEX_ORIGINAL_OUTPUT_(FORBIDDEN_CONTENT|PROFILE_PATH)'
        }
    } finally {
        if ($null -eq $previousSecrets) { Remove-Variable sensitiveEnvironmentValues -Scope Script -ErrorAction SilentlyContinue }
        else { $script:sensitiveEnvironmentValues=$previousSecrets.Value }
        if ($null -eq $previousLedger) { Remove-Variable adapterLedgerPath -Scope Script -ErrorAction SilentlyContinue }
        else { $script:adapterLedgerPath=$previousLedger.Value }
    }
}

function Invoke-HostCapabilityHelpNativeRegression {
    $specimen = Read-SashimiJsonFile (Join-Path $PSScriptRoot 'Fixtures/Codex.SecureExecHelp.0.153.2.json')
    $helpPath = [IO.Path]::ChangeExtension($script:fakeCodex.Path,'.exec-help.txt')
    $stderrPath = [IO.Path]::ChangeExtension($script:fakeCodex.Path,'.exec-help.stderr.txt')
    $exitPath = [IO.Path]::ChangeExtension($script:fakeCodex.Path,'.exec-help.exit.txt')
    $ownedFixtureFiles = @($helpPath,$stderrPath,$exitPath,$script:fakeCodex.AuditPath,$script:fakeCodex.MaliciousJsonlPath)
    $configPath = New-HostTestConfig -CodexExecutable $script:fakeCodex.Path
    $cases = @(
        @{ Name='reviewed-help'; Pass=$true; Help=[string]$specimen.StdOut },
        @{ Name='appended-byte'; Pass=$false; Help=([string]$specimen.StdOut + 'x') },
        @{ Name='changed-byte'; Pass=$false; Help=([string]$specimen.StdOut).Replace('Run Codex','Run Codey') },
        @{ Name='trimmed-help'; Pass=$false; Help=([string]$specimen.StdOut).TrimEnd() },
        @{ Name='stderr'; Pass=$false; Help=[string]$specimen.StdOut; Stderr='warning' },
        @{ Name='nonzero'; Pass=$false; Help=[string]$specimen.StdOut; Exit=7 },
        @{ Name='model-help'; Pass=$false; Help=[string]$specimen.StdOut; ModelOutput=[string]$specimen.StdOut }
    )
    try {
        foreach ($case in $cases) {
            foreach ($path in $ownedFixtureFiles) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
            }
            Write-HostTestFile $helpPath $case.Help
            if ($case.ContainsKey('Stderr')) { Write-HostTestFile $stderrPath $case.Stderr }
            if ($case.ContainsKey('Exit')) { Write-HostTestFile $exitPath ([string]$case.Exit) }
            if ($case.ContainsKey('ModelOutput')) { Write-HostTestFile $script:fakeCodex.MaliciousJsonlPath $case.ModelOutput }
            $runId = '20260921T140000Z-' + [Guid]::NewGuid().ToString('N')
            $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5352 -Role Reviewer -Mode Review -PullRequestNumber 6352
            $resultObject.headSha = 'd' * 40
            Write-HostTestFile $script:fakeCodex.ResultPath ($resultObject | ConvertTo-Json -Depth 32 -Compress)
            $artifacts = Join-Path $script:temporaryRoot ('help-native-' + $case.Name)
            $parameters = @{
                ConfigPath=$configPath; RepositoryPath=$script:fakeRepository; ArtifactsPath=$artifacts
                Role='Reviewer'; Mode='Review'; IssueNumber=5352; PullRequestNumber=6352
                PinnedHeadSha=('d' * 40); RunId=$runId; Prompt='Return the fixture result without commands.'
            }
            $native = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1') -Parameters $parameters -TimeoutSeconds 60
            $json = ConvertFrom-LastHostJson $native.StdOut
            $audit = @(Get-HostFakeCodexAudit $script:fakeCodex.AuditPath)
            if ($case.Pass) {
                Assert-HostTest ($native.ExitCode -eq 0 -and $json.Success -and $json.Executed -and $audit.Count -eq 2) "Reviewed native help failed: $($json.Error)"
            } else {
                Assert-HostTest ($native.ExitCode -ne 0 -and -not $json.Success -and [string]$json.Error -match 'CODEX_ORIGINAL_OUTPUT_FORBIDDEN_CONTENT') "Contaminated help passed: $($case.Name)"
                $expectedCalls = if ($case.ContainsKey('ModelOutput')) { 2 } else { 1 }
                # A rejected raw stream throws inside Invoke-AdapterProcess,
                # before the caller records Executed. Native audit is the call count.
                Assert-HostTest ($audit.Count -eq $expectedCalls -and -not [bool]$json.Executed) "Unexpected native call count or accepted execution after $($case.Name)"
                Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $artifacts 'CodexResult.json'))) 'Rejected help promoted a result.'
                Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $artifacts 'CodexEvents.jsonl'))) 'Rejected help promoted events.'
            }
        }
    } finally {
        foreach ($path in $ownedFixtureFiles) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
        }
    }
}
