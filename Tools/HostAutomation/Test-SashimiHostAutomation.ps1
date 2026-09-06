#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot,

    [string]$ConfigPath,

    [switch]$EnvironmentSmoke,

    [switch]$KeepTemporaryFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
}

$testScript = Join-Path $PSScriptRoot 'Tests\Test-SashimiHostAutomation.ps1'

if ($EnvironmentSmoke) {
    . (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $PSScriptRoot 'Config.example.json' }
    $checks = [Collections.Generic.List[object]]::new()
    function Add-SmokeCheck([string]$Name, [bool]$Passed, [string]$Detail) {
        $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=(Protect-SashimiText $Detail) })
    }
    try {
        $config = Import-SashimiHostConfig -ConfigPath $ConfigPath
        Add-SmokeCheck 'ConfigContract' $true "Schema $($config.SchemaVersion), $($config.Repository), Project $($config.ProjectOwner)/$($config.ProjectNumber)"
        Add-SmokeCheck 'PowerShell7' ($PSVersionTable.PSVersion.Major -ge 7) $PSVersionTable.PSVersion.ToString()
        Add-SmokeCheck 'StablePowerShellPath' (Test-Path -LiteralPath ([string]$config.PowerShellExecutable) -PathType Leaf) ([string]$config.PowerShellExecutable)
        Add-SmokeCheck 'UnityExecutable' (Test-Path -LiteralPath ([string]$config.UnityExecutable) -PathType Leaf) ([string]$config.UnityExecutable)
        foreach ($tool in @(
            [pscustomobject]@{ Name='GitExecutable'; Value=[string]$config.GitExecutable },
            [pscustomobject]@{ Name='GitLfsExecutable'; Value=[string]$config.GitLfsExecutable },
            [pscustomobject]@{ Name='GitHubCli'; Value=[string]$config.GitHubCli },
            [pscustomobject]@{ Name='CodexExecutable'; Value=[string]$config.CodexExecutable }
        )) {
            $command = Get-Command $tool.Value -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            Add-SmokeCheck $tool.Name ($null -ne $command) $(if ($null -eq $command) { $tool.Value } else { [string]$command.Source })
        }
        Add-SmokeCheck 'WindowsUser' ([Environment]::UserName -ceq '02031') ([Environment]::UserName)
        Add-SmokeCheck 'SchedulerModule' ($null -ne (Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue)) 'Register-ScheduledTask command availability only; no scheduler call made.'

        $smokeRunId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
        $smokeArtifactPath = Join-Path ([IO.Path]::GetTempPath()) ('SashimiBoyAutomation\CapabilitySmoke-' + [Guid]::NewGuid().ToString('N'))
        $adapterPath = Join-Path $PSScriptRoot 'Invoke-SashimiCodexExec.ps1'
        $adapterOutput = & ([string]$config.PowerShellExecutable) -NoLogo -NoProfile -NonInteractive -File $adapterPath `
            -ConfigPath $ConfigPath -RepositoryPath $RepositoryRoot -ArtifactsPath $smokeArtifactPath `
            -Role Reviewer -Mode Review -IssueNumber 52 -PullRequestNumber 1 `
            -PinnedHeadSha '1111111111111111111111111111111111111111' -RunId $smokeRunId -Prompt '' -DryRun
        $adapterExit = $LASTEXITCODE
        try { $adapter = @($adapterOutput)[-1] | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw "Codex capability smoke returned invalid JSON: $($_.Exception.Message)" }
        $adapterPassed = ($adapterExit -eq 0 -and [bool]$adapter.Success -and -not [bool]$adapter.Executed -and [string]$adapter.Sandbox -ceq 'read-only')
        Add-SmokeCheck 'CodexCapabilityProbe' $adapterPassed "version=$($adapter.CodexVersion); executed=$($adapter.Executed); sandbox=$($adapter.Sandbox)"
        Add-SmokeCheck 'NoSmokeArtifactsCreated' (-not (Test-Path -LiteralPath $smokeArtifactPath)) 'Codex -DryRun did not create its artifact path.'
    }
    catch {
        Add-SmokeCheck 'UnhandledSmokeFailure' $false $_.Exception.Message
    }
    $failed = @($checks | Where-Object { -not $_.Passed }).Count
    $output = [ordered]@{ SchemaVersion=1; Tool='Test-SashimiHostAutomation.EnvironmentSmoke'; Success=($failed -eq 0); Passed=$checks.Count-$failed; Failed=$failed; ExternalMutationCount=0; Checks=$checks.ToArray() }
    [Console]::Out.WriteLine((ConvertTo-SashimiJson $output))
    if ($failed -gt 0) { exit 1 } else { exit 0 }
}

if (-not (Test-Path -LiteralPath $testScript -PathType Leaf)) {
    throw "Host automation test harness is missing: $testScript"
}

$parameters = @{ RepositoryRoot = $RepositoryRoot }
if ($KeepTemporaryFiles) { $parameters.KeepTemporaryFiles = $true }
& $testScript @parameters
$testExitCode = $LASTEXITCODE
if ($null -eq $testExitCode) { $testExitCode = 0 }
exit ([int]$testExitCode)
