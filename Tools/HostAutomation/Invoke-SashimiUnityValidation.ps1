#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ConfigPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ArtifactsPath,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$IssueNumber,

    [ValidateNotNullOrEmpty()]
    [string]$BaselineRef = 'HEAD',

    [string]$IssueValidationId,

    [string]$OwnedUnityPidPath,

    [string]$CancellationMarkerPath,

    [switch]$DryRun,

    [Parameter(DontShow = $true)]
    [string]$ValidationFixturePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'HostAutomation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Host automation common helpers are missing: $commonPath")
    exit 1
}
. $commonPath

$commands = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[object]]::new()
$ownedUnityProcessIds = [Collections.Generic.List[int]]::new()
$stages = [ordered]@{}
$artifactHooks = [Collections.Generic.List[object]]::new()
$summaryWritten = $false
$normalizedArtifactsPath = $null
$rawValidationPath = $null
$rawValidationFiles = @()
$script:rawValidationCleanupSafe = $true
$script:unitySensitiveEnvironmentValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Protect-SashimiValidationMetadataText {
    [CmdletBinding()]
    param([AllowNull()][object]$Text)

    $protected = if ($null -eq $Text) { '' } else { [string]$Text }
    if (-not [string]::IsNullOrWhiteSpace($script:rawValidationPath)) {
        foreach ($rawSpelling in @(
                $script:rawValidationPath,
                $script:rawValidationPath.Replace('\', '\\'),
                $script:rawValidationPath.Replace('\', '/')
            )) {
            $protected = $protected.Replace($rawSpelling, '<run-state-raw-validation>', [StringComparison]::OrdinalIgnoreCase)
        }
    }
    return Protect-SashimiText $protected
}

function Add-SashimiValidationCheck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Detail,
        [bool]$Planned = $false,
        [AllowNull()][object]$Data = $null
    )

    $checks.Add([pscustomobject][ordered]@{
            Name = $Name
            Passed = $Passed
            Planned = $Planned
            Detail = Protect-SashimiValidationMetadataText $Detail
            Data = $Data
        })
}

function Add-SashimiValidationFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Message,
        [int]$NativeExitCode = 0
    )

    $failures.Add([pscustomobject][ordered]@{
            Code = $Code
            Stage = $Stage
            Message = Protect-SashimiValidationMetadataText $Message
            NativeExitCode = $NativeExitCode
        })
}

function Assert-SashimiSafeBaselineRef {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value.StartsWith('-') -or $Value -eq '@' -or $Value.Contains('..') -or
        $Value.Contains('@{') -or $Value.Contains('//') -or $Value.EndsWith('/') -or
        $Value.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase) -or
        $Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') {
        throw "BaselineRef is not a safe Git revision: $Value"
    }
}

function ConvertTo-SashimiGlobRegex {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Pattern)

    $normalized = $Pattern.Replace('\', '/')
    $builder = [Text.StringBuilder]::new('^')
    for ($index = 0; $index -lt $normalized.Length; $index++) {
        $character = $normalized[$index]
        if ($character -eq '*') {
            if (($index + 1) -lt $normalized.Length -and $normalized[$index + 1] -eq '*') {
                $index++
                if (($index + 1) -lt $normalized.Length -and $normalized[$index + 1] -eq '/') {
                    $index++
                    [void]$builder.Append('(?:.*/)?')
                }
                else {
                    [void]$builder.Append('.*')
                }
            }
            else {
                [void]$builder.Append('[^/]*')
            }
        }
        elseif ($character -eq '?') {
            [void]$builder.Append('[^/]')
        }
        else {
            [void]$builder.Append([regex]::Escape([string]$character))
        }
    }
    [void]$builder.Append('$')
    return $builder.ToString()
}

function Test-SashimiRelativePathPattern {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowEmptyCollection()][string[]]$Patterns = @()
    )

    $normalized = $Path.Replace('\', '/').TrimStart('/')
    foreach ($pattern in @($Patterns)) {
        if ([string]::IsNullOrWhiteSpace($pattern)) { continue }
        if ([regex]::IsMatch(
                $normalized,
                (ConvertTo-SashimiGlobRegex -Pattern $pattern),
                [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            return $true
        }
    }
    return $false
}

function ConvertTo-SashimiProjectRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$NormalizedProjectPath
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or
        $Value.IndexOfAny([char[]]@([char]0, [char]13, [char]10)) -ge 0) {
        throw "Validation path must be a non-empty repository-relative path: '$Value'"
    }
    $slashPath = $Value.Replace('\', '/')
    if ($slashPath.StartsWith('./', [StringComparison]::Ordinal)) {
        $slashPath = $slashPath.Substring(2)
    }
    $segments = @($slashPath -split '/')
    if ($segments.Count -eq 0 -or @($segments | Where-Object { $_ -eq '..' -or $_ -eq '' }).Count -gt 0 -or
        $segments[0] -ieq '.git' -or $segments[0] -ieq '.codex') {
        throw "Validation path escapes or targets host metadata: '$Value'"
    }
    $fullPath = ConvertTo-SashimiPath -Path (Join-Path $NormalizedProjectPath ($slashPath.Replace('/', [IO.Path]::DirectorySeparatorChar))) -AllowMissing -Lexical
    if (-not (Test-SashimiPathWithin -Path $fullPath -Root $NormalizedProjectPath)) {
        throw "Validation path is outside ProjectPath: '$Value'"
    }
    return $slashPath
}

function Get-SashimiFixtureEntry {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Fixture,
        [Parameter(Mandatory = $true)][string]$Group,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Fixture) { return $null }
    $groupValue = Get-SashimiPropertyValue -Object $Fixture -Name $Group -DefaultValue $null
    if ($null -eq $groupValue) { return $null }
    return Get-SashimiPropertyValue -Object $groupValue -Name $Name -DefaultValue $null
}

function Add-SashimiPlannedCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $evidenceArguments = @($Arguments | ForEach-Object {
        $argument = [string]$_
        if (-not [string]::IsNullOrWhiteSpace($script:rawValidationPath) -and
            $argument.StartsWith($script:rawValidationPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            '<run-state-raw-validation>'
        }
        else { $argument }
    })
    $record = [pscustomobject][ordered]@{
        Name = $Name
        Kind = $Kind
        FilePath = $FilePath
        Arguments = $evidenceArguments
        Command = Format-SashimiCommand -FilePath $FilePath -ArgumentList $evidenceArguments
        TimeoutSeconds = $TimeoutSeconds
    }
    $commands.Add($record)
    return $record
}

function Invoke-SashimiValidationProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('Git', 'Unity')][string]$Kind,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [AllowNull()][object]$Fixture,
        [Parameter(Mandatory = $true)][string]$FixtureGroup,
        [string]$LogPath,
        [string]$XmlPath,
        [switch]$DryRun
    )

    [void](Add-SashimiPlannedCommand -Name $Name -Kind $Kind -FilePath $FilePath -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds)
    $removeEnvironmentNames = @()
    if ($Kind -ceq 'Unity') {
        # Unity requires only ordinary Windows/profile locators. Remove every
        # other inherited variable, even when its innocuous name would evade a
        # credential-name denylist. The interactive user's Unity license may
        # continue to resolve through USERPROFILE/AppData, while GitHub, Codex,
        # cloud, proxy, and arbitrary caller variables never reach editor code.
        $allowedEnvironmentNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($allowedName in @(
                'SystemRoot','WINDIR','COMSPEC','PATH','PATHEXT','TEMP','TMP',
                'USERPROFILE','HOME','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','PROGRAMDATA','ALLUSERSPROFILE','PUBLIC',
                'ProgramFiles','ProgramFiles(x86)','ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432',
                'PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','PROCESSOR_LEVEL','PROCESSOR_REVISION','NUMBER_OF_PROCESSORS',
                'OS','SystemDrive','SystemDirectory','LANG','LC_ALL'
            )) {
            [void]$allowedEnvironmentNames.Add($allowedName)
        }
        $removeNames = [Collections.Generic.List[string]]::new()
        foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
            $environmentName = [string]$entry.Key
            if ($allowedEnvironmentNames.Contains($environmentName) -and
                -not (Test-SashimiSensitiveEnvironmentName -Name $environmentName)) { continue }
            $removeNames.Add($environmentName)
            $environmentValue = [string]$entry.Value
            # Very short values create destructive false positives in compiler
            # logs/XML, and very large values are not retained in memory. They
            # are still removed from the child environment; exact-value
            # defense in depth covers bounded, meaningful secret values.
            if ($environmentValue.Length -ge 8 -and $environmentValue.Length -le 4096) {
                [void]$script:unitySensitiveEnvironmentValues.Add($environmentValue)
            }
        }
        $removeEnvironmentNames = @($removeNames.ToArray() | Sort-Object -Unique)
    }
    if ($DryRun) {
        $plannedParameters = @{
            FilePath = $FilePath; ArgumentList = $Arguments; WorkingDirectory = $WorkingDirectory
            TimeoutSeconds = $TimeoutSeconds; Kind = $Kind; DryRun = $true
        }
        if ($Kind -ceq 'Unity') { $plannedParameters.RemoveEnvironmentVariables = $removeEnvironmentNames }
        $planned = Invoke-SashimiHostProcess @plannedParameters
        $planned | Add-Member -NotePropertyName Crashed -NotePropertyValue $false -Force
        return $planned
    }

    $fixtureEntry = Get-SashimiFixtureEntry -Fixture $Fixture -Group $FixtureGroup -Name $Name
    if ($null -ne $Fixture) {
        $timedOut = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'TimedOut' -DefaultValue $false)
        $crashed = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'Crashed' -DefaultValue $false)
        $defaultExit = if ($timedOut) { 124 } elseif ($crashed) { 139 } else { 0 }
        $exitCode = [int](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'ExitCode' -DefaultValue $defaultExit)
        $createLog = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'CreateLog' -DefaultValue $true)
        $createXml = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'CreateXml' -DefaultValue $true)
        if (-not [string]::IsNullOrWhiteSpace($LogPath) -and $createLog) {
            $defaultLog = if ($FixtureGroup -eq 'Stages' -and $Name -in @('EditMode', 'PlayMode')) {
                "Running tests for ExecutionSettings with details:`nTest run completed. Exiting with code $exitCode`n"
            }
            else { "Fixture stage $Name completed with exit code $exitCode.`n" }
            $logContent = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'LogContent' -DefaultValue $defaultLog)
            Write-SashimiUtf8File -Path $LogPath -Content $logContent
        }
        if (-not [string]::IsNullOrWhiteSpace($XmlPath) -and $createXml) {
            $defaultXml = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" />'
            $xmlContent = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'XmlContent' -DefaultValue $defaultXml)
            Write-SashimiUtf8File -Path $XmlPath -Content $xmlContent
        }
        $terminationConfirmed = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'TerminationConfirmed' -DefaultValue $true)
        return [pscustomobject][ordered]@{
            FilePath = $FilePath
            Arguments = @($Arguments)
            Command = Format-SashimiCommand -FilePath $FilePath -ArgumentList $Arguments
            ExitCode = $exitCode
            StdOut = Protect-SashimiText ([string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'StdOut' -DefaultValue ''))
            StdErr = Protect-SashimiText ([string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'StdErr' -DefaultValue ''))
            Succeeded = ($exitCode -eq 0 -and -not $timedOut -and -not $crashed -and $terminationConfirmed)
            TimedOut = $timedOut
            Crashed = $crashed
            TerminationConfirmed = $terminationConfirmed
            ProcessId = Get-SashimiPropertyValue -Object $fixtureEntry -Name 'ProcessId' -DefaultValue $null
            DurationMilliseconds = [int64](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'DurationMilliseconds' -DefaultValue 0)
            DryRun = $false
            Fixture = $true
        }
    }

    $processParameters = @{
        FilePath = $FilePath; ArgumentList = $Arguments; WorkingDirectory = $WorkingDirectory
        TimeoutSeconds = $TimeoutSeconds; Kind = $Kind
    }
    if ($Kind -ceq 'Unity') {
        $processParameters.RemoveEnvironmentVariables = $removeEnvironmentNames
        if (-not [string]::IsNullOrWhiteSpace($OwnedUnityPidPath)) {
            $processParameters.OwnedProcessRecordPath = $OwnedUnityPidPath
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($CancellationMarkerPath)) {
        $processParameters.CancellationMarkerPath = $CancellationMarkerPath
    }
    $invocation = Invoke-SashimiHostProcess @processParameters
    $invocation | Add-Member -NotePropertyName Crashed -NotePropertyValue $false -Force
    return $invocation
}

