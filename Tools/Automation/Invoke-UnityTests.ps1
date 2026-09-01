#requires -Version 5.1

[CmdletBinding()]
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

    [Parameter(DontShow = $true)]
    [string[]]$InternalRequiredPersistentPath,

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

$protectedCheckoutPaths = @(
    'C:\Dev\sashimi-boy-unity',
    'C:\Dev\sashimi-boy-unity-developer',
    'C:\Dev\sashimi-boy-unity-reviewer'
)
$persistentEvidencePaths = @($protectedCheckoutPaths)
if ($null -ne $InternalRequiredPersistentPath -and @($InternalRequiredPersistentPath).Count -gt 0) {
    if ($env:SASHIMI_BOY_AUTOMATION_TEST_HARNESS -cne '1') {
        throw 'Internal persistent-path injection is available only with the explicit smoke-harness guard.'
    }
    $persistentEvidencePaths = @($InternalRequiredPersistentPath)
}

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
    $processes = $null
    $cimProcessError = $null
    try {
        $processes = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
    }
    catch {
        $cimProcessError = $_.Exception.Message
    }
    if ($null -ne $cimProcessError) {
        # Some automation hosts deny Win32_Process command-line access. If no
        # Unity process exists at all, the project-specific process check still
        # has a definitive negative result; otherwise fail conservatively.
        try {
            $basicProcesses = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -ieq 'Unity' })
        }
        catch {
            throw "Unable to inspect Unity processes through CIM or Get-Process: $cimProcessError; fallback: $($_.Exception.Message)"
        }
        if ($basicProcesses.Count -gt 0) {
            throw "Unable to inspect Unity process command lines while Unity is running: $cimProcessError"
        }
        return @()
    }
    $slashProjectPath = $NormalizedProjectPath.Replace('\', '/')
    foreach ($process in $processes) {
        $commandLine = [string]$process.CommandLine
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            throw "Unable to prove that Unity.exe process $($process.ProcessId) is unrelated because its command line is unavailable."
        }
        if ($commandLine.IndexOf($NormalizedProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf($slashProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $matches += [ordered]@{
                ProcessId   = [int]$process.ProcessId
                CommandLine = $commandLine
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

function Add-LogDiagnosticMatches {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.ArrayList]$Diagnostics,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [object[]]$Signatures,

        [Parameter(Mandatory = $true)]
        [string]$Scope
    )

    foreach ($signature in $Signatures) {
        $matches = [regex]::Matches($Content, $signature.Pattern)
        if ($matches.Count -gt 0) {
            $samples = @($matches | Select-Object -First 5 | ForEach-Object { $_.Value })
            [void]$Diagnostics.Add([ordered]@{
                LogPath  = $LogPath
                Category = $signature.Name
                Scope    = $Scope
                Count    = $matches.Count
                Samples  = $samples
            })
        }
    }
}

function Get-CompileLogDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LogPaths
    )

    # Clean import/compile is deliberately strict. Any one of these signatures
    # blocks the test stages even when Unity happens to return exit code zero.
    $strictSignatures = @(
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
        Add-LogDiagnosticMatches `
            -Diagnostics $diagnostics `
            -LogPath $logPath `
            -Content $content `
            -Signatures $strictSignatures `
            -Scope 'CompileImport'
    }
    return @($diagnostics)
}

function Get-TestLogDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LogPaths
    )

    # Test results are authoritative through native exit + strict NUnit XML.
    # Broad Debug.LogError/Assertion/managed-exception scanning would reject a
    # passing LogAssert.Expect test. Only failures which remain independently
    # actionable are scanned across the entire test log.
    $wholeLogSignatures = @(
        [ordered]@{ Name = 'CompilerError'; Pattern = '(?im)(?:^|\s)error\s+CS\d{4}\s*:' },
        [ordered]@{ Name = 'CompilationFailure'; Pattern = '(?im)Scripts? (?:had|have) compilation errors|Compilation failed' },
        [ordered]@{ Name = 'MissingScript'; Pattern = '(?i)Missing Script|The referenced script .* is missing|The associated script cannot be loaded' },
        [ordered]@{ Name = 'UnhandledException'; Pattern = '(?im)^\s*Unhandled Exception\s*:' },
        [ordered]@{ Name = 'BatchModeAbort'; Pattern = '(?im)^\s*Aborting batchmode due to failure' },
        [ordered]@{ Name = 'UnityCrash'; Pattern = '(?im)^\s*(?:Fatal error\b|Unity has crashed\b|Crash!!!|Receiving unhandled NULL exception\b)' }
    )
    $outsideRunSignatures = @(
        [ordered]@{ Name = 'OutOfRunConsoleError'; Pattern = '(?im)^\s*Error\s*:' },
        [ordered]@{ Name = 'OutOfRunConsoleLogError'; Pattern = '(?im)^\s*UnityEngine\.Debug:(?:LogError|LogException|LogAssertion)\b' },
        [ordered]@{ Name = 'OutOfRunAssertionFailure'; Pattern = '(?im)^\s*(?:Assertion failed|AssertionException\b|UnityEngine\.Assertions\.AssertionException)' },
        [ordered]@{ Name = 'OutOfRunNullReferenceException'; Pattern = '(?im)^\s*(?:[A-Za-z_]\w*\.)*NullReferenceException\s*:' },
        [ordered]@{ Name = 'OutOfRunMissingReferenceException'; Pattern = '(?im)^\s*(?:[A-Za-z_]\w*\.)*MissingReferenceException\s*:' },
        [ordered]@{ Name = 'OutOfRunManagedException'; Pattern = '(?im)^\s*(?!(?:[A-Za-z_]\w*\.)*(?:NullReferenceException|MissingReferenceException)\s*:)(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception\s*:' }
    )

    $diagnostics = New-Object System.Collections.ArrayList
    foreach ($logPath in $LogPaths) {
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            continue
        }

        $content = Get-Content -Raw -LiteralPath $logPath
        Add-LogDiagnosticMatches `
            -Diagnostics $diagnostics `
            -LogPath $logPath `
            -Content $content `
            -Signatures $wholeLogSignatures `
            -Scope 'EntireTestProcess'

        $runStart = [regex]::Match($content, '(?im)^Running tests for ExecutionSettings with details:\s*$')
        $runEnd = [regex]::Match($content, '(?im)^Test run completed\. Exiting with code \d+.*$')
        if ($runStart.Success -and $runEnd.Success -and $runEnd.Index -ge $runStart.Index) {
            $outsideRun = $content.Substring(0, $runStart.Index) + [Environment]::NewLine
            $afterEnd = $runEnd.Index + $runEnd.Length
            if ($afterEnd -lt $content.Length) {
                $outsideRun += $content.Substring($afterEnd)
            }
        }
        else {
            # Without both boundary lines, no output can be proven to belong to
            # an authoritative NUnit run. Scan the entire log conservatively.
            $outsideRun = $content
        }

        Add-LogDiagnosticMatches `
            -Diagnostics $diagnostics `
            -LogPath $logPath `
            -Content $outsideRun `
            -Signatures $outsideRunSignatures `
            -Scope 'OutsideNUnitRun'
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

function Test-StringSequenceEqual {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Actual,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Expected
    )

    if ($Actual.Count -ne $Expected.Count) {
        return $false
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals($Actual[$index], $Expected[$index], [System.StringComparison]::Ordinal)) {
            return $false
        }
    }
    return $true
}

function Get-DisposableReviewIntegrationContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NormalizedProjectPath
    )

    $context = [ordered]@{
        Valid         = $false
        Reason        = $null
        WorkspaceRoot = $null
        RunId         = $null
        MarkerPath    = $null
    }

    try {
        $canonicalRootInput = [System.IO.Path]::Combine(
            [System.IO.Path]::GetTempPath(),
            'SashimiBoyAutomation')
        Assert-AutomationPathHasNoReparsePoint -Path $canonicalRootInput
        $canonicalRoot = ConvertTo-AutomationPath -Path $canonicalRootInput -AllowMissing
        if (-not (Test-AutomationPathWithin -Path $NormalizedProjectPath -Root $canonicalRoot) -or
            (Test-AutomationPathEqual -Left $NormalizedProjectPath -Right $canonicalRoot)) {
            $context.Reason = "ProjectPath is not below the canonical automation temp root '$canonicalRoot'."
            return [pscustomobject]$context
        }

        $workspaceRoot = Split-Path -Parent $NormalizedProjectPath
        $expectedRepositoryPath = Join-Path -Path $workspaceRoot -ChildPath 'Repository'
        if (-not (Test-AutomationPathEqual -Left $NormalizedProjectPath -Right $expectedRepositoryPath)) {
            $context.Reason = "ProjectPath is not the Repository child of an owned review-integration workspace."
            return [pscustomobject]$context
        }

        $ownedWorkspace = Get-AutomationOwnedWorkspace `
            -WorkspaceRoot $workspaceRoot `
            -AllowedRoot $canonicalRoot
        $context.Valid = $true
        $context.Reason = 'Ownership marker, RunId, canonical containment, and reparse-point checks passed.'
        $context.WorkspaceRoot = $ownedWorkspace.WorkspaceRoot
        $context.RunId = $ownedWorkspace.RunId
        $context.MarkerPath = $ownedWorkspace.MarkerPath
    }
    catch {
        $context.Reason = $_.Exception.Message
    }

    return [pscustomobject]$context
}

function Get-KnownDisposableUnityDriftAssessment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NormalizedProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$NormalizedArtifactsPath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$StatusLines,

        [Parameter(Mandatory = $true)]
        [psobject]$DisposableContext,

        [Parameter(Mandatory = $true)]
        [string[]]$RequiredPersistentPaths,

        [Parameter(Mandatory = $true)]
        [string]$ValidatedUnityVersion,

        [Parameter(Mandatory = $true)]
        [string]$GitExecutablePath
    )

    $assessment = [ordered]@{
        Classification        = if ($StatusLines.Count -gt 0) { 'WorkspaceMutation' } else { 'None' }
        Detected              = ($StatusLines.Count -gt 0)
        Allowed               = $false
        Reason                = if ($StatusLines.Count -gt 0) { 'Workspace changes require validation.' } else { 'No workspace mutation detected.' }
        RepositoryRelativePath = $null
        WorkspaceRoot         = $DisposableContext.WorkspaceRoot
        RunId                 = $DisposableContext.RunId
        MarkerPath            = $DisposableContext.MarkerPath
        ApprovedChanges       = @()
        DiffArtifactPath      = $null
        DiffSha256ArtifactPath = $null
        DiffSha256            = $null
        DiffSha256ArtifactSha256 = $null
        WorkingFileSha256     = $null
        ProtectedWorktrees    = @()
    }

    if ($StatusLines.Count -eq 0) {
        return [pscustomobject]$assessment
    }
    if (-not $DisposableContext.Valid) {
        $assessment.Reason = "Workspace is not a validated tool-owned disposable review integration: $($DisposableContext.Reason)"
        return [pscustomobject]$assessment
    }
    if (-not [string]::Equals($ValidatedUnityVersion, '6000.4.0f1', [System.StringComparison]::Ordinal)) {
        $assessment.Reason = "Known drift is approved only for Unity 6000.4.0f1; validated project version was '$ValidatedUnityVersion'."
        return [pscustomobject]$assessment
    }
    if ((Test-AutomationPathEqual -Left $NormalizedArtifactsPath -Right $DisposableContext.WorkspaceRoot) -or
        (Test-AutomationPathWithin -Path $NormalizedArtifactsPath -Root $DisposableContext.WorkspaceRoot)) {
        $assessment.Reason = 'Known drift evidence must be stored outside the disposable workspace so cleanup cannot remove it.'
        return [pscustomobject]$assessment
    }

    $expectedStatus = ' M ProjectSettings/ProjectSettings.asset'
    if ($StatusLines.Count -ne 1 -or
        -not [string]::Equals($StatusLines[0], $expectedStatus, [System.StringComparison]::Ordinal)) {
        $assessment.Reason = 'Known drift requires exactly one unstaged modification: ProjectSettings/ProjectSettings.asset.'
        return [pscustomobject]$assessment
    }
    $assessment.RepositoryRelativePath = 'ProjectSettings/ProjectSettings.asset'

    try {
        # Revalidate marker/RunId/tree immediately before granting the narrow
        # non-blocking classification; the Unity run may have lasted minutes.
        $canonicalRoot = ConvertTo-AutomationPath -Path ([System.IO.Path]::Combine(
                [System.IO.Path]::GetTempPath(),
                'SashimiBoyAutomation'))
        [void](Get-AutomationOwnedWorkspace `
                -WorkspaceRoot $DisposableContext.WorkspaceRoot `
                -AllowedRoot $canonicalRoot `
                -ExpectedRunId $DisposableContext.RunId)

        $fullDiffCommand = Invoke-AutomationNativeCommand `
            -FilePath $GitExecutablePath `
            -ArgumentList @(
                '-C', $NormalizedProjectPath,
                'diff', '--no-ext-diff', '--no-textconv', '--full-index', '--binary', '--no-color', '--',
                'ProjectSettings/ProjectSettings.asset')
        if (-not $fullDiffCommand.Succeeded -or [string]::IsNullOrWhiteSpace($fullDiffCommand.StdOut)) {
            throw "Unable to capture the ProjectSettings drift diff: $($fullDiffCommand.StdErr)"
        }

        $validationDiffCommand = Invoke-AutomationNativeCommand `
            -FilePath $GitExecutablePath `
            -ArgumentList @(
                '-C', $NormalizedProjectPath,
                'diff', '--no-ext-diff', '--no-textconv', '--unified=0', '--no-color', '--',
                'ProjectSettings/ProjectSettings.asset')
        if (-not $validationDiffCommand.Succeeded) {
            throw "Unable to validate the ProjectSettings drift diff: $($validationDiffCommand.StdErr)"
        }
        $metadataDiffCommand = Invoke-AutomationNativeCommand `
            -FilePath $GitExecutablePath `
            -ArgumentList @(
                '-C', $NormalizedProjectPath,
                'diff', '--no-ext-diff', '--no-textconv', '--summary', '--',
                'ProjectSettings/ProjectSettings.asset')
        if (-not $metadataDiffCommand.Succeeded) {
            throw "Unable to inspect ProjectSettings mode/type metadata: $($metadataDiffCommand.StdErr)"
        }
        if (-not [string]::IsNullOrWhiteSpace($metadataDiffCommand.StdOut)) {
            throw "ProjectSettings drift includes a forbidden mode, rename, copy, or type metadata change: $($metadataDiffCommand.StdOut)"
        }

        $expectedChangeLines = @(
            '-  targetPixelDensity: 0',
            '+  targetPixelDensity: 30',
            '-  buildNumber: {}',
            '+  buildNumber:',
            '+    Standalone: 0',
            '+    VisionOS: 0',
            '+    iPhone: 0',
            '+    tvOS: 0',
            '-  iOSTargetOSVersionString: ',
            '+  iOSTargetOSVersionString: 15.0',
            '-  tvOSTargetOSVersionString: ',
            '+  tvOSTargetOSVersionString: 15.0',
            '-  VisionOSTargetOSVersionString: ',
            '+  VisionOSTargetOSVersionString: 1.0',
            '-  macOSTargetOSVersion: ',
            '+  macOSTargetOSVersion: 12.0'
        )
        $actualChangeLines = @($validationDiffCommand.StdOut -split "`r?`n" | Where-Object {
                (($_ -like '+*') -and
                    -not [string]::Equals($_, '+++ b/ProjectSettings/ProjectSettings.asset', [System.StringComparison]::Ordinal)) -or
                (($_ -like '-*') -and
                    -not [string]::Equals($_, '--- a/ProjectSettings/ProjectSettings.asset', [System.StringComparison]::Ordinal))
            })
        if (-not (Test-StringSequenceEqual -Actual $actualChangeLines -Expected $expectedChangeLines)) {
            throw ("ProjectSettings drift contains a deletion, reordering, value, or key outside the approved Unity-default serialization sequence. Actual change lines: {0}" -f
                [string]::Join(' | ', [string[]]$actualChangeLines))
        }
        if ($validationDiffCommand.StdOut -match '(?m)^\\ No newline at end of file\r?$') {
            throw 'ProjectSettings drift changed the approved file end-of-file newline.'
        }
        if ($fullDiffCommand.StdOut -notmatch '(?m)^diff --git a/ProjectSettings/ProjectSettings\.asset b/ProjectSettings/ProjectSettings\.asset\r?$' -or
            $fullDiffCommand.StdOut -notmatch '(?m)^--- a/ProjectSettings/ProjectSettings\.asset\r?$' -or
            $fullDiffCommand.StdOut -notmatch '(?m)^\+\+\+ b/ProjectSettings/ProjectSettings\.asset\r?$') {
            throw 'ProjectSettings drift diff headers do not identify the exact approved file.'
        }

        $changedFilePath = Join-Path -Path $NormalizedProjectPath -ChildPath 'ProjectSettings\ProjectSettings.asset'
        Assert-AutomationPathHasNoReparsePoint -Path $changedFilePath
        if (-not (Test-Path -LiteralPath $changedFilePath -PathType Leaf)) {
            throw "Changed ProjectSettings file is missing: $changedFilePath"
        }

        # Diff-line comparison is useful evidence, but it cannot prove hunk
        # locations. Build the sole approved full-file result from the HEAD
        # content and require the working file to match it exactly as text.
        $headContentCommand = Invoke-AutomationNativeCommand `
            -FilePath $GitExecutablePath `
            -ArgumentList @('-C', $NormalizedProjectPath, 'cat-file', 'blob', 'HEAD:ProjectSettings/ProjectSettings.asset')
        if (-not $headContentCommand.Succeeded) {
            throw "Unable to read HEAD ProjectSettings content: $($headContentCommand.StdErr)"
        }
        $normalizedHeadContent = ($headContentCommand.StdOut -replace "`r`n", "`n") -replace "`r", "`n"
        # Invoke-AutomationNativeCommand captures native output line-by-line and
        # therefore omits the terminal newline. The accepted diff above proves
        # that neither side carries Git's no-final-newline marker.
        $normalizedHeadContent += "`n"
        $approvedLineReplacements = [ordered]@{
            '  targetPixelDensity: 0'         = @('  targetPixelDensity: 30')
            '  buildNumber: {}'               = @('  buildNumber:', '    Standalone: 0', '    VisionOS: 0', '    iPhone: 0', '    tvOS: 0')
            '  iOSTargetOSVersionString: '     = @('  iOSTargetOSVersionString: 15.0')
            '  tvOSTargetOSVersionString: '    = @('  tvOSTargetOSVersionString: 15.0')
            '  VisionOSTargetOSVersionString: ' = @('  VisionOSTargetOSVersionString: 1.0')
            '  macOSTargetOSVersion: '         = @('  macOSTargetOSVersion: 12.0')
        }
        $replacementCounts = @{}
        foreach ($sourceLine in $approvedLineReplacements.Keys) {
            $replacementCounts[$sourceLine] = 0
        }
        $expectedLines = New-Object System.Collections.Generic.List[string]
        foreach ($headLine in @($normalizedHeadContent -split "`n")) {
            if ($approvedLineReplacements.Contains($headLine)) {
                $replacementCounts[$headLine] = [int]$replacementCounts[$headLine] + 1
                foreach ($replacementLine in @($approvedLineReplacements[$headLine])) {
                    [void]$expectedLines.Add([string]$replacementLine)
                }
            }
            else {
                [void]$expectedLines.Add($headLine)
            }
        }
        foreach ($sourceLine in $approvedLineReplacements.Keys) {
            if ([int]$replacementCounts[$sourceLine] -ne 1) {
                throw "HEAD ProjectSettings must contain exactly one approved source line '$sourceLine'; found $($replacementCounts[$sourceLine])."
            }
        }
        $expectedWorkingContent = [string]::Join("`n", [string[]]$expectedLines)
        $actualWorkingContent = ([System.IO.File]::ReadAllText($changedFilePath) -replace "`r`n", "`n") -replace "`r", "`n"
        if (-not [string]::Equals($actualWorkingContent, $expectedWorkingContent, [System.StringComparison]::Ordinal)) {
            throw 'ProjectSettings working content is not the exact full-file result of the approved Unity-default transformations at their original locations.'
        }

        $diffArtifactPath = Join-Path -Path $NormalizedArtifactsPath -ChildPath 'KnownDisposableUnityDrift.diff'
        $shaArtifactPath = Join-Path -Path $NormalizedArtifactsPath -ChildPath 'KnownDisposableUnityDrift.diff.sha256'
        Assert-AutomationPathHasNoReparsePoint -Path $NormalizedArtifactsPath
        Assert-AutomationTreeHasNoReparsePoint -Root $NormalizedArtifactsPath
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText(
            $diffArtifactPath,
            $fullDiffCommand.StdOut + [Environment]::NewLine,
            $utf8NoBom)
        $diffSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $diffArtifactPath).Hash.ToUpperInvariant()
        [System.IO.File]::WriteAllText(
            $shaArtifactPath,
            "$diffSha256  KnownDisposableUnityDrift.diff" + [Environment]::NewLine,
            $utf8NoBom)

        $assessment.DiffArtifactPath = $diffArtifactPath
        $assessment.DiffSha256ArtifactPath = $shaArtifactPath
        $assessment.DiffSha256 = $diffSha256
        $assessment.DiffSha256ArtifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $shaArtifactPath).Hash.ToUpperInvariant()
        $assessment.WorkingFileSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $changedFilePath).Hash.ToUpperInvariant()

        # The Owner-approved non-blocking classification also requires the
        # base, Developer, and Reviewer worktrees to be valid, distinct, and
        # clean at the moment the drift is granted.
        if (@($RequiredPersistentPaths).Count -ne 3) {
            throw "Known drift requires exactly three persistent worktree paths; received $(@($RequiredPersistentPaths).Count)."
        }
        $projectOriginCommand = Invoke-AutomationNativeCommand `
            -FilePath $GitExecutablePath `
            -ArgumentList @('-C', $NormalizedProjectPath, 'remote', 'get-url', 'origin')
        if (-not $projectOriginCommand.Succeeded -or [string]::IsNullOrWhiteSpace($projectOriginCommand.StdOut)) {
            throw "Unable to resolve the disposable integration origin: $($projectOriginCommand.StdErr)"
        }
        $expectedOrigin = $projectOriginCommand.StdOut.Trim()
        $protectedEvidence = New-Object System.Collections.ArrayList
        $normalizedProtectedPaths = New-Object System.Collections.ArrayList
        $sharedCommonDirectory = $null
        for ($protectedIndex = 0; $protectedIndex -lt $RequiredPersistentPaths.Count; $protectedIndex++) {
            $configuredProtectedPath = $RequiredPersistentPaths[$protectedIndex]
            $expectedRole = @('Base', 'Developer', 'Reviewer')[$protectedIndex]
            $worktreeEvidence = [ordered]@{
                Role           = $expectedRole
                ConfiguredPath = $configuredProtectedPath
                Path           = $null
                IsGitRoot      = $false
                RepositoryShape = $null
                GitDirectory   = $null
                CommonDirectory = $null
                Origin         = $null
                Clean          = $false
                Status         = @()
                Error          = $null
            }
            try {
                Assert-AutomationPathHasNoReparsePoint -Path $configuredProtectedPath
                $normalizedProtectedPath = ConvertTo-AutomationPath -Path $configuredProtectedPath
                $worktreeEvidence.Path = $normalizedProtectedPath
                foreach ($otherProtectedPath in $normalizedProtectedPaths) {
                    if (Test-AutomationPathEqual -Left $normalizedProtectedPath -Right $otherProtectedPath) {
                        throw "Protected worktree paths must be distinct: $normalizedProtectedPath"
                    }
                }
                [void]$normalizedProtectedPaths.Add($normalizedProtectedPath)

                $protectedRootCommand = Invoke-AutomationNativeCommand `
                    -FilePath $GitExecutablePath `
                    -ArgumentList @('-C', $normalizedProtectedPath, 'rev-parse', '--show-toplevel')
                if (-not $protectedRootCommand.Succeeded) {
                    throw "Protected path is not a Git working tree: $($protectedRootCommand.StdErr)"
                }
                $protectedGitRoot = ConvertTo-AutomationPath -Path $protectedRootCommand.StdOut.Trim()
                if (-not (Test-AutomationPathEqual -Left $protectedGitRoot -Right $normalizedProtectedPath)) {
                    throw "Protected path is not its Git root; resolved root is '$protectedGitRoot'."
                }
                $worktreeEvidence.IsGitRoot = $true

                $gitDirectoryCommand = Invoke-AutomationNativeCommand `
                    -FilePath $GitExecutablePath `
                    -ArgumentList @('-C', $normalizedProtectedPath, 'rev-parse', '--absolute-git-dir')
                $commonDirectoryCommand = Invoke-AutomationNativeCommand `
                    -FilePath $GitExecutablePath `
                    -ArgumentList @('-C', $normalizedProtectedPath, 'rev-parse', '--path-format=absolute', '--git-common-dir')
                if (-not $gitDirectoryCommand.Succeeded -or -not $commonDirectoryCommand.Succeeded) {
                    throw 'Unable to inspect the protected checkout/worktree shape.'
                }
                $protectedGitDirectory = ConvertTo-AutomationPath -Path $gitDirectoryCommand.StdOut.Trim()
                $protectedCommonDirectory = ConvertTo-AutomationPath -Path $commonDirectoryCommand.StdOut.Trim()
                $worktreeEvidence.GitDirectory = $protectedGitDirectory
                $worktreeEvidence.CommonDirectory = $protectedCommonDirectory
                $gitMarkerPath = Join-Path -Path $normalizedProtectedPath -ChildPath '.git'
                $shapeIsValid = if ($expectedRole -eq 'Base') {
                    (Test-Path -LiteralPath $gitMarkerPath -PathType Container) -and
                        (Test-AutomationPathEqual -Left $protectedGitDirectory -Right $protectedCommonDirectory)
                }
                else {
                    (Test-Path -LiteralPath $gitMarkerPath -PathType Leaf) -and
                        -not (Test-AutomationPathEqual -Left $protectedGitDirectory -Right $protectedCommonDirectory)
                }
                if (-not $shapeIsValid) {
                    throw "Protected $expectedRole path does not have the required primary/linked-worktree shape."
                }
                $worktreeEvidence.RepositoryShape = if ($expectedRole -eq 'Base') { 'PrimaryCheckout' } else { 'LinkedWorktree' }
                if ($null -eq $sharedCommonDirectory) {
                    $sharedCommonDirectory = $protectedCommonDirectory
                }
                elseif (-not (Test-AutomationPathEqual -Left $sharedCommonDirectory -Right $protectedCommonDirectory)) {
                    throw 'Protected Base, Developer, and Reviewer paths do not share one Git common directory.'
                }

                $originCommand = Invoke-AutomationNativeCommand `
                    -FilePath $GitExecutablePath `
                    -ArgumentList @('-C', $normalizedProtectedPath, 'remote', 'get-url', 'origin')
                if (-not $originCommand.Succeeded -or [string]::IsNullOrWhiteSpace($originCommand.StdOut)) {
                    throw "Unable to resolve the protected $expectedRole origin: $($originCommand.StdErr)"
                }
                $worktreeEvidence.Origin = $originCommand.StdOut.Trim()
                if (-not [string]::Equals($worktreeEvidence.Origin, $expectedOrigin, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Protected $expectedRole origin does not match the disposable integration origin."
                }

                $protectedStatusCommand = Invoke-AutomationNativeCommand `
                    -FilePath $GitExecutablePath `
                    -ArgumentList @('-C', $normalizedProtectedPath, 'status', '--porcelain=v1', '--untracked-files=all')
                if (-not $protectedStatusCommand.Succeeded) {
                    throw "Unable to inspect protected worktree status: $($protectedStatusCommand.StdErr)"
                }
                $protectedStatusLines = @($protectedStatusCommand.StdOut -split "`r?`n" | Where-Object { $_ -match '\S' })
                $worktreeEvidence.Status = $protectedStatusLines
                if ($protectedStatusLines.Count -gt 0) {
                    throw "Protected worktree is not clean: $($protectedStatusLines -join '; ')"
                }
                $worktreeEvidence.Clean = $true
            }
            catch {
                $worktreeEvidence.Error = $_.Exception.Message
                [void]$protectedEvidence.Add($worktreeEvidence)
                $assessment.ProtectedWorktrees = @($protectedEvidence)
                throw "Protected worktree validation failed for '$configuredProtectedPath': $($_.Exception.Message)"
            }
            [void]$protectedEvidence.Add($worktreeEvidence)
        }
        $assessment.ProtectedWorktrees = @($protectedEvidence)

        $assessment.Classification = 'KnownDisposableUnityDrift'
        $assessment.Allowed = $true
        $assessment.Reason = 'Exact Owner-approved Unity 6000.4.0f1 default serialization drift in a validated disposable clone.'
        $assessment.WorkspaceRoot = $DisposableContext.WorkspaceRoot
        $assessment.RunId = $DisposableContext.RunId
        $assessment.MarkerPath = $DisposableContext.MarkerPath
        $assessment.ApprovedChanges = @(
            'targetPixelDensity: 0 -> 30',
            'buildNumber: empty -> Standalone/VisionOS/iPhone/tvOS zero defaults',
            'iOSTargetOSVersionString: empty -> 15.0',
            'tvOSTargetOSVersionString: empty -> 15.0',
            'VisionOSTargetOSVersionString: empty -> 1.0',
            'macOSTargetOSVersion: empty -> 12.0'
        )
    }
    catch {
        $assessment.Classification = 'WorkspaceMutation'
        $assessment.Allowed = $false
        $assessment.Reason = $_.Exception.Message
    }

    return [pscustomobject]$assessment
}

