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
$script:gitControlSecurityFailure = $false
$script:gitControlPassed = $false
$script:gitControlBaseline = $null
$script:gitControlSnapshotSequence = 0
$script:gitExecutable = ''
$script:gitTimeout = 0
$script:gitControlFixture = $null
$script:fixtureGitControlOverrides = @{}
$script:canonicalRepositoryUrl = ''
$script:unityArtifactRoot = ''
$script:unityArtifactStateRoot = ''
$script:unityArtifactBoundaryFailure = $false
$script:unityArtifactMaximumTotalBytes = [int64](128MB)
$script:unityLogMaximumBytes = [int64](8MB)
$script:unityXmlMaximumBytes = [int64](16MB)
$script:unityMetadataMaximumBytes = [int64](4MB)
$script:unityPngMaximumBytes = [int64](25MB)
$script:unityArtifactPolicies = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
$script:unityArtifactDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$script:gitStateEnvironmentOverrides = @(
    'GIT_DIR','GIT_COMMON_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY',
    'GIT_ALTERNATE_OBJECT_DIRECTORIES','GIT_REPLACE_REF_BASE','GIT_NAMESPACE','GIT_CEILING_DIRECTORIES',
    'GIT_CONFIG','GIT_CONFIG_COUNT','GIT_CONFIG_PARAMETERS','GIT_CONFIG_SYSTEM','GIT_CONFIG_GLOBAL',
    'GIT_CONFIG_NOSYSTEM','GIT_ATTR_NOSYSTEM','GIT_SSH','GIT_SSH_COMMAND','GIT_ASKPASS',
    'GIT_EXEC_PATH','GIT_TEMPLATE_DIR','GIT_OPTIONAL_LOCKS','GIT_TRACE','GIT_TRACE2','GIT_TRACE2_EVENT'
)
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
        if ($Kind -ceq 'Unity') {
            $plannedParameters.RemoveEnvironmentVariables = $removeEnvironmentNames
            $plannedParameters.RequireKillOnCloseJob = $true
        }
        else { $plannedParameters.RemoveEnvironmentVariables = $script:gitStateEnvironmentOverrides }
        $planned = Invoke-SashimiHostProcess @plannedParameters
        $planned | Add-Member -NotePropertyName Crashed -NotePropertyValue $false -Force
        return $planned
    }

    $fixtureEntry = Get-SashimiFixtureEntry -Fixture $Fixture -Group $FixtureGroup -Name $Name
    if ($null -ne $Fixture) {
        $fixtureOutputWriteLeases = [Collections.Generic.List[IDisposable]]::new()
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
            if ([bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'InvalidLogUtf8' -DefaultValue $false)) {
                if (-not (Test-SashimiHarnessMode)) { throw 'Raw-output encoding fixtures require the owned Host test harness.' }
                $invalidStream = [IO.FileStream]::new($LogPath,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read)
                try {
                    $invalidBytes = [byte[]]@(0x66,0x69,0x78,0x74,0x75,0x72,0x65,0xff,0xfe)
                    $invalidStream.Write($invalidBytes,0,$invalidBytes.Length)
                    $invalidStream.Flush($true)
                }
                finally { $invalidStream.Dispose() }
            }
            $fixtureLogLengthBytes = [int64](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'LogLengthBytes' -DefaultValue 0)
            if ($fixtureLogLengthBytes -gt 0) {
                if (-not (Test-SashimiHarnessMode)) { throw 'Raw-output size fixtures require the owned Host test harness.' }
                $sizeStream = [IO.FileStream]::new($LogPath,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read)
                try { $sizeStream.SetLength($fixtureLogLengthBytes); $sizeStream.Flush($true) }
                finally { $sizeStream.Dispose() }
            }
            if ([bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'KeepLogWriterOpen' -DefaultValue $false)) {
                if (-not (Test-SashimiHarnessMode)) { throw 'Raw-output growth fixtures require the owned Host test harness.' }
                $writeLease = [IO.FileStream]::new($LogPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::ReadWrite)
                [void]$writeLease.Seek(0,[IO.SeekOrigin]::End)
                $growthBytes = [Text.UTF8Encoding]::new($false).GetBytes("fixture output is still growing`n")
                $writeLease.Write($growthBytes,0,$growthBytes.Length)
                $writeLease.Flush($true)
                $fixtureOutputWriteLeases.Add($writeLease)
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($XmlPath) -and $createXml) {
            $defaultXml = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" />'
            $xmlContent = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'XmlContent' -DefaultValue $defaultXml)
            Write-SashimiUtf8File -Path $XmlPath -Content $xmlContent
        }
        if ($Kind -ceq 'Unity' -and $FixtureGroup -ceq 'Stages') {
            $gitControlMutation = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'GitControlMutation' -DefaultValue '')
            if (-not [string]::IsNullOrWhiteSpace($gitControlMutation)) {
                Invoke-SashimiUnityFixtureGitControlMutation -ProjectRoot $WorkingDirectory -Mutation $gitControlMutation
            }
            $publicArtifactMutation = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'PublicArtifactMutation' -DefaultValue '')
            if (-not [string]::IsNullOrWhiteSpace($publicArtifactMutation)) {
                if (-not (Test-SashimiHarnessMode)) { throw 'Public-artifact mutation fixtures require the owned Host test harness.' }
                $fixtureMarker = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'PublicArtifactMarker' -DefaultValue 'fixture-public-artifact-marker')
                switch ($publicArtifactMutation) {
                    'UnexpectedFile' {
                        Write-SashimiUtf8File -Path (Join-Path $script:unityArtifactRoot 'unexpected-public.bin') -Content $fixtureMarker
                    }
                    'AllowedPathSpoof' {
                        Write-SashimiUtf8File -Path (Join-Path $script:unityArtifactRoot 'CompileImport.log') -Content $fixtureMarker
                    }
                    'NestedFile' {
                        Write-SashimiUtf8File -Path (Join-Path $script:unityArtifactRoot 'unexpected-nested\payload.bin') -Content $fixtureMarker
                    }
                    'ReparseDirectory' {
                        $target = [string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'PublicArtifactReparseTarget' -DefaultValue '')
                        if ([string]::IsNullOrWhiteSpace($target) -or -not (Test-Path -LiteralPath $target -PathType Container)) {
                            throw 'The public-artifact reparse fixture target is missing.'
                        }
                        [void](New-Item -ItemType Junction -Path (Join-Path $script:unityArtifactRoot 'unexpected-reparse') -Target $target -ErrorAction Stop)
                    }
                    default { throw 'Unknown public-artifact mutation fixture.' }
                }
            }
        }
        $defaultStdOut = ''
        if ($FixtureGroup -ceq 'GitControl') {
            $defaultStdOut = switch ($Name) {
                'GitControlGitDirectory' { '.git' }
                'GitControlCommonDirectory' { '.git' }
                'GitControlHead' { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
                'GitControlSymbolicHead' { 'refs/heads/fixture-validation' }
                'GitControlBranch' { 'fixture-validation' }
                'GitControlOriginUrls' { $script:canonicalRepositoryUrl }
                'GitControlPushUrls' { $script:canonicalRepositoryUrl }
                'GitControlHooksPath' { 'NUL' }
                'GitControlLocalConfig' { "core.hookspath`nNUL`0remote.origin.url`n$($script:canonicalRepositoryUrl)`0" }
                'GitControlRefs' { "refs/heads/fixture-validation`0aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`0" }
                'GitControlWorktreeIdentity' { 'fixture-worktree' }
                default { '' }
            }
            if ($script:fixtureGitControlOverrides.ContainsKey($Name)) {
                $defaultStdOut = [string]$script:fixtureGitControlOverrides[$Name]
            }
        }
        $terminationConfirmed = [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'TerminationConfirmed' -DefaultValue $true)
        $killOnCloseJobAssigned = if ($Kind -ceq 'Unity') { [bool](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'KillOnCloseJobAssigned' -DefaultValue $true) } else { $false }
        $remainingDescendants = @()
        if ($Kind -ceq 'Unity') {
            $remainingDescendants = @((Get-SashimiPropertyValue -Object $fixtureEntry -Name 'RemainingDescendantProcessIds' -DefaultValue @()))
        }
        return [pscustomobject][ordered]@{
            FilePath = $FilePath
            Arguments = @($Arguments)
            Command = Format-SashimiCommand -FilePath $FilePath -ArgumentList $Arguments
            ExitCode = $exitCode
            StdOut = Protect-SashimiText ([string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'StdOut' -DefaultValue $defaultStdOut))
            StdErr = Protect-SashimiText ([string](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'StdErr' -DefaultValue ''))
            Succeeded = ($exitCode -eq 0 -and -not $timedOut -and -not $crashed -and $terminationConfirmed -and
                ($Kind -cne 'Unity' -or ($killOnCloseJobAssigned -and $remainingDescendants.Count -eq 0)))
            TimedOut = $timedOut
            Crashed = $crashed
            TerminationConfirmed = $terminationConfirmed
            KillOnCloseJobAssigned = $killOnCloseJobAssigned
            RemainingDescendantProcessIds = @($remainingDescendants | ForEach-Object { [int]$_ })
            ProcessId = Get-SashimiPropertyValue -Object $fixtureEntry -Name 'ProcessId' -DefaultValue $null
            DurationMilliseconds = [int64](Get-SashimiPropertyValue -Object $fixtureEntry -Name 'DurationMilliseconds' -DefaultValue 0)
            FixtureOutputWriteLeases = $fixtureOutputWriteLeases.ToArray()
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
        $processParameters.RequireKillOnCloseJob = $true
        if (-not [string]::IsNullOrWhiteSpace($OwnedUnityPidPath)) {
            $processParameters.OwnedProcessRecordPath = $OwnedUnityPidPath
        }
    }
    else {
        $processParameters.RemoveEnvironmentVariables = $script:gitStateEnvironmentOverrides
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

function Invoke-SashimiUnityFixtureGitControlMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Mutation
    )

    if (-not (Test-SashimiHarnessMode)) {
        throw 'Unity Git-control mutation fixtures are available only inside the owned Host test harness.'
    }
    $repositoryRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $gitRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.git'))
    if (-not (Test-SashimiPathWithin -Path $gitRoot -Root $repositoryRoot) -or
        -not (Test-Path -LiteralPath $gitRoot -PathType Container)) {
        throw 'Unity Git-control mutation fixture has no owned standalone .git directory.'
    }
    Assert-SashimiNoReparsePoint -Path $gitRoot -Recurse

    switch -CaseSensitive ($Mutation) {
        'RemoteOriginPushUrl' {
            $attackerUrl = 'https://attacker.invalid/replaced.git'
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'config') -Content "[remote `"origin`"]`nurl = $($script:canonicalRepositoryUrl)`npushurl = $attackerUrl`n"
            $script:fixtureGitControlOverrides['GitControlPushUrls'] = $attackerUrl
            $script:fixtureGitControlOverrides['GitControlLocalConfig'] = "core.hookspath`nNUL`0remote.origin.url`n$($script:canonicalRepositoryUrl)`0remote.origin.pushurl`n$attackerUrl`0"
        }
        'ConfigBytes' {
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'config') -Content "[fixture]`nvalue = changed-after-unity`n"
        }
        'HeadAndRef' {
            $refPath = Join-Path $gitRoot 'refs\heads\tampered'
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'HEAD') -Content "ref: refs/heads/tampered`n"
            Write-SashimiUtf8File -Path $refPath -Content (('b' * 40) + "`n")
        }
        'IndexAndStagedTree' {
            [IO.File]::WriteAllBytes((Join-Path $gitRoot 'index'), [Text.UTF8Encoding]::new($false).GetBytes('changed-index-after-unity'))
            $script:fixtureGitControlOverrides['GitControlStagedTree'] = 'changed-staged-tree-after-unity'
        }
        'HooksAndAlternates' {
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'hooks\post-checkout') -Content "fixture hook must never execute`n"
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'objects\info\alternates') -Content "C:\forbidden-object-store`n"
        }
        'MergeHeadOperation' {
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'MERGE_HEAD') -Content (('b' * 40) + "`n")
            Write-SashimiUtf8File -Path (Join-Path $gitRoot 'MERGE_MSG') -Content "fixture merge state must never reach Host commit`n"
        }
        'SequencerOperation' {
            $sequencerRoot = Join-Path $gitRoot 'sequencer'
            [IO.Directory]::CreateDirectory($sequencerRoot) | Out-Null
            Assert-SashimiNoReparsePoint -Path $sequencerRoot
            Write-SashimiUtf8File -Path (Join-Path $sequencerRoot 'todo') -Content "pick $('b' * 40) fixture-sequencer`n"
        }
        default { throw "Unsupported Unity Git-control mutation fixture '$Mutation'." }
    }
    Assert-SashimiNoReparsePoint -Path $gitRoot -Recurse
}