function Assert-SashimiValidationNotCancelled {
    [CmdletBinding()]
    param()

    if (-not $DryRun -and -not [string]::IsNullOrWhiteSpace($CancellationMarkerPath) -and
        (Test-Path -LiteralPath $CancellationMarkerPath -PathType Leaf)) {
        throw 'Unity/repository validation was cancelled.'
    }
}

function Protect-SashimiUnityOutputText {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $protected = Protect-SashimiValidationMetadataText $Text
    $sensitiveValues = @($script:unitySensitiveEnvironmentValues | Sort-Object { $_.Length } -Descending)
    foreach ($sensitiveValue in $sensitiveValues) {
        if ([string]::IsNullOrEmpty($sensitiveValue)) { continue }
        $forms = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        [void]$forms.Add($sensitiveValue)
        [void]$forms.Add($sensitiveValue.Replace('\', '\\'))
        [void]$forms.Add($sensitiveValue.Replace('\', '/'))
        foreach ($form in $forms) {
            if (-not [string]::IsNullOrEmpty($form)) {
                $protected = $protected.Replace($form, '[REDACTED_SECRET]', [StringComparison]::Ordinal)
            }
        }
    }
    if (Test-SashimiRecognizableSensitiveText -Text $protected -SensitiveValues $sensitiveValues) {
        throw 'Unity output still contains recognizable sensitive, credential, save, or profile content after sanitization.'
    }
    return $protected
}

function Publish-SashimiSanitizedTextArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { return $false }
    if (Test-Path -LiteralPath $DestinationPath) { throw "Refusing to overwrite a Unity validation artifact: $DestinationPath" }
    Assert-SashimiNoReparsePoint -Path $SourcePath
    Assert-SashimiNoReparsePoint -Path $DestinationPath
    try {
        $rawText = [IO.File]::ReadAllText($SourcePath, [Text.UTF8Encoding]::new($false, $true))
    }
    catch {
        throw 'Raw Unity output could not be read as strict UTF-8 text.'
    }
    $sanitizedText = Protect-SashimiUnityOutputText -Text $rawText
    $destinationParent = Split-Path -Parent $DestinationPath
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
        [IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    }
    Assert-SashimiNoReparsePoint -Path $destinationParent
    $temporaryPath = Join-Path $destinationParent ('.sanitized-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporaryPath, $sanitizedText, [Text.UTF8Encoding]::new($false))
        $verificationText = [IO.File]::ReadAllText($temporaryPath, [Text.UTF8Encoding]::new($false, $true))
        if (-not [string]::Equals($sanitizedText, $verificationText, [StringComparison]::Ordinal) -or
            (Test-SashimiRecognizableSensitiveText -Text $verificationText -SensitiveValues @($script:unitySensitiveEnvironmentValues))) {
            throw 'Sanitized Unity artifact failed its final content verification.'
        }
        [IO.File]::Move($temporaryPath, $DestinationPath, $false)
        $temporaryPath = ''
        try { [IO.File]::Delete($SourcePath) } catch { }
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
            try { [IO.File]::Delete($temporaryPath) } catch { }
        }
    }
    return $true
}

function Get-SashimiUnityLogDiagnostics {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [switch]$Compile
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { return @() }
    $content = [IO.File]::ReadAllText($LogPath, [Text.Encoding]::UTF8)
    $signatures = [ordered]@{
        CompilerError = '(?im)(?:^|\s)error\s+CS\d{4}\s*:'
        CompilationFailure = '(?im)Scripts? (?:had|have) compilation errors|Compilation failed'
        MissingScript = '(?i)Missing Script|The referenced script .* is missing|The associated script cannot be loaded'
        DuplicateAudioListener = '(?i)There are\s+2\s+audio listeners|multiple\s+AudioListener'
        DuplicateEventSystem = '(?i)multiple\s+(?:active\s+)?EventSystem|more than one EventSystem'
        BatchModeAbort = '(?im)^\s*Aborting batchmode due to failure'
        UnityCrash = '(?im)^\s*(?:Fatal error\b|Unity has crashed\b|Crash!!!|Receiving unhandled NULL exception\b)'
        UnhandledException = '(?im)^\s*Unhandled Exception\s*:'
    }
    if ($Compile) {
        $signatures.MissingReference = '(?i)MissingReferenceException'
        $signatures.NullReference = '(?i)NullReferenceException'
        $signatures.ConsoleError = '(?im)^\s*Error\s*:'
        $signatures.ConsoleLogError = '(?im)^\s*UnityEngine\.Debug:(?:LogError|LogException|LogAssertion)\b'
        $signatures.ManagedException = '(?im)^\s*(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception\s*:'
        $signatures.AssertionFailure = '(?im)^\s*(?:Assertion failed|AssertionException\b|UnityEngine\.Assertions\.AssertionException)'
    }

    $diagnostics = [Collections.Generic.List[object]]::new()
    foreach ($entry in $signatures.GetEnumerator()) {
        $matches = [regex]::Matches($content, [string]$entry.Value)
        if ($matches.Count -gt 0) {
            $diagnostics.Add([pscustomobject][ordered]@{
                    Category = [string]$entry.Key
                    Count = $matches.Count
                    Samples = @($matches | Select-Object -First 5 | ForEach-Object { Protect-SashimiText $_.Value })
                })
        }
    }

    if (-not $Compile) {
        $start = [regex]::Match($content, '(?im)^Running tests for ExecutionSettings with details:\s*$')
        $end = [regex]::Match($content, '(?im)^Test run completed\. Exiting with code \d+.*$')
        $outside = $content
        if ($start.Success -and $end.Success -and $end.Index -ge $start.Index) {
            $outside = $content.Substring(0, $start.Index)
            $after = $end.Index + $end.Length
            if ($after -lt $content.Length) { $outside += $content.Substring($after) }
        }
        foreach ($entry in ([ordered]@{
                    OutOfRunConsoleError = '(?im)^\s*Error\s*:'
                    OutOfRunLogError = '(?im)^\s*UnityEngine\.Debug:(?:LogError|LogException|LogAssertion)\b'
                    OutOfRunMissingReference = '(?im)^\s*(?:[A-Za-z_]\w*\.)*MissingReferenceException\s*:'
                    OutOfRunNullReference = '(?im)^\s*(?:[A-Za-z_]\w*\.)*NullReferenceException\s*:'
                    OutOfRunManagedException = '(?im)^\s*(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception\s*:'
                }).GetEnumerator()) {
            $matches = [regex]::Matches($outside, [string]$entry.Value)
            if ($matches.Count -gt 0) {
                $diagnostics.Add([pscustomobject][ordered]@{
                        Category = [string]$entry.Key
                        Count = $matches.Count
                        Samples = @($matches | Select-Object -First 5 | ForEach-Object { Protect-SashimiText $_.Value })
                    })
            }
        }
    }
    return $diagnostics.ToArray()
}

function Invoke-SashimiUnityValidationStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$RawLogPath,
        [string]$XmlPath,
        [string]$RawXmlPath,
        [Parameter(Mandatory = $true)][string]$UnityExecutable,
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [AllowNull()][object]$Fixture,
        [switch]$Compile,
        [switch]$TestStage,
        [switch]$DryRun
    )

    $native = Invoke-SashimiValidationProcess -Name $Name -Kind Unity -FilePath $UnityExecutable -Arguments $Arguments -WorkingDirectory $ProjectRoot -TimeoutSeconds $TimeoutSeconds -Fixture $Fixture -FixtureGroup 'Stages' -LogPath $RawLogPath -XmlPath $RawXmlPath -DryRun:$DryRun
    if ($null -ne $native.ProcessId) { $ownedUnityProcessIds.Add([int]$native.ProcessId) }

    $stage = [ordered]@{
        Name = $Name
        Planned = [bool]$DryRun
        Success = $false
        NativeExitCode = [int]$native.ExitCode
        NativeSucceeded = [bool]$native.Succeeded
        TimedOut = [bool]$native.TimedOut
        Crashed = [bool]$native.Crashed
        ProcessId = $native.ProcessId
        DurationMilliseconds = [int64]$native.DurationMilliseconds
        LogPath = $LogPath
        LogExists = $false
        XmlPath = if ($TestStage) { $XmlPath } else { $null }
        XmlExists = $false
        XmlSummary = $null
        NativeXmlAgreement = if ($TestStage) { $false } else { $null }
        Diagnostics = @()
        StdOut = Protect-SashimiUnityOutputText -Text ([string]$native.StdOut)
        StdErr = Protect-SashimiUnityOutputText -Text ([string]$native.StdErr)
    }
    if ($DryRun) {
        $stage.Success = $true
        return [pscustomobject]$stage
    }

    $terminationConfirmed = [bool](Get-SashimiPropertyValue -Object $native -Name 'TerminationConfirmed' -DefaultValue $true)
    if ($terminationConfirmed) {
        [void](Publish-SashimiSanitizedTextArtifact -SourcePath $RawLogPath -DestinationPath $LogPath)
        if ($TestStage -and -not [string]::IsNullOrWhiteSpace($RawXmlPath)) {
            [void](Publish-SashimiSanitizedTextArtifact -SourcePath $RawXmlPath -DestinationPath $XmlPath)
        }
    }
    else {
        $script:rawValidationCleanupSafe = $false
        Add-SashimiValidationFailure -Code 'UnityTerminationUnconfirmed' -Stage $Name -Message 'Unity process termination was not confirmed; raw state was preserved outside Artifacts.' -NativeExitCode $native.ExitCode
    }

    if ([bool]$native.TimedOut) {
        Add-SashimiValidationFailure -Code 'UnityTimeout' -Stage $Name -Message "Unity stage timed out after $TimeoutSeconds seconds." -NativeExitCode $native.ExitCode
    }
    if ([bool]$native.Crashed) {
        Add-SashimiValidationFailure -Code 'UnityCrash' -Stage $Name -Message 'Unity stage reported a crash.' -NativeExitCode $native.ExitCode
    }
    if (-not [bool]$native.Succeeded) {
        Add-SashimiValidationFailure -Code 'UnityNativeFailure' -Stage $Name -Message "Unity exited with code $($native.ExitCode): $($stage.StdErr)" -NativeExitCode $native.ExitCode
    }

    $stage.LogExists = Test-Path -LiteralPath $LogPath -PathType Leaf
    if (-not $stage.LogExists) {
        Add-SashimiValidationFailure -Code 'UnityLogMissing' -Stage $Name -Message "Unity log is missing: $LogPath" -NativeExitCode $native.ExitCode
    }
    else {
        $stage.Diagnostics = @(Get-SashimiUnityLogDiagnostics -LogPath $LogPath -Compile:$Compile)
        $crashDiagnostics = @($stage.Diagnostics | Where-Object { $_.Category -eq 'UnityCrash' })
        if ($crashDiagnostics.Count -gt 0 -and -not $stage.Crashed) {
            $stage.Crashed = $true
            Add-SashimiValidationFailure -Code 'UnityCrash' -Stage $Name -Message 'Unity crash signature was found in the stage log.' -NativeExitCode $native.ExitCode
        }
        if ($stage.Diagnostics.Count -gt 0) {
            Add-SashimiValidationFailure -Code 'UnityLogDiagnostics' -Stage $Name -Message "Forbidden Unity diagnostics were found in $LogPath." -NativeExitCode $native.ExitCode
        }
    }

    if ($TestStage) {
        $stage.XmlExists = Test-Path -LiteralPath $XmlPath -PathType Leaf
        $xmlPass = $false
        if (-not $stage.XmlExists) {
            Add-SashimiValidationFailure -Code 'UnityResultXmlMissing' -Stage $Name -Message "Unity result XML is missing: $XmlPath" -NativeExitCode $native.ExitCode
        }
        else {
            try {
                $stage.XmlSummary = Get-SashimiNUnitSummary -Path $XmlPath
                $xmlPass = [bool]$stage.XmlSummary.StrictPass
                if (-not $xmlPass) {
                    Add-SashimiValidationFailure -Code 'UnityResultXmlFailed' -Stage $Name -Message "Unity XML is not a strict PASS: result=$($stage.XmlSummary.Result), total=$($stage.XmlSummary.Total), passed=$($stage.XmlSummary.Passed), failed=$($stage.XmlSummary.Failed), skipped=$($stage.XmlSummary.Skipped), inconclusive=$($stage.XmlSummary.Inconclusive)." -NativeExitCode $native.ExitCode
                }
            }
            catch {
                Add-SashimiValidationFailure -Code 'UnityResultXmlInvalid' -Stage $Name -Message $_.Exception.Message -NativeExitCode $native.ExitCode
            }
        }
        $nativePass = [bool]$native.Succeeded -and -not [bool]$stage.Crashed -and [bool]$stage.LogExists
        $stage.NativeXmlAgreement = ($nativePass -eq $xmlPass)
        if (-not $stage.NativeXmlAgreement) {
            Add-SashimiValidationFailure -Code 'UnityNativeXmlDisagreement' -Stage $Name -Message "Unity native result and strict XML disagree: nativePass=$nativePass; xmlPass=$xmlPass." -NativeExitCode $native.ExitCode
        }
        $stage.Success = ($nativePass -and $xmlPass -and $stage.Diagnostics.Count -eq 0)
    }
    else {
        $stage.Success = ([bool]$native.Succeeded -and -not [bool]$stage.Crashed -and [bool]$stage.LogExists -and $stage.Diagnostics.Count -eq 0)
    }
    return [pscustomobject]$stage
}

