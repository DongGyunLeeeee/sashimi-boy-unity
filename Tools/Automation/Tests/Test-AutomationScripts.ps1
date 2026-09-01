#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot,

    [Parameter()]
    [string]$WindowsPowerShellPath = (Join-Path -Path $env:SystemRoot -ChildPath 'System32\WindowsPowerShell\v1.0\powershell.exe'),

    [switch]$KeepTemporaryFiles,

    [Parameter(DontShow = $true)]
    [ValidateSet('None', 'MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')]
    [string]$InternalLifecycleFailure = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '..\..\..')).ProviderPath
}

$commonPath = Join-Path -Path $RepositoryRoot -ChildPath 'Tools\Automation\Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    throw "Automation common helpers do not exist: $commonPath"
}

. $commonPath

$script:testResults = New-Object System.Collections.Generic.List[object]
$script:temporaryRoot = $null
$script:fixture = $null
$script:unityProjectPath = $null
$script:testRunId = $null
$script:ownedTestRootCreated = $false
$script:ownerMarkerWritten = $false
$script:suiteCompleted = $false
$script:suiteSucceeded = $false
$script:testRootPreserved = $false

if ($InternalLifecycleFailure -ne 'None' -and
    $env:SASHIMI_BOY_AUTOMATION_TEST_HARNESS -cne '1') {
    throw 'Internal lifecycle failure injection is available only to this script smoke harness.'
}

function Assert-AutomationTest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-AutomationTestCase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Body
    )

    $started = [DateTime]::UtcNow
    try {
        & $Body
        $script:testResults.Add([pscustomobject][ordered]@{
                Name       = $Name
                Passed     = $true
                DurationMs = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
                Error      = $null
            })
    }
    catch {
        $script:testResults.Add([pscustomobject][ordered]@{
                Name       = $Name
                Passed     = $false
                DurationMs = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
                Error      = $_.Exception.Message
            })
    }
}

function ConvertTo-SingleQuotedPowerShellLiteral {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "''") + "'"
}

function ConvertTo-PowerShellValueExpression {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return '$null'
    }

    if ($Value -is [bool]) {
        if ($Value) {
            return '$true'
        }

        return '$false'
    }

    if (($Value -is [System.Collections.IEnumerable]) -and -not ($Value -is [string])) {
        $parts = @()
        foreach ($item in $Value) {
            $parts += ConvertTo-PowerShellValueExpression -Value $item
        }

        return '@(' + [string]::Join(', ', $parts) + ')'
    }

    return ConvertTo-SingleQuotedPowerShellLiteral -Value ([string]$Value)
}

function Invoke-AutomationChildScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter()]
        [hashtable]$Parameters = @{},

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $commandParts = New-Object System.Collections.Generic.List[string]
    $commandParts.Add('Set-Location -LiteralPath ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $WorkingDirectory))
    $invocation = '& ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $ScriptPath)
    foreach ($key in @($Parameters.Keys | Sort-Object)) {
        $value = $Parameters[$key]
        if (($value -is [bool]) -and $value) {
            $invocation += ' -' + $key
            continue
        }

        if (($value -is [bool]) -and -not $value) {
            continue
        }

        $invocation += ' -' + $key + ' ' + (ConvertTo-PowerShellValueExpression -Value $value)
    }

    $commandParts.Add($invocation)
    $commandParts.Add('if ($null -eq $LASTEXITCODE) { exit 0 } else { exit $LASTEXITCODE }')
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes([string]::Join('; ', $commandParts)))

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # A lifecycle regression intentionally lets a child fail before it can
        # emit JSON. Capture native stderr as evidence without turning that
        # expected child failure into a terminating error in the parent suite.
        $ErrorActionPreference = 'Continue'
        $output = @(& $WindowsPowerShellPath -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    return [pscustomobject][ordered]@{
        ExitCode = [int]$exitCode
        Output   = [string]::Join([Environment]::NewLine, [string[]]$output)
    }
}

function Invoke-SmokeRunnerLifecycleProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemporaryPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')]
        [string]$Failure,

        [switch]$KeepTemporaryFiles
    )

    $previousTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
    $previousTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
    $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('TEMP', $TemporaryPath, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $TemporaryPath, 'Process')
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
        return Invoke-AutomationChildScript -ScriptPath $PSCommandPath -WorkingDirectory $repository -Parameters @{
            RepositoryRoot          = $repository
            WindowsPowerShellPath   = $WindowsPowerShellPath
            InternalLifecycleFailure = $Failure
            KeepTemporaryFiles      = [bool]$KeepTemporaryFiles
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('TEMP', $previousTemp, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $previousTmp, 'Process')
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
    }
}

function ConvertFrom-LastAutomationJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Output
    )

    $lines = @($Output -split "`r?`n")
    for ($index = $lines.Count - 1; $index -ge 0; $index--) {
        $candidate = $lines[$index].Trim()
        if (-not $candidate.StartsWith('{')) {
            continue
        }

        try {
            return $candidate | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            continue
        }
    }

    throw "No compact JSON object was found in child output: $Output"
}

function Invoke-CheckedNativeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter()]
        [string[]]$ArgumentList = @(),

        [Parameter()]
        [string]$WorkingDirectory
    )

    $result = Invoke-AutomationNativeCommand -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory
    if (-not $result.Succeeded) {
        throw "Native command failed ($($result.ExitCode)): $FilePath $([string]::Join(' ', $ArgumentList))`n$($result.StdErr)"
    }

    return $result
}

function New-TestFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function New-DisposableDriftUnityFixture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Exact', 'NoFinalNewline', 'HeaderLikeContent', 'RelocatedApprovedField', 'ExtraField', 'SecondProjectSettingsFile', 'AssetsFile', 'PackagesFile')]
        [string]$Mode
    )

    $editorDirectory = Join-Path $Root ("$Mode\6000.4.0f1\Editor")
    $fakeUnityPath = Join-Path $editorDirectory 'Unity.cmd'
    $fakeUnityScriptPath = Join-Path $editorDirectory 'Unity.ps1'
    $postLines = @(
        'PlayerSettings:',
        '  targetPixelDensity: 30',
        '  fixtureGapAfterPixel: 0',
        '  buildNumber:',
        '    Standalone: 0',
        '    VisionOS: 0',
        '    iPhone: 0',
        '    tvOS: 0',
        '  fixtureGapAfterBuildNumber: 0',
        '  iOSTargetOSVersionString: 15.0',
        '  fixtureGapAfterIOS: 0',
        '  tvOSTargetOSVersionString: 15.0',
        '  fixtureGapAfterTvOS: 0',
        '  VisionOSTargetOSVersionString: 1.0',
        '  fixtureGapAfterVisionOS: 0',
        '  macOSTargetOSVersion: 12.0'
    )
    if ($Mode -eq 'ExtraField') {
        $postLines += '  scriptingBackend: 1'
    }
    if ($Mode -eq 'HeaderLikeContent') {
        $postLines += '++ b/ProjectSettings/ProjectSettings.asset'
    }
    if ($Mode -eq 'RelocatedApprovedField') {
        $postLines = @(
            'PlayerSettings:',
            '  fixtureGapAfterPixel: 0',
            '  targetPixelDensity: 30'
        ) + @($postLines | Select-Object -Skip 3)
    }
    $postContent = [string]::Join("`n", $postLines)
    if ($Mode -ne 'NoFinalNewline') {
        $postContent += "`n"
    }
    $scriptLines = @(
        '$arguments = @($args)',
        '$projectPath = $null',
        '$logFile = $null',
        '$resultFile = $null',
        'for ($index = 0; $index -lt $arguments.Count; $index++) {',
        '    if ($arguments[$index] -ieq ''-projectPath'') { $projectPath = [string]$arguments[++$index]; continue }',
        '    if ($arguments[$index] -ieq ''-logFile'') { $logFile = [string]$arguments[++$index]; continue }',
        '    if ($arguments[$index] -ieq ''-testResults'') { $resultFile = [string]$arguments[++$index]; continue }',
        '}',
        'if ([string]::IsNullOrWhiteSpace($projectPath) -or [string]::IsNullOrWhiteSpace($logFile)) { exit 64 }',
        ('$mutationContent = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $postContent)),
        '$utf8NoBom = New-Object System.Text.UTF8Encoding($false)',
        '[System.IO.File]::WriteAllText((Join-Path $projectPath ''ProjectSettings\ProjectSettings.asset''), $mutationContent, $utf8NoBom)'
    )
    switch ($Mode) {
        'SecondProjectSettingsFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''ProjectSettings\Unexpected.asset''), ''unexpected'', $utf8NoBom)'
        }
        'AssetsFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''Assets\Unexpected.txt''), ''unexpected'', $utf8NoBom)'
        }
        'PackagesFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''Packages\Unexpected.json''), ''{}'', $utf8NoBom)'
        }
    }
    $scriptLines += @(
        'if ($resultFile) {',
        '    [System.IO.File]::WriteAllText($logFile, "Running tests for ExecutionSettings with details:`r`nTest run completed. Exiting with code 0`r`n", $utf8NoBom)',
        '    [System.IO.File]::WriteAllText($resultFile, ''<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" />'', $utf8NoBom)',
        '}',
        'else {',
        '    [System.IO.File]::WriteAllText($logFile, "Clean compile/import fixture`r`n", $utf8NoBom)',
        '}',
        'exit 0'
    )
    New-TestFile -Path $fakeUnityScriptPath -Content ([string]::Join("`r`n", $scriptLines) + "`r`n")
    $cmdLines = @(
        '@echo off',
        '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Unity.ps1" %*',
        'exit /b %ERRORLEVEL%'
    )
    New-TestFile -Path $fakeUnityPath -Content ([string]::Join("`r`n", $cmdLines) + "`r`n")
    return $fakeUnityPath
}