function Get-SashimiValidationControlFileState {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$RelativePath)

    $fullPath = [IO.Path]::GetFullPath((Join-Path $Root ($RelativePath.Replace('/', '\'))))
    if (-not (Test-SashimiPathWithin -Path $fullPath -Root $Root)) { throw 'A Git-control manifest path escaped its expected root.' }
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [pscustomobject][ordered]@{ Path=$RelativePath; Exists=$false; Length=0; Sha256='' }
    }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Git-control leaf '$RelativePath' is not an ordinary file."
    }
    return [pscustomobject][ordered]@{
        Path=$RelativePath
        Exists=$true
        Length=[int64]$item.Length
        Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Get-SashimiValidationControlTreeState {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$RelativeRoot)

    $treeRoot = [IO.Path]::GetFullPath((Join-Path $Root ($RelativeRoot.Replace('/', '\'))))
    if (-not (Test-SashimiPathWithin -Path $treeRoot -Root $Root)) { throw 'A Git-control tree escaped its expected root.' }
    if (-not (Test-Path -LiteralPath $treeRoot)) { return @() }
    Assert-SashimiNoReparsePoint -Path $treeRoot -Recurse
    return @(
        Get-ChildItem -LiteralPath $treeRoot -File -Force -Recurse -ErrorAction Stop |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring(([IO.Path]::GetFullPath($Root).TrimEnd('\') + '\').Length).Replace('\','/')
                [pscustomobject][ordered]@{
                    Path=$relative
                    Length=[int64]$_.Length
                    Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
    )
}

function Assert-SashimiValidationGitOperationStateAbsent {
    param([Parameter(Mandatory = $true)][string]$GitRoot)

    if (-not (Test-Path -LiteralPath $GitRoot -PathType Container)) { return }
    $operationEntries = @(
        'MERGE_HEAD','MERGE_MSG','MERGE_MODE','MERGE_RR','AUTO_MERGE','SQUASH_MSG',
        'CHERRY_PICK_HEAD','REVERT_HEAD','REBASE_HEAD',
        'BISECT_START','BISECT_LOG','BISECT_NAMES','BISECT_TERMS','BISECT_EXPECTED_REV','BISECT_ANCESTORS_OK',
        'NOTES_MERGE_REF','NOTES_MERGE_PARTIAL','NOTES_MERGE_WORKTREE',
        'rebase-apply','rebase-merge','sequencer'
    )
    foreach ($relativePath in $operationEntries) {
        if (Test-Path -LiteralPath (Join-Path $GitRoot $relativePath)) {
            throw "Git operation state '$relativePath' is present; Unity validation cannot authorize later delivery."
        }
    }
}

function Get-SashimiValidationGitControlManifest {
    param([Parameter(Mandatory = $true)][string]$Root)

    $gitRoot = [IO.Path]::GetFullPath((Join-Path $Root '.git')).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $gitRoot -PathType Container)) { return @() }
    Assert-SashimiNoReparsePoint -Path $gitRoot
    Assert-SashimiValidationGitOperationStateAbsent -GitRoot $gitRoot

    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($gitRoot)
    $records = [Collections.Generic.List[object]]::new()
    $entryCount = 0
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $relativeDirectory = if ($directory -ceq $gitRoot) { '' } else {
            $directory.Substring($gitRoot.Length + 1).Replace('\','/')
        }
        if ($relativeDirectory -ceq 'lfs' -or $relativeDirectory -cmatch '^modules/.+/lfs$') { continue }
        $objectPayloadDirectory = $relativeDirectory -ceq 'objects' -or
            $relativeDirectory -cmatch '^modules/.+/objects$'
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if ($objectPayloadDirectory -and $entry.Name -cne 'info') { continue }
            $entryCount++
            if ($entryCount -gt 250000) { throw 'Git control manifest exceeded its fixed entry bound.' }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Git control manifest contains a reparse point.'
            }
            $relative = $entry.FullName.Substring($gitRoot.Length + 1).Replace('\','/')
            if ($entry.PSIsContainer) {
                $records.Add([pscustomobject][ordered]@{ Kind='Directory'; Path=(".git/$relative"); Length=0; Sha256='' })
                $pending.Push($entry.FullName)
                continue
            }
            if ($entry.Name.EndsWith('.lock',[StringComparison]::OrdinalIgnoreCase)) {
                throw 'Git control manifest contains an active or stale lock file.'
            }
            if ([int64]$entry.Length -gt 16777216) {
                throw 'Git control manifest contains an oversized control file.'
            }
            $records.Add([pscustomobject][ordered]@{
                Kind='File'
                Path=(".git/$relative")
                Length=[int64]$entry.Length
                Sha256=(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
    }
    return @($records.ToArray() | Sort-Object Path,Kind)
}

function Get-SashimiValidationAttributeControlState {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return @() }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $skipAtRoot = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.git','Library','Temp','Logs','UserSettings','obj')) { [void]$skipAtRoot.Add($name) }
    $wanted = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.gitattributes','.lfsconfig','.gitmodules')) { [void]$wanted.Add($name) }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($rootFull)
    $records = [Collections.Generic.List[object]]::new()
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'A reparse point exists in the repository path used for Git attribute control.'
            }
            if ($entry.PSIsContainer) {
                if ($directory -ceq $rootFull -and $skipAtRoot.Contains($entry.Name)) { continue }
                $pending.Push($entry.FullName)
                continue
            }
            if (-not $wanted.Contains($entry.Name)) { continue }
            $relative = $entry.FullName.Substring($rootFull.Length + 1).Replace('\','/')
            $records.Add([pscustomobject][ordered]@{
                Path=$relative
                Length=[int64]$entry.Length
                Sha256=(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
    }
    return @($records.ToArray() | Sort-Object Path)
}

function Get-SashimiUnityGitControlSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Boundary,
        [switch]$DryRun
    )

    try {
        $repositoryFull = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
        $gitDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryFull '.git'))
        if (-not $DryRun) {
            if (-not (Test-Path -LiteralPath $gitDirectory -PathType Container)) { throw 'The Unity project is not the run-owned standalone Git worktree.' }
            Assert-SashimiNoReparsePoint -Path $repositoryFull
            Assert-SashimiNoReparsePoint -Path $gitDirectory
        }
        $read = {
            param([string]$Name,[string[]]$Arguments)
            (Invoke-SashimiValidationProcess -Name $Name -Kind Git -FilePath $script:gitExecutable -Arguments $Arguments -WorkingDirectory $repositoryFull -TimeoutSeconds $script:gitTimeout -Fixture $script:gitControlFixture -FixtureGroup GitControl -DryRun:$DryRun).StdOut
        }
        $gitDirectoryToken = (& $read 'GitControlGitDirectory' @('-C',$repositoryFull,'rev-parse','--git-dir')).Trim().Replace('\','/')
        $gitCommonDirectoryToken = (& $read 'GitControlCommonDirectory' @('-C',$repositoryFull,'rev-parse','--git-common-dir')).Trim().Replace('\','/')
        if (-not $DryRun -and ($gitDirectoryToken -cne '.git' -or $gitCommonDirectoryToken -cne '.git')) {
            throw 'Only the run-owned standalone .git directory may control Unity validation.'
        }
        $head = (& $read 'GitControlHead' @('-C',$repositoryFull,'rev-parse','HEAD')).Trim().ToLowerInvariant()
        $symbolicHead = (& $read 'GitControlSymbolicHead' @('-C',$repositoryFull,'symbolic-ref','--quiet','HEAD')).Trim()
        $branch = (& $read 'GitControlBranch' @('-C',$repositoryFull,'symbolic-ref','--quiet','--short','HEAD')).Trim()
        $upstream = (& $read 'GitControlUpstream' @('-C',$repositoryFull,'for-each-ref','--format=%(upstream:short)',"refs/heads/$branch")).Trim()
        $originLines = @((& $read 'GitControlOriginUrls' @('-C',$repositoryFull,'remote','get-url','--all','origin')) -split '\r?\n' | Where-Object { $_ -match '\S' })
        $pushOriginLines = @((& $read 'GitControlPushUrls' @('-C',$repositoryFull,'remote','get-url','--push','--all','origin')) -split '\r?\n' | Where-Object { $_ -match '\S' })
        $hooks = (& $read 'GitControlHooksPath' @('-C',$repositoryFull,'config','--local','--get','core.hooksPath')).Trim()
        $refs = & $read 'GitControlRefs' @('-C',$repositoryFull,'for-each-ref','--format=%(refname)%00%(objectname)%00%(symref)')
        $localConfig = & $read 'GitControlLocalConfig' @('-C',$repositoryFull,'config','--local','--null','--list')
        $indexFlags = & $read 'GitControlIndexFlags' @('-C',$repositoryFull,'ls-files','-v','-z')
        $indexEntries = & $read 'GitControlIndexEntries' @('-C',$repositoryFull,'ls-files','--stage','-z')
        $stagedTree = & $read 'GitControlStagedTree' @('-C',$repositoryFull,'diff','--cached','--raw','--no-abbrev','-z')
        $worktreeIdentity = & $read 'GitControlWorktreeIdentity' @('-C',$repositoryFull,'worktree','list','--porcelain','-z')
        $configRecords = @($localConfig -split "`0" | Where-Object { $_ -ne '' })
        $extensionRecords = @($configRecords | Where-Object { $_ -match '^(?i)extensions\.' } | Sort-Object)
        $remoteRecords = @($configRecords | Where-Object { $_ -match '^(?i)remote\.' } | Sort-Object)

        if (-not $DryRun) {
            if ($head -cnotmatch '^[0-9a-f]{40}$' -or $symbolicHead -cne "refs/heads/$branch") { throw 'Unity validation observed an invalid or detached HEAD.' }
            if ($originLines.Count -ne 1 -or $originLines[0] -cne $script:canonicalRepositoryUrl) { throw 'origin fetch URLs do not equal the canonical repository URL.' }
            if ($pushOriginLines.Count -ne 1 -or $pushOriginLines[0] -cne $script:canonicalRepositoryUrl) { throw 'origin push URLs do not equal the canonical repository URL.' }
            if ($hooks -cne 'NUL') { throw 'Repository hooks are not disabled.' }
            foreach ($record in $configRecords) {
                $key = if ($record.Contains("`n")) { $record.Substring(0,$record.IndexOf("`n")) } elseif ($record.Contains('=')) { $record.Substring(0,$record.IndexOf('=')) } else { $record }
                if ($key -match '^(?i)include(?:if)?\.') { throw 'Repository-local Git config includes external configuration.' }
                if ($key -match '^(?i)remote\.origin\.pushurl$') { throw 'Repository-local origin.pushurl is forbidden.' }
                if ($key -match '^(?i)remote\.(?!origin\.)[^.]+\.') { throw 'An unexpected repository-local Git remote exists.' }
            }
        }
        $controlFiles = @(
            '.git/HEAD','.git/config','.git/config.worktree','.git/index','.git/packed-refs','.git/shallow',
            '.git/commondir','.git/gitdir','.git/info/exclude','.git/info/attributes','.git/objects/info/alternates'
        ) | ForEach-Object { Get-SashimiValidationControlFileState -Root $repositoryFull -RelativePath $_ }
        $fixtureState = 'stable'
        if ($null -ne $script:gitControlFixture) {
            $stateSpec = Get-SashimiPropertyValue $script:gitControlFixture 'GitControlSnapshotStates' $null
            if ($null -ne $stateSpec) {
                if ($stateSpec -is [Collections.IEnumerable] -and $stateSpec -isnot [string] -and $stateSpec -isnot [pscustomobject]) {
                    $stateSequence = @($stateSpec)
                    if ($stateSequence.Count -gt 0) {
                        $stateIndex = [Math]::Min($script:gitControlSnapshotSequence, $stateSequence.Count - 1)
                        $fixtureState = [string]$stateSequence[$stateIndex]
                    }
                }
                else { $fixtureState = [string](Get-SashimiPropertyValue $stateSpec $Boundary 'stable') }
            }
        }
        $script:gitControlSnapshotSequence++
        return [pscustomobject][ordered]@{
            SchemaVersion=2; CanonicalWorkTree=$repositoryFull; CanonicalGitDirectory=$gitDirectory; CanonicalCommonDirectory=$gitDirectory
            GitDirectoryToken=$gitDirectoryToken; GitCommonDirectoryToken=$gitCommonDirectoryToken
            Head=$head; SymbolicHead=$symbolicHead; Branch=$branch; Upstream=$upstream; ExpectedBranch=$branch; ExpectedUpstream=$upstream
            OriginUrls=@($originLines); PushOriginUrls=@($pushOriginLines); HooksPath=$hooks
            RefsSha256=(Get-SashimiTextSha256 -Text ([string]$refs)); LocalConfigSha256=(Get-SashimiTextSha256 -Text ([string]$localConfig))
            RepositoryExtensionsSha256=(Get-SashimiTextSha256 -Text ([string]::Join("`0",$extensionRecords)))
            RemoteConfigurationSha256=(Get-SashimiTextSha256 -Text ([string]::Join("`0",$remoteRecords)))
            IndexFlagsSha256=(Get-SashimiTextSha256 -Text ([string]$indexFlags)); IndexEntriesSha256=(Get-SashimiTextSha256 -Text ([string]$indexEntries))
            StagedTreeSha256=(Get-SashimiTextSha256 -Text ([string]$stagedTree)); WorktreeIdentitySha256=(Get-SashimiTextSha256 -Text ([string]$worktreeIdentity))
            ControlFiles=@($controlFiles); GitControlManifest=@(Get-SashimiValidationGitControlManifest -Root $repositoryFull)
            RefFiles=@(Get-SashimiValidationControlTreeState -Root $repositoryFull -RelativeRoot '.git/refs')
            HookFiles=@(Get-SashimiValidationControlTreeState -Root $repositoryFull -RelativeRoot '.git/hooks')
            AttributeControls=@(Get-SashimiValidationAttributeControlState -Root $repositoryFull); FixtureBoundaryState=$fixtureState
        }
    }
    catch {
        $script:gitControlSecurityFailure = $true
        $script:gitControlPassed = $false
        Add-SashimiValidationFailure -Code GitControlSecurityFailure -Stage $Boundary -Message 'Git control state could not be securely captured or validated.'
        throw "Terminal Git-control security failure at $Boundary."
    }
}

function Assert-SashimiUnityGitControlUnchanged {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][string]$Boundary)

    $after = Get-SashimiUnityGitControlSnapshot -ProjectRoot $ProjectRoot -Boundary $Boundary
    foreach ($property in $script:gitControlBaseline.PSObject.Properties) {
        $name = [string]$property.Name
        # Preserve empty arrays as values instead of allowing PowerShell's
        # argument enumeration to turn one side of the comparison into no
        # output and the other into JSON null.
        $beforeJson = ConvertTo-SashimiJson -InputObject (, $property.Value)
        $afterProperty = $after.PSObject.Properties[$name]
        $afterValue = $null
        if ($null -ne $afterProperty) { $afterValue = $afterProperty.Value }
        $afterJson = ConvertTo-SashimiJson -InputObject (, $afterValue)
        if (-not [string]::Equals($beforeJson,$afterJson,[StringComparison]::Ordinal)) {
            $script:gitControlSecurityFailure = $true
            $script:gitControlPassed = $false
            Add-SashimiValidationFailure -Code GitControlDrift -Stage $Boundary -Message "Immutable Git-control field '$name' changed."
            throw "Terminal Git-control security failure after $Boundary."
        }
    }
    $script:gitControlPassed = $true
    Add-SashimiValidationCheck -Name "GitControl:$Boundary" -Passed $true -Detail 'Complete Git control state remained identical after the kill-on-close process boundary.'
}

function Register-SashimiUnityArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateRange(1, 268435456)][int64]$MaximumBytes,
        [Parameter(Mandatory = $true)][ValidateSet('StrictUtf8Text','Binary')][string]$ContentKind
    )

    if ([string]::IsNullOrWhiteSpace($script:unityArtifactRoot)) {
        throw 'Unity artifact policy was used before its root was initialized.'
    }
    $fullPath = ConvertTo-SashimiPath -Path $Path -AllowMissing -Lexical
    if (-not (Test-SashimiPathWithin -Path $fullPath -Root $script:unityArtifactRoot)) {
        throw 'A Unity artifact policy path escaped the exact artifact root.'
    }
    if ($script:unityArtifactPolicies.ContainsKey($fullPath)) {
        $existing = $script:unityArtifactPolicies[$fullPath]
        if ([int64]$existing.MaximumBytes -ne $MaximumBytes -or [string]$existing.ContentKind -cne $ContentKind) {
            throw 'A Unity artifact path was registered with conflicting policy.'
        }
        return
    }
    $script:unityArtifactPolicies.Add($fullPath, [pscustomobject][ordered]@{
            MaximumBytes = $MaximumBytes
            ContentKind = $ContentKind
            Published = $false
            Length = [int64]0
            Sha256 = ''
        })
    [void]$script:unityArtifactDirectories.Add($script:unityArtifactRoot)
    $cursor = Split-Path -Parent $fullPath
    while (-not (Test-SashimiPathEqual -Left $cursor -Right $script:unityArtifactRoot)) {
        if (-not (Test-SashimiPathWithin -Path $cursor -Root $script:unityArtifactRoot)) {
            throw 'A Unity artifact directory policy escaped the exact artifact root.'
        }
        [void]$script:unityArtifactDirectories.Add($cursor)
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            throw 'A Unity artifact directory policy has no trusted root.'
        }
        $cursor = $parent
    }
}

function Read-SashimiBoundedStableUtf8File {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateRange(1, 268435456)][int64]$MaximumBytes
    )

    $stream = $null
    try {
        Assert-SashimiNoReparsePoint -Path $Path
        $stream = [IO.FileStream]::new(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read,
            65536,
            [IO.FileOptions]::SequentialScan
        )
        $initialLength = [int64]$stream.Length
        if ($initialLength -lt 0 -or $initialLength -gt $MaximumBytes -or $initialLength -gt [int]::MaxValue) {
            throw 'size'
        }
        $bytes = [byte[]]::new([int]$initialLength)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) { throw 'short-read' }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1 -or [int64]$stream.Length -ne $initialLength) {
            throw 'growth'
        }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        return [pscustomobject][ordered]@{ Text = $text; Length = $initialLength; Sha256 = $sha256 }
    }
    catch {
        throw [IO.InvalidDataException]::new('Unity text output was rejected because it was unavailable, changing, oversized, or not strict UTF-8.')
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Measure-SashimiBoundedStableFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateRange(1, 268435456)][int64]$MaximumBytes
    )

    $stream = $null
    $hash = $null
    try {
        Assert-SashimiNoReparsePoint -Path $Path
        $stream = [IO.FileStream]::new(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read,
            65536,
            [IO.FileOptions]::SequentialScan
        )
        $initialLength = [int64]$stream.Length
        if ($initialLength -lt 0 -or $initialLength -gt $MaximumBytes) { throw 'size' }
        $hash = [Security.Cryptography.SHA256]::Create()
        $buffer = [byte[]]::new(65536)
        $totalRead = [int64]0
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $totalRead += $read
            if ($totalRead -gt $MaximumBytes) { throw 'growth' }
            [void]$hash.TransformBlock($buffer, 0, $read, $null, 0)
        }
        [void]$hash.TransformFinalBlock([byte[]]::new(0), 0, 0)
        if ($totalRead -ne $initialLength -or [int64]$stream.Length -ne $initialLength) { throw 'growth' }
        return [pscustomobject][ordered]@{
            Length = $initialLength
            Sha256 = [Convert]::ToHexString($hash.Hash).ToLowerInvariant()
        }
    }
    catch {
        throw [IO.InvalidDataException]::new('Unity artifact was rejected because it was unavailable, changing, or oversized.')
    }
    finally {
        if ($null -ne $hash) { $hash.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Remove-SashimiUnityTreeWithoutReparseTraversal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ExpectedParent
    )

    $rootFull = ConvertTo-SashimiPath -Path $Root -AllowMissing -Lexical
    $parentFull = ConvertTo-SashimiPath -Path $ExpectedParent -AllowMissing -Lexical
    if (-not (Test-SashimiPathWithin -Path $rootFull -Root $parentFull)) {
        throw 'Refusing to remove a Unity quarantine tree outside its exact State parent.'
    }
    if (-not (Test-Path -LiteralPath $rootFull)) { return }
    $rootItem = Get-Item -LiteralPath $rootFull -Force -ErrorAction Stop
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        if ($rootItem.PSIsContainer) { [IO.Directory]::Delete($rootFull, $false) }
        else { [IO.File]::Delete($rootFull) }
        return
    }
    if (-not $rootItem.PSIsContainer) {
        [IO.File]::Delete($rootFull)
        return
    }

    $pending = [Collections.Generic.Stack[string]]::new()
    $directories = [Collections.Generic.List[string]]::new()
    $pending.Push($rootFull)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $directories.Add($directory)
        foreach ($entry in (Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            $entryFull = [IO.Path]::GetFullPath($entry.FullName)
            if (-not (Test-SashimiPathWithin -Path $entryFull -Root $rootFull)) {
                throw 'A Unity quarantine entry escaped its exact root.'
            }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                if ($entry.PSIsContainer) { [IO.Directory]::Delete($entryFull, $false) }
                else { [IO.File]::Delete($entryFull) }
            }
            elseif ($entry.PSIsContainer) { $pending.Push($entryFull) }
            else { [IO.File]::Delete($entryFull) }
        }
    }
    foreach ($directory in @($directories | Sort-Object { $_.Length } -Descending)) {
        [IO.Directory]::Delete($directory, $false)
    }
}

