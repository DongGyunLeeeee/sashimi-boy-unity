#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot,

    [Parameter()]
    [string]$WindowsPowerShellPath = (Join-Path -Path $env:SystemRoot -ChildPath 'System32\WindowsPowerShell\v1.0\powershell.exe'),

    [switch]$KeepTemporaryFiles
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

    $output = @(& $WindowsPowerShellPath -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1)
    $exitCode = $LASTEXITCODE
    return [pscustomobject][ordered]@{
        ExitCode = [int]$exitCode
        Output   = [string]::Join([Environment]::NewLine, [string[]]$output)
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

function Remove-OwnedTestRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $automationRootInput = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation'
    Assert-AutomationPathHasNoReparsePoint -Path $automationRootInput
    Assert-AutomationPathHasNoReparsePoint -Path $Path
    $automationRoot = ConvertTo-AutomationPath -Path $automationRootInput -AllowMissing
    $normalizedPath = ConvertTo-AutomationPath -Path $Path -AllowMissing
    $markerPath = Join-Path -Path $normalizedPath -ChildPath '.automation-script-tests-owner'
    if (-not (Test-AutomationPathWithin -Path $normalizedPath -Root $automationRoot) -or
        (Test-AutomationPathEqual -Left $normalizedPath -Right $automationRoot) -or
        -not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Refusing to remove unowned test path: $normalizedPath"
    }

    Assert-AutomationTreeHasNoReparsePoint -Root $normalizedPath
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $directories = New-Object 'System.Collections.Generic.List[string]'
    $pending.Push($normalizedPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
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
$automationTempRoot = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation'
if (-not (Test-Path -LiteralPath $automationTempRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $automationTempRoot -Force | Out-Null
}

$script:temporaryRoot = Join-Path -Path $automationTempRoot -ChildPath ('script-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:temporaryRoot | Out-Null
New-TestFile -Path (Join-Path $script:temporaryRoot '.automation-script-tests-owner') -Content "owned`n"

try {
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
        $commonContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Automation.Common.ps1'))
        Assert-AutomationTest -Condition (@([regex]::Matches($commonContent, '(?is)Remove-Item[^\r\n]*-Recurse')).Count -eq 1) -Message 'The vetted common helper must contain the sole production recursive delete.'
        Assert-AutomationTest -Condition ($commonContent -match 'Assert-AutomationTreeHasNoReparsePoint') -Message 'Recursive cleanup lacks a full-tree reparse-point guard.'
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
            SkipUnityProcessCheck   = $true
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
                SkipUnityProcessCheck   = $true
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
            SkipUnityProcessCheck   = $true
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
        New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
        try {
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

        $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
            PullRequestNumber = 23
            RepositoryUrl     = 'https://example.invalid/repository.git'
            TempRoot          = $integrationTemp
            GitExecutable     = $fakeGitPath
        }
        Assert-AutomationTest -Condition ($integration.ExitCode -eq 12) -Message "Dual primary/cleanup failure did not return cleanup exit 12: $($integration.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $integration.Output
        Assert-AutomationTest -Condition ([string]$json.PrimaryError.Message -match 'exit code 7.*intentional clone exit 7') -Message "Primary clone failure was not preserved: $($integration.Output)"
        Assert-AutomationTest -Condition ([string]$json.Error.Message -eq [string]$json.PrimaryError.Message) -Message 'Backward-compatible Error no longer reports the primary failure.'
        Assert-AutomationTest -Condition ([string]$json.CleanupError.Message -match 'reparse point') -Message "Cleanup failure was not recorded separately: $($integration.Output)"

        $workspace = [string]$json.WorkspaceRoot
        $integrationJunction = Join-Path $workspace 'Repository'
        if (Test-Path -LiteralPath $integrationJunction) {
            [System.IO.Directory]::Delete($integrationJunction, $false)
        }
        $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
            WorkspaceRoot = $workspace
            TempRoot      = $integrationTemp
        }
        Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Dual-failure retained workspace could not be safely cleaned: $($cleanup.Output)"
        Assert-AutomationTest -Condition (Test-Path -LiteralPath (Join-Path $junctionTarget 'target-sentinel.txt') -PathType Leaf) -Message 'Dual-failure cleanup modified the junction target.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperHandlesArgumentsLogsAndResults' -Body {
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

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsConsoleErrorsWithZeroNativeExit' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Console Error Unity\6000.4.0f1\Editor\Unity.cmd'
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
            'if defined logFile echo Error: fixture import failure>"%logFile%"',
            'if defined logFile echo UnityEngine.Debug:LogError ^(object^)>>"%logFile%"',
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
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted a Console error with native exit 0.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'ConsoleError' }).Count -ge 1) -Message 'Console error was not reported in structured diagnostics.'
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'ConsoleLogError' }).Count -ge 1) -Message 'Unity Debug.LogError stack trace was not reported in structured diagnostics.'
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
finally {
    if (-not $KeepTemporaryFiles -and $script:temporaryRoot -and (Test-Path -LiteralPath $script:temporaryRoot)) {
        Remove-OwnedTestRoot -Path $script:temporaryRoot
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
    TemporaryRoot = if ($KeepTemporaryFiles) { $script:temporaryRoot } else { $null }
}

$summary | ConvertTo-AutomationJson
if ($failed.Count -gt 0) {
    exit 1
}

exit 0