function Remove-OwnedTestRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$AutomationRoot,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9a-fA-F]{32}$')]
        [string]$ExpectedRunId,

        [Parameter(Mandatory = $true)]
        [bool]$MarkerWasWritten
    )

    Assert-AutomationPathHasNoReparsePoint -Path $AutomationRoot
    Assert-AutomationPathHasNoReparsePoint -Path $Path
    $normalizedAutomationRoot = ConvertTo-AutomationLexicalPath -Path $AutomationRoot
    $normalizedPath = ConvertTo-AutomationLexicalPath -Path $Path
    $expectedPath = Join-Path -Path $normalizedAutomationRoot -ChildPath ('script-tests-' + $ExpectedRunId.ToLowerInvariant())
    $markerPath = Join-Path -Path $normalizedPath -ChildPath '.automation-script-tests-owner'
    if (-not (Test-AutomationPathEqual -Left $normalizedPath -Right $expectedPath) -or
        -not (Test-AutomationPathWithin -Path $normalizedPath -Root $normalizedAutomationRoot) -or
        (Test-AutomationPathEqual -Left $normalizedPath -Right $normalizedAutomationRoot)) {
        throw "Refusing to remove unowned test path: $normalizedPath"
    }

    if (-not (Test-Path -LiteralPath $normalizedPath)) {
        return
    }

    if (-not (Test-Path -LiteralPath $normalizedPath -PathType Container)) {
        throw "Owned test root is not a directory: $normalizedPath"
    }

    Assert-AutomationTreeHasNoReparsePoint -Root $normalizedPath
    if (-not $MarkerWasWritten) {
        $unexpectedEntries = @(Get-ChildItem -LiteralPath $normalizedPath -Force -ErrorAction Stop |
            Where-Object { -not (Test-AutomationPathEqual -Left $_.FullName -Right $markerPath) })
        if ($unexpectedEntries.Count -gt 0) {
            throw "Refusing marker-failure cleanup because the root contains an unexpected entry: $($unexpectedEntries[0].FullName)"
        }
        if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
            (Get-Item -LiteralPath $markerPath -Force).Attributes = [System.IO.FileAttributes]::Normal
            [System.IO.File]::Delete($markerPath)
        }
        [System.IO.Directory]::Delete($normalizedPath, $false)
        return
    }
    else {
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            throw "Owned test marker disappeared before cleanup: $markerPath"
        }
        try {
            $marker = Get-Content -Raw -LiteralPath $markerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Owned test marker is invalid: $($_.Exception.Message)"
        }
        if ([int]$marker.SchemaVersion -ne 1 -or
            -not [string]::Equals([string]$marker.RunId, $ExpectedRunId, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-AutomationPathEqual -Left ([string]$marker.WorkspaceRoot) -Right $normalizedPath) -or
            [System.IO.Path]::GetFileName([string]$marker.Script) -cne 'Test-AutomationScripts.ps1') {
            throw "Owned test marker does not match this run: $markerPath"
        }
    }

    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $directories = New-Object 'System.Collections.Generic.List[string]'
    $pending.Push($normalizedPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $directoryItem = Get-Item -LiteralPath $directory -Force -ErrorAction Stop
        if (-not $directoryItem.PSIsContainer -or
            ($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing cleanup because a queued test directory changed type: $directory"
        }
        $directories.Add($directory)
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to remove test tree containing a reparse point: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                $pending.Push($entry.FullName)
            }
            else {
                $entry.Attributes = [System.IO.FileAttributes]::Normal
                [System.IO.File]::Delete($entry.FullName)
            }
        }
    }
    foreach ($directory in @($directories | Sort-Object { $_.Length } -Descending)) {
        (Get-Item -LiteralPath $directory -Force).Attributes = [System.IO.FileAttributes]::Directory
        [System.IO.Directory]::Delete($directory, $false)
    }
}

