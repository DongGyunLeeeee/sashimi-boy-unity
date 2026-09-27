#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$ChildExecutable,
    [Parameter(Mandatory)][string]$EvidencePath,
    [ValidateSet('Assignment','Ledger','Termination')][string]$FailureBoundary = 'Assignment'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commonPath = Join-Path $RepositoryRoot 'Tools/HostAutomation/HostAutomation.Common.ps1'
. $commonPath
if (-not (Test-SashimiHarnessMode)) { throw 'Pre-assignment fault injection requires the marked test harness.' }
[void](Assert-SashimiFixtureAllowed $ChildExecutable)
[void](Assert-SashimiFixtureAllowed $EvidencePath)
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($commonPath,[ref]$tokens,[ref]$errors)
$definition = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Initialize-SashimiKillOnCloseProcessNative' },$true))
if ($errors.Count -ne 0 -or $definition.Count -ne 1) { throw 'Cannot extract the exact production native boundary.' }
$body = $definition[0].Body.Extent.Text.Replace("`r`n","`n")
if ($FailureBoundary -in @('Assignment','Termination')) {
    $declaration = @'
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
'@
    $declaration = $declaration.Replace("`r`n","`n")
    if ([regex]::Matches($body,[regex]::Escape($declaration)).Count -ne 1) { throw 'Fault boundary is not unique.' }
    # Only this Win32 return value changes. Creation, suspension, ledger,
    # termination and handle cleanup execute the production implementation.
    $body = $body.Replace($declaration,'        private static bool AssignProcessToJobObject(IntPtr job, IntPtr process) { return false; }')
}
if ($FailureBoundary -ceq 'Termination') {
    $declaration = @'
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, UInt32 exitCode);
'@
    $declaration = $declaration.Replace("`r`n","`n")
    if ([regex]::Matches($body,[regex]::Escape($declaration)).Count -ne 1) { throw 'Termination fault boundary is not unique.' }
    $body = $body.Replace($declaration,'        private static bool TerminateProcess(IntPtr process, UInt32 exitCode) { return false; }')
}
& ([scriptblock]::Create($body.Substring(1,$body.Length-2)))
$events = [Collections.Generic.List[object]]::new()
$callback = [Action[int,string,bool]]{
    param($childId,$startedUtc,$added)
    $events.Add([pscustomobject]@{Id=$childId;StartTimeUtc=$startedUtc;Added=$added})
    if ($added -and $FailureBoundary -ceq 'Ledger') { throw 'Injected ledger failure before job assignment.' }
}
$environment = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('SystemRoot','WINDIR','TEMP','TMP')) { $environment[$name] = [Environment]::GetEnvironmentVariable($name) }
$sentinel = $EvidencePath + '.executed'
$began = [DateTime]::Now
$failure = ''; $leaked = [Collections.Generic.List[int]]::new()
try {
    [void][SashimiBoyAutomation.KillOnCloseProcess]::Run($ChildExecutable,[string[]]@("--sashimi-write-sentinel=$sentinel"),
        (Split-Path -Parent $EvidencePath),'',$environment,2,'',$callback,$null)
} catch { $failure = $_.Exception.Message }
# A regression must not leave a suspended test child behind. Scope cleanup
# to this fixture's parent, exact executable argument and kernel start time.
$childName = [IO.Path]::GetFileName($ChildExecutable)
$children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$PID" | Where-Object { $_.Name -ceq $childName -and $_.CreationDate -ge $began })
foreach ($child in $children) {
    $owned = Get-Process -Id $child.ProcessId -ErrorAction Stop
    try {
        if (-not ([string]$child.CommandLine).StartsWith('"' + $ChildExecutable + '"',[StringComparison]::Ordinal) -or
            -not ([string]$child.CommandLine).Contains("--sashimi-write-sentinel=$sentinel") -or
            [Math]::Abs(($owned.StartTime.ToUniversalTime()-$child.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
            throw 'Unexpected child identity; cleanup refused.'
        }
        $leaked.Add($owned.Id)
        $owned.Kill()
        if (-not $owned.WaitForExit(5000)) { throw 'Owned fixture child cleanup is unconfirmed.' }
    } finally { $owned.Dispose() }
}
$added = @($events | Where-Object Added); $removed = @($events | Where-Object { -not $_.Added })
$expectedError = if ($FailureBoundary -ceq 'Ledger') { 'Injected ledger failure' } else { 'AssignProcessToJobObject' }
$identityCorrect = if ($FailureBoundary -ceq 'Termination') {
    # Failed termination must retain the exact owned PID for later recovery.
    $leaked.Count -eq 1 -and $added.Count -eq 1 -and $removed.Count -eq 0 -and $added[0].Id -eq $leaked[0]
} else {
    $leaked.Count -eq 0 -and $added.Count -eq 1 -and $removed.Count -eq 1 -and $added[0].Id -eq $removed[0].Id -and
        $added[0].StartTimeUtc -ceq $removed[0].StartTimeUtc
}
$success = $failure -match $expectedError -and $identityCorrect -and -not (Test-Path -LiteralPath $sentinel)
$result = [ordered]@{
    Success=$success; FailureBoundary=$FailureBoundary; SourceSha256=(Get-FileHash -LiteralPath $commonPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Failure=$failure; LedgerEvents=@($events.ToArray()); LeakedProcessIds=@($leaked.ToArray())
    ChildExecuted=(Test-Path -LiteralPath $sentinel); CleanupConfirmed=$true
}
Write-SashimiUtf8File $EvidencePath (ConvertTo-SashimiJson $result -Pretty)
[Console]::Out.WriteLine((ConvertTo-SashimiJson $result))
if (-not $success) { exit 1 }
