#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Developer', 'Reviewer')]
    [string]$Role,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WorktreePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaseCheckoutPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DeveloperWorktreePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReviewerWorktreePath,

    [ValidateNotNullOrEmpty()]
    [string]$Repository = 'DongGyunLeeeee/sashimi-boy-unity',

    [ValidateNotNullOrEmpty()]
    [string]$ProjectOwner = 'DongGyunLeeeee',

    [ValidateRange(1, 2147483647)]
    [int]$ProjectNumber = 1,

    [ValidateNotNullOrEmpty()]
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe',

    [ValidateNotNullOrEmpty()]
    [string]$ExpectedUnityVersion = '6000.4.0f1',

    [ValidateScript({ $_ -ge 0 })]
    [long]$MinimumTempFreeBytes = 10GB,

    [ValidateNotNullOrEmpty()]
    [string]$GitExecutable = 'git',

    [ValidateNotNullOrEmpty()]
    [string]$GitHubCliPath = 'gh',

    [switch]$DryRun,

    [switch]$SkipUnityProcessCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Automation common helpers are missing: $commonPath")
    exit 1
}
. $commonPath

$checks = New-Object 'System.Collections.Generic.List[object]'
$errors = New-Object 'System.Collections.Generic.List[string]'
$preflightExitCode = 0
$specVersion = $null
$normalizedWorktree = $null
$tempFreeBytes = $null

function Add-PreflightCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [bool]$Passed,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Detail,

        [bool]$Skipped = $false
    )

    $checks.Add([pscustomobject][ordered]@{
            name    = $Name
            passed  = $Passed
            skipped = $Skipped
            detail  = $Detail
        })
}

function Stop-Preflight {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [int]$NativeExitCode = 1
    )

    $script:preflightExitCode = ConvertTo-AutomationExitCode -NativeExitCode $NativeExitCode
    throw $Message
}

function Invoke-RequiredNativeCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [string]$WorkingDirectory,

        [string]$SuccessDetail
    )

    $invocation = Invoke-AutomationNativeCommand -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory
    if (-not $invocation.Succeeded) {
        $detail = $invocation.StdErr
        if (-not $detail) {
            $detail = $invocation.StdOut
        }
        $command = Format-AutomationCommand -FilePath $FilePath -ArgumentList $ArgumentList
        Add-PreflightCheck -Name $Name -Passed $false -Detail ("command={0}; exit={1}; stderr={2}" -f $command, $invocation.ExitCode, $detail)
        Stop-Preflight -Message ("{0} failed. Command: {1}; exit code: {2}; stderr: {3}" -f $Name, $command, $invocation.ExitCode, $detail) -NativeExitCode $invocation.ExitCode
    }

    $reportedDetail = if ($PSBoundParameters.ContainsKey('SuccessDetail')) { $SuccessDetail } else { $invocation.StdOut }
    Add-PreflightCheck -Name $Name -Passed $true -Detail $reportedDetail
    return $invocation
}

function Test-ExactStringSet {
    param(
        [string[]]$Actual,
        [string[]]$Expected
    )

    if ($Actual.Count -ne $Expected.Count) {
        return $false
    }
    foreach ($expectedValue in $Expected) {
        if (-not ($Actual -ccontains $expectedValue)) {
            return $false
        }
    }
    return $true
}