function Get-SashimiIssueValidationDefinition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Config,
        [AllowEmptyString()][string]$ValidationId,
        [Parameter(Mandatory = $true)][int]$SelectedIssueNumber
    )

    $definitions = Get-SashimiPropertyValue -Object $Config -Name 'IssueValidations' -DefaultValue $null
    if ($null -eq $definitions) { throw 'Config IssueValidations is missing.' }
    if ([string]::IsNullOrWhiteSpace($ValidationId)) {
        $matches = @($definitions.PSObject.Properties | Where-Object {
            $candidate = $_.Value
            $boundIssue = [int](Get-SashimiPropertyValue -Object $candidate -Name 'IssueNumber' -DefaultValue 0)
            $allowedIssuesForCandidate = @((Get-SashimiPropertyValue -Object $candidate -Name 'AllowedIssueNumbers' -DefaultValue @()) | ForEach-Object { [int]$_ })
            $boundIssue -eq $SelectedIssueNumber -or $allowedIssuesForCandidate -contains $SelectedIssueNumber
        })
        if ($matches.Count -eq 0) { return $null }
        if ($matches.Count -ne 1) { throw "Multiple issue-validation definitions match Issue #$SelectedIssueNumber; selection is ambiguous." }
        $ValidationId = [string]$matches[0].Name
    }
    if ($ValidationId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
        throw "IssueValidationId has an invalid format: $ValidationId"
    }
    $definitionProperty = $definitions.PSObject.Properties[$ValidationId]
    if ($null -eq $definitionProperty) {
        throw "IssueValidationId '$ValidationId' is not allowlisted in Config.IssueValidations."
    }
    $definition = $definitionProperty.Value
    $configuredIssue = [int](Get-SashimiPropertyValue -Object $definition -Name 'IssueNumber' -DefaultValue 0)
    if ($configuredIssue -gt 0 -and $configuredIssue -ne $SelectedIssueNumber) {
        throw "Issue validation '$ValidationId' is bound to Issue #$configuredIssue, not Issue #$SelectedIssueNumber."
    }
    $allowedIssues = @((Get-SashimiPropertyValue -Object $definition -Name 'AllowedIssueNumbers' -DefaultValue @()) | ForEach-Object { [int]$_ })
    if ($allowedIssues.Count -gt 0 -and $allowedIssues -notcontains $SelectedIssueNumber) {
        throw "Issue validation '$ValidationId' is not allowlisted for Issue #$SelectedIssueNumber."
    }

    $method = [string](Get-SashimiPropertyValue -Object $definition -Name 'UnityExecuteMethod' -DefaultValue '')
    if ($method -notmatch '^[A-Za-z_][A-Za-z0-9_.+]*$') {
        throw "Issue validation '$ValidationId' has an invalid UnityExecuteMethod."
    }
    $arguments = @((Get-SashimiPropertyValue -Object $definition -Name 'Arguments' -DefaultValue @()) | ForEach-Object { [string]$_ })
    $reservedArgumentPattern = '^--?(?:projectPath|logFile|executeMethod|runTests|testPlatform|testResults|batchmode|quit)(?:$|[=:\s])'
    foreach ($argument in $arguments) {
        if ($argument.IndexOfAny([char[]]@([char]0, [char]13, [char]10)) -ge 0 -or $argument.Length -gt 4096) {
            throw "Issue validation '$ValidationId' contains an unsafe generator argument."
        }
        if ([regex]::IsMatch(
                $argument,
                $reservedArgumentPattern,
                [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            throw "Issue validation '$ValidationId' may not override reserved Unity argument '$argument'."
        }
    }
    $determinismPaths = @((Get-SashimiPropertyValue -Object $definition -Name 'DeterminismPaths' -DefaultValue @()) | ForEach-Object { [string]$_ })
    if ($determinismPaths.Count -eq 0) {
        throw "Issue validation '$ValidationId' must declare at least one DeterminismPaths entry."
    }

    return [pscustomobject][ordered]@{
        Id = $ValidationId
        UnityExecuteMethod = $method
        Arguments = $arguments
        DeterminismPaths = $determinismPaths
        ScreenshotPaths = @((Get-SashimiPropertyValue -Object $definition -Name 'ScreenshotPaths' -DefaultValue @()) | ForEach-Object { [string]$_ })
        PreviewPaths = @((Get-SashimiPropertyValue -Object $definition -Name 'PreviewPaths' -DefaultValue @()) | ForEach-Object { [string]$_ })
        AllowedProtectedPathPatterns = @((Get-SashimiPropertyValue -Object $definition -Name 'AllowedProtectedPathPatterns' -DefaultValue @()) | ForEach-Object { [string]$_ })
    }
}

function Get-SashimiDeterminismSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string[]]$RelativePaths
    )

    $entries = [Collections.Generic.List[object]]::new()
    foreach ($configuredPath in $RelativePaths) {
        Assert-SashimiValidationNotCancelled
        $relativePath = ConvertTo-SashimiProjectRelativePath -Value $configuredPath -NormalizedProjectPath $ProjectRoot
        $fullPath = Join-Path $ProjectRoot ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        Assert-SashimiNoReparsePoint -Path $fullPath
        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "Determinism path was not generated: $relativePath"
        }
        $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Determinism paths may not contain reparse points: $relativePath"
        }
        if (-not $item.PSIsContainer) {
            $entries.Add([pscustomobject][ordered]@{
                    Path = $relativePath
                    Kind = 'File'
                    Length = [int64]$item.Length
                    Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                })
            continue
        }

        $files = @(Get-ChildItem -LiteralPath $item.FullName -File -Force -Recurse -ErrorAction Stop | Sort-Object FullName)
        $reparseEntries = @(Get-ChildItem -LiteralPath $item.FullName -Force -Recurse -ErrorAction Stop | Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            })
        if ($reparseEntries.Count -gt 0) {
            throw "Determinism tree contains a reparse point: $($reparseEntries[0].FullName)"
        }
        if ($files.Count -eq 0) {
            $entries.Add([pscustomobject][ordered]@{ Path = $relativePath; Kind = 'EmptyDirectory'; Length = 0; Sha256 = '' })
        }
        foreach ($file in $files) {
            Assert-SashimiValidationNotCancelled
            $fileRelative = [IO.Path]::GetRelativePath($ProjectRoot, $file.FullName).Replace('\', '/')
            $entries.Add([pscustomobject][ordered]@{
                    Path = $fileRelative
                    Kind = 'File'
                    Length = [int64]$file.Length
                    Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                })
        }
    }
    return @($entries.ToArray() | Sort-Object Path, Kind)
}

function ConvertTo-SashimiGitPathList {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return @() }
    $parts = if ($Text.Contains([char]0)) { @($Text -split "`0") } else { @($Text -split "`r?`n") }
    return @($parts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/').Trim() })
}