function New-AutomationSmokeFixture {
    [CmdletBinding()]
    param()

    $originPath = Join-Path -Path $script:temporaryRoot -ChildPath 'origin repository.git'
    $basePath = Join-Path -Path $script:temporaryRoot -ChildPath 'base checkout'
    $developerPath = Join-Path -Path $script:temporaryRoot -ChildPath 'developer worktree'
    $reviewerPath = Join-Path -Path $script:temporaryRoot -ChildPath 'reviewer worktree'
    $fakeUnityPath = Join-Path -Path $script:temporaryRoot -ChildPath 'Fake Unity\6000.4.0f1\Editor\Unity.cmd'
    $fakeGitHubPath = Join-Path -Path $script:temporaryRoot -ChildPath 'Fake GitHub\gh.cmd'
    $fakeGitHubScriptPath = Join-Path -Path $script:temporaryRoot -ChildPath 'Fake GitHub\gh.ps1'
    $statusSentinelPath = Join-Path -Path $script:temporaryRoot -ChildPath 'project-item-edit-called.txt'

    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('init', '--bare', $originPath) | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('init', '--initial-branch=main', $basePath) | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'config', 'user.name', 'Automation Script Tests') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'config', 'user.email', 'automation-tests@example.invalid') | Out-Null

    New-TestFile -Path (Join-Path $basePath 'AGENTS.md') -Content "# Test Agent Rules`n"
    New-TestFile -Path (Join-Path $basePath 'Docs\Automation\SPEC_VERSION') -Content "9.9.9`n"
    New-TestFile -Path (Join-Path $basePath 'Docs\Automation\DEVELOPER.md') -Content "# Developer`n"
    New-TestFile -Path (Join-Path $basePath 'Docs\Automation\REVIEWER.md') -Content "# Reviewer`n"
    New-TestFile -Path (Join-Path $basePath 'Assets\.fixture') -Content "fixture`n"
    New-TestFile -Path (Join-Path $basePath 'Packages\manifest.json') -Content "{}`n"
    New-TestFile -Path (Join-Path $basePath 'ProjectSettings\ProjectVersion.txt') -Content "m_EditorVersion: 6000.4.0f1`nm_EditorVersionWithRevision: 6000.4.0f1 (fixture)`n"
    $projectSettingsFixture = @(
        'PlayerSettings:',
        '  targetPixelDensity: 0',
        '  fixtureGapAfterPixel: 0',
        '  buildNumber: {}',
        '  fixtureGapAfterBuildNumber: 0',
        '  iOSTargetOSVersionString: ',
        '  fixtureGapAfterIOS: 0',
        '  tvOSTargetOSVersionString: ',
        '  fixtureGapAfterTvOS: 0',
        '  VisionOSTargetOSVersionString: ',
        '  fixtureGapAfterVisionOS: 0',
        '  macOSTargetOSVersion: '
    )
    New-TestFile -Path (Join-Path $basePath 'ProjectSettings\ProjectSettings.asset') -Content ([string]::Join("`n", $projectSettingsFixture) + "`n")
    New-TestFile -Path (Join-Path $basePath 'pilot-main.txt') -Content "main`n"
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'add', '--all') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'commit', '-m', 'test: seed automation fixture') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'remote', 'add', 'origin', $originPath) | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'push', '-u', 'origin', 'main') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'worktree', 'add', '-b', 'developer-test', $developerPath, 'main') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'worktree', 'add', '-b', 'reviewer-test', $reviewerPath, 'main') | Out-Null

    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'switch', '-c', 'pilot-pr') | Out-Null
    New-TestFile -Path (Join-Path $basePath 'pilot-pr.txt') -Content "pull request`n"
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'add', 'pilot-pr.txt') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'commit', '-m', 'test: pilot pull request') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'push', 'origin', 'HEAD:refs/pull/23/head') | Out-Null
    Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $basePath, 'switch', 'main') | Out-Null

    $fakeUnity = @(
        '@echo off',
        'setlocal EnableExtensions EnableDelayedExpansion',
        'set "logFile="',
        'set "resultFile="',
        'set "allArguments=%*"',
        ':parse',
        'if "%~1"=="" goto writeResults',
        'if /I "%~1"=="-logFile" (',
        '  set "logFile=%~2"',
        '  shift',
        '  shift',
        '  goto parse',
        ')',
        'if /I "%~1"=="-testResults" (',
        '  set "resultFile=%~2"',
        '  shift',
        '  shift',
        '  goto parse',
        ')',
        'shift',
        'goto parse',
        ':writeResults',
        'if defined logFile echo Fake Unity invocation: !allArguments!>"!logFile!"',
        'if defined resultFile echo Running tests for ExecutionSettings with details:>>"!logFile!"',
        'if defined resultFile echo LogAssert.Expect matched the expected negative-path error.>>"!logFile!"',
        'if defined resultFile echo UnityEngine.Debug:LogError ^(object^)>>"!logFile!"',
        'if defined resultFile echo Test run completed. Exiting with code 0 >>"!logFile!"',
        'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" /^>>"!resultFile!"',
        'exit /b 0'
    )
    New-TestFile -Path $fakeUnityPath -Content ([string]::Join("`r`n", $fakeUnity) + "`r`n")
    $fieldJson = '{"fields":[{"id":"status-field","name":"Status","type":"ProjectV2SingleSelectField","options":[{"id":"backlog","name":"Backlog"},{"id":"ready","name":"Ready"},{"id":"in-progress","name":"In Progress"},{"id":"review","name":"Review"},{"id":"verification","name":"Verification"},{"id":"done","name":"Done"}]},{"id":"priority-field","name":"Priority","type":"ProjectV2SingleSelectField","options":[{"id":"p0","name":"P0"},{"id":"p1","name":"P1"},{"id":"p2","name":"P2"},{"id":"p3","name":"P3"}]},{"id":"area-field","name":"Area","type":"ProjectV2SingleSelectField","options":[]},{"id":"size-field","name":"Size","type":"ProjectV2SingleSelectField","options":[]}]}'
    $itemJson = '{"items":[{"id":"item-33","status":"Ready","content":{"number":33,"repository":"DongGyunLeeeee/sashimi-boy-unity","url":"https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/33"}}],"totalCount":1}'
    $fakeGitHubPowerShell = @(
        '$commandName = [string]::Join('' '', @($args | Select-Object -First 2))',
        'switch ($commandName) {',
        '    ''auth status'' { exit 0 }',
        '    ''repo view'' { [Console]::Out.WriteLine(''{"nameWithOwner":"DongGyunLeeeee/sashimi-boy-unity"}''); exit 0 }',
        '    ''issue view'' { [Console]::Out.WriteLine(''{"id":"issue-id","number":33,"url":"https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/33"}''); exit 0 }',
        '    ''project view'' { [Console]::Out.WriteLine(''{"id":"project-id","number":1,"title":"SASHIMI BOY Development"}''); exit 0 }',
        '    ''project field-list'' { [Console]::Out.WriteLine(''' + $fieldJson + '''); exit 0 }',
        '    ''project item-list'' { [Console]::Out.WriteLine(''' + $itemJson + '''); exit 0 }',
        '    ''project item-edit'' { [System.IO.File]::WriteAllText(''' + $statusSentinelPath.Replace("'", "''") + ''', ''called''); exit 0 }',
        '    default { [Console]::Error.WriteLine("Unexpected fake gh invocation: $([string]::Join('' '', $args))"); exit 64 }',
        '}'
    )
    New-TestFile -Path $fakeGitHubScriptPath -Content ([string]::Join("`r`n", $fakeGitHubPowerShell) + "`r`n")
    $fakeGitHub = @(
        '@echo off',
        '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0gh.ps1" %*',
        'exit /b %ERRORLEVEL%'
    )
    New-TestFile -Path $fakeGitHubPath -Content ([string]::Join("`r`n", $fakeGitHub) + "`r`n")

    return [pscustomobject][ordered]@{
        OriginPath         = $originPath
        BasePath           = $basePath
        DeveloperPath      = $developerPath
        ReviewerPath       = $reviewerPath
        FakeUnityPath      = $fakeUnityPath
        FakeGitHubPath     = $fakeGitHubPath
        StatusSentinelPath = $statusSentinelPath
    }
}

$repository = ConvertTo-AutomationPath -Path $RepositoryRoot
$automationTempRoot = $null
$script:testRunId = [Guid]::NewGuid().ToString('N')

try {
    $automationTempRootInput = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation'
    # This must precede the first write. It inspects the lexical path and every
    # existing ancestor without resolving a junction to its target.
    Assert-AutomationPathHasNoReparsePoint -Path $automationTempRootInput
    $automationTempRoot = ConvertTo-AutomationLexicalPath -Path $automationTempRootInput
    $testRootCandidate = Join-Path -Path $automationTempRoot -ChildPath ('script-tests-' + $script:testRunId)
    Assert-AutomationPathHasNoReparsePoint -Path $testRootCandidate
    if (-not (Test-Path -LiteralPath $automationTempRoot)) {
        New-Item -ItemType Directory -Path $automationTempRoot -ErrorAction Stop | Out-Null
    }
    Assert-AutomationPathHasNoReparsePoint -Path $automationTempRoot

    if (Test-Path -LiteralPath $testRootCandidate) {
        throw "Refusing to reuse a pre-existing test root: $testRootCandidate"
    }
    Assert-AutomationPathHasNoReparsePoint -Path $testRootCandidate
    New-Item -ItemType Directory -Path $testRootCandidate -ErrorAction Stop | Out-Null
    $script:temporaryRoot = ConvertTo-AutomationLexicalPath -Path $testRootCandidate
    $script:ownedTestRootCreated = $true
    Assert-AutomationPathHasNoReparsePoint -Path $script:temporaryRoot

    if ($InternalLifecycleFailure -eq 'MarkerWrite') {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:temporaryRoot '.automation-script-tests-owner'),
            '{"partial":',
            (New-Object System.Text.UTF8Encoding($false)))
        throw 'Injected ownership-marker write failure.'
    }
    $markerData = [ordered]@{
        SchemaVersion = 1
        RunId          = $script:testRunId
        WorkspaceRoot  = $script:temporaryRoot
        Script         = $PSCommandPath
    }
    $testMarkerPath = Join-Path $script:temporaryRoot '.automation-script-tests-owner'
    New-TestFile -Path $testMarkerPath -Content ((ConvertTo-AutomationJson -InputObject $markerData) + "`n")
    $writtenMarker = Get-Content -Raw -LiteralPath $testMarkerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ([int]$writtenMarker.SchemaVersion -ne 1 -or
        -not [string]::Equals([string]$writtenMarker.RunId, $script:testRunId, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-AutomationPathEqual -Left ([string]$writtenMarker.WorkspaceRoot) -Right $script:temporaryRoot) -or
        [System.IO.Path]::GetFileName([string]$writtenMarker.Script) -cne 'Test-AutomationScripts.ps1') {
        throw 'Ownership marker readback did not match this smoke run.'
    }
    $script:ownerMarkerWritten = $true

    if ($InternalLifecycleFailure -eq 'ChildExitOne') {
        & $WindowsPowerShellPath -NoProfile -NonInteractive -Command 'exit 1'
        $injectedChildExitCode = $LASTEXITCODE
        if ($injectedChildExitCode -ne 1) {
            throw "Injected child returned unexpected exit code $injectedChildExitCode."
        }
        throw 'Injected child process exit 1.'
    }
    if ($InternalLifecycleFailure -eq 'Assertion') {
        Assert-AutomationTest -Condition $false -Message 'Injected assertion failure.'
    }

    if ($InternalLifecycleFailure -eq 'RecordedAssertion') {
        Invoke-AutomationTestCase -Name 'InjectedRecordedAssertion' -Body {
            Assert-AutomationTest -Condition $false -Message 'Injected recorded assertion failure.'
        }
    }
    else {
    Invoke-AutomationTestCase -Name 'RequiredFilesExist' -Body {
        $requiredFiles = @(
            'AGENTS.md',
            'Docs\Automation\SPEC_VERSION',
            'Docs\Automation\WORKFLOW.md',
            'Docs\Automation\DEVELOPER.md',
            'Docs\Automation\REVIEWER.md',
            'Docs\Automation\BOOTSTRAP.md',
            'Tools\Automation\Automation.Common.ps1',
            'Tools\Automation\Invoke-AutomationPreflight.ps1',
            'Tools\Automation\New-ReviewIntegration.ps1',
            'Tools\Automation\Remove-ReviewIntegration.ps1',
            'Tools\Automation\Invoke-UnityTests.ps1',
            'Tools\Automation\Set-GitHubProjectStatus.ps1',
            'Tools\Automation\Tests\Test-AutomationScripts.ps1'
        )

        foreach ($relativePath in $requiredFiles) {
            $path = Join-Path -Path $repository -ChildPath $relativePath
            Assert-AutomationTest -Condition (Test-Path -LiteralPath $path -PathType Leaf) -Message "Required file is missing: $relativePath"
        }
    }

    Invoke-AutomationTestCase -Name 'PowerShellParserAcceptsEveryScript' -Body {
        $scripts = @(Get-ChildItem -LiteralPath (Join-Path $repository 'Tools\Automation') -Filter '*.ps1' -File -Recurse)
        Assert-AutomationTest -Condition ($scripts.Count -ge 7) -Message 'Expected at least seven PowerShell scripts.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $WindowsPowerShellPath -PathType Leaf) -Message "Windows PowerShell 5.1 was not found: $WindowsPowerShellPath"
        $parserProbePath = Join-Path -Path $script:temporaryRoot -ChildPath 'Parse-OneScript.ps1'
        $parserProbe = @'
param([Parameter(Mandatory = $true)][string]$ScriptPath)
$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    [Console]::Error.WriteLine([string]::Join('; ', [string[]]$parseErrors))
    exit 1
}
exit 0
'@
        New-TestFile -Path $parserProbePath -Content $parserProbe
        foreach ($script in $scripts) {
            $tokens = $null
            $parseErrors = $null
            [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
            Assert-AutomationTest -Condition ($parseErrors.Count -eq 0) -Message "Parser errors in $($script.FullName): $([string]::Join('; ', [string[]]$parseErrors))"

            $desktopParse = Invoke-AutomationChildScript -ScriptPath $parserProbePath -Parameters @{
                ScriptPath = $script.FullName
            } -WorkingDirectory $repository
            Assert-AutomationTest -Condition ($desktopParse.ExitCode -eq 0) -Message "Windows PowerShell 5.1 parser rejected $($script.FullName): $($desktopParse.Output)"
        }
    }

    Invoke-AutomationTestCase -Name 'SmokeRunnerRejectsJunctionBeforeFirstWrite' -Body {
        $probeTemp = Join-Path $script:temporaryRoot 'runner junction probe temp'
        $junctionTarget = Join-Path $script:temporaryRoot 'runner junction probe target'
        $junctionRoot = Join-Path $probeTemp 'SashimiBoyAutomation'
        New-Item -ItemType Directory -Path $probeTemp -ErrorAction Stop | Out-Null
        New-Item -ItemType Directory -Path $junctionTarget -ErrorAction Stop | Out-Null
        $sentinel = Join-Path $junctionTarget 'target-sentinel.txt'
        New-TestFile -Path $sentinel -Content "must survive`n"
        New-Item -ItemType Junction -Path $junctionRoot -Target $junctionTarget -ErrorAction Stop | Out-Null
        try {
            $probe = Invoke-SmokeRunnerLifecycleProbe -TemporaryPath $probeTemp -Failure Assertion
            Assert-AutomationTest -Condition ($probe.ExitCode -ne 0) -Message 'Smoke runner accepted a junction canonical temp root.'
            Assert-AutomationTest -Condition ($probe.Output -match 'Reparse points are not allowed') -Message "Smoke runner junction rejection was not explicit: $($probe.Output)"
            $targetEntries = @(Get-ChildItem -LiteralPath $junctionTarget -Force -ErrorAction Stop)
            Assert-AutomationTest -Condition ($targetEntries.Count -eq 1 -and (Test-AutomationPathEqual -Left $targetEntries[0].FullName -Right $sentinel)) -Message 'Smoke runner wrote fixture data through the junction before validation.'
        }
        finally {
            if (Test-Path -LiteralPath $junctionRoot) {
                [System.IO.Directory]::Delete($junctionRoot, $false)
            }
        }
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinel -PathType Leaf) -Message 'Junction rejection modified the external target sentinel.'
    }

    Invoke-AutomationTestCase -Name 'SmokeRunnerCleansEveryInjectedFailure' -Body {
        foreach ($failure in @('MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')) {
            foreach ($keepRequested in @($false, $true)) {
                $probeTemp = Join-Path $script:temporaryRoot ('runner lifecycle ' + $failure + ' keep-' + $keepRequested)
                New-Item -ItemType Directory -Path $probeTemp -ErrorAction Stop | Out-Null
                $probe = Invoke-SmokeRunnerLifecycleProbe `
                    -TemporaryPath $probeTemp `
                    -Failure $failure `
                    -KeepTemporaryFiles:$keepRequested
                Assert-AutomationTest -Condition ($probe.ExitCode -ne 0) -Message "Injected runner failure unexpectedly succeeded: $failure (KeepTemporaryFiles=$keepRequested)"
                $automationRoot = Join-Path $probeTemp 'SashimiBoyAutomation'
                $ownedRootRemainders = @(
                    if (Test-Path -LiteralPath $automationRoot -PathType Container) {
                        Get-ChildItem -LiteralPath $automationRoot -Directory -Filter 'script-tests-*' -Force -ErrorAction Stop
                    }
                )
                Assert-AutomationTest -Condition ($ownedRootRemainders.Count -eq 0) -Message "Injected $failure left an owned test root behind when KeepTemporaryFiles=$keepRequested."
            }
        }
    }

    Invoke-AutomationTestCase -Name 'SmokeRunnerNeverDeletesUnownedPath' -Body {
        $localAutomationRoot = Join-Path $script:temporaryRoot 'nonowned cleanup boundary'
        $actualRunId = [Guid]::NewGuid().ToString('N')
        $differentRunId = [Guid]::NewGuid().ToString('N')
        $nonOwnedPath = Join-Path $localAutomationRoot ('script-tests-' + $actualRunId)
        $sentinel = Join-Path $nonOwnedPath 'nonowned-sentinel.txt'
        New-TestFile -Path $sentinel -Content "must survive refusal`n"
        $rejected = $false
        try {
            Remove-OwnedTestRoot `
                -Path $nonOwnedPath `
                -AutomationRoot $localAutomationRoot `
                -ExpectedRunId $differentRunId `
                -MarkerWasWritten $false
        }
        catch {
            $rejected = $_.Exception.Message -match 'unowned test path'
        }
        Assert-AutomationTest -Condition $rejected -Message 'Smoke runner cleanup accepted an unrecorded path.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinel -PathType Leaf) -Message 'Smoke runner cleanup deleted an unowned path.'
    }

    Invoke-AutomationTestCase -Name 'SpecVersionHasOneSource' -Body {
        $versionPath = Join-Path $repository 'Docs\Automation\SPEC_VERSION'
        $versionLines = @([System.IO.File]::ReadAllLines($versionPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        Assert-AutomationTest -Condition ($versionLines.Count -eq 1) -Message 'SPEC_VERSION must contain one non-empty line.'
        $version = $versionLines[0].Trim()
        Assert-AutomationTest -Condition ($version -match '^\d+\.\d+\.\d+$') -Message "SPEC_VERSION is not semantic: $version"
        $candidateFiles = @(Get-ChildItem -LiteralPath (Join-Path $repository 'Docs\Automation') -File -Recurse) +
            @(Get-ChildItem -LiteralPath (Join-Path $repository 'Tools\Automation') -File -Recurse)
        foreach ($candidate in $candidateFiles) {
            if (Test-AutomationPathEqual -Left $candidate.FullName -Right $versionPath) {
                continue
            }

            $content = [System.IO.File]::ReadAllText($candidate.FullName)
            Assert-AutomationTest -Condition (-not $content.Contains($version)) -Message "SPEC_VERSION literal is duplicated in $($candidate.FullName)."
        }
    }

    Invoke-AutomationTestCase -Name 'ProductionScriptsExcludeForbiddenCommands' -Body {
        $productionScripts = @(Get-ChildItem -LiteralPath (Join-Path $repository 'Tools\Automation') -Filter '*.ps1' -File)
        $forbiddenPatterns = [ordered]@{
            HardReset          = '(?:\bgit\s+reset\s+--hard|[\x27\x22]reset[\x27\x22]\s*,\s*[\x27\x22]--hard[\x27\x22])'
            GitClean          = '(?:\bgit\s+clean\s+-|[\x27\x22]clean[\x27\x22]\s*,\s*[\x27\x22]-[^\x27\x22]*[dfx])'
            ReadTreeUpdate    = '(?:\bgit\s+read-tree\s+-u|[\x27\x22]read-tree[\x27\x22]\s*,\s*[\x27\x22]-u[\x27\x22])'
            GitPush           = '(?m)[\x27\x22]push[\x27\x22]'
            ForcePush         = '--force(?:-with-lease)?'
            RemoteBranchDelete = 'push[^\r\n]*--delete'
            PullRequestMerge  = 'pr\s+merge'
        }

        foreach ($script in $productionScripts) {
            $content = [System.IO.File]::ReadAllText($script.FullName)
            foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
                Assert-AutomationTest -Condition (-not [regex]::IsMatch($content, $entry.Value, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) -Message "$($entry.Key) found in $($script.Name)."
            }

            if ($script.Name -ne 'Automation.Common.ps1') {
                Assert-AutomationTest -Condition ($content -notmatch '(?is)Remove-Item[^\r\n]*-Recurse') -Message "Recursive Remove-Item exists outside the vetted common cleanup helper: $($script.Name)."
                Assert-AutomationTest -Condition ($content -notmatch '(?is)Directory\]::Delete\s*\([^\)]*,\s*\$?true\s*\)') -Message "Recursive Directory.Delete exists outside the vetted common cleanup helper: $($script.Name)."
            }
        }

        $integrationContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'))
        Assert-AutomationTest -Condition ($integrationContent -match 'SashimiBoyAutomation') -Message 'Integration cleanup is not rooted under SashimiBoyAutomation.'
        Assert-AutomationTest -Condition ($integrationContent -match '(?i)marker|owner') -Message 'Integration cleanup has no ownership-marker guard.'
        $preflightContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Invoke-AutomationPreflight.ps1'))
        Assert-AutomationTest -Condition ($preflightContent -notmatch 'SkipUnityProcessCheck') -Message 'Production preflight still exposes SkipUnityProcessCheck.'
        $unityContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'))
        Assert-AutomationTest -Condition ($unityContent -notmatch 'AllowSkipped') -Message 'Unity wrapper still exposes an AllowSkipped bypass.'
        Assert-AutomationTest -Condition ($unityContent -notmatch '\$ProtectedProjectPath\b') -Message 'Unity wrapper still exposes a public protected-worktree override.'
        Assert-AutomationTest -Condition ($unityContent -match "ValidatedUnityVersion, '6000\.4\.0f1'") -Message 'Known drift is not bound to literal Unity 6000.4.0f1.'
        Assert-AutomationTest -Condition ($unityContent -match '\$protectedCheckoutPaths' -and $unityContent -match '\$persistentEvidencePaths') -Message 'Immutable overlap protection and test-injectable evidence paths are not separated.'
        Assert-AutomationTest -Condition (@([regex]::Matches($unityContent, "'-buildTarget', 'StandaloneWindows64'")).Count -eq 3) -Message 'Unity wrapper must use StandaloneWindows64 for compile, EditMode, and PlayMode.'
        $commonContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Automation.Common.ps1'))
        Assert-AutomationTest -Condition (@([regex]::Matches($commonContent, '(?is)Remove-Item[^\r\n]*-Recurse')).Count -eq 1) -Message 'The vetted common helper must contain the sole production recursive delete.'
        Assert-AutomationTest -Condition ($commonContent -match 'Assert-AutomationTreeHasNoReparsePoint') -Message 'Recursive cleanup lacks a full-tree reparse-point guard.'
    }

    Invoke-AutomationTestCase -Name 'SourceOfTruthHierarchyAndStaleCommentPolicyAreExact' -Body {
        $documents = [ordered]@{
            'AGENTS.md'                      = 1
            'Docs\Automation\WORKFLOW.md'  = 1
            'Docs\Automation\DEVELOPER.md' = 1
            'Docs\Automation\REVIEWER.md'  = 1
            'Docs\Automation\BOOTSTRAP.md' = 2
        }
        $rankPatterns = @(
            '^\s*1\.\s+the current Issue''s latest Owner Decision\s*$',
            '^\s*2\.\s+the current Issue''s latest body and Acceptance Criteria\s*$',
            '^\s*3\.\s+repository-wide safety rules',
            '^\s*4\.\s+the role-specific',
            '^\s*5\.\s+the latest independent Review finding on the linked PR\s*$',
            '^\s*6\.\s+older Issue/PR comments and older Reviews\s*$',
            '^\s*7\.\s+previous chat summaries and Automation memory\s*$'
        )
        $nonRelaxablePatterns = @(
            "destroy the user's checkout",
            'reset --hard.*git clean.*force push',
            'never merge a PR.*move an Issue to.*Done',
            '(?:exactly )?one Issue per run|One run handles exactly one Issue',
            'never report an unexecuted test as PASS'
        )

        foreach ($documentEntry in $documents.GetEnumerator()) {
            $relativePath = [string]$documentEntry.Key
            $expectedBlockCount = [int]$documentEntry.Value
            $content = [System.IO.File]::ReadAllText((Join-Path $repository $relativePath))
            $lines = @($content -split "`r?`n")
            $blockStarts = @()
            for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
                if ($lines[$lineIndex] -match $rankPatterns[0]) {
                    $blockStarts += [int]$lineIndex
                }
            }
            Assert-AutomationTest -Condition ($blockStarts.Count -eq $expectedBlockCount) -Message "$relativePath must contain exactly $expectedBlockCount Source of Truth block(s)."

            for ($blockIndex = 0; $blockIndex -lt $blockStarts.Count; $blockIndex++) {
                $start = $blockStarts[$blockIndex]
                $end = if ($blockIndex + 1 -lt $blockStarts.Count) { $blockStarts[$blockIndex + 1] - 1 } else { $lines.Count - 1 }
                $cursor = $start - 1
                foreach ($rankPattern in $rankPatterns) {
                    $rankMatches = @()
                    for ($candidateIndex = $start; $candidateIndex -le $end; $candidateIndex++) {
                        if ($lines[$candidateIndex] -match $rankPattern) {
                            $rankMatches += [int]$candidateIndex
                        }
                    }
                    Assert-AutomationTest -Condition ($rankMatches.Count -eq 1) -Message "$relativePath Source of Truth block has a missing or duplicate rank: $rankPattern"
                    Assert-AutomationTest -Condition ($rankMatches[0] -gt $cursor) -Message "$relativePath Source of Truth ranks are out of order."
                    $cursor = $rankMatches[0]
                }

                $segment = [string]::Join(' ', [string[]]$lines[$start..$end]) -replace '\s+', ' '
                Assert-AutomationTest -Condition ($segment -match 'Only the latest Owner Decision and (?:the )?current Acceptance Criteria are authoritative comments') -Message "$relativePath does not limit authoritative comments to the latest Owner Decision/current Acceptance Criteria."
                Assert-AutomationTest -Condition ($segment -match 'Older or non-Owner general comments.*(?:yield|conflict)') -Message "$relativePath does not make stale/non-Owner comments yield on conflict."
                foreach ($prohibitionPattern in $nonRelaxablePatterns) {
                    Assert-AutomationTest -Condition ($segment -match $prohibitionPattern) -Message "$relativePath Source of Truth entry point can relax a required safety prohibition: $prohibitionPattern"
                }
            }
        }
    }

    Invoke-AutomationTestCase -Name 'DocumentationStateMachineIsConsistent' -Body {
        $workflow = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\WORKFLOW.md'))
        $agents = [System.IO.File]::ReadAllText((Join-Path $repository 'AGENTS.md')) -replace '--human', '--Owner'
        $developer = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\DEVELOPER.md'))
        $reviewer = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\REVIEWER.md'))
        $bootstrap = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\BOOTSTRAP.md'))
        $statusScript = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Set-GitHubProjectStatus.ps1'))
        $transitions = @(
            'Backlog --Owner--> Ready',
            'Ready --Developer--> In Progress',
            'In Progress --Developer--> Review',
            'Review --Reviewer, Blocker/Major--> In Progress',
            'Review --Reviewer, automated PASS--> Verification',
            'Verification --Owner FAIL--> In Progress',
            'Verification --Owner PASS + Merge--> Done'
        )

        foreach ($transition in $transitions) {
            Assert-AutomationTest -Condition ($workflow.IndexOf($transition, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -Message "WORKFLOW is missing transition: $transition"
            Assert-AutomationTest -Condition ($agents.IndexOf($transition, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -Message "AGENTS is missing transition: $transition"
        }
        foreach ($transition in @('Ready -> In Progress', 'In Progress -> Review')) {
            Assert-AutomationTest -Condition ($developer.Contains($transition)) -Message "DEVELOPER is missing allowed transition: $transition"
        }
        foreach ($transition in @('Review -> In Progress', 'Review -> Verification')) {
            Assert-AutomationTest -Condition ($reviewer.Contains($transition)) -Message "REVIEWER is missing allowed transition: $transition"
        }
        Assert-AutomationTest -Condition ($developer -match 'must not merge.*Verification.*Done') -Message 'DEVELOPER does not forbid merge/Verification/Done.'
        Assert-AutomationTest -Condition ($reviewer -match '(?s)must not.*merge.*Done') -Message 'REVIEWER does not forbid merge/Done.'
        Assert-AutomationTest -Condition ($bootstrap -match '(?s)Developer bootstrap.*Never\s+merge a PR or move an Issue to Verification or Done') -Message 'Developer bootstrap exceeds its state authority.'
        Assert-AutomationTest -Condition ($bootstrap -match '(?s)Reviewer bootstrap.*Never push an\s+integration result.*move an Issue to Done') -Message 'Reviewer bootstrap exceeds its state authority.'
        Assert-AutomationTest -Condition ($statusScript -match "'Ready'\s*=\s*@\('In Progress'\)") -Message 'Status script Developer Ready transition differs from the canonical machine.'
        Assert-AutomationTest -Condition ($statusScript -match "'In Progress'\s*=\s*@\('Review'\)") -Message 'Status script Developer Review transition differs from the canonical machine.'
        Assert-AutomationTest -Condition ($statusScript -match "'Review'\s*=\s*@\('In Progress', 'Verification'\)") -Message 'Status script Reviewer transitions differ from the canonical machine.'
    }

    Invoke-AutomationTestCase -Name 'SmokeFixtureCanBeCreated' -Body {
        $script:fixture = New-AutomationSmokeFixture
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $script:fixture.DeveloperPath -PathType Container) -Message 'Developer linked worktree was not created.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $script:fixture.ReviewerPath -PathType Container) -Message 'Reviewer linked worktree was not created.'
    }

    Invoke-AutomationTestCase -Name 'PreflightSuccessPath' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $preflightPath = Join-Path $repository 'Tools\Automation\Invoke-AutomationPreflight.ps1'
        $preflight = Invoke-AutomationChildScript -ScriptPath $preflightPath -WorkingDirectory $script:fixture.DeveloperPath -Parameters @{
            Role                    = 'Developer'
            WorktreePath            = $script:fixture.DeveloperPath
            BaseCheckoutPath        = $script:fixture.BasePath
            DeveloperWorktreePath   = $script:fixture.DeveloperPath
            ReviewerWorktreePath    = $script:fixture.ReviewerPath
            Repository              = 'DongGyunLeeeee/sashimi-boy-unity'
            ProjectOwner            = 'DongGyunLeeeee'
            ProjectNumber           = 1
            UnityExecutable         = $script:fixture.FakeUnityPath
            ExpectedUnityVersion    = '6000.4.0f1'
            MinimumTempFreeBytes    = 1
            GitHubCliPath           = $script:fixture.FakeGitHubPath
        }
        Assert-AutomationTest -Condition ($preflight.ExitCode -eq 0) -Message "Preflight success path failed: $($preflight.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $preflight.Output
        Assert-AutomationTest -Condition ([bool]$json.succeeded) -Message 'Preflight success JSON reported failure.'
        Assert-AutomationTest -Condition ([string]$json.specVersion -eq '9.9.9') -Message 'Preflight did not read SPEC_VERSION from the inspected worktree.'
        Assert-AutomationTest -Condition (@($json.checks | Where-Object { $_.name -eq 'GitFetch' -and $_.passed -and -not $_.skipped }).Count -eq 1) -Message 'Preflight did not execute the fetch success path.'
    }

    Invoke-AutomationTestCase -Name 'PreflightFailurePathDoesNotMutateRemoteState' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $dirtyPath = Join-Path $script:fixture.DeveloperPath 'untracked preflight failure.txt'
        New-TestFile -Path $dirtyPath -Content "dirty`n"
        try {
            $preflightPath = Join-Path $repository 'Tools\Automation\Invoke-AutomationPreflight.ps1'
            $preflight = Invoke-AutomationChildScript -ScriptPath $preflightPath -WorkingDirectory $script:fixture.DeveloperPath -Parameters @{
                Role                    = 'Developer'
                WorktreePath            = $script:fixture.DeveloperPath
                BaseCheckoutPath        = $script:fixture.BasePath
                DeveloperWorktreePath   = $script:fixture.DeveloperPath
                ReviewerWorktreePath    = $script:fixture.ReviewerPath
                UnityExecutable         = $script:fixture.FakeUnityPath
                ExpectedUnityVersion    = '6000.4.0f1'
                MinimumTempFreeBytes    = 1
                GitHubCliPath           = $script:fixture.FakeGitHubPath
            }
            Assert-AutomationTest -Condition ($preflight.ExitCode -ne 0) -Message 'Dirty-worktree preflight unexpectedly succeeded.'
            $json = ConvertFrom-LastAutomationJson -Output $preflight.Output
            Assert-AutomationTest -Condition (-not [bool]$json.succeeded) -Message 'Dirty-worktree preflight JSON unexpectedly reported success.'
            Assert-AutomationTest -Condition (@($json.checks | Where-Object { $_.name -eq 'GitFetch' }).Count -eq 0) -Message 'Preflight fetched after an earlier clean-tree failure.'
        }
        finally {
            if (Test-Path -LiteralPath $dirtyPath -PathType Leaf) {
                [System.IO.File]::Delete($dirtyPath)
            }
        }
    }

    Invoke-AutomationTestCase -Name 'PreflightRejectsMismatchedLocalRepositoryIdentity' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $wrongGitHubDirectory = Join-Path $script:temporaryRoot 'Wrong Repository GitHub'
        $wrongGitHubPath = Join-Path $wrongGitHubDirectory 'gh.cmd'
        $wrongGitHubScriptPath = Join-Path $wrongGitHubDirectory 'gh.ps1'
        $sourceGitHubDirectory = Split-Path -Parent $script:fixture.FakeGitHubPath
        $sourceGitHubScript = [System.IO.File]::ReadAllText((Join-Path $sourceGitHubDirectory 'gh.ps1'))
        $wrongGitHubScript = $sourceGitHubScript.Replace('DongGyunLeeeee/sashimi-boy-unity', 'DifferentOwner/different-repo')
        New-TestFile -Path $wrongGitHubScriptPath -Content $wrongGitHubScript
        New-TestFile -Path $wrongGitHubPath -Content ([System.IO.File]::ReadAllText($script:fixture.FakeGitHubPath))

        $preflightPath = Join-Path $repository 'Tools\Automation\Invoke-AutomationPreflight.ps1'
        $preflight = Invoke-AutomationChildScript -ScriptPath $preflightPath -WorkingDirectory $script:fixture.DeveloperPath -Parameters @{
            Role                    = 'Developer'
            WorktreePath            = $script:fixture.DeveloperPath
            BaseCheckoutPath        = $script:fixture.BasePath
            DeveloperWorktreePath   = $script:fixture.DeveloperPath
            ReviewerWorktreePath    = $script:fixture.ReviewerPath
            Repository              = 'DongGyunLeeeee/sashimi-boy-unity'
            UnityExecutable         = $script:fixture.FakeUnityPath
            ExpectedUnityVersion    = '6000.4.0f1'
            MinimumTempFreeBytes    = 1
            GitHubCliPath           = $wrongGitHubPath
            DryRun                   = $true
        }
        Assert-AutomationTest -Condition ($preflight.ExitCode -ne 0) -Message 'Preflight accepted a mismatched local repository identity.'
        $json = ConvertFrom-LastAutomationJson -Output $preflight.Output
        Assert-AutomationTest -Condition (-not [bool]$json.succeeded) -Message 'Repository mismatch JSON unexpectedly reported success.'
        Assert-AutomationTest -Condition ([string]::Join(' ', [string[]]$json.errors) -match 'repository mismatch') -Message 'Repository mismatch was not reported explicitly.'
    }

    Invoke-AutomationTestCase -Name 'ProjectStatusWhatIfDoesNotEdit' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $statusScript = Join-Path $repository 'Tools\Automation\Set-GitHubProjectStatus.ps1'
        $status = Invoke-AutomationChildScript -ScriptPath $statusScript -WorkingDirectory $repository -Parameters @{
            IssueNumber     = 33
            Role            = 'Developer'
            ToStatus        = 'In Progress'
            Repository      = 'DongGyunLeeeee/sashimi-boy-unity'
            ProjectOwner    = 'DongGyunLeeeee'
            ProjectNumber   = 1
            GitHubCliPath   = $script:fixture.FakeGitHubPath
            WhatIf          = $true
        }
        Assert-AutomationTest -Condition ($status.ExitCode -eq 0) -Message "Status -WhatIf failed: $($status.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $status.Output
        Assert-AutomationTest -Condition ([bool]$json.succeeded -and [bool]$json.whatIf -and [bool]$json.wouldChange) -Message 'Status -WhatIf JSON contract is incorrect.'
        Assert-AutomationTest -Condition (-not [bool]$json.changed) -Message 'Status -WhatIf claimed an applied change.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $script:fixture.StatusSentinelPath)) -Message 'Status -WhatIf invoked project item-edit.'
    }

    Invoke-AutomationTestCase -Name 'ProjectStatusRejectsForbiddenRoleTransition' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $statusScript = Join-Path $repository 'Tools\Automation\Set-GitHubProjectStatus.ps1'
        $status = Invoke-AutomationChildScript -ScriptPath $statusScript -WorkingDirectory $repository -Parameters @{
            IssueNumber   = 33
            Role          = 'Reviewer'
            ToStatus      = 'Verification'
            Repository    = 'DongGyunLeeeee/sashimi-boy-unity'
            ProjectOwner  = 'DongGyunLeeeee'
            ProjectNumber = 1
            GitHubCliPath = $script:fixture.FakeGitHubPath
        }
        Assert-AutomationTest -Condition ($status.ExitCode -ne 0) -Message 'Reviewer was allowed to transition Ready -> Verification.'
        $json = ConvertFrom-LastAutomationJson -Output $status.Output
        Assert-AutomationTest -Condition (-not [bool]$json.succeeded) -Message 'Forbidden transition JSON unexpectedly reported success.'
        Assert-AutomationTest -Condition ([string]::Join(' ', [string[]]$json.errors) -match 'not allowed') -Message 'Forbidden transition did not report an explicit authorization failure.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $script:fixture.StatusSentinelPath)) -Message 'Forbidden transition invoked project item-edit.'
    }

    Invoke-AutomationTestCase -Name 'SyntheticMergeCreatesAndCleansOwnedClone' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $integrationTemp = Join-Path $script:temporaryRoot 'integration runs with spaces'
        $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
            PullRequestNumber = 23
            RepositoryUrl     = $script:fixture.OriginPath
            TempRoot           = $integrationTemp
        }
        Assert-AutomationTest -Condition ($integration.ExitCode -eq 0) -Message "Synthetic merge smoke failed: $($integration.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $integration.Output
        Assert-AutomationTest -Condition ([bool]$json.Success) -Message 'Synthetic merge JSON reported failure.'
        Assert-AutomationTest -Condition ([int]$json.MergeExitCode -eq 0) -Message 'Synthetic merge did not preserve its zero native exit code.'
        Assert-AutomationTest -Condition ([bool]$json.WorkspaceCleaned) -Message 'Synthetic merge did not report cleanup.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath ([string]$json.WorkspaceRoot))) -Message 'Synthetic merge workspace still exists after default cleanup.'
        Assert-AutomationTest -Condition ([string]$json.PullRequestHead -match '^[0-9a-f]{40}$' -and [string]$json.MainHead -match '^[0-9a-f]{40}$' -and [string]$json.MergeHead -match '^[0-9a-f]{40}$') -Message 'Synthetic merge did not report exact SHAs.'
    }

    Invoke-AutomationTestCase -Name 'SyntheticMergeNeverAdoptsPreExistingWorkspace' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $integrationTemp = Join-Path $script:temporaryRoot 'exclusive integration collision'
        $fixedLeaf = 'ReviewIntegration-20000101T000000Z-0123456789abcdef0123456789abcdef'
        $preExistingWorkspace = Join-Path $integrationTemp $fixedLeaf
        $sentinelPath = Join-Path $preExistingWorkspace 'non-owner-sentinel.txt'
        New-TestFile -Path $sentinelPath -Content "must survive`n"
        $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber     = 23
                RepositoryUrl         = $script:fixture.OriginPath
                TempRoot              = $integrationTemp
                InternalWorkspaceLeaf = $fixedLeaf
            }
        }
        finally {
            [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
        }

        Assert-AutomationTest -Condition ($integration.ExitCode -ne 0) -Message 'Synthetic merge adopted a pre-existing unowned workspace.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinelPath -PathType Leaf) -Message 'Synthetic merge deleted or overwrote a pre-existing sentinel.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath (Join-Path $preExistingWorkspace '.sashimi-boy-automation-owned.json'))) -Message 'Synthetic merge wrote an ownership marker into a pre-existing workspace.'
        $json = ConvertFrom-LastAutomationJson -Output $integration.Output
        Assert-AutomationTest -Condition (-not [bool]$json.Success -and -not [bool]$json.WorkspaceCleaned) -Message 'Pre-existing workspace collision was not reported as a non-cleanup failure.'
    }

    Invoke-AutomationTestCase -Name 'PreservedIntegrationHasExplicitSafeCleanup' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $cleanupScript = Join-Path $repository 'Tools\Automation\Remove-ReviewIntegration.ps1'
        $integrationTemp = Join-Path $script:temporaryRoot 'preserved integration runs'
        $neighborPath = Join-Path $integrationTemp 'neighbor-must-survive.txt'
        New-TestFile -Path $neighborPath -Content "neighbor`n"
        $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
            PullRequestNumber = 23
            RepositoryUrl     = $script:fixture.OriginPath
            TempRoot          = $integrationTemp
            KeepWorkspace     = $true
        }
        Assert-AutomationTest -Condition ($integration.ExitCode -eq 0) -Message "Preserved synthetic merge failed: $($integration.Output)"
        $integrationJson = ConvertFrom-LastAutomationJson -Output $integration.Output
        $workspace = [string]$integrationJson.WorkspaceRoot
        Assert-AutomationTest -Condition ([bool]$integrationJson.WorkspaceKept -and (Test-Path -LiteralPath $workspace -PathType Container)) -Message 'KeepWorkspace did not preserve its marked integration.'

        $whatIfCleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
            WorkspaceRoot = $workspace
            TempRoot      = $integrationTemp
            WhatIf        = $true
        }
        Assert-AutomationTest -Condition ($whatIfCleanup.ExitCode -eq 0) -Message "Cleanup -WhatIf failed validation: $($whatIfCleanup.Output)"
        $whatIfJson = ConvertFrom-LastAutomationJson -Output $whatIfCleanup.Output
        Assert-AutomationTest -Condition ([bool]$whatIfJson.Success -and [bool]$whatIfJson.WouldRemove -and -not [bool]$whatIfJson.Removed) -Message 'Cleanup -WhatIf JSON contract is incorrect.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $workspace -PathType Container) -Message 'Cleanup -WhatIf removed the integration.'

        $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
            WorkspaceRoot = $workspace
            TempRoot      = $integrationTemp
        }
        Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Explicit integration cleanup failed: $($cleanup.Output)"
        $cleanupJson = ConvertFrom-LastAutomationJson -Output $cleanup.Output
        Assert-AutomationTest -Condition ([bool]$cleanupJson.Success -and [bool]$cleanupJson.Removed) -Message 'Explicit cleanup did not report removal.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $workspace)) -Message 'Explicit cleanup left the owned workspace behind.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $neighborPath -PathType Leaf) -Message 'Explicit cleanup removed a neighboring path.'
    }

    Invoke-AutomationTestCase -Name 'SyntheticMergeRejectsProtectedPaths' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $protected = @($script:fixture.BasePath, $script:fixture.DeveloperPath, $script:fixture.ReviewerPath)
        foreach ($candidate in $protected) {
            $rejection = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = $script:fixture.OriginPath
                TempRoot           = $candidate
                ProtectedPath      = $protected
                DryRun             = $true
            }
            Assert-AutomationTest -Condition ($rejection.ExitCode -ne 0) -Message "Synthetic merge accepted protected TempRoot: $candidate"
            $json = ConvertFrom-LastAutomationJson -Output $rejection.Output
            Assert-AutomationTest -Condition ([string]$json.Error.Message -match 'overlaps protected path') -Message "Protected path rejection was not explicit: $($rejection.Output)"
            Assert-AutomationTest -Condition (Test-Path -LiteralPath $candidate -PathType Container) -Message "Protected path disappeared: $candidate"
        }
    }

    Invoke-AutomationTestCase -Name 'SyntheticMergeRejectsReparsePointTempRoot' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $junctionTarget = Join-Path $script:temporaryRoot 'junction target'
        $junctionPath = Join-Path $script:temporaryRoot 'junction temp root'
        New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
        New-TestFile -Path (Join-Path $junctionTarget 'target-sentinel.txt') -Content "survives`n"
        try {
            New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
            $rejection = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = $script:fixture.OriginPath
                TempRoot          = $junctionPath
                DryRun            = $true
            }
            Assert-AutomationTest -Condition ($rejection.ExitCode -ne 0) -Message 'Synthetic merge accepted a junction TempRoot.'
            $json = ConvertFrom-LastAutomationJson -Output $rejection.Output
            Assert-AutomationTest -Condition ([string]$json.Error.Message -match 'Reparse points are not allowed') -Message "Reparse-point rejection was not explicit: $($rejection.Output)"
            Assert-AutomationTest -Condition (Test-Path -LiteralPath (Join-Path $junctionTarget 'target-sentinel.txt') -PathType Leaf) -Message 'Reparse-point rejection modified the junction target.'
        }
        finally {
            if (Test-Path -LiteralPath $junctionPath) {
                [System.IO.Directory]::Delete($junctionPath, $false)
            }
        }
    }

    Invoke-AutomationTestCase -Name 'SyntheticMergePreservesPrimaryAndCleanupErrors' -Body {
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $cleanupScript = Join-Path $repository 'Tools\Automation\Remove-ReviewIntegration.ps1'
        $integrationTemp = Join-Path $script:temporaryRoot 'dual failure integration'
        $junctionTarget = Join-Path $script:temporaryRoot 'dual failure junction target'
        $fakeGitDirectory = Join-Path $script:temporaryRoot 'Dual Failure Git'
        $fakeGitPath = Join-Path $fakeGitDirectory 'git.cmd'
        $fakeGitPowerShellPath = Join-Path $fakeGitDirectory 'git.ps1'
        New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
        New-TestFile -Path (Join-Path $junctionTarget 'target-sentinel.txt') -Content "survives`n"

        $junctionTargetLiteral = ConvertTo-SingleQuotedPowerShellLiteral -Value $junctionTarget
        $fakeGitPowerShell = @(
            '$arguments = @($args)',
            'if ($arguments -contains ''ls-remote'') {',
            '    $refName = [string]$arguments[$arguments.Count - 1]',
            '    [Console]::Out.WriteLine((''a'' * 40) + "`t" + $refName)',
            '    exit 0',
            '}',
            'if ($arguments -contains ''clone'') {',
            '    $destination = [string]$arguments[$arguments.Count - 1]',
            ('    New-Item -ItemType Junction -Path $destination -Target ' + $junctionTargetLiteral + ' | Out-Null'),
            '    [Console]::Error.WriteLine(''intentional clone exit 7'')',
            '    exit 7',
            '}',
            '[Console]::Error.WriteLine(''unexpected fake git command'')',
            'exit 64'
        )
        New-TestFile -Path $fakeGitPowerShellPath -Content ([string]::Join("`r`n", $fakeGitPowerShell) + "`r`n")
        $fakeGit = @(
            '@echo off',
            '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0git.ps1" %*',
            'exit /b %ERRORLEVEL%'
        )
        New-TestFile -Path $fakeGitPath -Content ([string]::Join("`r`n", $fakeGit) + "`r`n")

        $workspace = $null
        try {
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = 'https://example.invalid/repository.git'
                TempRoot          = $integrationTemp
                GitExecutable     = $fakeGitPath
            }
            $json = ConvertFrom-LastAutomationJson -Output $integration.Output
            $workspace = [string]$json.WorkspaceRoot
            Assert-AutomationTest -Condition ($integration.ExitCode -eq 12) -Message "Dual primary/cleanup failure did not return cleanup exit 12: $($integration.Output)"
            Assert-AutomationTest -Condition ([string]$json.PrimaryError.Message -match 'exit code 7.*intentional clone exit 7') -Message "Primary clone failure was not preserved: $($integration.Output)"
            Assert-AutomationTest -Condition ([string]$json.Error.Message -eq [string]$json.PrimaryError.Message) -Message 'Backward-compatible Error no longer reports the primary failure.'
            Assert-AutomationTest -Condition ([string]$json.CleanupError.Message -match 'reparse point') -Message "Cleanup failure was not recorded separately: $($integration.Output)"
        }
        finally {
            if (Test-Path -LiteralPath $integrationTemp -PathType Container) {
                foreach ($retainedWorkspace in @(Get-ChildItem -LiteralPath $integrationTemp -Directory -Filter 'ReviewIntegration-*' -Force -ErrorAction SilentlyContinue)) {
                    $retainedRepository = Join-Path $retainedWorkspace.FullName 'Repository'
                    if (Test-Path -LiteralPath $retainedRepository) {
                        $retainedRepositoryItem = Get-Item -LiteralPath $retainedRepository -Force -ErrorAction Stop
                        if (($retainedRepositoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                            [System.IO.Directory]::Delete($retainedRepository, $false)
                        }
                    }
                }
            }
        }

        Assert-AutomationTest -Condition (-not [string]::IsNullOrWhiteSpace($workspace)) -Message 'Dual-failure test did not report its retained workspace.'
        $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
            WorkspaceRoot = $workspace
            TempRoot      = $integrationTemp
        }
        Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Dual-failure retained workspace could not be safely cleaned: $($cleanup.Output)"
        Assert-AutomationTest -Condition (Test-Path -LiteralPath (Join-Path $junctionTarget 'target-sentinel.txt') -PathType Leaf) -Message 'Dual-failure cleanup modified the junction target.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperAllowsExpectedLogAssertAndHandlesArguments' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $script:unityProjectPath = Join-Path $script:temporaryRoot 'fresh Unity project clone'
        Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('clone', '--branch', 'main', '--single-branch', '--', $script:fixture.OriginPath, $script:unityProjectPath) | Out-Null
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\success run'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $script:fixture.FakeUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -eq 0) -Message "Unity wrapper success smoke failed: $($unity.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition ([bool]$json.Success) -Message 'Unity wrapper JSON reported failure.'
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { [string]$_.Category -match 'Console|LogError' }).Count -eq 0) -Message 'Expected LogAssert output was reclassified as a new Console error.'
        Assert-AutomationTest -Condition ([int]$json.Stages.EditMode.Counts.Total -eq 1 -and [int]$json.Stages.EditMode.Counts.Passed -eq 1) -Message 'EditMode XML counts were not parsed.'
        Assert-AutomationTest -Condition ([int]$json.Stages.PlayMode.Counts.Total -eq 1 -and [int]$json.Stages.PlayMode.Counts.Passed -eq 1) -Message 'PlayMode XML counts were not parsed.'
        foreach ($artifact in @('CompileImport.log', 'EditMode.log', 'EditMode.xml', 'PlayMode.log', 'PlayMode.xml', 'Summary.json')) {
            Assert-AutomationTest -Condition (Test-Path -LiteralPath (Join-Path $artifactsPath $artifact) -PathType Leaf) -Message "Unity wrapper artifact is missing: $artifact"
        }
        $compileLog = [System.IO.File]::ReadAllText((Join-Path $artifactsPath 'CompileImport.log'))
        $editLog = [System.IO.File]::ReadAllText((Join-Path $artifactsPath 'EditMode.log'))
        $playLog = [System.IO.File]::ReadAllText((Join-Path $artifactsPath 'PlayMode.log'))
        Assert-AutomationTest -Condition ($compileLog -match '(?i)-quit') -Message 'Compile/import invocation did not include -quit.'
        Assert-AutomationTest -Condition ($editLog -notmatch '(?i)-quit' -and $playLog -notmatch '(?i)-quit') -Message 'A Unity test invocation incorrectly included -quit.'
        Assert-AutomationTest -Condition ($editLog.Contains($script:unityProjectPath) -and $playLog.Contains($script:unityProjectPath)) -Message 'Unity wrapper did not preserve the project path containing spaces.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperPropagatesNativeFailure' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $failingUnityPath = Join-Path $script:temporaryRoot 'Failing Unity\6000.4.0f1\Editor\Unity.cmd'
        $failingUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
            'if /I "%~1"=="-logFile" (',
            '  set "logFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'shift',
            'goto parse',
            ':writeLog',
            'if defined logFile echo Fake Unity native failure>"%logFile%"',
            'echo licensing stderr 1>&2',
            'exit /b 7'
        )
        New-TestFile -Path $failingUnityPath -Content ([string]::Join("`r`n", $failingUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\failure run'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $failingUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -eq 7) -Message "Unity wrapper did not propagate native exit 7: $($unity.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (-not [bool]$json.Success -and [int]$json.ExitCode -eq 7) -Message 'Unity failure JSON did not preserve native exit 7.'
        Assert-AutomationTest -Condition (@($json.Failures | Where-Object { $_.Stage -eq 'CompileImport' -and [int]$_.NativeExitCode -eq 7 }).Count -ge 1) -Message 'Unity failure details omitted the native exit code.'
        Assert-AutomationTest -Condition (@($json.Failures | Where-Object { $_.Stage -eq 'CompileImport' -and [string]$_.StdErr -match 'licensing stderr' }).Count -ge 1) -Message 'Unity failure details omitted native stderr.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsCompilerDiagnosticsWithZeroNativeExit' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Diagnostic Unity\6000.4.0f1\Editor\Unity.cmd'
        $diagnosticUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
            'if /I "%~1"=="-logFile" (',
            '  set "logFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'shift',
            'goto parse',
            ':writeLog',
            'if defined logFile echo error CS1002: expected token>"%logFile%"',
            'exit /b 0'
        )
        New-TestFile -Path $diagnosticUnityPath -Content ([string]::Join("`r`n", $diagnosticUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\diagnostic run'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $diagnosticUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted an error CS diagnostic with native exit 0.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'CompilerError' }).Count -ge 1) -Message 'Compiler diagnostic was not reported in structured output.'
        Assert-AutomationTest -Condition (@($json.Failures | Where-Object { $_.Stage -eq 'DiagnosticScan' -or $_.Stage -eq 'CompileImport' }).Count -ge 1) -Message 'Compiler diagnostic did not produce a structured failure.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsUnexpectedErrorViaFailedNUnitResult' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Console Error Unity\6000.4.0f1\Editor\Unity.cmd'
        $diagnosticUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            'set "resultFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
            'if /I "%~1"=="-logFile" (',
            '  set "logFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'if /I "%~1"=="-testResults" (',
            '  set "resultFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'shift',
            'goto parse',
            ':writeLog',
            'if not defined resultFile if defined logFile echo Clean compile/import fixture>"%logFile%"',
            'if defined resultFile if defined logFile echo Error: unexpected test error>"%logFile%"',
            'if defined resultFile if defined logFile echo UnityEngine.Debug:LogError ^(object^)>>"%logFile%"',
            'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Failed" total="1" passed="0" failed="1" inconclusive="0" skipped="0" duration="0.1" /^>"%resultFile%"',
            'exit /b 0'
        )
        New-TestFile -Path $diagnosticUnityPath -Content ([string]::Join("`r`n", $diagnosticUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\console error run'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $diagnosticUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted an unexpected test error with failed NUnit XML.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        $nonAuthoritativeStages = @($json.Stages.EditMode, $json.Stages.PlayMode | Where-Object { -not [bool]$_.AuthoritativePass })
        Assert-AutomationTest -Condition ($nonAuthoritativeStages.Count -ge 1) -Message 'Unexpected Error regression was not rejected through authoritative failed NUnit XML.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsOutOfRunConsoleErrorWithPassedNUnitXml' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Out Of Run Console Unity\6000.4.0f1\Editor\Unity.cmd'
        $diagnosticUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            'set "resultFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
            'if /I "%~1"=="-logFile" (',
            '  set "logFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'if /I "%~1"=="-testResults" (',
            '  set "resultFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'shift',
            'goto parse',
            ':writeLog',
            'if not defined resultFile if defined logFile echo Clean compile/import fixture>"%logFile%"',
            'if defined resultFile if defined logFile echo Error: outside NUnit execution>"%logFile%"',
            'if defined resultFile if defined logFile echo Running tests for ExecutionSettings with details:>>"%logFile%"',
            'if defined resultFile if defined logFile echo LogAssert.Expect matched an expected in-test error.>>"%logFile%"',
            'if defined resultFile if defined logFile echo UnityEngine.Debug:LogError ^(object^)>>"%logFile%"',
            'if defined resultFile if defined logFile echo Test run completed. Exiting with code 0 >>"%logFile%"',
            'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" /^>>"%resultFile%"',
            'exit /b 0'
        )
        New-TestFile -Path $diagnosticUnityPath -Content ([string]::Join("`r`n", $diagnosticUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\out of run console error'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $diagnosticUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted an out-of-run Console error with Passed NUnit XML.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'OutOfRunConsoleError' }).Count -ge 1) -Message 'Out-of-run Console error was not reported as structured diagnostic evidence.'
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'OutOfRunConsoleLogError' }).Count -eq 0) -Message 'Expected in-run LogAssert output was reclassified as out-of-run.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsInvalidRootResultAndNegativeCounts' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $invalidResults = @(
            [ordered]@{
                Name = 'failed root result'
                Xml  = '<test-run id="2" testcasecount="1" result="Failed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" />'
                ErrorPattern = 'root result is not Passed'
            },
            [ordered]@{
                Name = 'negative result count'
                Xml  = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="-1" failed="2" inconclusive="0" skipped="0" duration="0.1" />'
                ErrorPattern = 'negative.*count'
            },
            [ordered]@{
                Name = 'skipped strict result'
                Xml  = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="0" failed="0" inconclusive="0" skipped="1" duration="0.1" />'
                ErrorPattern = 'Skipped tests are not allowed'
            }
        )
        foreach ($invalidResult in $invalidResults) {
            $fakeUnityPath = Join-Path $script:temporaryRoot ("Invalid XML Unity\$($invalidResult.Name)\6000.4.0f1\Editor\Unity.cmd")
            $escapedXml = ([string]$invalidResult.Xml).Replace('<', '^<').Replace('>', '^>')
            $fakeUnity = @(
                '@echo off',
                'setlocal EnableExtensions',
                'set "logFile="',
                'set "resultFile="',
                ':parse',
                'if "%~1"=="" goto writeResults',
                'if /I "%~1"=="-logFile" (',
                '  set "logFile=%~2"',
                '  shift',
                '  shift',
                '  goto parse',
                ')',
                'if /I "%~1"=="-testResults" (',
                '  set "resultFile=%~2"',
                '  shift',
                '  shift',
                '  goto parse',
                ')',
                'shift',
                'goto parse',
                ':writeResults',
                'if defined logFile echo Invalid XML fixture>"%logFile%"',
                ('if defined resultFile echo ' + $escapedXml + '>"%resultFile%"'),
                'exit /b 0'
            )
            New-TestFile -Path $fakeUnityPath -Content ([string]::Join("`r`n", $fakeUnity) + "`r`n")
            $artifactsPath = Join-Path $script:temporaryRoot ("Unity Artifacts\$($invalidResult.Name)")
            $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
                ProjectPath          = $script:unityProjectPath
                ArtifactsPath        = $artifactsPath
                UnityExecutable      = $fakeUnityPath
                ExpectedUnityVersion = '6000.4.0f1'
            }
            Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message "Unity wrapper accepted invalid XML contract '$($invalidResult.Name)'."
            $json = ConvertFrom-LastAutomationJson -Output $unity.Output
            $failureText = [string]::Join(' ', @($json.Failures | ForEach-Object { [string]$_.Message }))
            Assert-AutomationTest -Condition ($failureText -match $invalidResult.ErrorPattern) -Message "Invalid XML contract '$($invalidResult.Name)' was not reported explicitly: $($unity.Output)"
        }
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperAllowsOnlyExactDisposableUnityDrift' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $cleanupScript = Join-Path $repository 'Tools\Automation\Remove-ReviewIntegration.ps1'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $protectedFixturePaths = @(
            $script:fixture.BasePath,
            $script:fixture.DeveloperPath,
            $script:fixture.ReviewerPath
        )
        $scenarios = @(
            [ordered]@{ Name = 'ArtifactsInsideWorkspaceRejected'; Mode = 'Exact'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $true },
            [ordered]@{ Name = 'Exact'; Mode = 'Exact'; ShouldPass = $true; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'NoFinalNewline'; Mode = 'NoFinalNewline'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'HeaderLikeContent'; Mode = 'HeaderLikeContent'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'RelocatedApprovedField'; Mode = 'RelocatedApprovedField'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'ExtraField'; Mode = 'ExtraField'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'SecondProjectSettingsFile'; Mode = 'SecondProjectSettingsFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'AssetsFile'; Mode = 'AssetsFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'PackagesFile'; Mode = 'PackagesFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false }
        )

        foreach ($scenario in $scenarios) {
            $scenarioName = [string]$scenario.Name
            $mode = [string]$scenario.Mode
            $fakeUnityPath = New-DisposableDriftUnityFixture -Root (Join-Path $script:temporaryRoot 'Disposable Drift Unity') -Mode $mode
            $integrationTemp = Join-Path $script:temporaryRoot ("disposable drift integration $scenarioName")
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = $script:fixture.OriginPath
                TempRoot          = $integrationTemp
                KeepWorkspace     = $true
            }
            Assert-AutomationTest -Condition ($integration.ExitCode -eq 0) -Message "Disposable drift integration setup failed for $mode`: $($integration.Output)"
            $integrationJson = ConvertFrom-LastAutomationJson -Output $integration.Output
            $workspace = [string]$integrationJson.WorkspaceRoot
            try {
                $artifactsPath = if ([bool]$scenario.ArtifactsInsideWorkspace) {
                    Join-Path $workspace 'Artifacts'
                }
                else {
                    Join-Path $script:temporaryRoot ("Unity Artifacts\disposable drift $scenarioName")
                }
                $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
                try {
                    if ([bool]$scenario.EnableTestHarness) {
                        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
                    }
                    else {
                        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $null, 'Process')
                    }
                    $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
                        ProjectPath          = [string]$integrationJson.IntegrationPath
                        ArtifactsPath        = $artifactsPath
                        UnityExecutable      = $fakeUnityPath
                        ExpectedUnityVersion = '6000.4.0f1'
                        InternalRequiredPersistentPath = $protectedFixturePaths
                    }
                }
                finally {
                    [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
                }
                $unityJson = ConvertFrom-LastAutomationJson -Output $unity.Output
                if ([bool]$scenario.ShouldPass) {
                    Assert-AutomationTest -Condition ($unity.ExitCode -eq 0 -and [bool]$unityJson.Success) -Message "Exact disposable drift was not accepted: $($unity.Output)"
                    Assert-AutomationTest -Condition ([string]$unityJson.KnownDisposableUnityDrift.Classification -eq 'KnownDisposableUnityDrift' -and [bool]$unityJson.KnownDisposableUnityDrift.Allowed) -Message 'Exact disposable drift classification is missing.'
                    Assert-AutomationTest -Condition (@($unityJson.KnownDisposableUnityDrift.ProtectedWorktrees | Where-Object { $_.IsGitRoot -and $_.Clean -and -not $_.Error }).Count -eq 3) -Message 'Exact drift did not verify all three persistent worktrees as clean.'
                    $diffPath = [string]$unityJson.KnownDisposableUnityDrift.DiffArtifactPath
                    $shaPath = [string]$unityJson.KnownDisposableUnityDrift.DiffSha256ArtifactPath
                    Assert-AutomationTest -Condition (Test-Path -LiteralPath $diffPath -PathType Leaf) -Message 'Known drift diff artifact is missing.'
                    Assert-AutomationTest -Condition (Test-Path -LiteralPath $shaPath -PathType Leaf) -Message 'Known drift SHA-256 artifact is missing.'
                    $actualDiffHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $diffPath).Hash.ToUpperInvariant()
                    Assert-AutomationTest -Condition ($actualDiffHash -eq [string]$unityJson.KnownDisposableUnityDrift.DiffSha256) -Message 'Known drift diff SHA-256 does not match its Summary evidence.'
                    $shaEvidence = [System.IO.File]::ReadAllText($shaPath)
                    Assert-AutomationTest -Condition ($shaEvidence -match ('^' + [regex]::Escape($actualDiffHash) + '  KnownDisposableUnityDrift\.diff\r?\n?$')) -Message 'Known drift SHA-256 artifact has an unexpected contract.'
                }
                else {
                    Assert-AutomationTest -Condition ($unity.ExitCode -ne 0 -and -not [bool]$unityJson.Success) -Message "Rejected drift scenario was accepted for $scenarioName`: $($unity.Output)"
                    Assert-AutomationTest -Condition (-not [bool]$unityJson.KnownDisposableUnityDrift.Allowed) -Message "Rejected drift scenario was classified as allowed for $scenarioName."
                    Assert-AutomationTest -Condition (@($unityJson.Failures | Where-Object { $_.Stage -eq 'WorkspaceMutation' }).Count -eq 1) -Message "Rejected drift scenario did not produce WorkspaceMutation for $scenarioName."
                }
            }
            finally {
                if ($workspace -and (Test-Path -LiteralPath $workspace -PathType Container)) {
                    $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
                        WorkspaceRoot = $workspace
                        TempRoot      = $integrationTemp
                    }
                    Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Disposable drift cleanup failed for $scenarioName`: $($cleanup.Output)"
                    Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $workspace)) -Message "Disposable drift workspace remains after cleanup for $scenarioName."
                }
            }
        }
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsTrackedWorkspaceMutation' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $mutatingUnityPath = Join-Path $script:temporaryRoot 'Mutating Unity\6000.4.0f1\Editor\Unity.cmd'
        $mutatingUnity = @(
            '@echo off',
            'setlocal EnableExtensions EnableDelayedExpansion',
            'set "projectPath="',
            'set "logFile="',
            'set "resultFile="',
            ':parse',
            'if "%~1"=="" goto writeResults',
            'if /I "%~1"=="-projectPath" (',
            '  set "projectPath=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'if /I "%~1"=="-logFile" (',
            '  set "logFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'if /I "%~1"=="-testResults" (',
            '  set "resultFile=%~2"',
            '  shift',
            '  shift',
            '  goto parse',
            ')',
            'shift',
            'goto parse',
            ':writeResults',
            'if defined logFile echo Fake Unity mutation smoke>"!logFile!"',
            'if not defined resultFile echo mutation>>"!projectPath!\pilot-main.txt"',
            'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" /^>>"!resultFile!"',
            'exit /b 0'
        )
        New-TestFile -Path $mutatingUnityPath -Content ([string]::Join("`r`n", $mutatingUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\mutation run'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $mutatingUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted a tracked workspace mutation.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (@($json.GitStatusAfter).Count -ge 1) -Message 'Tracked workspace mutation was not reported.'
        Assert-AutomationTest -Condition (@($json.Failures | Where-Object { $_.Stage -eq 'WorkspaceMutation' }).Count -eq 1) -Message 'Tracked workspace mutation did not produce the expected failure.'
    }

    Invoke-AutomationTestCase -Name 'SmokeWorktreesRemainClean' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        foreach ($path in @($script:fixture.BasePath, $script:fixture.DeveloperPath, $script:fixture.ReviewerPath)) {
            $status = Invoke-CheckedNativeCommand -FilePath 'git' -ArgumentList @('-C', $path, 'status', '--porcelain=v1', '--untracked-files=all')
            Assert-AutomationTest -Condition ([string]::IsNullOrWhiteSpace($status.StdOut)) -Message "Smoke fixture worktree is not clean: $path`n$($status.StdOut)"
        }
    }
    }

    $script:suiteCompleted = $true
    $script:suiteSucceeded = (@($script:testResults | Where-Object { -not $_.Passed }).Count -eq 0)
}
finally {
    $script:testRootPreserved = [bool]$KeepTemporaryFiles -and
        $script:ownedTestRootCreated -and
        $script:ownerMarkerWritten -and
        $script:suiteCompleted -and
        $script:suiteSucceeded
    if ($script:ownedTestRootCreated -and $script:temporaryRoot -and -not $script:testRootPreserved) {
        Remove-OwnedTestRoot `
            -Path $script:temporaryRoot `
            -AutomationRoot $automationTempRoot `
            -ExpectedRunId $script:testRunId `
            -MarkerWasWritten $script:ownerMarkerWritten
    }
}

$failed = @($script:testResults | Where-Object { -not $_.Passed })
$summary = [pscustomobject][ordered]@{
    SchemaVersion = 1
    Success       = ($failed.Count -eq 0)
    PowerShell    = $PSVersionTable.PSVersion.ToString()
    Repository    = $repository
    Passed        = @($script:testResults | Where-Object { $_.Passed }).Count
    Failed        = $failed.Count
    Tests         = $script:testResults.ToArray()
    TemporaryRoot = if ($script:testRootPreserved) { $script:temporaryRoot } else { $null }
}

$summary | ConvertTo-AutomationJson
if ($failed.Count -gt 0) {
    exit 1
}

exit 0