try {
    foreach ($configuredPath in @($WorktreePath, $BaseCheckoutPath, $DeveloperWorktreePath, $ReviewerWorktreePath, $UnityExecutable)) {
        Assert-AutomationPathHasNoReparsePoint -Path $configuredPath
    }
    $normalizedWorktree = ConvertTo-AutomationPath -Path $WorktreePath
    $normalizedBase = ConvertTo-AutomationPath -Path $BaseCheckoutPath
    $normalizedDeveloper = ConvertTo-AutomationPath -Path $DeveloperWorktreePath
    $normalizedReviewer = ConvertTo-AutomationPath -Path $ReviewerWorktreePath
    $normalizedCurrent = ConvertTo-AutomationPath -Path (Get-Location).ProviderPath
    $normalizedUnity = ConvertTo-AutomationPath -Path $UnityExecutable
    if (-not (Test-Path -LiteralPath $normalizedUnity -PathType Leaf)) {
        Stop-Preflight -Message "Unity executable is not a file: $normalizedUnity"
    }
    Add-PreflightCheck -Name 'PathsExist' -Passed $true -Detail 'All configured paths exist.'

    $expectedRolePath = if ($Role -eq 'Developer') { $normalizedDeveloper } else { $normalizedReviewer }
    $locationMatches = (Test-AutomationPathEqual -Left $normalizedCurrent -Right $normalizedWorktree) -and
        (Test-AutomationPathEqual -Left $normalizedWorktree -Right $expectedRolePath)
    Add-PreflightCheck -Name 'RoleWorktreePath' -Passed $locationMatches -Detail ("current={0}; expected={1}" -f $normalizedCurrent, $expectedRolePath)
    if (-not $locationMatches) {
        Stop-Preflight -Message ("Current path must be the configured {0} worktree. Current: {1}; expected: {2}" -f $Role, $normalizedCurrent, $expectedRolePath)
    }

    $pathsAreDistinct = -not (Test-AutomationPathEqual -Left $normalizedWorktree -Right $normalizedBase) -and
        -not (Test-AutomationPathEqual -Left $normalizedBase -Right $normalizedDeveloper) -and
        -not (Test-AutomationPathEqual -Left $normalizedBase -Right $normalizedReviewer) -and
        -not (Test-AutomationPathEqual -Left $normalizedDeveloper -Right $normalizedReviewer)
    Add-PreflightCheck -Name 'ProtectedPathsDistinct' -Passed $pathsAreDistinct -Detail 'Base, Developer, and Reviewer paths must be distinct.'
    if (-not $pathsAreDistinct) {
        Stop-Preflight -Message 'Base checkout, Developer worktree, and Reviewer worktree paths must be distinct.'
    }

    $topLevel = Invoke-RequiredNativeCheck -Name 'GitTopLevel' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'rev-parse', '--show-toplevel')
    $reportedTopLevel = ConvertTo-AutomationPath -Path $topLevel.StdOut.Trim()
    $topLevelMatches = Test-AutomationPathEqual -Left $reportedTopLevel -Right $normalizedWorktree
    Add-PreflightCheck -Name 'GitTopLevelMatches' -Passed $topLevelMatches -Detail $reportedTopLevel
    if (-not $topLevelMatches) {
        Stop-Preflight -Message "Configured worktree is not the Git top-level directory: $normalizedWorktree"
    }

    $branchResult = Invoke-RequiredNativeCheck -Name 'CurrentBranchCommand' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'branch', '--show-current')
    $currentBranch = $branchResult.StdOut.Trim()
    $isMainBranch = [string]::Equals($currentBranch, 'main', [System.StringComparison]::OrdinalIgnoreCase)
    Add-PreflightCheck -Name 'CurrentBranchNotMain' -Passed (-not $isMainBranch) -Detail $(if ($currentBranch) { $currentBranch } else { 'detached HEAD' })
    if ($isMainBranch) {
        Stop-Preflight -Message 'Automation must never run directly on the main branch.'
    }

    $originResult = Invoke-RequiredNativeCheck -Name 'OriginRemote' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'remote', 'get-url', 'origin')
    Add-PreflightCheck -Name 'OriginRemoteConfigured' -Passed (-not [string]::IsNullOrWhiteSpace($originResult.StdOut)) -Detail $originResult.StdOut.Trim()

    $gitDirectoryResult = Invoke-RequiredNativeCheck -Name 'GitDirectory' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'rev-parse', '--absolute-git-dir')
    $commonDirectoryResult = Invoke-RequiredNativeCheck -Name 'GitCommonDirectory' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'rev-parse', '--path-format=absolute', '--git-common-dir')
    $gitDirectory = ConvertTo-AutomationPath -Path $gitDirectoryResult.StdOut.Trim()
    $commonDirectory = ConvertTo-AutomationPath -Path $commonDirectoryResult.StdOut.Trim()
    $gitMarker = Join-Path -Path $normalizedWorktree -ChildPath '.git'
    $isLinkedWorktree = (Test-Path -LiteralPath $gitMarker -PathType Leaf) -and
        -not (Test-AutomationPathEqual -Left $gitDirectory -Right $commonDirectory)
    Add-PreflightCheck -Name 'LinkedWorktree' -Passed $isLinkedWorktree -Detail ("gitDir={0}; commonDir={1}" -f $gitDirectory, $commonDirectory)
    if (-not $isLinkedWorktree) {
        Stop-Preflight -Message "The configured path is not a linked Git worktree: $normalizedWorktree"
    }

    $protectedRepositories = [ordered]@{
        Base      = $normalizedBase
        Developer = $normalizedDeveloper
        Reviewer  = $normalizedReviewer
    }
    $protectedRepositoryMetadata = @{}
    foreach ($repositoryEntry in $protectedRepositories.GetEnumerator()) {
        $repositoryLabel = [string]$repositoryEntry.Key
        $repositoryPath = [string]$repositoryEntry.Value
        $repositoryTopLevelResult = Invoke-RequiredNativeCheck -Name ("{0}GitTopLevelCommand" -f $repositoryLabel) -FilePath $GitExecutable -ArgumentList @('-C', $repositoryPath, 'rev-parse', '--show-toplevel')
        $repositoryTopLevel = ConvertTo-AutomationPath -Path $repositoryTopLevelResult.StdOut.Trim()
        $repositoryTopLevelMatches = Test-AutomationPathEqual -Left $repositoryTopLevel -Right $repositoryPath
        Add-PreflightCheck -Name ("{0}GitTopLevel" -f $repositoryLabel) -Passed $repositoryTopLevelMatches -Detail $repositoryTopLevel
        if (-not $repositoryTopLevelMatches) {
            Stop-Preflight -Message ("{0} path is not a Git top-level directory: {1}" -f $repositoryLabel, $repositoryPath)
        }

        $protectedGitDirectoryResult = Invoke-RequiredNativeCheck -Name ("{0}GitDirectoryCommand" -f $repositoryLabel) -FilePath $GitExecutable -ArgumentList @('-C', $repositoryPath, 'rev-parse', '--absolute-git-dir')
        $protectedCommonDirectoryResult = Invoke-RequiredNativeCheck -Name ("{0}GitCommonDirectoryCommand" -f $repositoryLabel) -FilePath $GitExecutable -ArgumentList @('-C', $repositoryPath, 'rev-parse', '--path-format=absolute', '--git-common-dir')
        $protectedGitDirectory = ConvertTo-AutomationPath -Path $protectedGitDirectoryResult.StdOut.Trim()
        $protectedCommonDirectory = ConvertTo-AutomationPath -Path $protectedCommonDirectoryResult.StdOut.Trim()
        $protectedGitMarker = Join-Path -Path $repositoryPath -ChildPath '.git'
        $repositoryShapeIsValid = if ($repositoryLabel -eq 'Base') {
            (Test-Path -LiteralPath $protectedGitMarker -PathType Container) -and
                (Test-AutomationPathEqual -Left $protectedGitDirectory -Right $protectedCommonDirectory)
        }
        else {
            (Test-Path -LiteralPath $protectedGitMarker -PathType Leaf) -and
                -not (Test-AutomationPathEqual -Left $protectedGitDirectory -Right $protectedCommonDirectory)
        }
        Add-PreflightCheck -Name ("{0}RepositoryShape" -f $repositoryLabel) -Passed $repositoryShapeIsValid -Detail ("gitDir={0}; commonDir={1}" -f $protectedGitDirectory, $protectedCommonDirectory)
        if (-not $repositoryShapeIsValid) {
            $expectedShape = if ($repositoryLabel -eq 'Base') { 'primary checkout' } else { 'linked worktree' }
            Stop-Preflight -Message ("{0} path is not the expected {1}: {2}" -f $repositoryLabel, $expectedShape, $repositoryPath)
        }
        $protectedRepositoryMetadata[$repositoryLabel] = [pscustomobject]@{
            GitDirectory    = $protectedGitDirectory
            CommonDirectory = $protectedCommonDirectory
        }

        $repositoryStatusResult = Invoke-RequiredNativeCheck -Name ("{0}GitStatusCommand" -f $repositoryLabel) -FilePath $GitExecutable -ArgumentList @('-C', $repositoryPath, 'status', '--porcelain=v1', '--untracked-files=all')
        $repositoryStatusIsEmpty = [string]::IsNullOrWhiteSpace($repositoryStatusResult.StdOut)
        Add-PreflightCheck -Name ("{0}WorkingTreeClean" -f $repositoryLabel) -Passed $repositoryStatusIsEmpty -Detail $(if ($repositoryStatusIsEmpty) { 'clean' } else { $repositoryStatusResult.StdOut })
        if (-not $repositoryStatusIsEmpty) {
            Stop-Preflight -Message ("{0} working tree is not clean: {1}" -f $repositoryLabel, $repositoryStatusResult.StdOut)
        }
    }

    $baseCommonDirectory = [string]$protectedRepositoryMetadata['Base'].CommonDirectory
    $commonDirectoryMatches = (Test-AutomationPathEqual -Left $baseCommonDirectory -Right ([string]$protectedRepositoryMetadata['Developer'].CommonDirectory)) -and
        (Test-AutomationPathEqual -Left $baseCommonDirectory -Right ([string]$protectedRepositoryMetadata['Reviewer'].CommonDirectory))
    Add-PreflightCheck -Name 'SharedGitCommonDirectory' -Passed $commonDirectoryMatches -Detail $baseCommonDirectory
    if (-not $commonDirectoryMatches) {
        Stop-Preflight -Message 'Base, Developer, and Reviewer paths do not belong to the same Git worktree set.'
    }

    $agentsPath = Join-Path -Path $normalizedWorktree -ChildPath 'AGENTS.md'
    $roleSpecPath = Join-Path -Path $normalizedWorktree -ChildPath ("Docs\Automation\{0}.md" -f $Role.ToUpperInvariant())
    $requiredSpecsExist = (Test-Path -LiteralPath $agentsPath -PathType Leaf) -and (Test-Path -LiteralPath $roleSpecPath -PathType Leaf)
    Add-PreflightCheck -Name 'RequiredSpecs' -Passed $requiredSpecsExist -Detail ("AGENTS={0}; roleSpec={1}" -f $agentsPath, $roleSpecPath)
    if (-not $requiredSpecsExist) {
        Stop-Preflight -Message "AGENTS.md or the $Role role spec is missing from the inspected worktree."
    }

    try {
        $specVersion = Get-AutomationSpecVersion -RepositoryPath $normalizedWorktree
        Add-PreflightCheck -Name 'SpecVersion' -Passed $true -Detail $specVersion
    }
    catch {
        Add-PreflightCheck -Name 'SpecVersion' -Passed $false -Detail $_.Exception.Message
        Stop-Preflight -Message $_.Exception.Message
    }

    $projectVersionPath = Join-Path -Path $normalizedWorktree -ChildPath 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
        Add-PreflightCheck -Name 'UnityVersion' -Passed $false -Detail "Missing: $projectVersionPath"
        Stop-Preflight -Message "Unity project version file is missing: $projectVersionPath"
    }
    $projectVersionText = [System.IO.File]::ReadAllText($projectVersionPath)
    $versionPattern = '(?m)^m_EditorVersion:\s*' + [regex]::Escape($ExpectedUnityVersion) + '\s*$'
    $unityVersionMatches = [regex]::IsMatch($projectVersionText, $versionPattern) -and
        ($normalizedUnity.IndexOf($ExpectedUnityVersion, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
    Add-PreflightCheck -Name 'UnityVersion' -Passed $unityVersionMatches -Detail ("executable={0}; expected={1}" -f $normalizedUnity, $ExpectedUnityVersion)
    if (-not $unityVersionMatches) {
        Stop-Preflight -Message "Unity executable/project version does not match $ExpectedUnityVersion."
    }

    $unityLockPath = Join-Path -Path $normalizedWorktree -ChildPath 'Temp\UnityLockfile'
    $unityLockAbsent = -not (Test-Path -LiteralPath $unityLockPath)
    Add-PreflightCheck -Name 'UnityLock' -Passed $unityLockAbsent -Detail $(if ($unityLockAbsent) { 'absent' } else { $unityLockPath })
    if (-not $unityLockAbsent) {
        Stop-Preflight -Message "Unity lock exists for the inspected worktree: $unityLockPath"
    }

    if ($SkipUnityProcessCheck) {
        Add-PreflightCheck -Name 'UnityProcess' -Passed $true -Skipped $true -Detail 'Skipped by explicit test-only opt-in.'
    }
    else {
        try {
            $unityProcesses = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
            $slashWorktree = $normalizedWorktree.Replace('\', '/')
            $matchingUnityProcesses = @($unityProcesses | Where-Object {
                    $commandLine = [string]$_.CommandLine
                    $commandLine.IndexOf($normalizedWorktree, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                    $commandLine.IndexOf($slashWorktree, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
                })
            $noMatchingProcess = $matchingUnityProcesses.Count -eq 0
            $processDetail = if ($noMatchingProcess) { 'none' } else { [string]::Join(',', [string[]]@($matchingUnityProcesses | ForEach-Object { $_.ProcessId })) }
            Add-PreflightCheck -Name 'UnityProcess' -Passed $noMatchingProcess -Detail $processDetail
            if (-not $noMatchingProcess) {
                Stop-Preflight -Message "A Unity process is using the inspected worktree. Process IDs: $processDetail"
            }
        }
        catch {
            Add-PreflightCheck -Name 'UnityProcess' -Passed $false -Detail $_.Exception.Message
            Stop-Preflight -Message ("Unable to verify Unity processes: {0}" -f $_.Exception.Message)
        }
    }

    $temporaryPath = [System.IO.Path]::GetTempPath()
    $temporaryRoot = [System.IO.Path]::GetPathRoot($temporaryPath)
    $driveInfo = New-Object System.IO.DriveInfo($temporaryRoot)
    $tempFreeBytes = [long]$driveInfo.AvailableFreeSpace
    $hasEnoughTemporarySpace = $tempFreeBytes -ge $MinimumTempFreeBytes
    Add-PreflightCheck -Name 'TemporaryDiskSpace' -Passed $hasEnoughTemporarySpace -Detail ("path={0}; freeBytes={1}; requiredBytes={2}" -f $temporaryPath, $tempFreeBytes, $MinimumTempFreeBytes)
    if (-not $hasEnoughTemporarySpace) {
        Stop-Preflight -Message ("Insufficient temporary disk space. Free: {0} bytes; required: {1} bytes." -f $tempFreeBytes, $MinimumTempFreeBytes)
    }

    $lfsResult = Invoke-RequiredNativeCheck -Name 'GitLfs' -FilePath $GitExecutable -ArgumentList @('lfs', 'version')

    if ($DryRun) {
        Add-PreflightCheck -Name 'GitFetch' -Passed $true -Skipped $true -Detail 'DryRun: git fetch origin --prune was not executed.'
    }
    else {
        $fetchResult = Invoke-RequiredNativeCheck -Name 'GitFetch' -FilePath $GitExecutable -ArgumentList @('-C', $normalizedWorktree, 'fetch', 'origin', '--prune')
    }

    # Resolve through the inspected worktree so an unrelated repository cannot
    # pass merely because the caller can access the expected repository by name.
    $repositoryResult = Invoke-RequiredNativeCheck -Name 'GitHubRepositoryAccess' -FilePath $GitHubCliPath -ArgumentList @('repo', 'view', '--json', 'nameWithOwner') -WorkingDirectory $normalizedWorktree -SuccessDetail $Repository
    try {
        $repositoryJson = $repositoryResult.StdOut | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Stop-Preflight -Message ("GitHub repository response is not valid JSON: {0}" -f $_.Exception.Message)
    }
    $repositoryNameProperty = $repositoryJson.PSObject.Properties['nameWithOwner']
    if ($null -eq $repositoryNameProperty) {
        Stop-Preflight -Message ("GitHub repository response does not contain nameWithOwner. Raw stdout: {0}" -f $repositoryResult.StdOut)
    }
    if ([string]$repositoryNameProperty.Value -cne $Repository) {
        Stop-Preflight -Message ("GitHub repository mismatch. Expected {0}; received {1}." -f $Repository, $repositoryNameProperty.Value)
    }

    $fieldResult = Invoke-RequiredNativeCheck -Name 'GitHubProjectAccess' -FilePath $GitHubCliPath -ArgumentList @('project', 'field-list', [string]$ProjectNumber, '--owner', $ProjectOwner, '--format', 'json') -SuccessDetail ("{0}/projects/{1}" -f $ProjectOwner, $ProjectNumber)
    try {
        $fieldJson = $fieldResult.StdOut | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Stop-Preflight -Message ("GitHub Project field response is not valid JSON: {0}" -f $_.Exception.Message)
    }

    $fieldsProperty = $fieldJson.PSObject.Properties['fields']
    if ($null -eq $fieldsProperty) {
        Stop-Preflight -Message ("GitHub Project response does not contain fields. Raw stdout: {0}" -f $fieldResult.StdOut)
    }
    $projectFields = @($fieldsProperty.Value)
    $requiredFieldNames = @('Status', 'Priority', 'Area', 'Size')
    $fieldNames = @($projectFields | ForEach-Object { [string]$_.name })
    $missingFields = New-Object 'System.Collections.Generic.List[string]'
    $duplicateFields = New-Object 'System.Collections.Generic.List[string]'
    foreach ($requiredFieldName in $requiredFieldNames) {
        $matchingFieldCount = 0
        foreach ($fieldName in $fieldNames) {
            if ([string]::Equals($fieldName, $requiredFieldName, [System.StringComparison]::Ordinal)) {
                $matchingFieldCount++
            }
        }
        if ($matchingFieldCount -eq 0) {
            $missingFields.Add($requiredFieldName)
        }
        if ($matchingFieldCount -ne 1) {
            $duplicateFields.Add($requiredFieldName)
        }
    }
    $fieldsAreExact = $missingFields.Count -eq 0 -and $duplicateFields.Count -eq 0
    Add-PreflightCheck -Name 'GitHubProjectFields' -Passed $fieldsAreExact -Detail ("fields={0}" -f [string]::Join(',', [string[]]$fieldNames))
    if (-not $fieldsAreExact) {
        Stop-Preflight -Message ("GitHub Project must contain exactly one Status, Priority, Area, and Size field. Missing: {0}; invalid counts: {1}" -f ([string]::Join(',', [string[]]$missingFields)), ([string]::Join(',', [string[]]$duplicateFields)))
    }

    $statusField = @($projectFields | Where-Object { [string]$_.name -ceq 'Status' })[0]
    $priorityField = @($projectFields | Where-Object { [string]$_.name -ceq 'Priority' })[0]
    $expectedStatuses = @('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done')
    $expectedPriorities = @('P0', 'P1', 'P2', 'P3')
    $actualStatuses = @($statusField.options | ForEach-Object { [string]$_.name })
    $actualPriorities = @($priorityField.options | ForEach-Object { [string]$_.name })
    $optionsAreExact = (Test-ExactStringSet -Actual $actualStatuses -Expected $expectedStatuses) -and
        (Test-ExactStringSet -Actual $actualPriorities -Expected $expectedPriorities)
    Add-PreflightCheck -Name 'GitHubProjectOptions' -Passed $optionsAreExact -Detail ("Status={0}; Priority={1}" -f ([string]::Join(',', [string[]]$actualStatuses)), ([string]::Join(',', [string[]]$actualPriorities)))
    if (-not $optionsAreExact) {
        Stop-Preflight -Message 'GitHub Project Status or Priority options do not match AGENTS.md.'
    }
}
catch {
    if ($preflightExitCode -eq 0) {
        $preflightExitCode = 1
    }
    $errors.Add($_.Exception.Message)
}

$succeeded = $errors.Count -eq 0
$result = [pscustomobject][ordered]@{
    schemaVersion         = 1
    script                = 'Invoke-AutomationPreflight'
    role                  = $Role
    dryRun                = [bool]$DryRun
    succeeded             = $succeeded
    exitCode              = $(if ($succeeded) { 0 } else { $preflightExitCode })
    worktreePath          = $normalizedWorktree
    specVersion           = $specVersion
    tempFreeBytes         = $tempFreeBytes
    minimumTempFreeBytes  = $MinimumTempFreeBytes
    checks                = $checks.ToArray()
    errors                = $errors.ToArray()
}

[Console]::Out.WriteLine((ConvertTo-AutomationJson -InputObject $result))
if (-not $succeeded) {
    exit $preflightExitCode
}
exit 0