function Get-SashimiMetaGuidIntegrity {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $assetsRoot = Join-Path $ProjectRoot 'Assets'
    $missingMeta = [Collections.Generic.List[string]]::new()
    $orphanMeta = [Collections.Generic.List[string]]::new()
    $invalidMeta = [Collections.Generic.List[string]]::new()
    $duplicateGuids = [Collections.Generic.List[object]]::new()
    $missingReferences = [Collections.Generic.List[object]]::new()
    $missingScripts = [Collections.Generic.List[string]]::new()
    $guidOwners = @{}
    $knownGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    if (-not (Test-Path -LiteralPath $assetsRoot -PathType Container)) {
        throw "Unity Assets directory is missing: $assetsRoot"
    }
    Assert-SashimiValidationNotCancelled
    $assetEntries = @(Get-ChildItem -LiteralPath $assetsRoot -Force -Recurse -ErrorAction Stop)
    foreach ($entry in $assetEntries) {
        Assert-SashimiValidationNotCancelled
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Assets contains a forbidden reparse point: $($entry.FullName)"
        }
        $relative = [IO.Path]::GetRelativePath($ProjectRoot, $entry.FullName).Replace('\', '/')
        if (-not $entry.Name.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) {
            if (-not (Test-Path -LiteralPath ($entry.FullName + '.meta') -PathType Leaf)) { $missingMeta.Add($relative) }
            continue
        }

        $targetPath = $entry.FullName.Substring(0, $entry.FullName.Length - 5)
        if (-not (Test-Path -LiteralPath $targetPath)) { $orphanMeta.Add($relative) }
        $metaText = [IO.File]::ReadAllText($entry.FullName, [Text.Encoding]::UTF8)
        $guidMatches = [regex]::Matches($metaText, '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$')
        if ($guidMatches.Count -ne 1) {
            $invalidMeta.Add($relative)
            continue
        }
        $guid = $guidMatches[0].Groups[1].Value.ToLowerInvariant()
        [void]$knownGuids.Add($guid)
        if (-not $guidOwners.ContainsKey($guid)) { $guidOwners[$guid] = [Collections.Generic.List[string]]::new() }
        $guidOwners[$guid].Add($relative)
    }
    foreach ($guid in $guidOwners.Keys) {
        Assert-SashimiValidationNotCancelled
        if ($guidOwners[$guid].Count -gt 1) {
            $duplicateGuids.Add([pscustomobject]@{ Guid = $guid; Paths = $guidOwners[$guid].ToArray() })
        }
    }

    foreach ($additionalRoot in @((Join-Path $ProjectRoot 'Packages'), (Join-Path $ProjectRoot 'Library\PackageCache'))) {
        Assert-SashimiValidationNotCancelled
        if (-not (Test-Path -LiteralPath $additionalRoot -PathType Container)) { continue }
        foreach ($meta in @(Get-ChildItem -LiteralPath $additionalRoot -File -Filter '*.meta' -Force -Recurse -ErrorAction SilentlyContinue)) {
            Assert-SashimiValidationNotCancelled
            $match = [regex]::Match([IO.File]::ReadAllText($meta.FullName, [Text.Encoding]::UTF8), '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$')
            if ($match.Success) { [void]$knownGuids.Add($match.Groups[1].Value) }
        }
    }

    $serializedExtensions = @('.unity', '.prefab', '.asset', '.controller', '.anim', '.overridecontroller', '.mat', '.playable', '.mask', '.guiskin')
    foreach ($file in @($assetEntries | Where-Object { -not $_.PSIsContainer -and $serializedExtensions -contains $_.Extension.ToLowerInvariant() })) {
        Assert-SashimiValidationNotCancelled
        try { $content = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) } catch { continue }
        if ($content.IndexOf('guid:', [StringComparison]::Ordinal) -lt 0 -and $content.IndexOf('m_Script:', [StringComparison]::Ordinal) -lt 0) { continue }
        $relative = [IO.Path]::GetRelativePath($ProjectRoot, $file.FullName).Replace('\', '/')
        if ([regex]::IsMatch($content, '(?m)^\s*m_Script:\s*\{\s*fileID:\s*0(?:\s*[,}])')) {
            $missingScripts.Add($relative)
        }
        foreach ($reference in [regex]::Matches($content, 'guid:\s*([0-9a-fA-F]{32})')) {
            $guid = $reference.Groups[1].Value.ToLowerInvariant()
            if ($guid -eq ('0' * 32) -or $guid -match '^0{16}[ef]0{15}$' -or $knownGuids.Contains($guid)) { continue }
            $missingReferences.Add([pscustomobject]@{ Path = $relative; Guid = $guid })
        }
    }

    $uniqueMissingReferences = @($missingReferences.ToArray() | Sort-Object Path, Guid -Unique)
    return [pscustomobject][ordered]@{
        Passed = ($missingMeta.Count -eq 0 -and $orphanMeta.Count -eq 0 -and $invalidMeta.Count -eq 0 -and $duplicateGuids.Count -eq 0 -and $missingScripts.Count -eq 0 -and $uniqueMissingReferences.Count -eq 0)
        MetaFiles = $guidOwners.Count
        MissingMetaCount = $missingMeta.Count
        MissingMeta = @($missingMeta | Select-Object -First 100)
        OrphanMetaCount = $orphanMeta.Count
        OrphanMeta = @($orphanMeta | Select-Object -First 100)
        InvalidMetaCount = $invalidMeta.Count
        InvalidMeta = @($invalidMeta | Select-Object -First 100)
        DuplicateGuidCount = $duplicateGuids.Count
        DuplicateGuids = @($duplicateGuids | Select-Object -First 100)
        MissingScriptCount = $missingScripts.Count
        MissingScripts = @($missingScripts | Sort-Object -Unique | Select-Object -First 100)
        MissingReferenceCount = $uniqueMissingReferences.Count
        MissingReferences = @($uniqueMissingReferences | Select-Object -First 100)
    }
}

function Get-SashimiWorkingTreePointers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string[]]$TrackedPaths
    )

    $pointers = [Collections.Generic.List[string]]::new()
    $header = 'version https://git-lfs.github.com/spec/v1'
    foreach ($relativeInput in $TrackedPaths) {
        Assert-SashimiValidationNotCancelled
        try { $relative = ConvertTo-SashimiProjectRelativePath -Value $relativeInput -NormalizedProjectPath $ProjectRoot } catch { continue }
        $path = Join-Path $ProjectRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $stream = $null
        try {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $length = [Math]::Min(256, [int]$stream.Length)
            if ($length -eq 0) { continue }
            $bytes = [byte[]]::new($length)
            [void]$stream.Read($bytes, 0, $length)
            $prefix = [Text.Encoding]::ASCII.GetString($bytes)
            if ($prefix.StartsWith($header, [StringComparison]::Ordinal)) { $pointers.Add($relative) }
        }
        finally {
            if ($null -ne $stream) { $stream.Dispose() }
        }
    }
    return $pointers.ToArray()
}

function Test-SashimiKnownUnityDefaultDrift {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$DiffText)

    if ([string]::IsNullOrWhiteSpace($DiffText)) { return $false }
    $expected = @(
        '-  targetPixelDensity: 0', '+  targetPixelDensity: 30',
        '-  buildNumber: {}', '+  buildNumber:', '+    Standalone: 0', '+    VisionOS: 0', '+    iPhone: 0', '+    tvOS: 0',
        '-  iOSTargetOSVersionString: ', '+  iOSTargetOSVersionString: 15.0',
        '-  tvOSTargetOSVersionString: ', '+  tvOSTargetOSVersionString: 15.0',
        '-  VisionOSTargetOSVersionString: ', '+  VisionOSTargetOSVersionString: 1.0',
        '-  macOSTargetOSVersion: ', '+  macOSTargetOSVersion: 12.0'
    )
    $actual = @($DiffText -split "`r?`n" | Where-Object {
            (($_.StartsWith('+') -and -not $_.StartsWith('+++')) -or ($_.StartsWith('-') -and -not $_.StartsWith('---')))
        })
    if ($actual.Count -ne $expected.Count) { return $false }
    for ($index = 0; $index -lt $expected.Count; $index++) {
        if (-not [string]::Equals($actual[$index], $expected[$index], [StringComparison]::Ordinal)) { return $false }
    }
    return ($DiffText -notmatch '(?m)^\\ No newline at end of file\r?$')
}

function Copy-SashimiValidationArtifactHooks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Screenshot', 'Preview')][string]$Kind,
        [AllowEmptyCollection()][string[]]$Paths,
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [AllowEmptyCollection()][string[]]$ExclusionPatterns = @()
    )

    $copied = [Collections.Generic.List[object]]::new()
    foreach ($configuredPath in @($Paths)) {
        Assert-SashimiValidationNotCancelled
        $relative = ConvertTo-SashimiProjectRelativePath -Value $configuredPath -NormalizedProjectPath $ProjectRoot
        if (Test-SashimiRelativePathPattern -Path $relative -Patterns $ExclusionPatterns) {
            throw "$Kind artifact hook targets an excluded path: $relative"
        }
        $source = Join-Path $ProjectRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        Assert-SashimiNoReparsePoint -Path $source
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "$Kind artifact hook file is missing: $relative"
        }
        $sourceInfo = Get-Item -LiteralPath $source -Force -ErrorAction Stop
        if ($sourceInfo.Length -lt 1 -or $sourceInfo.Length -gt 25MB) {
            throw "$Kind artifact hook PNG must be between 1 byte and 25 MiB: $relative"
        }
        if ([IO.Path]::GetExtension($source).ToLowerInvariant() -cne '.png') {
            throw "$Kind artifact hooks accept only PNG images: $relative"
        }
        $destination = Join-Path (Join-Path $ArtifactRoot ($Kind + 's')) ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        Assert-SashimiNoReparsePoint -Path $destination
        $destinationParent = Split-Path -Parent $destination
        [IO.Directory]::CreateDirectory($destinationParent) | Out-Null
        $image = $null
        $bitmap = $null
        $graphics = $null
        try {
            Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop
            $image = [Drawing.Image]::FromFile($source, $false)
            if ($image.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or
                $image.Width -lt 1 -or $image.Height -lt 1 -or
                $image.Width -gt 16384 -or $image.Height -gt 16384 -or
                ([int64]$image.Width * [int64]$image.Height) -gt 67108864) {
                throw 'The file is not a bounded, decodable PNG image.'
            }
            # Re-render pixels into a new bitmap. This intentionally drops PNG
            # text/profile/EXIF chunks instead of copying arbitrary source bytes
            # into a retained run artifact.
            $bitmap = [Drawing.Bitmap]::new($image.Width, $image.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.DrawImageUnscaled($image, 0, 0)
            $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
        }
        catch {
            throw "$Kind artifact hook could not sanitize '$relative': $($_.Exception.Message)"
        }
        finally {
            if ($null -ne $graphics) { $graphics.Dispose() }
            if ($null -ne $bitmap) { $bitmap.Dispose() }
            if ($null -ne $image) { $image.Dispose() }
        }
        $copied.Add([pscustomobject][ordered]@{
                Kind = $Kind
                SourceRelativePath = $relative
                ArtifactPath = $destination
                Sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
            })
    }
    return $copied.ToArray()
}

function Protect-SashimiValidationData {
    [CmdletBinding()]
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) { return Protect-SashimiValidationMetadataText $Value }
    if ($Value -is [Collections.IDictionary]) {
        $protectedDictionary = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $protectedDictionary[[string]$key] = Protect-SashimiValidationData -Value $Value[$key]
        }
        return [pscustomobject]$protectedDictionary
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        return @($Value | ForEach-Object { Protect-SashimiValidationData -Value $_ })
    }
    if ($Value -is [pscustomobject]) {
        $protectedObject = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $protectedObject[$property.Name] = Protect-SashimiValidationData -Value $property.Value
        }
        return [pscustomobject]$protectedObject
    }
    return $Value
}

