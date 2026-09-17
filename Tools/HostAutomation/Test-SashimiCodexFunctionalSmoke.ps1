#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [ValidateRange(1,300)][int]$TimeoutSeconds = 60,
    [switch]$RunRealCodex,
    [switch]$DryRun,
    [Parameter(DontShow)][switch]$FixtureExecution
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')
$result = [ordered]@{ Success=$false; Mode='Plan'; RealCodexFunctionalSmoke='NOT_RUN';
    DeveloperEdit=$false; ReviewerReadOnly=$false; Cleaned=$false; Plans=@(); Error=$null }
$root = $null
$owned = $false
try {
    if ($RunRealCodex -and $FixtureExecution) { throw 'Real and fixture execution are mutually exclusive.' }
    if ($FixtureExecution -and -not (Test-SashimiHarnessMode)) { throw 'FixtureExecution requires the owned test harness.' }
    if ($RunRealCodex -and (Test-SashimiHarnessMode)) { throw 'Real Codex is prohibited in the fixture harness.' }
    $execute = (-not $DryRun) -and ($RunRealCodex -or $FixtureExecution)
    if ($execute) { $result.Mode = if ($FixtureExecution) { 'Fixture' } else { 'RealHostOptIn' } }
    $ConfigPath = ConvertTo-SashimiPath -Path $ConfigPath
    $config = Import-SashimiHostConfig -ConfigPath $ConfigPath
    if ($execute -and $FixtureExecution) {
        Assert-SashimiFixtureExecutableBoundary -FilePath $config.CodexExecutable -Kind Codex
    }
    $id = [Guid]::NewGuid().ToString('N')
    $root = Join-Path ([IO.Path]::GetTempPath()) ('SashimiBoyFunctionalSmoke-' + $id)
    Assert-SashimiNoReparsePoint -Path $root
    if (Test-Path -LiteralPath $root) { throw 'Refusing an existing smoke workspace.' }
    [void][IO.Directory]::CreateDirectory($root)
    $owned = $true
    Write-SashimiUtf8File -Path (Join-Path $root '.functional-smoke-owner') -Content $id
    $workspace = Join-Path $root 'Workspace'
    foreach ($relative in @('.git/objects','.git/refs/heads')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $workspace $relative))
    }
    # Minimal empty Git repository metadata; no Git executable or network call.
    Write-SashimiUtf8File -Path (Join-Path $workspace '.git/HEAD') -Content "ref: refs/heads/smoke`n"
    Write-SashimiUtf8File -Path (Join-Path $workspace '.git/config') -Content "[core]`nrepositoryformatversion = 0`nbare = false`n"
    Write-SashimiUtf8File -Path (Join-Path $workspace 'Example.txt') -Content "value=1`n"
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + $id
    $head = '1111111111111111111111111111111111111111'
    $plans = [Collections.Generic.List[object]]::new()
    $gitBefore = @{}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspace '.git') -File -Recurse -Force) {
        $gitBefore[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    foreach ($role in @('Developer','Reviewer')) {
        $mode = if ($role -ceq 'Developer') { 'NewWork' } else { 'Review' }
        $artifacts = Join-Path $root ('Artifacts/' + $role)
        $prompt = if ($role -ceq 'Developer') {
            "HOST FUNCTIONAL SMOKE. The complete source of Example.txt is value=1 followed by LF. Use a supported non-shell editing interface to change exactly that file to value=2 followed by LF. Do not run commands. Report changedFiles containing only Example.txt. If no supported editing interface exists, return Blocked."
        } else {
            "HOST FUNCTIONAL SMOKE. Host-verified complete diff for Example.txt: -value=1 +value=2 (LF preserved). Inspect this supplied diff without commands or edits. Return summary containing SMOKE_REVIEW_VALUE_2, changedFiles=[], findings=[]."
        }
        if ($FixtureExecution -and $execute) {
            $fixtureResult = [ordered]@{ schemaVersion=$(if ($role -ceq 'Reviewer') { 2 } else { 1 }); runId=$runId; role=$role; mode=$mode;
                issueNumber=52; pullRequestNumber=$(if ($role -ceq 'Reviewer') { 53 } else { $null });
                headSha=$head; issueValidationId=$null; outcome='Succeeded';
                summary=$(if ($role -ceq 'Reviewer') { 'SMOKE_REVIEW_VALUE_2' } else { 'Fixture source edit' });
                changedFiles=[object[]]@(if ($role -ceq 'Developer') { 'Example.txt' });
                findings=@(); manualVerification=@() }
            Write-SashimiUtf8File -Path (Join-Path $workspace 'functional-smoke.fixture.json') `
                -Content (ConvertTo-SashimiJson $fixtureResult)
        }
        $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',
            (Join-Path $PSScriptRoot 'Invoke-SashimiCodexExec.ps1'),'-ConfigPath',$ConfigPath,
            '-RepositoryPath',$workspace,'-ArtifactsPath',$artifacts,'-Role',$role,'-Mode',$mode,
            '-IssueNumber','52','-PinnedHeadSha',$head,'-RunId',$runId,'-Prompt',$prompt,
            '-TimeoutSeconds',[string]$TimeoutSeconds,'-OwnedProcessRecordPath',(Join-Path $root 'OwnedHostPids.json'))
        if ($role -ceq 'Reviewer') { $arguments += @('-PullRequestNumber','53') }
        if (-not $execute) { $arguments += '-DryRun' }
        $native = Invoke-SashimiHostProcess -FilePath $config.PowerShellExecutable -ArgumentList $arguments `
            -WorkingDirectory $PSScriptRoot -TimeoutSeconds ($TimeoutSeconds + 45)
        if (-not $native.Succeeded) { throw "Production adapter failed for $role (exit $($native.ExitCode))." }
        $adapter = $native.StdOut.Trim() | ConvertFrom-Json -Depth 64 -DateKind String
        if (-not $adapter.Success) { throw "Production adapter rejected $role." }
        $plans.Add([pscustomobject]@{ Role=$role; Arguments=$adapter.PlannedArguments; Executed=$adapter.Executed })
        if ($execute) {
            if ([IO.File]::ReadAllText((Join-Path $workspace 'Example.txt')) -cne "value=2`n") {
                throw 'Expected exact source edit was not produced.'
            }
            if ($role -ceq 'Developer') { $result.DeveloperEdit = $true }
            else {
                if (@($adapter.Result.changedFiles).Count -ne 0 -or
                    [string]$adapter.Result.summary -notmatch 'SMOKE_REVIEW_VALUE_2') {
                    throw 'Reviewer did not acknowledge the Host-provided diff with an empty changedFiles result.'
                }
                $result.ReviewerReadOnly = $true
            }
            $files = [Collections.Generic.List[object]]::new()
            $directories = [Collections.Generic.Stack[string]]::new(); $directories.Push($workspace)
            while ($directories.Count -gt 0) {
                foreach ($entry in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
                    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Smoke workspace contains a reparse point.' }
                    if ($entry.PSIsContainer) {
                        $relativeDirectory = [IO.Path]::GetRelativePath($workspace,$entry.FullName).Replace('\','/')
                        if (@('.git','.git/objects','.git/refs','.git/refs/heads') -cnotcontains $relativeDirectory) { throw 'Smoke workspace contains an unexpected directory.' }
                        $directories.Push($entry.FullName)
                    } else { $files.Add($entry); if ($files.Count -gt 4 -or $entry.Length -gt 16384) { throw 'Smoke workspace exceeds its closed manifest quota.' } }
                }
            }
            $allowed = @('Example.txt','.git/HEAD','.git/config','functional-smoke.fixture.json')
            foreach ($file in $files) {
                Assert-SashimiNoReparsePoint -Path $file.FullName
                $relative = [IO.Path]::GetRelativePath($workspace,$file.FullName).Replace('\','/')
                if ($allowed -cnotcontains $relative) { throw 'Smoke workspace contains an unexpected file.' }
            }
            foreach ($path in $gitBefore.Keys) {
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $gitBefore[$path]) {
                    throw 'Smoke Git metadata changed.'
                }
            }
        }
    }
    $result.Plans = $plans.ToArray()
    $result.Success = $true
    if ($execute -and $RunRealCodex) { $result.RealCodexFunctionalSmoke='PASS' }
}
catch {
    $result.Error = Protect-SashimiText $_.Exception.Message
    if ($RunRealCodex -and -not $DryRun) { $result.RealCodexFunctionalSmoke='FAIL' }
}
finally {
    if ($owned) {
        try {
            $expected = Join-Path ([IO.Path]::GetTempPath()) ('SashimiBoyFunctionalSmoke-' + $id)
            if (-not (Test-SashimiPathEqual -Left $root -Right $expected)) { throw 'Smoke cleanup root mismatch.' }
            Assert-SashimiNoReparsePoint -Path $root
            $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($root)
            while ($pending.Count -gt 0) {
                foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Smoke cleanup refuses reparse traversal.' }
                    if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
                }
            }
            if ([IO.File]::ReadAllText((Join-Path $root '.functional-smoke-owner')) -cne $id) { throw 'Smoke cleanup marker mismatch.' }
            $ledger = Join-Path $root 'OwnedHostPids.json'
            if ((Test-Path -LiteralPath $ledger) -and @((Read-SashimiJsonFile $ledger).Processes).Count -gt 0) {
                throw 'Smoke cleanup refused: process termination remains unconfirmed.'
            }
            Remove-Item -LiteralPath $root -Recurse -Force
            $result.Cleaned = -not (Test-Path -LiteralPath $root)
        } catch { $result.Success=$false; $result.Error='Smoke cleanup refused; inspect the marked temporary workspace.' }
    }
}
[Console]::Out.WriteLine((ConvertTo-SashimiJson $result))
if (-not $result.Success) { exit 1 }