$commands = New-Object System.Collections.ArrayList
$failures = New-Object System.Collections.ArrayList
$effectiveDryRun = [bool]$DryRun
$exitCode = 0
$artifactsCreated = $false
$result = $null
$disposableContext = $null

try {
    foreach ($configuredPath in @($ProjectPath, $ArtifactsPath, $UnityExecutable)) {
        Assert-AutomationPathHasNoReparsePoint -Path $configuredPath
    }
    $normalizedProjectPath = ConvertTo-AutomationPath -Path $ProjectPath -AllowMissing:$effectiveDryRun
    $normalizedArtifactsPath = ConvertTo-AutomationPath -Path $ArtifactsPath -AllowMissing
    $normalizedUnityExecutable = ConvertTo-AutomationPath -Path $UnityExecutable -AllowMissing:$effectiveDryRun

    Assert-PathDoesNotOverlap -Path $normalizedProjectPath -Description 'ProjectPath' -Protected $protectedCheckoutPaths
    Assert-PathDoesNotOverlap -Path $normalizedArtifactsPath -Description 'ArtifactsPath' -Protected $protectedCheckoutPaths
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

    $compileArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-logFile', $compileLog, '-quit')
    # Unity Test Framework 1.6.0 explicitly rejects -quit for command-line test runs.
    $editArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'EditMode', '-testResults', $editXml, '-logFile', $editLog)
    $playArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'PlayMode', '-testResults', $playXml, '-logFile', $playLog)

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
        BuildTarget          = 'StandaloneWindows64'
        Stages               = [ordered]@{
            CompileImport = $null
            EditMode      = $null
            PlayMode      = $null
        }
        Diagnostics          = @()
        GitStatusAfter       = @()
        ProtectedChanges     = @()
        KnownDisposableUnityDrift = [ordered]@{
            Classification = 'None'
            Detected       = $false
            Allowed        = $false
            Reason         = 'Not evaluated.'
        }
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
        $disposableContext = Get-DisposableReviewIntegrationContext -NormalizedProjectPath $normalizedProjectPath

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
        $compileLogExists = Test-Path -LiteralPath $compileLog -PathType Leaf
        if (-not $compileLogExists) {
            Add-Failure -Failures $failures -Stage 'CompileImport' -Message "Compile/import log was not created: $compileLog" -NativeExitCode $compile.ExitCode -StdOut $compile.StdOut -StdErr $compile.StdErr
            if ($exitCode -eq 0) { $exitCode = 1 }
        }
        if (-not $compile.Succeeded) {
            Add-Failure -Failures $failures -Stage 'CompileImport' -Message 'Unity clean import/C# compilation process failed.' -NativeExitCode $compile.ExitCode -StdOut $compile.StdOut -StdErr $compile.StdErr
            if ($exitCode -eq 0) { $exitCode = if ($compile.ExitCode -ne 0) { [int]$compile.ExitCode } else { 1 } }
        }

        $compileDiagnostics = @(Get-CompileLogDiagnostics -LogPaths @($compileLog))
        $result.Diagnostics = @($compileDiagnostics)
        $canRunTests = $compile.Succeeded -and $compileLogExists -and $compileDiagnostics.Count -eq 0
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
                    AuthoritativePass = $false
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
                $strictXmlPassed = $false
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
                    if ($testSummary.Skipped -gt 0) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Skipped tests are not allowed in the strict full-PASS contract: skipped=$($testSummary.Skipped)." -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if (($testSummary.Passed + $testSummary.Failed + $testSummary.Inconclusive + $testSummary.Skipped) -ne $testSummary.Total) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message 'XML test counts do not add up to total.' -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }
                    if ($testSummary.Passed -ne $testSummary.Total) {
                        Add-Failure -Failures $failures -Stage $testStage.Name -Message "Strict full-PASS count mismatch: passed=$($testSummary.Passed), total=$($testSummary.Total)." -NativeExitCode $native.ExitCode
                        if ($exitCode -eq 0) { $exitCode = 1 }
                    }

                    $strictXmlPassed = (
                        [string]::Equals($testSummary.Result, 'Passed', [System.StringComparison]::OrdinalIgnoreCase) -and
                        $testSummary.Total -gt 0 -and
                        $testSummary.Failed -eq 0 -and
                        $testSummary.Inconclusive -eq 0 -and
                        $testSummary.Skipped -eq 0 -and
                        $testSummary.Passed -eq $testSummary.Total -and
                        ($testSummary.Passed + $testSummary.Failed + $testSummary.Inconclusive + $testSummary.Skipped) -eq $testSummary.Total)
                }
                if ([bool]$native.Succeeded -ne [bool]$strictXmlPassed) {
                    Add-Failure -Failures $failures -Stage $testStage.Name -Message "Unity native exit and strict NUnit XML disagree: nativeSucceeded=$([bool]$native.Succeeded), strictXmlPassed=$strictXmlPassed." -NativeExitCode $native.ExitCode -StdOut $native.StdOut -StdErr $native.StdErr
                    if ($exitCode -eq 0) { $exitCode = if ($native.ExitCode -ne 0) { [int]$native.ExitCode } else { 1 } }
                }
                $stageResult.AuthoritativePass = ([bool]$native.Succeeded -and [bool]$strictXmlPassed)
            }
        }

        $testDiagnostics = @(Get-TestLogDiagnostics -LogPaths @($editLog, $playLog))
        $result.Diagnostics = @($compileDiagnostics) + @($testDiagnostics)
        if ($testDiagnostics.Count -gt 0) {
            Add-Failure -Failures $failures -Stage 'TestDiagnosticScan' -Message 'A compile failure, crash, Missing Script, or out-of-run Console error, assertion, or managed exception was found in a Unity test log.'
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
            $result.KnownDisposableUnityDrift = Get-KnownDisposableUnityDriftAssessment `
                -NormalizedProjectPath $normalizedProjectPath `
                -NormalizedArtifactsPath $normalizedArtifactsPath `
                -StatusLines $statusLines `
                -DisposableContext $disposableContext `
                -RequiredPersistentPaths $persistentEvidencePaths `
                -ValidatedUnityVersion ([string]$result.DetectedUnityVersion) `
                -GitExecutablePath $GitExecutable
            if ($statusLines.Count -gt 0 -and -not $result.KnownDisposableUnityDrift.Allowed) {
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
        Assert-AutomationPathHasNoReparsePoint -Path $result.ArtifactsPath
        Assert-AutomationTreeHasNoReparsePoint -Root $result.ArtifactsPath
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