$result = [ordered]@{
    SchemaVersion = 1
    Tool = 'Invoke-SashimiUnityValidation'
    Success = $false
    Succeeded = $false
    ExitCode = 1
    DryRun = [bool]$DryRun
    IssueNumber = $IssueNumber
    IssueValidationId = if ([string]::IsNullOrWhiteSpace($IssueValidationId)) { $null } else { $IssueValidationId }
    BaselineRef = $BaselineRef
    ProjectPath = $ProjectPath
    ArtifactsPath = $ArtifactsPath
    SummaryPath = $null
    SummaryWritten = $false
    ExpectedUnityVersion = $null
    DetectedUnityVersion = $null
    BuildTarget = 'StandaloneWindows64'
    ExistingUnityProcessIds = @()
    OwnedUnityProcessIds = @()
    Stages = $stages
    Determinism = [ordered]@{
        Required = $false
        Paths = @()
        Run1Snapshot = @()
        Run2Snapshot = @()
        Passed = $null
    }
    ChangedPaths = @()
    PreUnityChangedPaths = @()
    PreUnityProtectedChanges = @()
    PreUnityBlockedProtectedChanges = @()
    ProtectedChanges = @()
    BlockedProtectedChanges = @()
    KnownUnityDefaultDrift = [ordered]@{
        Detected = $false
        Allowed = $false
        DiffArtifactPath = $null
        DiffSha256 = $null
    }
    Lfs = $null
    Integrity = $null
    ArtifactHooks = @()
    Commands = @()
    Checks = @()
    Failures = @()
}

$fixture = $null
$config = $null
$validationDefinition = $null
$exitCode = 1