function Remove-SashimiUnsafeUnityArtifactRoot {
    [CmdletBinding()]
    param()

    if ([string]::IsNullOrWhiteSpace($script:unityArtifactRoot) -or
        -not (Test-Path -LiteralPath $script:unityArtifactRoot)) { return }
    if ([string]::IsNullOrWhiteSpace($script:unityArtifactStateRoot)) {
        throw 'Unity artifact State root is unavailable for closed-tree quarantine.'
    }
    if (-not (Test-Path -LiteralPath $script:unityArtifactStateRoot -PathType Container)) {
        [IO.Directory]::CreateDirectory($script:unityArtifactStateRoot) | Out-Null
    }
    Assert-SashimiNoReparsePoint -Path $script:unityArtifactStateRoot
    $sourceItem = Get-Item -LiteralPath $script:unityArtifactRoot -Force -ErrorAction Stop
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        if ($sourceItem.PSIsContainer) { [IO.Directory]::Delete($sourceItem.FullName, $false) }
        else { [IO.File]::Delete($sourceItem.FullName) }
        return
    }
    $quarantine = Join-Path $script:unityArtifactStateRoot ('.discarded-unity-artifacts-' + [Guid]::NewGuid().ToString('N'))
    if ($sourceItem.PSIsContainer) { [IO.Directory]::Move($sourceItem.FullName, $quarantine) }
    else { [IO.File]::Move($sourceItem.FullName, $quarantine, $false) }
    Remove-SashimiUnityTreeWithoutReparseTraversal -Root $quarantine -ExpectedParent $script:unityArtifactStateRoot
    if (Test-Path -LiteralPath $quarantine) {
        throw 'Unity artifact quarantine removal could not be confirmed.'
    }
}

