#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,

    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe',

    [ValidateNotNullOrEmpty()]
    [string]$ExpectedUnityVersion = '6000.4.0f1',

    [ValidateNotNullOrEmpty()]
    [string]$GitExecutable = 'git',

    [string[]]$ProtectedProjectPath = @(
        'C:\Dev\sashimi-boy-unity',
        'C:\Dev\sashimi-boy-unity-developer',
        'C:\Dev\sashimi-boy-unity-reviewer'
    ),

    [switch]$AllowSkipped,

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    Write-Error "Required helper was not found: $commonPath"
    exit 10
}
. $commonPath

function Add-PlannedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$List,

        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    [void]$List.Add([ordered]@{
        Stage     = $Stage
        FilePath  = $FilePath
        Arguments = @($ArgumentList)
    })
}

function Add-Failure {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$Failures,

        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$Message,

        [int]$NativeExitCode = 0,

        [AllowEmptyString()]
        [string]$StdOut = '',

        [AllowEmptyString()]
        [string]$StdErr = ''
    )

    [void]$Failures.Add([ordered]@{
        Stage          = $Stage
        Message        = $Message
        NativeExitCode = $NativeExitCode
        StdOut         = $StdOut
        StdErr         = $StdErr
    })
}

function Assert-PathDoesNotOverlap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [string[]]$Protected
    )

    foreach ($protectedPath in $Protected) {
        if ([string]::IsNullOrWhiteSpace($protectedPath)) {
            continue
        }

        $normalizedProtected = ConvertTo-AutomationPath -Path $protectedPath -AllowMissing
        if ((Test-AutomationPathEqual -Left $Path -Right $normalizedProtected) -or
            (Test-AutomationPathWithin -Path $Path -Root $normalizedProtected) -or
            (Test-AutomationPathWithin -Path $normalizedProtected -Root $Path)) {
            throw "$Description '$Path' overlaps protected checkout '$normalizedProtected'."
        }
    }
}

function Get-UnityProcessesForProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NormalizedProjectPath
    )

    $matches = @()
    try {
        $processes = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
    }
    catch {
        # Some automation hosts deny Win32_Process command-line access. If no
        # Unity process exists at all, the project-specific process check still
        # has a definitive negative result; otherwise fail conservatively.
        $basicProcesses = @(Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)
        if ($basicProcesses.Count -gt 0) {
            throw "Unable to inspect Unity process command lines while Unity is running: $($_.Exception.Message)"
        }
        return @()
    }
    foreach ($process in $processes) {
        if ($null -ne $process.CommandLine -and
            $process.CommandLine.IndexOf($NormalizedProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $matches += [ordered]@{
                ProcessId   = [int]$process.ProcessId
                CommandLine = [string]$process.CommandLine
            }
        }
    }
    return @($matches)
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($Value.Contains('"')) {
        throw 'Unity arguments containing a double quote are not supported.'
    }
    if ($Value.Length -eq 0 -or $Value -match '\s') {
        return '"' + $Value + '"'
    }
    return $Value
}

function Invoke-WaitedNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList = @()
    )

    $standardOutputPath = [System.IO.Path]::GetTempFileName()
    $standardErrorPath = [System.IO.Path]::GetTempFileName()
    $exitCode = 127
    $standardOutput = ''
    $standardError = ''

    try {
        $argumentLine = [string]::Join(' ', @($ArgumentList | ForEach-Object {
                    ConvertTo-ProcessArgument -Value $_
                }))
        try {
            # Windows PowerShell does not wait for GUI-subsystem executables
            # invoked with &, even in a script. Start-Process -Wait is required
            # so compile/import, EditMode, and PlayMode remain strictly serial.
            $process = Start-Process -FilePath $FilePath `
                -ArgumentList $argumentLine `
                -RedirectStandardOutput $standardOutputPath `
                -RedirectStandardError $standardErrorPath `
                -WindowStyle Hidden `
                -Wait `
                -PassThru
            $exitCode = [int]$process.ExitCode
        }
        catch {
            $standardError = $_.Exception.Message
        }

        if (Test-Path -LiteralPath $standardOutputPath -PathType Leaf) {
            $standardOutput = [System.IO.File]::ReadAllText($standardOutputPath).TrimEnd()
        }
        if (Test-Path -LiteralPath $standardErrorPath -PathType Leaf) {
            $capturedError = [System.IO.File]::ReadAllText($standardErrorPath).TrimEnd()
            if ($capturedError) {
                if ($standardError) {
                    $standardError += [Environment]::NewLine
                }
                $standardError += $capturedError
            }
        }
    }
    finally {
        foreach ($temporaryPath in @($standardOutputPath, $standardErrorPath)) {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                [System.IO.File]::Delete($temporaryPath)
            }
        }
    }

    return [pscustomobject][ordered]@{
        FilePath  = $FilePath
        Arguments = @($ArgumentList)
        ExitCode  = [int]$exitCode
        StdOut    = $standardOutput
        StdErr    = $standardError
        Succeeded = ($exitCode -eq 0)
    }
}