try {
    Assert-SashimiSafeBaselineRef -Value $BaselineRef
    $config = Import-SashimiHostConfig -ConfigPath $ConfigPath
    $result.ExpectedUnityVersion = [string](Get-SashimiPropertyValue -Object $config -Name 'ExpectedUnityVersion' -DefaultValue '')
    if ([string]::IsNullOrWhiteSpace($result.ExpectedUnityVersion)) {
        throw 'Config ExpectedUnityVersion is required.'
    }
    $unityExecutable = [string](Get-SashimiPropertyValue -Object $config -Name 'UnityExecutable' -DefaultValue '')
    $gitExecutable = [string](Get-SashimiPropertyValue -Object $config -Name 'GitExecutable' -DefaultValue '')
    $gitLfsExecutable = [string](Get-SashimiPropertyValue -Object $config -Name 'GitLfsExecutable' -DefaultValue '')
    if ([string]::IsNullOrWhiteSpace($unityExecutable) -or [string]::IsNullOrWhiteSpace($gitExecutable) -or [string]::IsNullOrWhiteSpace($gitLfsExecutable)) {
        throw 'Config UnityExecutable, GitExecutable, and GitLfsExecutable are required.'
    }
    $unityTimeout = [int](Get-SashimiPropertyValue -Object $config.Timeouts -Name 'UnityStageSeconds' -DefaultValue 0)
    $generatorTimeout = [int](Get-SashimiPropertyValue -Object $config.Timeouts -Name 'GeneratorSeconds' -DefaultValue 0)
    $gitTimeout = [int](Get-SashimiPropertyValue -Object $config.Timeouts -Name 'GitSeconds' -DefaultValue 600)
    foreach ($timeout in @($unityTimeout, $generatorTimeout, $gitTimeout)) {
        if ($timeout -lt 1 -or $timeout -gt 86400) { throw 'Unity/Generator/Git timeout values must be between 1 and 86400 seconds.' }
    }

    if (-not [string]::IsNullOrWhiteSpace($ValidationFixturePath)) {
        $fixturePath = Assert-SashimiFixtureAllowed -FixturePath $ValidationFixturePath -DryRun:$DryRun
        $fixture = Read-SashimiJsonFile -Path $fixturePath
        $fixtureSchema = [int](Get-SashimiPropertyValue -Object $fixture -Name 'SchemaVersion' -DefaultValue 1)
        if ($fixtureSchema -ne 1) { throw "Validation fixture SchemaVersion must be 1; received $fixtureSchema." }
    }
    $validationDefinition = Get-SashimiIssueValidationDefinition -Config $config -ValidationId $IssueValidationId -SelectedIssueNumber $IssueNumber
    if ($null -ne $validationDefinition) { $result.IssueValidationId = [string]$validationDefinition.Id }

    $allowMissingPaths = [bool]$DryRun -or $null -ne $fixture
    Assert-SashimiNoReparsePoint -Path $ProjectPath
    Assert-SashimiNoReparsePoint -Path $ArtifactsPath
    $normalizedProjectPath = ConvertTo-SashimiPath -Path $ProjectPath -AllowMissing:$allowMissingPaths -Lexical
    $normalizedArtifactsPath = ConvertTo-SashimiPath -Path $ArtifactsPath -AllowMissing -Lexical
    $result.ProjectPath = $normalizedProjectPath
    $result.ArtifactsPath = $normalizedArtifactsPath
    $result.SummaryPath = Join-Path $normalizedArtifactsPath 'UnityValidation.Summary.json'
    if ((Test-SashimiPathEqual -Left $normalizedProjectPath -Right $normalizedArtifactsPath) -or
        (Test-SashimiPathWithin -Path $normalizedArtifactsPath -Root $normalizedProjectPath) -or
        (Test-SashimiPathWithin -Path $normalizedProjectPath -Root $normalizedArtifactsPath)) {
        throw 'ArtifactsPath and ProjectPath must not overlap.'
    }

    if (-not [string]::IsNullOrWhiteSpace($OwnedUnityPidPath)) {
        $normalizedOwnedUnityPidPath = ConvertTo-SashimiPath -Path $OwnedUnityPidPath -AllowMissing -Lexical
        if ((Split-Path -Leaf $normalizedOwnedUnityPidPath) -cne 'OwnedUnityPids.json') {
            throw 'OwnedUnityPidPath must name State\OwnedUnityPids.json.'
        }
        $stateRoot = Split-Path -Parent $normalizedOwnedUnityPidPath
        if ((Split-Path -Leaf $stateRoot) -cne 'State') {
            throw 'OwnedUnityPidPath must be inside the run-owned State directory.'
        }
        $runRoot = Split-Path -Parent $stateRoot
        $expectedArtifactsRoot = Join-Path $runRoot 'Artifacts'
        if (-not (Test-SashimiPathWithin -Path $normalizedArtifactsPath -Root $expectedArtifactsRoot)) {
            throw 'ArtifactsPath and OwnedUnityPidPath do not identify the same run workspace.'
        }
    }
    else {
        if (-not $DryRun -and $null -eq $fixture) {
            throw 'Live Unity validation requires the run-owned State\OwnedUnityPids.json path.'
        }
        # Fixture and DryRun callers do not own a live Unity process. Keep their
        # raw staging beneath a sibling State directory and isolate each
        # ArtifactsPath by hash so parallel fixture cases cannot collide.
        $stateRoot = Join-Path (Split-Path -Parent $normalizedArtifactsPath) 'State'
    }
    $rawValidationLeaf = if (-not [string]::IsNullOrWhiteSpace($OwnedUnityPidPath)) {
        Split-Path -Leaf $normalizedArtifactsPath
    }
    else {
        'fixture-' + (Get-SashimiTextSha256 -Text $normalizedArtifactsPath.ToLowerInvariant()).Substring(0, 16)
    }
    if ($rawValidationLeaf -notmatch '^[A-Za-z0-9._-]{1,80}$') { throw 'ArtifactsPath has an unsafe raw-validation identity.' }
    $rawValidationPath = Join-Path (Join-Path $stateRoot 'raw-validation') $rawValidationLeaf
    if ((Test-SashimiPathEqual -Left $rawValidationPath -Right $normalizedArtifactsPath) -or
        (Test-SashimiPathWithin -Path $rawValidationPath -Root $normalizedArtifactsPath) -or
        (Test-SashimiPathWithin -Path $normalizedArtifactsPath -Root $rawValidationPath) -or
        (Test-SashimiPathEqual -Left $rawValidationPath -Right $normalizedProjectPath) -or
        (Test-SashimiPathWithin -Path $rawValidationPath -Root $normalizedProjectPath) -or
        (Test-SashimiPathWithin -Path $normalizedProjectPath -Root $rawValidationPath)) {
        throw 'Raw Unity validation state must not overlap the repository or ArtifactsPath.'
    }

    $determinismPaths = @()
    $screenshotPaths = @()
    $previewPaths = @()
    if ($null -ne $validationDefinition) {
        $determinismPaths = @($validationDefinition.DeterminismPaths | ForEach-Object {
                ConvertTo-SashimiProjectRelativePath -Value $_ -NormalizedProjectPath $normalizedProjectPath
            })
        $screenshotPaths = @($validationDefinition.ScreenshotPaths | ForEach-Object {
                ConvertTo-SashimiProjectRelativePath -Value $_ -NormalizedProjectPath $normalizedProjectPath
            })
        $previewPaths = @($validationDefinition.PreviewPaths | ForEach-Object {
                ConvertTo-SashimiProjectRelativePath -Value $_ -NormalizedProjectPath $normalizedProjectPath
            })
        $result.Determinism.Required = $true
        $result.Determinism.Paths = $determinismPaths
    }

    $protectedPatterns = @((Get-SashimiPropertyValue -Object $config.Security -Name 'ProtectedPathPatterns' -DefaultValue @()) | ForEach-Object { [string]$_ })
    $allowedProtectedPatterns = @()
    if ($null -ne $validationDefinition) {
        $allowedProtectedPatterns += @($validationDefinition.AllowedProtectedPathPatterns)
        foreach ($determinismPath in $determinismPaths) {
            $allowedProtectedPatterns += $determinismPath
            $allowedProtectedPatterns += ($determinismPath.TrimEnd('/') + '/**')
        }
    }
    foreach ($allowedPattern in @($allowedProtectedPatterns)) {
        $normalizedAllowedPattern = ([string]$allowedPattern).Replace('\', '/')
        # Issue generators may only receive narrow exceptions under Assets.
        # Packages and every ProjectSettings path remain Host-blocked even if
        # an administrator accidentally configures an over-broad wildcard.
        if ($normalizedAllowedPattern -notmatch '^Assets/' -or
            $normalizedAllowedPattern -match '(^|/)\.\.(/|$)') {
            throw "Issue validation may allow protected generator outputs only below Assets/: $allowedPattern"
        }
    }

    $compileLog = Join-Path $normalizedArtifactsPath 'CompileImport.log'
    $editLog = Join-Path $normalizedArtifactsPath 'EditMode.log'
    $editXml = Join-Path $normalizedArtifactsPath 'EditMode.xml'
    $playLog = Join-Path $normalizedArtifactsPath 'PlayMode.log'
    $playXml = Join-Path $normalizedArtifactsPath 'PlayMode.xml'
    $generatorRun1Log = Join-Path $normalizedArtifactsPath 'GeneratorRun1.log'
    $generatorRun2Log = Join-Path $normalizedArtifactsPath 'GeneratorRun2.log'

    $compileRawLog = Join-Path $rawValidationPath 'CompileImport.raw.log'
    $editRawLog = Join-Path $rawValidationPath 'EditMode.raw.log'
    $editRawXml = Join-Path $rawValidationPath 'EditMode.raw.xml'
    $playRawLog = Join-Path $rawValidationPath 'PlayMode.raw.log'
    $playRawXml = Join-Path $rawValidationPath 'PlayMode.raw.xml'
    $generatorRun1RawLog = Join-Path $rawValidationPath 'GeneratorRun1.raw.log'
    $generatorRun2RawLog = Join-Path $rawValidationPath 'GeneratorRun2.raw.log'
    $rawValidationFiles = @(
        $compileRawLog, $editRawLog, $editRawXml, $playRawLog, $playRawXml,
        $generatorRun1RawLog, $generatorRun2RawLog
    )

    $compileArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-logFile', $compileRawLog, '-quit')
    $editArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'EditMode', '-testResults', $editRawXml, '-logFile', $editRawLog)
    $playArguments = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-runTests', '-testPlatform', 'PlayMode', '-testResults', $playRawXml, '-logFile', $playRawLog)
    $generatorRun1Arguments = @()
    $generatorRun2Arguments = @()
    if ($null -ne $validationDefinition) {
        $generatorBase = @('-batchmode', '-nographics', '-buildTarget', 'StandaloneWindows64', '-projectPath', $normalizedProjectPath, '-executeMethod', $validationDefinition.UnityExecuteMethod) + @($validationDefinition.Arguments)
        $generatorRun1Arguments = @($generatorBase + @('-logFile', $generatorRun1RawLog, '-quit'))
        $generatorRun2Arguments = @($generatorBase + @('-logFile', $generatorRun2RawLog, '-quit'))
    }

    # Avoid comparing --show-toplevel output: the shared process adapter
    # intentionally redacts user-profile paths, and runtime clones live below
    # %LOCALAPPDATA%. `true` with an empty --show-prefix proves that -C names
    # the root of a non-bare working tree without exposing its absolute path.
    $gitRootArguments = @('-C', $normalizedProjectPath, 'rev-parse', '--is-inside-work-tree', '--show-prefix')
    $diffCheckArguments = @('-C', $normalizedProjectPath, 'diff', '--check', $BaselineRef, '--')
    $changedPathArguments = @('-C', $normalizedProjectPath, 'diff', '--name-only', '-z', '--diff-filter=ACDMRTUXB', $BaselineRef, '--')
    $untrackedPathArguments = @('-C', $normalizedProjectPath, 'ls-files', '--others', '--exclude-standard', '-z')
    $lfsArguments = @('ls-files', '--long')
    $trackedPathArguments = @('-C', $normalizedProjectPath, 'ls-files', '-z')
    $knownDriftArguments = @('-C', $normalizedProjectPath, 'diff', '--no-ext-diff', '--no-textconv', '--unified=0', '--no-color', $BaselineRef, '--', 'ProjectSettings/ProjectSettings.asset')

    if ($DryRun) {
        foreach ($gitPlan in @(
                @{ Name = 'GitRoot'; Arguments = $gitRootArguments },
                @{ Name = 'PreUnityChangedPaths'; Arguments = $changedPathArguments },
                @{ Name = 'PreUnityUntrackedPaths'; Arguments = $untrackedPathArguments }
            )) {
            [void](Invoke-SashimiValidationProcess -Name $gitPlan.Name -Kind Git -FilePath $gitExecutable -Arguments $gitPlan.Arguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git -DryRun)
        }
        Add-SashimiValidationCheck -Name 'PreUnityProtectedProductionScope' -Passed $true -Planned $true -Detail 'Changed, untracked, and protected paths will be checked before Unity loads the project.'
        $stages.CompileImport = Invoke-SashimiUnityValidationStage -Name CompileImport -Arguments $compileArguments -LogPath $compileLog -RawLogPath $compileRawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -Compile -DryRun
        if ($null -ne $validationDefinition) {
            $stages.GeneratorRun1 = Invoke-SashimiUnityValidationStage -Name GeneratorRun1 -Arguments $generatorRun1Arguments -LogPath $generatorRun1Log -RawLogPath $generatorRun1RawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $generatorTimeout -Fixture $fixture -Compile -DryRun
            $stages.GeneratorRun2 = Invoke-SashimiUnityValidationStage -Name GeneratorRun2 -Arguments $generatorRun2Arguments -LogPath $generatorRun2Log -RawLogPath $generatorRun2RawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $generatorTimeout -Fixture $fixture -Compile -DryRun
        }
        $stages.EditMode = Invoke-SashimiUnityValidationStage -Name EditMode -Arguments $editArguments -LogPath $editLog -RawLogPath $editRawLog -XmlPath $editXml -RawXmlPath $editRawXml -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -TestStage -DryRun
        $stages.PlayMode = Invoke-SashimiUnityValidationStage -Name PlayMode -Arguments $playArguments -LogPath $playLog -RawLogPath $playRawLog -XmlPath $playXml -RawXmlPath $playRawXml -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -TestStage -DryRun
        foreach ($gitPlan in @(
                @{ Name = 'DiffCheck'; Arguments = $diffCheckArguments },
                @{ Name = 'ChangedPaths'; Arguments = $changedPathArguments },
                @{ Name = 'UntrackedPaths'; Arguments = $untrackedPathArguments },
                @{ Name = 'TrackedPaths'; Arguments = $trackedPathArguments },
                @{ Name = 'KnownUnityDriftDiff'; Arguments = $knownDriftArguments }
            )) {
            [void](Invoke-SashimiValidationProcess -Name $gitPlan.Name -Kind Git -FilePath $gitExecutable -Arguments $gitPlan.Arguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git -DryRun)
        }
        [void](Invoke-SashimiValidationProcess -Name LfsLsFiles -Kind Git -FilePath $gitLfsExecutable -Arguments $lfsArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git -DryRun)
        Add-SashimiValidationCheck -Name 'DryRunNoMutation' -Passed $true -Planned $true -Detail 'All Unity, Git, generator, artifact, and filesystem mutations were planned only.'
        Add-SashimiValidationCheck -Name 'IssueGeneratorAllowlist' -Passed $true -Planned $true -Detail $(if ($null -eq $validationDefinition) { 'No issue-specific generator requested.' } else { "Allowlisted validation '$IssueValidationId' will run twice." })
        $result.Success = $true
        $result.Succeeded = $true
        $exitCode = 0
    }
    else {
        $skipFileSystemValidation = $null -ne $fixture -and [bool](Get-SashimiPropertyValue -Object $fixture -Name 'SkipFileSystemValidation' -DefaultValue $true)
        if (-not $skipFileSystemValidation) {
            foreach ($requiredPath in @('Assets', 'Packages', 'ProjectSettings', 'ProjectSettings\ProjectVersion.txt')) {
                if (-not (Test-Path -LiteralPath (Join-Path $normalizedProjectPath $requiredPath))) {
                    throw "ProjectPath is not a complete Unity project; missing '$requiredPath'."
                }
            }
            if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) { throw "Unity executable is missing: $unityExecutable" }
            $dirtyGeneratedDirectories = @('Library', 'Temp', 'Logs', 'UserSettings' | Where-Object { Test-Path -LiteralPath (Join-Path $normalizedProjectPath $_) })
            if ($dirtyGeneratedDirectories.Count -gt 0) {
                throw "Clean import requires a fresh clone without generated directories: $($dirtyGeneratedDirectories -join ', ')."
            }
            $projectVersionText = [IO.File]::ReadAllText((Join-Path $normalizedProjectPath 'ProjectSettings\ProjectVersion.txt'), [Text.Encoding]::UTF8)
            if ($projectVersionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)\s*$') { throw 'ProjectVersion.txt has no m_EditorVersion.' }
            $result.DetectedUnityVersion = $Matches[1]
            if ($result.DetectedUnityVersion -cne $result.ExpectedUnityVersion) {
                throw "Unity project version '$($result.DetectedUnityVersion)' does not match '$($result.ExpectedUnityVersion)'."
            }
        }
        else {
            $result.DetectedUnityVersion = $result.ExpectedUnityVersion
        }

        $lockPath = Join-Path $normalizedProjectPath 'Temp\UnityLockfile'
        if (-not $skipFileSystemValidation -and (Test-Path -LiteralPath $lockPath)) {
            throw "Unity lock exists for this run-owned project: $lockPath"
        }
        $visibleUnity = @(Get-Process -Name Unity -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.Id })
        $result.ExistingUnityProcessIds = $visibleUnity
        Add-SashimiValidationCheck -Name 'SecondaryUnityProcessObservation' -Passed $true -Detail $(if ($visibleUnity.Count -eq 0) { 'No ambient Unity process observed.' } else { "Ambient Unity PID(s) observed but not used as a mandatory gate: $($visibleUnity -join ',')." }) -Data $visibleUnity

        if (-not (Test-Path -LiteralPath $normalizedArtifactsPath -PathType Container)) {
            [IO.Directory]::CreateDirectory($normalizedArtifactsPath) | Out-Null
        }
        Assert-SashimiNoReparsePoint -Path $normalizedArtifactsPath
        if (-not (Test-Path -LiteralPath $rawValidationPath -PathType Container)) {
            [IO.Directory]::CreateDirectory($rawValidationPath) | Out-Null
        }
        Assert-SashimiNoReparsePoint -Path $rawValidationPath
        foreach ($outputPath in @($compileLog, $editLog, $editXml, $playLog, $playXml, $generatorRun1Log, $generatorRun2Log, $result.SummaryPath)) {
            if (Test-Path -LiteralPath $outputPath) { throw "Refusing to overwrite stale Unity validation artifact: $outputPath" }
        }
        if (@($rawValidationFiles | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0) {
            throw 'Refusing to overwrite stale run-owned raw Unity validation state.'
        }

        $gitRoot = Invoke-SashimiValidationProcess -Name GitRoot -Kind Git -FilePath $gitExecutable -Arguments $gitRootArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $gitRootOutput = (($gitRoot.StdOut -replace "`r`n", "`n") -replace "`r", "`n").Trim()
        if ($null -ne $fixture -and [string]::IsNullOrWhiteSpace($gitRootOutput)) { $gitRootOutput = 'true' }
        $gitRootPassed = [bool]$gitRoot.Succeeded -and [string]::Equals($gitRootOutput, 'true', [StringComparison]::Ordinal)
        Add-SashimiValidationCheck -Name 'GitRoot' -Passed $gitRootPassed -Detail "reported=$gitRootOutput"
        if (-not $gitRootPassed) { Add-SashimiValidationFailure -Code GitRootMismatch -Stage Preflight -Message "ProjectPath is not the root of a non-bare Git working tree: $gitRootOutput" -NativeExitCode $gitRoot.ExitCode }

        if ($gitRootPassed) {
            $preUnityChangedResult = Invoke-SashimiValidationProcess -Name PreUnityChangedPaths -Kind Git -FilePath $gitExecutable -Arguments $changedPathArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
            $preUnityUntrackedResult = Invoke-SashimiValidationProcess -Name PreUnityUntrackedPaths -Kind Git -FilePath $gitExecutable -Arguments $untrackedPathArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
            $preUnityScanPassed = [bool]$preUnityChangedResult.Succeeded -and [bool]$preUnityUntrackedResult.Succeeded
            if (-not $preUnityScanPassed) {
                Add-SashimiValidationFailure -Code PreUnityChangedPathScanFailed -Stage Preflight -Message "Unable to enumerate changed paths before Unity execution: $($preUnityChangedResult.StdErr) $($preUnityUntrackedResult.StdErr)"
            }
            $preUnityChangedPaths = @((ConvertTo-SashimiGitPathList $preUnityChangedResult.StdOut) + (ConvertTo-SashimiGitPathList $preUnityUntrackedResult.StdOut) | Sort-Object -Unique)
            $fixturePreUnityChangedPaths = Get-SashimiPropertyValue -Object $fixture -Name 'PreUnityChangedPaths' -DefaultValue (Get-SashimiPropertyValue -Object $fixture -Name 'ChangedPaths' -DefaultValue $null)
            if ($null -ne $fixturePreUnityChangedPaths) {
                $preUnityChangedPaths = @($fixturePreUnityChangedPaths | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique)
            }
            $preUnityProtectedChanges = @($preUnityChangedPaths | Where-Object { Test-SashimiRelativePathPattern -Path $_ -Patterns $protectedPatterns })
            $preUnityBlockedProtectedChanges = @($preUnityProtectedChanges | Where-Object { -not (Test-SashimiRelativePathPattern -Path $_ -Patterns $allowedProtectedPatterns) })
            $result.PreUnityChangedPaths = $preUnityChangedPaths
            $result.PreUnityProtectedChanges = $preUnityProtectedChanges
            $result.PreUnityBlockedProtectedChanges = $preUnityBlockedProtectedChanges
            $preUnityScopePassed = $preUnityScanPassed -and $preUnityBlockedProtectedChanges.Count -eq 0
            Add-SashimiValidationCheck -Name 'PreUnityProtectedProductionScope' -Passed $preUnityScopePassed -Detail $(if (-not $preUnityScanPassed) { 'Changed/untracked path enumeration failed before Unity execution.' } elseif ($preUnityScopePassed) { 'No unauthorized protected path is present before Unity execution.' } else { "Unity execution blocked before project load; unauthorized protected changes: $($preUnityBlockedProtectedChanges -join ', ')" }) -Data $preUnityProtectedChanges
            if ($preUnityBlockedProtectedChanges.Count -gt 0) {
                Add-SashimiValidationFailure -Code PreUnityProtectedProductionScopeChanged -Stage Preflight -Message "Refusing to load the Unity project with unauthorized protected changes: $($preUnityBlockedProtectedChanges -join ', ')"
            }

            if ($preUnityScopePassed) {
            $stages.CompileImport = Invoke-SashimiUnityValidationStage -Name CompileImport -Arguments $compileArguments -LogPath $compileLog -RawLogPath $compileRawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -Compile
            $generatorPassed = $true
            if ($null -ne $validationDefinition -and $stages.CompileImport.Success) {
                $stages.GeneratorRun1 = Invoke-SashimiUnityValidationStage -Name GeneratorRun1 -Arguments $generatorRun1Arguments -LogPath $generatorRun1Log -RawLogPath $generatorRun1RawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $generatorTimeout -Fixture $fixture -Compile
                $generatorPassed = [bool]$stages.GeneratorRun1.Success
                if ($generatorPassed) {
                    $fixtureDeterminism = Get-SashimiPropertyValue -Object $fixture -Name 'Determinism' -DefaultValue $null
                    $run1Fixture = Get-SashimiPropertyValue -Object $fixtureDeterminism -Name 'Run1' -DefaultValue $null
                    $snapshot1 = if ($null -ne $run1Fixture) { @($run1Fixture) } else { @(Get-SashimiDeterminismSnapshot -ProjectRoot $normalizedProjectPath -RelativePaths $determinismPaths) }
                    $result.Determinism.Run1Snapshot = $snapshot1
                    Write-SashimiUtf8File -Path (Join-Path $normalizedArtifactsPath 'GeneratorRun1.snapshot.json') -Content (ConvertTo-SashimiJson $snapshot1 -Pretty)

                    $stages.GeneratorRun2 = Invoke-SashimiUnityValidationStage -Name GeneratorRun2 -Arguments $generatorRun2Arguments -LogPath $generatorRun2Log -RawLogPath $generatorRun2RawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $generatorTimeout -Fixture $fixture -Compile
                    $generatorPassed = [bool]$stages.GeneratorRun2.Success
                    if ($generatorPassed) {
                        $run2Fixture = Get-SashimiPropertyValue -Object $fixtureDeterminism -Name 'Run2' -DefaultValue $null
                        $snapshot2 = if ($null -ne $run2Fixture) { @($run2Fixture) } else { @(Get-SashimiDeterminismSnapshot -ProjectRoot $normalizedProjectPath -RelativePaths $determinismPaths) }
                        $result.Determinism.Run2Snapshot = $snapshot2
                        Write-SashimiUtf8File -Path (Join-Path $normalizedArtifactsPath 'GeneratorRun2.snapshot.json') -Content (ConvertTo-SashimiJson $snapshot2 -Pretty)
                        $snapshot1Json = ConvertTo-SashimiJson $snapshot1
                        $snapshot2Json = ConvertTo-SashimiJson $snapshot2
                        $result.Determinism.Passed = [string]::Equals($snapshot1Json, $snapshot2Json, [StringComparison]::Ordinal)
                        Add-SashimiValidationCheck -Name 'GeneratorDeterminism' -Passed ([bool]$result.Determinism.Passed) -Detail $(if ($result.Determinism.Passed) { 'Two generator runs produced identical path, length, and SHA-256 snapshots.' } else { 'Generator run snapshots differ.' })
                        if (-not $result.Determinism.Passed) { Add-SashimiValidationFailure -Code GeneratorNonDeterministic -Stage GeneratorRun2 -Message 'Two generator runs produced different outputs.' }
                    }
                }
            }

            if ($stages.CompileImport.Success -and $generatorPassed) {
                $stages.EditMode = Invoke-SashimiUnityValidationStage -Name EditMode -Arguments $editArguments -LogPath $editLog -RawLogPath $editRawLog -XmlPath $editXml -RawXmlPath $editRawXml -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -TestStage
                $stages.PlayMode = Invoke-SashimiUnityValidationStage -Name PlayMode -Arguments $playArguments -LogPath $playLog -RawLogPath $playRawLog -XmlPath $playXml -RawXmlPath $playRawXml -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $unityTimeout -Fixture $fixture -TestStage
            }
            }
        }

        $diffCheck = Invoke-SashimiValidationProcess -Name DiffCheck -Kind Git -FilePath $gitExecutable -Arguments $diffCheckArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $diffPassed = [bool]$diffCheck.Succeeded -and [string]::IsNullOrWhiteSpace($diffCheck.StdOut)
        Add-SashimiValidationCheck -Name 'GitDiffCheck' -Passed $diffPassed -Detail $(if ($diffPassed) { 'git diff --check passed.' } else { $diffCheck.StdOut + ' ' + $diffCheck.StdErr })
        if (-not $diffPassed) { Add-SashimiValidationFailure -Code GitDiffCheckFailed -Stage Git -Message "git diff --check failed: $($diffCheck.StdOut) $($diffCheck.StdErr)" -NativeExitCode $diffCheck.ExitCode }

        $changedResult = Invoke-SashimiValidationProcess -Name ChangedPaths -Kind Git -FilePath $gitExecutable -Arguments $changedPathArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $untrackedResult = Invoke-SashimiValidationProcess -Name UntrackedPaths -Kind Git -FilePath $gitExecutable -Arguments $untrackedPathArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        if (-not $changedResult.Succeeded -or -not $untrackedResult.Succeeded) {
            Add-SashimiValidationFailure -Code GitChangedPathScanFailed -Stage Git -Message "Unable to enumerate changed paths: $($changedResult.StdErr) $($untrackedResult.StdErr)"
        }
        $changedPaths = @((ConvertTo-SashimiGitPathList $changedResult.StdOut) + (ConvertTo-SashimiGitPathList $untrackedResult.StdOut) | Sort-Object -Unique)
        $fixtureChangedPaths = Get-SashimiPropertyValue -Object $fixture -Name 'ChangedPaths' -DefaultValue $null
        if ($null -ne $fixtureChangedPaths) { $changedPaths = @($fixtureChangedPaths | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique) }
        $result.ChangedPaths = $changedPaths

        $protectedChanges = @($changedPaths | Where-Object { Test-SashimiRelativePathPattern -Path $_ -Patterns $protectedPatterns })
        $blockedProtectedChanges = @($protectedChanges | Where-Object { -not (Test-SashimiRelativePathPattern -Path $_ -Patterns $allowedProtectedPatterns) })

        $knownDrift = Invoke-SashimiValidationProcess -Name KnownUnityDriftDiff -Kind Git -FilePath $gitExecutable -Arguments $knownDriftArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $knownDriftDetected = [bool]$knownDrift.Succeeded -and (Test-SashimiKnownUnityDefaultDrift -DiffText $knownDrift.StdOut)
        if ($blockedProtectedChanges -ccontains 'ProjectSettings/ProjectSettings.asset' -and $knownDriftDetected) {
            $result.KnownUnityDefaultDrift.Detected = $true
            # This validator has no trusted proof that its caller is a disposable,
            # never-delivered Reviewer integration. Retain exact evidence, but
            # fail closed so a Developer cannot stage this production drift.
            $result.KnownUnityDefaultDrift.Allowed = $false
            $driftArtifact = Join-Path $normalizedArtifactsPath 'KnownUnityDefaultDrift.diff'
            Write-SashimiUtf8File -Path $driftArtifact -Content ((Protect-SashimiText $knownDrift.StdOut) + [Environment]::NewLine)
            $result.KnownUnityDefaultDrift.DiffArtifactPath = $driftArtifact
            $result.KnownUnityDefaultDrift.DiffSha256 = (Get-FileHash -LiteralPath $driftArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $result.ProtectedChanges = $protectedChanges
        $result.BlockedProtectedChanges = $blockedProtectedChanges
        $protectedPassed = $blockedProtectedChanges.Count -eq 0
        Add-SashimiValidationCheck -Name 'ProtectedProductionScope' -Passed $protectedPassed -Detail $(if ($protectedPassed) { 'No unauthorized protected production path changed.' } else { "Unauthorized protected changes: $($blockedProtectedChanges -join ', ')" }) -Data $protectedChanges
        if (-not $protectedPassed) { Add-SashimiValidationFailure -Code ProtectedProductionScopeChanged -Stage Integrity -Message "Unauthorized protected production paths changed: $($blockedProtectedChanges -join ', ')" }

        $lfs = Invoke-SashimiValidationProcess -Name LfsLsFiles -Kind Git -FilePath $gitLfsExecutable -Arguments $lfsArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $lfsPointerEntries = [Collections.Generic.List[string]]::new()
        $invalidLfsLines = [Collections.Generic.List[string]]::new()
        if ($lfs.Succeeded) {
            foreach ($line in @($lfs.StdOut -split "`r?`n" | Where-Object { $_ -match '\S' })) {
                if ($line -match '^[0-9a-fA-F]{64}\s+([*-])\s+(.+)$') {
                    if ($Matches[1] -eq '-') { $lfsPointerEntries.Add($Matches[2]) }
                }
                else { $invalidLfsLines.Add($line) }
            }
        }
        else {
            Add-SashimiValidationFailure -Code GitLfsFailed -Stage Lfs -Message "git lfs ls-files failed: $($lfs.StdErr)" -NativeExitCode $lfs.ExitCode
        }

        $trackedResult = Invoke-SashimiValidationProcess -Name TrackedPaths -Kind Git -FilePath $gitExecutable -Arguments $trackedPathArguments -WorkingDirectory $normalizedProjectPath -TimeoutSeconds $gitTimeout -Fixture $fixture -FixtureGroup Git
        $trackedPaths = ConvertTo-SashimiGitPathList $trackedResult.StdOut
        $workingTreePointers = @()
        $fixturePointers = Get-SashimiPropertyValue -Object $fixture -Name 'PointerFiles' -DefaultValue $null
        if ($null -ne $fixturePointers) { $workingTreePointers = @($fixturePointers | ForEach-Object { [string]$_ }) }
        elseif (-not $skipFileSystemValidation -and $trackedResult.Succeeded) { $workingTreePointers = @(Get-SashimiWorkingTreePointers -ProjectRoot $normalizedProjectPath -TrackedPaths $trackedPaths) }
        if (-not $trackedResult.Succeeded) { Add-SashimiValidationFailure -Code GitTrackedFileScanFailed -Stage Lfs -Message "git ls-files failed: $($trackedResult.StdErr)" -NativeExitCode $trackedResult.ExitCode }
        $lfsPassed = [bool]$lfs.Succeeded -and [bool]$trackedResult.Succeeded -and $lfsPointerEntries.Count -eq 0 -and $workingTreePointers.Count -eq 0 -and $invalidLfsLines.Count -eq 0
        $result.Lfs = [pscustomobject][ordered]@{
            Passed = $lfsPassed
            PointerEntries = $lfsPointerEntries.ToArray()
            WorkingTreePointers = $workingTreePointers
            InvalidOutputLines = $invalidLfsLines.ToArray()
        }
        Add-SashimiValidationCheck -Name 'GitLfsAndPointerScan' -Passed $lfsPassed -Detail $(if ($lfsPassed) { 'Git LFS inventory and working-tree pointer scan passed.' } else { 'Git LFS contains pointer-only/malformed entries or its scan failed.' }) -Data $result.Lfs
        if (-not $lfsPassed) { Add-SashimiValidationFailure -Code GitLfsPointerFailure -Stage Lfs -Message 'Git LFS or working-tree pointer validation failed.' }

        $fixtureIntegrity = Get-SashimiPropertyValue -Object $fixture -Name 'Integrity' -DefaultValue $null
        if ($null -ne $fixtureIntegrity) {
            $result.Integrity = $fixtureIntegrity
        }
        elseif ($skipFileSystemValidation) {
            $result.Integrity = [pscustomobject][ordered]@{ Passed = $true; FixtureSkipped = $true; MissingMetaCount = 0; OrphanMetaCount = 0; InvalidMetaCount = 0; DuplicateGuidCount = 0; MissingScriptCount = 0; MissingReferenceCount = 0 }
        }
        else {
            $result.Integrity = Get-SashimiMetaGuidIntegrity -ProjectRoot $normalizedProjectPath
        }
        $integrityPassed = [bool](Get-SashimiPropertyValue -Object $result.Integrity -Name 'Passed' -DefaultValue $false)
        Add-SashimiValidationCheck -Name 'MetaGuidAndSerializedReferences' -Passed $integrityPassed -Detail $(if ($integrityPassed) { 'Meta, GUID, Missing Script, and serialized-reference scans passed.' } else { 'Meta/GUID/Missing Script/serialized-reference scan failed.' }) -Data $result.Integrity
        if (-not $integrityPassed) { Add-SashimiValidationFailure -Code UnityAssetIntegrityFailed -Stage Integrity -Message 'Meta, GUID, Missing Script, or serialized-reference integrity failed.' }

        if ($null -ne $validationDefinition) {
            $artifactExclusions = @((Get-SashimiPropertyValue -Object $config.Security -Name 'ArtifactExclusionPatterns' -DefaultValue @()) | ForEach-Object { [string]$_ })
            if ($skipFileSystemValidation) {
                foreach ($hookKind in @(@{ Kind = 'Screenshot'; Paths = $screenshotPaths }, @{ Kind = 'Preview'; Paths = $previewPaths })) {
                    foreach ($path in $hookKind.Paths) {
                        $artifactHooks.Add([pscustomobject]@{ Kind = $hookKind.Kind; SourceRelativePath = $path; ArtifactPath = $null; Planned = $true; FixtureSkipped = $true })
                    }
                }
            }
            else {
                foreach ($hook in @(Copy-SashimiValidationArtifactHooks -Kind Screenshot -Paths $screenshotPaths -ProjectRoot $normalizedProjectPath -ArtifactRoot $normalizedArtifactsPath -ExclusionPatterns $artifactExclusions)) { $artifactHooks.Add($hook) }
                foreach ($hook in @(Copy-SashimiValidationArtifactHooks -Kind Preview -Paths $previewPaths -ProjectRoot $normalizedProjectPath -ArtifactRoot $normalizedArtifactsPath -ExclusionPatterns $artifactExclusions)) { $artifactHooks.Add($hook) }
            }
        }
        Add-SashimiValidationCheck -Name 'ScreenshotPreviewArtifactHooks' -Passed $true -Detail "Captured or recorded $($artifactHooks.Count) allowlisted screenshot/preview artifact hook(s)." -Data $artifactHooks.ToArray()

        if ($null -eq $fixture) {
            $stillRunning = [Collections.Generic.List[int]]::new()
            foreach ($ownedPid in @($ownedUnityProcessIds | Sort-Object -Unique)) {
                if ($null -ne (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { $stillRunning.Add($ownedPid) }
            }
            $ownedPidPassed = $stillRunning.Count -eq 0
            Add-SashimiValidationCheck -Name 'OwnedUnityProcessesExited' -Passed $ownedPidPassed -Detail $(if ($ownedPidPassed) { 'Every run-owned Unity PID exited.' } else { "Run-owned Unity PID(s) remain: $($stillRunning -join ',')." }) -Data $stillRunning.ToArray()
            if (-not $ownedPidPassed) { Add-SashimiValidationFailure -Code UnityProcessStillRunning -Stage Cleanup -Message "Run-owned Unity processes remain: $($stillRunning -join ',')." }
        }

        $result.Success = ($failures.Count -eq 0)
        $result.Succeeded = $result.Success
        $exitCode = if ($result.Success) { 0 } else { 1 }
    }
}
catch {
    Add-SashimiValidationFailure -Code UnhandledValidationFailure -Stage PreflightOrUnhandled -Message $_.Exception.Message
    $result.Success = $false
    $result.Succeeded = $false
    $exitCode = 1
}

if (-not $DryRun -and -not [string]::IsNullOrWhiteSpace($rawValidationPath)) {
    $rawCleanupPassed = $true
    if (-not $script:rawValidationCleanupSafe) {
        $rawCleanupPassed = $false
        Add-SashimiValidationFailure -Code RawValidationStatePreserved -Stage Cleanup -Message 'Raw Unity validation state was preserved outside Artifacts because process termination was not confirmed.'
        $result.Success = $false
        $result.Succeeded = $false
        $exitCode = 1
    }
    else {
      try {
        if (Test-Path -LiteralPath $rawValidationPath) {
            Assert-SashimiNoReparsePoint -Path $rawValidationPath -Recurse
            # The staging directory is host-owned and has a closed set of
            # permitted leaf files. Detect anything else before deleting the
            # known outputs so unexpected raw evidence remains quarantined in
            # State for investigation. Do not include an entry name or path in
            # the thrown diagnostic because neither has been sanitized.
            $expectedRawPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($rawFile in @($rawValidationFiles)) {
                [void]$expectedRawPaths.Add([IO.Path]::GetFullPath($rawFile))
            }
            $unexpectedRawEntry = $false
            foreach ($rawEntry in @(Get-ChildItem -LiteralPath $rawValidationPath -Force -ErrorAction Stop)) {
                if ($rawEntry.PSIsContainer -or
                    ($rawEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                    -not $expectedRawPaths.Contains([IO.Path]::GetFullPath($rawEntry.FullName))) {
                    $unexpectedRawEntry = $true
                    break
                }
            }
            if ($unexpectedRawEntry) {
                throw 'Raw Unity validation state contains an unexpected filesystem entry.'
            }
            foreach ($rawFile in @($rawValidationFiles)) {
                if (-not (Test-Path -LiteralPath $rawFile)) { continue }
                $rawItem = Get-Item -LiteralPath $rawFile -Force -ErrorAction Stop
                if ($rawItem.PSIsContainer -or ($rawItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Raw Unity validation state contains an unexpected filesystem entry.'
                }
                [IO.File]::Delete($rawItem.FullName)
            }
            # Re-enumerate after deletion to close the race where an entry is
            # created after the allowlist check. A non-empty directory is a
            # cleanup failure and remains outside Artifacts.
            if (@(Get-ChildItem -LiteralPath $rawValidationPath -Force -ErrorAction Stop).Count -ne 0) {
                throw 'Raw Unity validation state remained non-empty after guarded cleanup.'
            }
            [IO.Directory]::Delete($rawValidationPath, $false)
            if (Test-Path -LiteralPath $rawValidationPath) {
                throw 'Raw Unity validation state removal could not be confirmed.'
            }
        }
        $rawParent = Split-Path -Parent $rawValidationPath
        if (Test-Path -LiteralPath $rawParent -PathType Container) {
            Assert-SashimiNoReparsePoint -Path $rawParent
            if (@(Get-ChildItem -LiteralPath $rawParent -Force -ErrorAction Stop).Count -eq 0) {
                [IO.Directory]::Delete($rawParent, $false)
            }
        }
      }
      catch {
        $rawCleanupPassed = $false
        Add-SashimiValidationFailure -Code RawValidationStateCleanupFailed -Stage Cleanup -Message 'Raw Unity validation state cleanup could not be confirmed; state was kept outside Artifacts.'
        $result.Success = $false
        $result.Succeeded = $false
        $exitCode = 1
      }
    }
    $rawCleanupDetail = if ($rawCleanupPassed) {
        'Raw Unity output was sanitized before promotion and run-owned raw state was removed.'
    }
    elseif (-not $script:rawValidationCleanupSafe) {
        'Raw Unity state was deliberately preserved outside Artifacts because process termination was not confirmed.'
    }
    else {
        'Raw Unity state remained outside Artifacts because guarded cleanup failed.'
    }
    Add-SashimiValidationCheck -Name 'RawValidationStateCleanup' -Passed $rawCleanupPassed -Detail $rawCleanupDetail
}

$result.ExitCode = $exitCode
$result.OwnedUnityProcessIds = @($ownedUnityProcessIds | Sort-Object -Unique)
$result.ArtifactHooks = $artifactHooks.ToArray()
$result.Commands = $commands.ToArray()
$result.Checks = $checks.ToArray()
$result.Failures = $failures.ToArray()

if (-not $DryRun -and -not [string]::IsNullOrWhiteSpace($normalizedArtifactsPath) -and (Test-Path -LiteralPath $normalizedArtifactsPath -PathType Container)) {
    try {
        Assert-SashimiNoReparsePoint -Path $normalizedArtifactsPath
        $result.SummaryWritten = $true
        $protectedSummary = Protect-SashimiValidationData -Value $result
        Write-SashimiUtf8File -Path $result.SummaryPath -Content (ConvertTo-SashimiJson $protectedSummary -Pretty)
        $summaryWritten = $true
    }
    catch {
        $result.SummaryWritten = $false
        Add-SashimiValidationFailure -Code SummaryWriteFailed -Stage Artifacts -Message "Unable to write Unity validation summary: $($_.Exception.Message)"
        $result.Success = $false
        $result.Succeeded = $false
        $exitCode = 1
        $result.ExitCode = 1
        $result.Failures = $failures.ToArray()
    }
}

[Console]::Out.WriteLine((ConvertTo-SashimiJson (Protect-SashimiValidationData -Value $result)))
exit $exitCode