function Get-SashimiUnityArtifactTreeMeasurement {
    [CmdletBinding()]
    param()

    if (-not (Test-Path -LiteralPath $script:unityArtifactRoot -PathType Container)) {
        throw 'The exact Unity artifact root is missing.'
    }
    Assert-SashimiNoReparsePoint -Path $script:unityArtifactRoot
    $pending = [Collections.Generic.Stack[string]]::new()
    $records = [Collections.Generic.List[string]]::new()
    $seenFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending.Push($script:unityArtifactRoot)
    $totalBytes = [int64]0
    $entryCount = 0
    $maximumEntries = $script:unityArtifactPolicies.Count + $script:unityArtifactDirectories.Count
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in (Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            $entryCount++
            if ($entryCount -gt $maximumEntries) { throw 'The Unity artifact tree contains too many entries.' }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'The Unity artifact tree contains a forbidden reparse point.'
            }
            $fullPath = ConvertTo-SashimiPath -Path $entry.FullName -Lexical
            if (-not (Test-SashimiPathWithin -Path $fullPath -Root $script:unityArtifactRoot)) {
                throw 'The Unity artifact tree escaped its exact root.'
            }
            if ($entry.PSIsContainer) {
                if (-not $script:unityArtifactDirectories.Contains($fullPath)) {
                    throw 'The Unity artifact tree contains an unexpected directory.'
                }
                $records.Add('D:' + $fullPath.Substring($script:unityArtifactRoot.Length).Replace('\','/'))
                $pending.Push($fullPath)
                continue
            }
            $policy = $null
            if (-not $script:unityArtifactPolicies.TryGetValue($fullPath, [ref]$policy)) {
                throw 'The Unity artifact tree contains an unexpected file.'
            }
            if (-not [bool]$policy.Published) {
                throw 'The Unity artifact tree contains a file that was not promoted by the Host.'
            }
            $measurement = if ([string]$policy.ContentKind -ceq 'StrictUtf8Text') {
                Read-SashimiBoundedStableUtf8File -Path $fullPath -MaximumBytes ([int64]$policy.MaximumBytes)
            }
            else {
                Measure-SashimiBoundedStableFile -Path $fullPath -MaximumBytes ([int64]$policy.MaximumBytes)
            }
            $totalBytes += [int64]$measurement.Length
            if ($totalBytes -gt $script:unityArtifactMaximumTotalBytes) {
                throw 'The Unity artifact tree exceeds its total byte quota.'
            }
            if ([int64]$measurement.Length -ne [int64]$policy.Length -or
                -not [string]::Equals([string]$measurement.Sha256,[string]$policy.Sha256,[StringComparison]::Ordinal)) {
                throw 'A promoted Unity artifact changed after Host validation.'
            }
            [void]$seenFiles.Add($fullPath)
            $records.Add('F:' + $fullPath.Substring($script:unityArtifactRoot.Length).Replace('\','/') + ':' + $measurement.Length + ':' + $measurement.Sha256)
        }
    }
    foreach ($policyEntry in $script:unityArtifactPolicies.GetEnumerator()) {
        if ([bool]$policyEntry.Value.Published -and -not $seenFiles.Contains([string]$policyEntry.Key)) {
            throw 'A Host-promoted Unity artifact is missing from the public tree.'
        }
    }
    return [pscustomobject][ordered]@{
        TotalBytes = $totalBytes
        Records = @($records | Sort-Object)
    }
}

function Assert-SashimiUnityArtifactTree {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Boundary)

    if ($script:unityArtifactBoundaryFailure) {
        throw 'The Unity artifact boundary is already terminally failed.'
    }
    try {
        $first = Get-SashimiUnityArtifactTreeMeasurement
        $second = Get-SashimiUnityArtifactTreeMeasurement
        if ($first.TotalBytes -ne $second.TotalBytes -or
            -not [string]::Equals(
                [string]::Join("`n", @($first.Records)),
                [string]::Join("`n", @($second.Records)),
                [StringComparison]::Ordinal
            )) {
            throw 'The Unity artifact tree changed during validation.'
        }
    }
    catch {
        $script:unityArtifactBoundaryFailure = $true
        $removalConfirmed = $false
        try {
            Remove-SashimiUnsafeUnityArtifactRoot
            $removalConfirmed = -not (Test-Path -LiteralPath $script:unityArtifactRoot)
        }
        catch { $removalConfirmed = $false }
        if (-not $removalConfirmed) {
            throw "Unity artifact boundary validation failed at $Boundary; removal of the public tree could not be confirmed."
        }
        throw "Unity artifact boundary validation failed at $Boundary; the public tree was removed without retaining its unvalidated content."
    }
}

function Write-SashimiBoundedUnityTextArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )

    $fullPath = ConvertTo-SashimiPath -Path $Path -AllowMissing -Lexical
    $policy = $null
    if (-not $script:unityArtifactPolicies.TryGetValue($fullPath, [ref]$policy) -or
        [string]$policy.ContentKind -cne 'StrictUtf8Text') {
        throw 'Refusing to write a Unity text artifact outside the exact manifest.'
    }
    Assert-SashimiUnityArtifactTree -Boundary 'immediately before a text artifact write'
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($Content)
    if ([int64]$bytes.Length -gt [int64]$policy.MaximumBytes) {
        throw 'Refusing to write an oversized Unity text artifact.'
    }
    if (Test-Path -LiteralPath $fullPath) { throw 'Refusing to overwrite a Unity text artifact.' }
    $parent = Split-Path -Parent $fullPath
    if (-not $script:unityArtifactDirectories.Contains($parent)) {
        throw 'Refusing to create an unregistered Unity artifact directory.'
    }
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    Assert-SashimiNoReparsePoint -Path $parent
    $temporaryPath = Join-Path $parent ('.bounded-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $stream = [IO.FileStream]::new($temporaryPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {
            $stream.Write($bytes,0,$bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        $verification = Read-SashimiBoundedStableUtf8File -Path $temporaryPath -MaximumBytes ([int64]$policy.MaximumBytes)
        if (-not [string]::Equals($Content,[string]$verification.Text,[StringComparison]::Ordinal)) {
            throw 'A bounded Unity text artifact failed verification.'
        }
        [IO.File]::Move($temporaryPath,$fullPath,$false)
        $temporaryPath = ''
        $policy.Published = $true
        $policy.Length = [int64]$verification.Length
        $policy.Sha256 = [string]$verification.Sha256
        Assert-SashimiUnityArtifactTree -Boundary 'immediately after a text artifact write'
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
            try { [IO.File]::Delete($temporaryPath) } catch { }
        }
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
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][ValidateRange(1, 268435456)][int64]$MaximumSourceBytes
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { return $false }
    if (Test-Path -LiteralPath $DestinationPath) { throw "Refusing to overwrite a Unity validation artifact: $DestinationPath" }
    $raw = Read-SashimiBoundedStableUtf8File -Path $SourcePath -MaximumBytes $MaximumSourceBytes
    $sanitizedText = Protect-SashimiUnityOutputText -Text ([string]$raw.Text)
    if (Test-SashimiRecognizableSensitiveText -Text $sanitizedText -SensitiveValues @($script:unitySensitiveEnvironmentValues)) {
        throw 'Sanitized Unity artifact failed its final content verification.'
    }
    Write-SashimiBoundedUnityTextArtifact -Path $DestinationPath -Content $sanitizedText
    try { [IO.File]::Delete($SourcePath) } catch { }
    return [pscustomobject][ordered]@{
        Published = $true
        Text = $sanitizedText
        RawLength = [int64]$raw.Length
    }
}