function Invoke-UnityStage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$Commands
    )

    Add-PlannedCommand -List $Commands -Stage $Stage -FilePath $UnityExecutable -ArgumentList $ArgumentList
    return Invoke-WaitedNativeCommand -FilePath $UnityExecutable -ArgumentList $ArgumentList
}

function Get-TestResultSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResultPath
    )

    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw "Unity did not create the expected result XML: $ResultPath"
    }

    try {
        [xml]$document = Get-Content -Raw -LiteralPath $ResultPath
    }
    catch {
        throw "Unity result XML is not valid XML at '$ResultPath': $($_.Exception.Message)"
    }

    $testRun = $document.'test-run'
    if ($null -eq $testRun) {
        throw "Unity result XML does not contain a <test-run> root: $ResultPath"
    }

    $values = [ordered]@{}
    foreach ($name in @('total', 'passed', 'failed', 'inconclusive', 'skipped')) {
        $parsed = 0
        if (-not [int]::TryParse([string]$testRun.GetAttribute($name), [ref]$parsed)) {
            throw "Unity result XML has an invalid '$name' count at '$ResultPath'."
        }
        if ($parsed -lt 0) {
            throw "Unity result XML has a negative '$name' count at '$ResultPath'."
        }
        $values[$name] = $parsed
    }

    return [ordered]@{
        Path         = $ResultPath
        Result       = [string]$testRun.GetAttribute('result')
        Total        = $values.total
        Passed       = $values.passed
        Failed       = $values.failed
        Inconclusive = $values.inconclusive
        Skipped      = $values.skipped
        Duration     = [string]$testRun.GetAttribute('duration')
    }
}

function Get-LogDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LogPaths
    )

    $signatures = @(
        [ordered]@{ Name = 'CompilerError'; Pattern = '(?im)(?:^|\s)error\s+CS\d{4}\s*:' },
        [ordered]@{ Name = 'CompilationFailure'; Pattern = '(?im)Scripts? (?:had|have) compilation errors|Compilation failed' },
        [ordered]@{ Name = 'NullReferenceException'; Pattern = '(?i)NullReferenceException' },
        [ordered]@{ Name = 'MissingReferenceException'; Pattern = '(?i)MissingReferenceException' },
        [ordered]@{ Name = 'MissingScript'; Pattern = '(?i)Missing Script|The referenced script .* is missing|The associated script cannot be loaded' },
        [ordered]@{ Name = 'ConsoleError'; Pattern = '(?im)^\s*Error\s*:' },
        [ordered]@{ Name = 'ConsoleLogError'; Pattern = '(?im)^\s*UnityEngine\.Debug:(?:LogError|LogException|LogAssertion)\b' },
        [ordered]@{ Name = 'ManagedException'; Pattern = '(?im)^\s*(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception\s*:' },
        [ordered]@{ Name = 'AssertionFailure'; Pattern = '(?im)^\s*(?:Assertion failed|AssertionException\b|UnityEngine\.Assertions\.AssertionException)' },
        [ordered]@{ Name = 'UnhandledException'; Pattern = '(?im)^\s*Unhandled Exception\s*:' },
        [ordered]@{ Name = 'ShaderError'; Pattern = '(?im)^\s*Shader error(?:\s+in|\s*:)' },
        [ordered]@{ Name = 'ImportError'; Pattern = '(?im)^\s*(?:Asset import failed|Import error|Error while importing|Failed to import)\b' },
        [ordered]@{ Name = 'BatchModeAbort'; Pattern = '(?im)^\s*Aborting batchmode due to failure' }
    )

    $diagnostics = New-Object System.Collections.ArrayList
    foreach ($logPath in $LogPaths) {
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            continue
        }
        $content = Get-Content -Raw -LiteralPath $logPath
        foreach ($signature in $signatures) {
            $matches = [regex]::Matches($content, $signature.Pattern)
            if ($matches.Count -gt 0) {
                $samples = @($matches | Select-Object -First 5 | ForEach-Object { $_.Value })
                [void]$diagnostics.Add([ordered]@{
                    LogPath  = $logPath
                    Category = $signature.Name
                    Count    = $matches.Count
                    Samples  = $samples
                })
            }
        }
    }
    return @($diagnostics)
}

function Test-IsProtectedContentPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRelativePath
    )

    $path = $RepositoryRelativePath.Replace('\', '/')
    return ($path -like 'Assets/_SashimiBoy/Audio/*' -or
        $path -like 'Assets/_SashimiBoy/Scenes/*' -or
        $path -like 'Assets/_SashimiBoy/Art/Source/*' -or
        $path -like '*.prefab' -or
        $path -like '*.prefab.meta')
}

$commands = New-Object System.Collections.ArrayList
$failures = New-Object System.Collections.ArrayList
$effectiveDryRun = [bool]$DryRun -or [bool]$WhatIfPreference
$exitCode = 0
$artifactsCreated = $false
$result = $null

try {
    foreach ($configuredPath in @($ProjectPath, $ArtifactsPath, $UnityExecutable)) {
        Assert-AutomationPathHasNoReparsePoint -Path $configuredPath
    }
    $normalizedProjectPath = ConvertTo-AutomationPath -Path $ProjectPath -AllowMissing:$effectiveDryRun
    $normalizedArtifactsPath = ConvertTo-AutomationPath -Path $ArtifactsPath -AllowMissing
    $normalizedUnityExecutable = ConvertTo-AutomationPath -Path $UnityExecutable -AllowMissing:$effectiveDryRun

    Assert-PathDoesNotOverlap -Path $normalizedProjectPath -Description 'ProjectPath' -Protected $ProtectedProjectPath
    Assert-PathDoesNotOverlap -Path $normalizedArtifactsPath -Description 'ArtifactsPath' -Protected $ProtectedProjectPath
    if ((Test-AutomationPathEqual -Left $normalizedProjectPath -Right $normalizedArtifactsPath) -or
        (Test-AutomationPathWithin -Path $normalizedArtifactsPath -Root $normalizedProjectPath) -or
        (Test-AutomationPathWithin -Path $normalizedProjectPath -Root $normalizedArtifactsPath)) {
        throw "ArtifactsPath must be outside and must not contain ProjectPath. Project='$normalizedProjectPath', Artifacts='$normalizedArtifactsPath'."
    }

    $compileLog = Join-Path -Path $normalizedArtifactsPath -ChildPath 'CompileImport.log'
    $editLog = Join-Path -Path $normalizedArtifactsPath -ChildPath 'EditMode.log'
    $editXml = Join-Path -Path $normalizedArtifactsPath -ChildPath 'EditMode.xml'
    $playLog = Join-Path -Path $normalizedArtifactsPath -ChildPath 'PlayMode.log'
    $playXml = Join-Path -Path $normalizedArtifactsPath -ChildPath 'PlayMode.xml'
    $summaryPath = Join-Path -Path $normalizedArtifactsPath -ChildPath 'Summary.json'

    $compileArguments = @('-batchmode', '-nographics', '-projectPath', $normalizedProjectPath, '-logFile', $compileLog, '-quit')
    # Unity Test Framework 1.6.0 explicitly rejects -quit for command-line test runs.
    $editArguments = @('-batchmode', '-nographics', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'EditMode', '-testResults', $editXml, '-logFile', $editLog)
    $playArguments = @('-batchmode', '-nographics', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'PlayMode', '-testResults', $playXml, '-logFile', $playLog)

    $result = [ordered]@{
        Tool                 = 'Invoke-UnityTests'
        Success              = $false
        ExitCode             = 0
        DryRun               = $effectiveDryRun
        ProjectPath          = $normalizedProjectPath
        ArtifactsPath        = $normalizedArtifactsPath
        SummaryPath          = $summaryPath
        SummaryWritten       = $false
        UnityExecutable      = $normalizedUnityExecutable
        ExpectedUnityVersion = $ExpectedUnityVersion
        DetectedUnityVersion = $null
        AllowSkipped         = [bool]$AllowSkipped
        Stages               = [ordered]@{
            CompileImport = $null
            EditMode      = $null
            PlayMode      = $null
        }
        Diagnostics          = @()
        GitStatusAfter       = @()
        ProtectedChanges     = @()
        Commands             = $commands
        Failures             = $failures
    }

    if ($effectiveDryRun) {
        Add-PlannedCommand -List $commands -Stage 'CompileImport' -FilePath $normalizedUnityExecutable -ArgumentList $compileArguments
        Add-PlannedCommand -List $commands -Stage 'EditMode' -FilePath $normalizedUnityExecutable -ArgumentList $editArguments
        Add-PlannedCommand -List $commands -Stage 'PlayMode' -FilePath $normalizedUnityExecutable -ArgumentList $playArguments
        $result.Success = $true
    }
    else {
        if (-not (Test-Path -LiteralPath $normalizedUnityExecutable -PathType Leaf)) {
            throw "Unity executable was not found: $normalizedUnityExecutable"
        }
        foreach ($requiredPath in @('Assets', 'Packages', 'ProjectSettings', 'ProjectSettings\ProjectVersion.txt')) {
            if (-not (Test-Path -LiteralPath (Join-Path -Path $normalizedProjectPath -ChildPath $requiredPath))) {
                throw "ProjectPath is not a complete Unity project; missing '$requiredPath'."
            }
        }
        foreach ($generatedPath in @('Library', 'Temp', 'Logs', 'UserSettings')) {
            if (Test-Path -LiteralPath (Join-Path -Path $normalizedProjectPath -ChildPath $generatedPath)) {
                throw "ProjectPath is not a fresh clone because '$generatedPath' already exists."
            }
        }

        $lockPath = Join-Path -Path $normalizedProjectPath -ChildPath 'Temp\UnityLockfile'
        if (Test-Path -LiteralPath $lockPath) {
            throw "Unity project lock exists: $lockPath"
        }
        $unityProcesses = @(Get-UnityProcessesForProject -NormalizedProjectPath $normalizedProjectPath)
        if ($unityProcesses.Count -gt 0) {
            throw "A Unity process is already using ProjectPath (PID(s): $(@($unityProcesses.ProcessId) -join ', '))."
        }

        $projectVersionText = Get-Content -Raw -LiteralPath (Join-Path -Path $normalizedProjectPath -ChildPath 'ProjectSettings\ProjectVersion.txt')
        if ($projectVersionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)\s*$') {
            throw 'ProjectSettings/ProjectVersion.txt does not contain m_EditorVersion.'
        }
        $result.DetectedUnityVersion = $Matches[1]
        if ($result.DetectedUnityVersion -ne $ExpectedUnityVersion) {
            throw "Unity version mismatch. Expected '$ExpectedUnityVersion', project requires '$($result.DetectedUnityVersion)'."
        }

        $rootCommand = Invoke-AutomationNativeCommand -FilePath $GitExecutable -ArgumentList @('-C', $normalizedProjectPath, 'rev-parse', '--show-toplevel')
        if (-not $rootCommand.Succeeded) {
            throw "ProjectPath is not a Git working tree: $($rootCommand.StdErr)"
        }
        $gitRoot = ConvertTo-AutomationPath -Path $rootCommand.StdOut.Trim()
        if (-not (Test-AutomationPathEqual -Left $gitRoot -Right $normalizedProjectPath)) {
            throw "ProjectPath must be the root of its fresh clone. Git root is '$gitRoot'."
        }
        $initialStatus = Invoke-AutomationNativeCommand -FilePath $GitExecutable -ArgumentList @('-C', $normalizedProjectPath, 'status', '--porcelain=v1', '--untracked-files=all')
        if (-not $initialStatus.Succeeded) {
            throw "Unable to inspect initial Git status: $($initialStatus.StdErr)"
        }
        if (-not [string]::IsNullOrWhiteSpace($initialStatus.StdOut)) {
            throw "ProjectPath must be clean before Unity runs: $($initialStatus.StdOut)"
        }

        if (Test-Path -LiteralPath $normalizedArtifactsPath) {
            $existingArtifacts = @(Get-ChildItem -LiteralPath $normalizedArtifactsPath -Force)
            if ($existingArtifacts.Count -gt 0) {
                throw "ArtifactsPath must be absent or empty so stale results cannot be mistaken for this run: $normalizedArtifactsPath"
            }
        }
        else {
            New-Item -ItemType Directory -Path $normalizedArtifactsPath -Force | Out-Null
        }
        Assert-AutomationPathHasNoReparsePoint -Path $normalizedArtifactsPath
        $artifactsCreated = $true

        $compile = Invoke-UnityStage -Stage 'CompileImport' -ArgumentList $compileArguments -Commands $commands
        $result.Stages.CompileImport = [ordered]@{
            NativeExitCode = [int]$compile.ExitCode
            Succeeded      = [bool]$compile.Succeeded
            LogPath        = $compileLog
            StdOut         = $compile.StdOut
            StdErr         = $compile.StdErr
        }
        if (-not (Test-Path -LiteralPath $compileLog -PathType Leaf)) {
            Add-Failure -Failures $failures -Stage 'CompileImport' -Message "Compile/import log was not created: $compileLog" -NativeExitCode $compile.ExitCode -StdOut $compile.StdOut -StdErr $compile.StdErr
            if ($exitCode -eq 0) { $exitCode = 1 }
        }
        if (-not $compile.Succeeded) {
            Add-Failure -Failures $failures -Stage 'CompileImport' -Message 'Unity clean import/C# compilation process failed.' -NativeExitCode $compile.ExitCode -StdOut $compile.StdOut -StdErr $compile.StdErr
            if ($exitCode -eq 0) { $exitCode = if ($compile.ExitCode -ne 0) { [int]$compile.ExitCode } else { 1 } }
        }

        $compileDiagnostics = @(Get-LogDiagnostics -LogPaths @($compileLog))
        $canRunTests = $compile.Succeeded -and $compileDiagnostics.Count -eq 0
        if (-not $canRunTests -and $compileDiagnostics.Count -gt 0) {
            Add-Failure -Failures $failures -Stage 'CompileImport' -Message 'Forbidden error signatures were found in the compile/import log.' -StdOut $compile.StdOut -StdErr $compile.StdErr
            if ($exitCode -eq 0) { $exitCode = 1 }
        }

        if ($canRunTests) {
            foreach ($testStage in @(
                [ordered]@{ Name = 'EditMode'; Arguments = $editArguments; LogPath = $editLog; XmlPath = $editXml },
                [ordered]@{ Name = 'PlayMode'; Arguments = $playArguments; LogPath = $playLog; XmlPath = $playXml }
            )) {
                $native = Invoke-UnityStage -Stage $testStage.Name -ArgumentList $testStage.Arguments -Commands $commands
                $testSummary = $null
                try {
                    $testSummary = Get-TestResultSummary -ResultPath $testStage.XmlPath
                }
                catch {
                    Add-Failure -Failures $failures -Stage $testStage.Name -Message $_.Exception.Message -NativeExitCode $native.ExitCode -StdOut $native.StdOut -StdErr $native.StdErr
                    if ($exitCode -eq 0) { $exitCode = if ($native.ExitCode -ne 0) { [int]$native.ExitCode } else { 1 } }
                }

                $stageResult = [ordered]@{
                    NativeExitCode = [int]$native.ExitCode
                    Succeeded      = [bool]$native.Succeeded
                    LogPath        = $testStage.LogPath
                    ResultPath     = $testStage.XmlPath
                    Counts         = $testSummary
                    StdOut         = $native.StdOut
                    StdErr         = $native.StdErr
                }
                $result.Stages[$testStage.Name] = $stageResult

                if (-not (Test-Path -LiteralPath $testStage.LogPath -PathType Leaf)) {
                    Add-Failure -Failures $failures -Stage $testStage.Name -Message "Unity log was not created: $($testStage.LogPath)" -NativeExitCode $native.ExitCode -StdOut $native.StdOut -StdErr $native.StdErr
                    if ($exitCode -eq 0) { $exitCode = 1 }
                }
                if (-not $native.Succeeded) {
                    Add-Failure -Failures $failures -Stage $testStage.Name -Message 'Unity test process returned a non-zero exit code.' -NativeExitCode $native.ExitCode -StdOut $native.StdOut -StdErr $native.StdErr
                    if ($exitCode -eq 0) { $exitCode = if ($native.ExitCode -ne 0) { [int]$native.ExitCode } else { 1 } }
                }
                if ($null -ne $testSummary) {
                    if (-not [string]::Equals($testSummary.Result, 'Passed', [System.StringComparison]::OrdinalIgnoreCase)) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Unity XML root result is not Passed: result='$($testSummary.Result)'." -NativeExitCode $native.ExitCode -StdOut $native.StdOut -StdErr $native.StdErr
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if ($testSummary.Total -le 0) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message 'No tests were executed (XML total is zero).' -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if ($testSummary.Failed -gt 0 -or $testSummary.Inconclusive -gt 0) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Test failures found: failed=$($testSummary.Failed), inconclusive=$($testSummary.Inconclusive)." -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = if ($native.ExitCode -ne 0) { [int]$native.ExitCode } else { 1 } }
                    }
                    if (-not $AllowSkipped -and $testSummary.Skipped -gt 0) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Skipped tests are not allowed in the strict full-PASS contract: skipped=$($testSummary.Skipped)." -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if (($testSummary.Passed + $testSummary.Failed + $testSummary.Inconclusive + $testSummary.Skipped) -ne $testSummary.Total) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message 'XML test counts do not add up to total.' -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if (-not $AllowSkipped -and $testSummary.Passed -ne $testSummary.Total) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Strict full-PASS count mismatch: passed=$($testSummary.Passed), total=$($testSummary.Total)." -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                }
            }
        }

        $allLogPaths = @($compileLog, $editLog, $playLog)
        $result.Diagnostics = @(Get-LogDiagnostics -LogPaths $allLogPaths)
        if ($result.Diagnostics.Count -gt 0) {
            Add-Failure -Failures $failures -Stage 'DiagnosticScan' -Message 'One or more forbidden diagnostic signatures were found in Unity logs.'
            if ($exitCode -eq 0) { $exitCode = 1 }
        }

        $finalStatus = Invoke-AutomationNativeCommand -FilePath $GitExecutable -ArgumentList @('-C', $normalizedProjectPath, 'status', '--porcelain=v1', '--untracked-files=all')
        if (-not $finalStatus.Succeeded) {
            Add-Failure -Failures $failures -Stage 'GitStatusAfter' -Message "Unable to inspect final Git status: $($finalStatus.StdErr)" -NativeExitCode $finalStatus.ExitCode
            if ($exitCode -eq 0) { $exitCode = 1 }
        }
        else {
            $statusLines = @($finalStatus.StdOut -split "`r?`n" | Where-Object { $_ -match '\S' })
            $result.GitStatusAfter = $statusLines
            if ($statusLines.Count -gt 0) {
                Add-Failure -Failures $failures -Stage 'WorkspaceMutation' -Message 'Unity left tracked or unignored changes in the fresh integration workspace.'
                if ($exitCode -eq 0) { $exitCode = 1 }
            }
            $protectedChanges = New-Object System.Collections.ArrayList
            foreach ($statusLine in $statusLines) {
                $relativePath = if ($statusLine.Length -gt 3) { $statusLine.Substring(3).Trim('"') } else { $statusLine }
                if (Test-IsProtectedContentPath -RepositoryRelativePath $relativePath) {
                    [void]$protectedChanges.Add($statusLine)
                }
            }
            $result.ProtectedChanges = @($protectedChanges)
            if ($result.ProtectedChanges.Count -gt 0) {
                Add-Failure -Failures $failures -Stage 'ProtectedContent' -Message 'Unity changed protected source audio, Scene, Prefab, or Art/Source content.'
                if ($exitCode -eq 0) { $exitCode = 1 }
            }
        }

        $result.Success = ($failures.Count -eq 0 -and $exitCode -eq 0)
    }
}
catch {
    if ($null -eq $result) {
        $result = [ordered]@{
            Tool           = 'Invoke-UnityTests'
            Success        = $false
            ExitCode       = 1
            DryRun         = $effectiveDryRun
            ProjectPath    = $ProjectPath
            ArtifactsPath  = $ArtifactsPath
            SummaryWritten = $false
            Commands       = $commands
            Failures       = $failures
        }
    }
    Add-Failure -Failures $failures -Stage 'PreflightOrUnhandled' -Message $_.Exception.Message
    $result.Success = $false
    if ($exitCode -eq 0) {
        $exitCode = 1
    }
}

$result.ExitCode = $exitCode
if ($artifactsCreated) {
    try {
        $result.SummaryWritten = $true
        Set-Content -LiteralPath $result.SummaryPath -Value (ConvertTo-AutomationJson -InputObject $result) -Encoding UTF8
    }
    catch {
        $result.SummaryWritten = $false
        $result.Success = $false
        Add-Failure -Failures $failures -Stage 'Summary' -Message "Unable to write Summary.json: $($_.Exception.Message)"
        if ($exitCode -eq 0) { $exitCode = 1 }
        $result.ExitCode = $exitCode
    }
}

[Console]::Out.WriteLine((ConvertTo-AutomationJson -InputObject $result))
exit $exitCode