function Get-SashimiUnityLogDiagnostics {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content,
        [switch]$Compile
    )

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

function Get-SashimiUnityNUnitSummaryFromText {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content)

    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = $script:unityXmlMaximumBytes
        $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($Content), $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        finally { $reader.Dispose() }
    }
    catch {
        throw 'Unity result XML is not a bounded, well-formed document.'
    }
    $root = $document.'test-run'
    if ($null -eq $root) { $root = $document.'test-results' }
    if ($null -eq $root) { throw 'Unity result XML has no supported root.' }
    try {
        $total = [int]$root.total
        $passed = [int]$root.passed
        $failed = [int]$root.failed
        $skipped = [int]$root.skipped
        $inconclusive = [int]$root.inconclusive
    }
    catch { throw 'Unity result XML contains invalid numeric summary fields.' }
    $strict = ([string]$root.result -ceq 'Passed' -and $total -gt 0 -and $passed -eq $total -and
        $failed -eq 0 -and $skipped -eq 0 -and $inconclusive -eq 0)
    return [pscustomobject][ordered]@{
        Result = [string]$root.result
        Total = $total
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        Inconclusive = $inconclusive
        StrictPass = $strict
    }
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
        TerminationConfirmed = [bool](Get-SashimiPropertyValue -Object $native -Name 'TerminationConfirmed' -DefaultValue $false)
        KillOnCloseJobAssigned = [bool](Get-SashimiPropertyValue -Object $native -Name 'KillOnCloseJobAssigned' -DefaultValue $false)
        RemainingDescendantProcessIds = @((Get-SashimiPropertyValue -Object $native -Name 'RemainingDescendantProcessIds' -DefaultValue @(-1)))
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

    $fixtureOutputWriteLeases = @((Get-SashimiPropertyValue -Object $native -Name 'FixtureOutputWriteLeases' -DefaultValue @()))
    try {
        $terminationConfirmed = [bool](Get-SashimiPropertyValue -Object $native -Name 'TerminationConfirmed' -DefaultValue $false)
        $killOnCloseJobAssigned = [bool](Get-SashimiPropertyValue -Object $native -Name 'KillOnCloseJobAssigned' -DefaultValue $false)
        $remainingDescendants = @((Get-SashimiPropertyValue -Object $native -Name 'RemainingDescendantProcessIds' -DefaultValue @(-1)))
        if (-not $terminationConfirmed -or -not $killOnCloseJobAssigned -or $remainingDescendants.Count -ne 0) {
            $script:rawValidationCleanupSafe = $false
            $script:gitControlSecurityFailure = $true
            $script:gitControlPassed = $false
            if (-not $terminationConfirmed) {
                Add-SashimiValidationFailure -Code 'UnityTerminationUnconfirmed' -Stage $Name -Message 'Unity process-tree termination could not be confirmed.' -NativeExitCode $native.ExitCode
            }
            Add-SashimiValidationFailure -Code 'UnityProcessBoundaryUnconfirmed' -Stage $Name -Message 'The per-stage kill-on-close job or descendant termination proof was absent; Git state was not trusted.' -NativeExitCode $native.ExitCode
            throw "Terminal Git-control security failure after Unity stage '$Name': process-boundary closure was not proved."
        }

        Assert-SashimiUnityGitControlUnchanged -ProjectRoot $ProjectRoot -Boundary "after Unity stage $Name"

        $publishedLog = $null
        $publishedXml = $null
        if ($terminationConfirmed) {
            $publishedLog = Publish-SashimiSanitizedTextArtifact -SourcePath $RawLogPath -DestinationPath $LogPath -MaximumSourceBytes $script:unityLogMaximumBytes
            if ($TestStage -and -not [string]::IsNullOrWhiteSpace($RawXmlPath)) {
                $publishedXml = Publish-SashimiSanitizedTextArtifact -SourcePath $RawXmlPath -DestinationPath $XmlPath -MaximumSourceBytes $script:unityXmlMaximumBytes
            }
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
        if (-not $stage.LogExists -or $null -eq $publishedLog) {
            Add-SashimiValidationFailure -Code 'UnityLogMissing' -Stage $Name -Message "Unity log is missing: $LogPath" -NativeExitCode $native.ExitCode
        }
        else {
            $stage.Diagnostics = @(Get-SashimiUnityLogDiagnostics -Content ([string]$publishedLog.Text) -Compile:$Compile)
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
            if (-not $stage.XmlExists -or $null -eq $publishedXml) {
                Add-SashimiValidationFailure -Code 'UnityResultXmlMissing' -Stage $Name -Message "Unity result XML is missing: $XmlPath" -NativeExitCode $native.ExitCode
            }
            else {
                try {
                    $stage.XmlSummary = Get-SashimiUnityNUnitSummaryFromText -Content ([string]$publishedXml.Text)
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
    finally {
        foreach ($fixtureOutputWriteLease in $fixtureOutputWriteLeases) {
            if ($null -ne $fixtureOutputWriteLease) {
                try { $fixtureOutputWriteLease.Dispose() } catch { }
            }
        }
    }
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
        $destinationFull = ConvertTo-SashimiPath -Path $destination -AllowMissing -Lexical
        $destinationPolicy = $null
        if (-not $script:unityArtifactPolicies.TryGetValue($destinationFull,[ref]$destinationPolicy) -or
            [string]$destinationPolicy.ContentKind -cne 'Binary') {
            throw "$Kind artifact hook has no exact public-artifact policy: $relative"
        }
        Assert-SashimiUnityArtifactTree -Boundary "immediately before $Kind artifact promotion"
        Assert-SashimiNoReparsePoint -Path $destinationFull
        $destinationParent = Split-Path -Parent $destination
        [IO.Directory]::CreateDirectory($destinationParent) | Out-Null
        Assert-SashimiNoReparsePoint -Path $destinationParent
        $temporaryPath = Join-Path $destinationParent ('.bounded-image-' + [Guid]::NewGuid().ToString('N') + '.tmp')
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
            $bitmap.Save($temporaryPath, [Drawing.Imaging.ImageFormat]::Png)
            $verifiedImage = Measure-SashimiBoundedStableFile -Path $temporaryPath -MaximumBytes ([int64]$destinationPolicy.MaximumBytes)
            [IO.File]::Move($temporaryPath,$destinationFull,$false)
            $temporaryPath = ''
            $destinationPolicy.Published = $true
            $destinationPolicy.Length = [int64]$verifiedImage.Length
            $destinationPolicy.Sha256 = [string]$verifiedImage.Sha256
            Assert-SashimiUnityArtifactTree -Boundary "immediately after $Kind artifact promotion"
        }
        catch {
            throw "$Kind artifact hook could not sanitize '$relative': $($_.Exception.Message)"
        }
        finally {
            if ($null -ne $graphics) { $graphics.Dispose() }
            if ($null -ne $bitmap) { $bitmap.Dispose() }
            if ($null -ne $image) { $image.Dispose() }
            if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
                try { [IO.File]::Delete($temporaryPath) } catch { }
            }
        }
        $copied.Add([pscustomobject][ordered]@{
                Kind = $Kind
                SourceRelativePath = $relative
                ArtifactPath = $destinationFull
                Sha256 = (Get-FileHash -LiteralPath $destinationFull -Algorithm SHA256).Hash.ToLowerInvariant()
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
    GitControlPassed = $false
    GitControlSecurityFailure = $false
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
    $script:canonicalRepositoryUrl = [string](Get-SashimiPropertyValue -Object $config -Name 'RemoteUrl' -DefaultValue '')
    if ($script:canonicalRepositoryUrl -cne 'https://github.com/DongGyunLeeeee/sashimi-boy-unity.git') {
        throw 'Protected configuration does not contain the immutable canonical repository URL.'
    }
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
    $script:gitExecutable = $gitExecutable
    $script:gitTimeout = $gitTimeout
    foreach ($timeout in @($unityTimeout, $generatorTimeout, $gitTimeout)) {
        if ($timeout -lt 1 -or $timeout -gt 86400) { throw 'Unity/Generator/Git timeout values must be between 1 and 86400 seconds.' }
    }

    if (-not [string]::IsNullOrWhiteSpace($ValidationFixturePath)) {
        $fixturePath = Assert-SashimiFixtureAllowed -FixturePath $ValidationFixturePath -DryRun:$DryRun
        $fixture = Read-SashimiJsonFile -Path $fixturePath
        $fixtureSchema = [int](Get-SashimiPropertyValue -Object $fixture -Name 'SchemaVersion' -DefaultValue 1)
        if ($fixtureSchema -ne 1) { throw "Validation fixture SchemaVersion must be 1; received $fixtureSchema." }
    }
    $script:gitControlFixture = $fixture
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
    $script:unityArtifactRoot = $normalizedArtifactsPath
    $script:unityArtifactStateRoot = ConvertTo-SashimiPath -Path $stateRoot -AllowMissing -Lexical
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

    foreach ($artifactPath in @($compileLog,$editLog,$playLog)) {
        Register-SashimiUnityArtifact -Path $artifactPath -MaximumBytes $script:unityLogMaximumBytes -ContentKind StrictUtf8Text
    }
    foreach ($artifactPath in @($editXml,$playXml)) {
        Register-SashimiUnityArtifact -Path $artifactPath -MaximumBytes $script:unityXmlMaximumBytes -ContentKind StrictUtf8Text
    }
    Register-SashimiUnityArtifact -Path $result.SummaryPath -MaximumBytes $script:unityMetadataMaximumBytes -ContentKind StrictUtf8Text
    Register-SashimiUnityArtifact -Path (Join-Path $normalizedArtifactsPath 'KnownUnityDefaultDrift.diff') -MaximumBytes $script:unityMetadataMaximumBytes -ContentKind StrictUtf8Text
    if ($null -ne $validationDefinition) {
        foreach ($artifactPath in @($generatorRun1Log,$generatorRun2Log)) {
            Register-SashimiUnityArtifact -Path $artifactPath -MaximumBytes $script:unityLogMaximumBytes -ContentKind StrictUtf8Text
        }
        foreach ($artifactName in @('GeneratorRun1.snapshot.json','GeneratorRun2.snapshot.json')) {
            Register-SashimiUnityArtifact -Path (Join-Path $normalizedArtifactsPath $artifactName) -MaximumBytes $script:unityMetadataMaximumBytes -ContentKind StrictUtf8Text
        }
        foreach ($hookSpec in @(
                @{ Kind='Screenshot'; Paths=$screenshotPaths },
                @{ Kind='Preview'; Paths=$previewPaths }
            )) {
            foreach ($relativeHookPath in @($hookSpec.Paths)) {
                $hookDestination = Join-Path (Join-Path $normalizedArtifactsPath ($hookSpec.Kind + 's')) ($relativeHookPath.Replace('/',[IO.Path]::DirectorySeparatorChar))
                Register-SashimiUnityArtifact -Path $hookDestination -MaximumBytes $script:unityPngMaximumBytes -ContentKind Binary
            }
        }
    }

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
        Add-SashimiValidationCheck -Name 'GitControlPerStage' -Passed $true -Planned $true -Detail 'Complete Git control state and the per-stage kill-on-close process boundary will be checked after every Unity stage.'
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
        $result.GitControlPassed = $true
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
        Assert-SashimiUnityArtifactTree -Boundary 'before Unity output production'
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
            $script:gitControlBaseline = Get-SashimiUnityGitControlSnapshot -ProjectRoot $normalizedProjectPath -Boundary 'immediately before first Unity stage'
            $script:gitControlPassed = $true
            Add-SashimiValidationCheck -Name 'GitControlBaseline' -Passed $true -Detail 'Complete Git control state was captured immediately before Unity execution.'
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
                    Write-SashimiBoundedUnityTextArtifact -Path (Join-Path $normalizedArtifactsPath 'GeneratorRun1.snapshot.json') -Content (ConvertTo-SashimiJson $snapshot1 -Pretty)

                    $stages.GeneratorRun2 = Invoke-SashimiUnityValidationStage -Name GeneratorRun2 -Arguments $generatorRun2Arguments -LogPath $generatorRun2Log -RawLogPath $generatorRun2RawLog -UnityExecutable $unityExecutable -ProjectRoot $normalizedProjectPath -TimeoutSeconds $generatorTimeout -Fixture $fixture -Compile
                    $generatorPassed = [bool]$stages.GeneratorRun2.Success
                    if ($generatorPassed) {
                        $run2Fixture = Get-SashimiPropertyValue -Object $fixtureDeterminism -Name 'Run2' -DefaultValue $null
                        $snapshot2 = if ($null -ne $run2Fixture) { @($run2Fixture) } else { @(Get-SashimiDeterminismSnapshot -ProjectRoot $normalizedProjectPath -RelativePaths $determinismPaths) }
                        $result.Determinism.Run2Snapshot = $snapshot2
                        Write-SashimiBoundedUnityTextArtifact -Path (Join-Path $normalizedArtifactsPath 'GeneratorRun2.snapshot.json') -Content (ConvertTo-SashimiJson $snapshot2 -Pretty)
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
            Write-SashimiBoundedUnityTextArtifact -Path $driftArtifact -Content ((Protect-SashimiText $knownDrift.StdOut) + [Environment]::NewLine)
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

        Assert-SashimiUnityArtifactTree -Boundary 'after every Unity artifact and hook promotion'
        Add-SashimiValidationCheck -Name 'UnityArtifactClosedTree' -Passed $true -Detail 'Every public Unity artifact matched the recursive exact allowlist, strict type policy, per-file quota, and total quota.'
        $result.GitControlPassed = [bool]$script:gitControlPassed
        $result.GitControlSecurityFailure = [bool]$script:gitControlSecurityFailure
        $result.Success = ($failures.Count -eq 0 -and $result.GitControlPassed -and -not $result.GitControlSecurityFailure)
        $result.Succeeded = $result.Success
        $exitCode = if ($result.Success) { 0 } else { 1 }
    }
}
catch {
    $failureCode = if ($script:unityArtifactBoundaryFailure) { 'UnityArtifactBoundaryViolation' } else { 'UnhandledValidationFailure' }
    Add-SashimiValidationFailure -Code $failureCode -Stage PreflightOrUnhandled -Message $_.Exception.Message
    $result.Success = $false
    $result.Succeeded = $false
    $result.GitControlPassed = $false
    $result.GitControlSecurityFailure = [bool]$script:gitControlSecurityFailure
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
$result.GitControlSecurityFailure = [bool]$script:gitControlSecurityFailure
if (-not $DryRun) { $result.GitControlPassed = [bool]$script:gitControlPassed }
$result.OwnedUnityProcessIds = @($ownedUnityProcessIds | Sort-Object -Unique)
$result.ArtifactHooks = $artifactHooks.ToArray()
$result.Commands = $commands.ToArray()
$result.Checks = $checks.ToArray()
$result.Failures = $failures.ToArray()

if (-not $DryRun -and -not $script:unityArtifactBoundaryFailure -and
    -not [string]::IsNullOrWhiteSpace($normalizedArtifactsPath) -and
    (Test-Path -LiteralPath $normalizedArtifactsPath -PathType Container)) {
    try {
        $result.SummaryWritten = $true
        $protectedSummary = Protect-SashimiValidationData -Value $result
        Write-SashimiBoundedUnityTextArtifact -Path $result.SummaryPath -Content (ConvertTo-SashimiJson $protectedSummary -Pretty)
        Assert-SashimiUnityArtifactTree -Boundary 'after final Unity summary promotion'
        $summaryWritten = $true
    }
    catch {
        $result.SummaryWritten = $false
        $summaryFailureCode = if ($script:unityArtifactBoundaryFailure) { 'UnityArtifactBoundaryViolation' } else { 'SummaryWriteFailed' }
        Add-SashimiValidationFailure -Code $summaryFailureCode -Stage Artifacts -Message "Unable to write Unity validation summary: $($_.Exception.Message)"
        $result.Success = $false
        $result.Succeeded = $false
        $exitCode = 1
        $result.ExitCode = 1
        $result.Failures = $failures.ToArray()
    }
}

[Console]::Out.WriteLine((ConvertTo-SashimiJson (Protect-SashimiValidationData -Value $result)))
exit $exitCode
