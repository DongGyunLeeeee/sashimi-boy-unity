#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$SelectionPath,
    [Parameter(Mandatory = $true)][string]$RunPath,
    [string]$CodexFixturePath,
    [string]$UnityFixturePath,
    [string]$PublishFixturePath,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')

$commands = New-Object 'System.Collections.Generic.List[object]'
$events = New-Object 'System.Collections.Generic.List[object]'
$exitCode = 0
$failure = ''
$transition = ''
$findingCount = 0
$script:cancellationMarkerPath = ''
$script:ownedHostPidPath = ''
$script:issueUpdatedAt = ''
$script:issueBodySha256 = ''
$script:conversationSha256 = ''
$script:pullRequestContentSha256 = ''
$script:pinnedHeadSha = ''
$script:pinnedHeadRef = ''
$script:pinnedMainSha = ''
$script:repositoryPath = ''
$script:artifactsPath = ''
$repositoryReady = $false
$script:reviewerSensitiveValues = @(
    Get-SashimiSensitiveEnvironmentEntries |
        ForEach-Object { [string]$_.Value } |
        Where-Object { $_.Length -ge 8 -and $_.Length -le 4096 } |
        Sort-Object -Unique
)

function Test-ReviewerTextContainsSensitiveContent {
    param([AllowEmptyString()][string]$Text)
    return (Test-SashimiRecognizableSensitiveText -Text $Text -SensitiveValues $script:reviewerSensitiveValues)
}

function Get-SafeReviewerDiagnostic {
    param([AllowEmptyString()][string]$Text)
    if (Test-ReviewerTextContainsSensitiveContent -Text $Text) {
        return 'Host Reviewer run failed with sensitive diagnostic content suppressed.'
    }
    return Protect-SashimiTextWithExactValues -Text $Text -ExactValues $script:reviewerSensitiveValues
}

function Add-ReviewerPlan {
    param([string]$Stage, [string]$FilePath, [string[]]$Arguments)
    $commands.Add([pscustomobject]@{ Stage = $Stage; FilePath = Protect-SashimiText $FilePath; Arguments = @($Arguments | ForEach-Object { Protect-SashimiText $_ }); Mutation = $false })
}

function Assert-ReviewerNotCancelled {
    if (-not $DryRun -and -not [string]::IsNullOrWhiteSpace($script:cancellationMarkerPath) -and
        (Test-Path -LiteralPath $script:cancellationMarkerPath -PathType Leaf)) {
        throw 'Run cancellation was requested.'
    }
}

function Invoke-ReviewerGit {
    param([string]$Stage, [string[]]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment = @{})
    # Suppress hooks from the first clone onward.  Setting local config only
    # after checkout is too late when a global core.hooksPath targets files in
    # the worktree being reviewed.
    $safeArguments = @('-c', 'core.hooksPath=NUL') + @($Arguments)
    [void](Assert-SashimiSafeCommand -FilePath ([string]$script:reviewerConfig.GitExecutable) -ArgumentList $safeArguments -Kind Git)
    if ($Arguments -contains 'push') { throw 'Reviewer is forbidden from pushing.' }
    if ($Arguments -contains 'branch' -or $Arguments -contains 'checkout' -or $Arguments -contains 'commit' -or
        (($Arguments -contains 'switch') -and -not ($Arguments -contains '--detach'))) {
        throw 'Reviewer is forbidden from creating, changing, or committing a branch.'
    }
    Add-ReviewerPlan $Stage ([string]$script:reviewerConfig.GitExecutable) $safeArguments
    if ($DryRun) { return [pscustomobject]@{ Succeeded=$true; ExitCode=0; StdOut=''; StdErr=''; TimedOut=$false } }
    Assert-ReviewerNotCancelled
    $gitEnvironment = @{ GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'Never' }
    foreach ($entry in $Environment.GetEnumerator()) { $gitEnvironment[[string]$entry.Key] = [string]$entry.Value }
    $result = Invoke-SashimiHostProcess -FilePath ([string]$script:reviewerConfig.GitExecutable) -ArgumentList $safeArguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds ([int]$script:reviewerConfig.Timeouts.GitSeconds) -Environment $gitEnvironment -Kind Git -OwnedProcessRecordPath $script:ownedHostPidPath -CancellationMarkerPath $script:cancellationMarkerPath
    if (-not $result.Succeeded) { throw "$Stage failed; exit=$($result.ExitCode); stderr=$($result.StdErr); command=$($result.Command)" }
    return $result
}

function Invoke-ReviewerGitLfs {
    param([string]$Stage, [string[]]$Arguments, [string]$WorkingDirectory)

    $executable = [string]$script:reviewerConfig.GitLfsExecutable
    [void](Assert-SashimiSafeCommand -FilePath $executable -ArgumentList $Arguments -Kind Git)
    if ($Arguments -contains 'push') { throw 'Reviewer is forbidden from pushing Git LFS objects.' }
    Add-ReviewerPlan $Stage $executable $Arguments
    if ($DryRun) { return [pscustomobject]@{ Succeeded=$true; ExitCode=0; StdOut=''; StdErr=''; TimedOut=$false } }
    Assert-ReviewerNotCancelled
    $result = Invoke-SashimiHostProcess -FilePath $executable -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds ([int]$script:reviewerConfig.Timeouts.GitSeconds) -Environment @{ GIT_TERMINAL_PROMPT='0'; GCM_INTERACTIVE='Never' } -Kind Git -OwnedProcessRecordPath $script:ownedHostPidPath -CancellationMarkerPath $script:cancellationMarkerPath
    if (-not $result.Succeeded) { throw "$Stage failed; exit=$($result.ExitCode); stderr=$($result.StdErr); command=$($result.Command)" }
    return $result
}

function Invoke-ReviewerScriptJson {
    param([string]$Stage, [string]$ScriptPath, [string[]]$Arguments, [int]$TimeoutSeconds = 0)
    $fullArgs = @('-NoLogo','-NoProfile','-NonInteractive','-File',$ScriptPath) + @($Arguments)
    Add-ReviewerPlan $Stage ([string]$script:reviewerConfig.PowerShellExecutable) $fullArgs
    if ($DryRun) { return [pscustomobject]@{ Success=$true; DryRun=$true; Result=[pscustomobject]@{ Findings=@() } } }
    if ($TimeoutSeconds -lt 1) { $TimeoutSeconds = [int]$script:reviewerConfig.Timeouts.GitHubSeconds + 120 }
    Assert-ReviewerNotCancelled
    $native = Invoke-SashimiHostProcess -FilePath ([string]$script:reviewerConfig.PowerShellExecutable) -ArgumentList $fullArgs -WorkingDirectory $PSScriptRoot -TimeoutSeconds $TimeoutSeconds -OwnedProcessRecordPath $script:ownedHostPidPath -CancellationMarkerPath $script:cancellationMarkerPath
    $lines = @($native.StdOut -split '\r?\n' | Where-Object { $_ -match '^\s*\{' })
    if ($lines.Count -eq 0) { throw "$Stage returned no JSON; exit=$($native.ExitCode); stderr=$($native.StdErr)" }
    try { $json = $lines[-1] | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw "$Stage returned invalid JSON: $($_.Exception.Message)" }
    if (-not $native.Succeeded -or -not [bool](Get-SashimiPropertyValue $json 'Success' $false)) { throw "$Stage failed: $([string](Get-SashimiPropertyValue $json 'Error' $native.StdErr))" }
    return $json
}

function Invoke-ReviewerPublish {
    param([string]$Stage, [string[]]$Arguments)
    $args = @('-ConfigPath',$ConfigPath) + @($Arguments)
    if ($Arguments -cnotcontains '-ProjectItemId' -and $null -ne (Get-Variable selection -ErrorAction SilentlyContinue)) { $args += @('-ProjectItemId',[string](Get-SashimiPropertyValue $selection 'ProjectItemId' '')) }
    if ($script:issueUpdatedAt) { $args += @('-PinnedIssueUpdatedAt',$script:issueUpdatedAt) }
    if ($script:issueBodySha256) { $args += @('-PinnedIssueBodySha256',$script:issueBodySha256) }
    if ($script:conversationSha256) { $args += @('-PinnedConversationSha256',$script:conversationSha256) }
    if ($script:pullRequestContentSha256) { $args += @('-PinnedPullRequestContentSha256',$script:pullRequestContentSha256) }
    if ($script:cancellationMarkerPath) { $args += @('-CancellationMarkerPath',$script:cancellationMarkerPath) }
    if ($PublishFixturePath) { $args += @('-FixturePath',$PublishFixturePath) }
    if ($DryRun) { $args += '-DryRun' }
    $publishResult = Invoke-ReviewerScriptJson $Stage (Join-Path $PSScriptRoot 'Publish-SashimiRunResult.ps1') $args ([int]$script:reviewerConfig.Timeouts.GitHubSeconds + 120)
    $payload = Get-SashimiPropertyValue $publishResult 'Result' $null
    $updatedAt = [string](Get-SashimiPropertyValue $payload 'IssueUpdatedAt' '')
    $bodySha = [string](Get-SashimiPropertyValue $payload 'IssueBodySha256' '')
    $conversationSha = [string](Get-SashimiPropertyValue $payload 'ConversationSha256' '')
    $pullRequestContentSha = [string](Get-SashimiPropertyValue $payload 'PullRequestContentSha256' '')
    $currentProperty = if ($null -eq $payload) { $null } else { $payload.PSObject.Properties['Current'] }
    if (($null -eq $currentProperty -or [bool]$currentProperty.Value) -and $updatedAt -and $bodySha -match '^[0-9a-f]{64}$') { $script:issueUpdatedAt=$updatedAt; $script:issueBodySha256=$bodySha }
    if (($null -eq $currentProperty -or [bool]$currentProperty.Value) -and $conversationSha -cmatch '^[0-9a-f]{64}$') { $script:conversationSha256=$conversationSha }
    if (($null -eq $currentProperty -or [bool]$currentProperty.Value) -and $pullRequestContentSha -cmatch '^[0-9a-f]{64}$') { $script:pullRequestContentSha256=$pullRequestContentSha }
    return $publishResult
}

function Assert-NoReviewerGitUrlRewrite {
    $result = Invoke-ReviewerGit 'Audit Git URL rewrites' @('config','--list') $normalizedRun
    foreach ($line in @($result.StdOut -split '\r?\n')) {
        if ($line -match '^(?i)url\..+\.insteadof=(?<prefix>.*)$') {
            $prefix = [string]$Matches.prefix
            if ($prefix -and ([string]$script:reviewerConfig.RemoteUrl).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) {
                throw 'Git URL rewrite configuration would redirect the canonical repository URL.'
            }
        }
    }
}

function Assert-ReviewerPin {
    param([string]$Stage)
    $pin = Invoke-ReviewerPublish $Stage @(
        '-Action','RevalidatePin','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,
        '-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,
        '-PinnedHeadRef',$script:pinnedHeadRef)
    if (-not $DryRun -and -not [bool]$pin.Result.Current) { throw "PR head/ref is stale ($($pin.Result.ChangedField)); no review mutation is allowed." }
}

function Assert-ReviewerIssuePin {
    param([string]$Stage)
    $pin = Invoke-ReviewerPublish $Stage @(
        '-Action','RevalidateIssue','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,
        '-ProjectItemId',[string]$selection.ProjectItemId,'-PullRequestNumber',[string]$selection.PullRequestNumber,
        '-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,
        '-FromStatus','Review')
    if (-not $DryRun -and -not [bool]$pin.Result.Current) { throw 'Issue body, updatedAt, Project status, or linked PR state is stale; no review mutation is allowed.' }
}

function Get-ReviewerGitVisibleContentSnapshot {
    # Hash every tracked file and every non-ignored untracked file.  Unity's
    # normal ignored import products (for example Library/) are intentionally
    # outside the deliverable worktree, while source/test files remain covered
    # byte-for-byte even if their Git status category does not change.
    $listed = Invoke-ReviewerGit 'Snapshot Git-visible worktree paths' @(
        '-C',$script:repositoryPath,'ls-files','-z','--cached','--others','--exclude-standard','--') $normalizedRun
    if ($DryRun) { return [pscustomobject]@{ FileCount=0; Sha256='planned' } }

    $repositoryRoot = [IO.Path]::GetFullPath($script:repositoryPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $repositoryPrefix = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
    $uniquePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rawPath in @($listed.StdOut -split "`0" | Where-Object { -not [string]::IsNullOrEmpty($_) })) {
        $relativePath = ([string]$rawPath).Replace('\','/')
        $candidatePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not $candidatePath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Reviewer worktree snapshot contained a path outside the fresh clone.'
        }
        [void]$uniquePaths.Add($relativePath)
    }

    $sortedPaths = [string[]]@($uniquePaths)
    [Array]::Sort($sortedPaths, [StringComparer]::Ordinal)
    $records = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in $sortedPaths) {
        $candidatePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            Assert-SashimiNoReparsePoint -Path $candidatePath
            $item = Get-Item -LiteralPath $candidatePath -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reviewer worktree snapshot refuses reparse-point files.'
            }
            $digest = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $records.Add("$relativePath`0file`0$([int64]$item.Length)`0$digest")
        }
        elseif (Test-Path -LiteralPath $candidatePath -PathType Container) {
            Assert-SashimiNoReparsePoint -Path $candidatePath
            $records.Add("$relativePath`0directory`00`0")
        }
        else {
            $records.Add("$relativePath`0missing`00`0")
        }
    }

    return [pscustomobject]@{
        FileCount = $sortedPaths.Count
        Sha256 = Get-SashimiTextSha256 -Text ([string]::Join("`n", $records.ToArray()))
    }
}

function Get-ReviewerGitSnapshot {
    $head = (Invoke-ReviewerGit 'Snapshot local HEAD' @('-C',$script:repositoryPath,'rev-parse','HEAD') $normalizedRun).StdOut.Trim().ToLowerInvariant()
    $ref = (Invoke-ReviewerGit 'Snapshot current ref' @('-C',$script:repositoryPath,'rev-parse','--symbolic-full-name','HEAD') $normalizedRun).StdOut.Trim()
    $status = (Invoke-ReviewerGit 'Snapshot working tree and index' @('-C',$script:repositoryPath,'status','--porcelain=v1','--untracked-files=all') $normalizedRun).StdOut
    $refs = (Invoke-ReviewerGit 'Snapshot Git refs' @('-C',$script:repositoryPath,'for-each-ref','--format=%(refname) %(objectname)','refs/heads','refs/remotes/origin') $normalizedRun).StdOut
    $origin = (Invoke-ReviewerGit 'Snapshot canonical origin' @('-C',$script:repositoryPath,'remote','get-url','origin') $normalizedRun).StdOut.Trim()
    $pushOrigin = (Invoke-ReviewerGit 'Snapshot canonical push origin' @('-C',$script:repositoryPath,'remote','get-url','--push','origin') $normalizedRun).StdOut.Trim()
    $hooks = (Invoke-ReviewerGit 'Snapshot disabled hooks' @('-C',$script:repositoryPath,'config','--get','core.hooksPath') $normalizedRun).StdOut.Trim()
    $localConfig = (Invoke-ReviewerGit 'Snapshot local Git config' @('-C',$script:repositoryPath,'config','--local','--list') $normalizedRun).StdOut.Trim()
    $indexFlags = (Invoke-ReviewerGit 'Snapshot index flags' @('-C',$script:repositoryPath,'ls-files','-v') $normalizedRun).StdOut
    $visibleContent = Get-ReviewerGitVisibleContentSnapshot
    $controlFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($relativeControlPath in @('.git/HEAD','.git/index','.git/config','.git/info/exclude','.git/info/attributes','.git/packed-refs','.git/shallow')) {
        $controlPath = Join-Path $script:repositoryPath ($relativeControlPath.Replace('/','\'))
        $digest = if (Test-Path -LiteralPath $controlPath -PathType Leaf) { (Get-FileHash -LiteralPath $controlPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'missing' }
        $controlFiles.Add("$relativeControlPath=$digest")
    }
    if (-not $DryRun) {
        if ($head -notmatch '^[0-9a-f]{40}$') { throw 'Reviewer snapshot contains an invalid local HEAD.' }
        if ($origin -cne [string]$script:reviewerConfig.RemoteUrl) { throw 'Reviewer repository origin no longer equals the canonical repository URL.' }
        if ($pushOrigin -cne [string]$script:reviewerConfig.RemoteUrl -or $hooks -cne 'NUL') { throw 'Reviewer remote or hooks configuration crossed the Host ownership boundary.' }
    }
    return [pscustomobject][ordered]@{ Head=$head; Ref=$ref; Status=$status; Refs=$refs; Origin=$origin; PushOrigin=$pushOrigin; Hooks=$hooks; LocalConfig=$localConfig; IndexFlags=$indexFlags; VisibleFileCount=$visibleContent.FileCount; VisibleContentSha256=$visibleContent.Sha256; ControlFiles=[string]::Join(';',$controlFiles) }
}

function Assert-ReviewerGitSnapshotUnchanged {
    param(
        [Parameter(Mandatory = $true)][object]$Before,
        [Parameter(Mandatory = $true)][string]$Boundary
    )
    $after = Get-ReviewerGitSnapshot
    foreach ($name in @('Head','Ref','Status','Refs','Origin','PushOrigin','Hooks','LocalConfig','IndexFlags','VisibleFileCount','VisibleContentSha256','ControlFiles')) {
        if (-not [string]::Equals([string]$Before.$name, [string]$after.$name, [StringComparison]::Ordinal)) {
            throw "$Boundary crossed the read-only Reviewer boundary by changing Git $name."
        }
    }
}

function Assert-ReviewerPublicationFreshness {
    param([Parameter(Mandatory = $true)][string]$Stage)

    Assert-ReviewerNotCancelled
    [void](Invoke-ReviewerGit "$Stage - refresh main" @('-C',$script:repositoryPath,'fetch','--no-tags','origin','+refs/heads/main:refs/remotes/origin/main') $normalizedRun)
    [void](Invoke-ReviewerGit "$Stage - refresh PR ref" @('-C',$script:repositoryPath,'fetch','--no-tags','origin',"+refs/heads/$($script:pinnedHeadRef):refs/remotes/origin/sashimi-review-pinned") $normalizedRun)
    if (-not $DryRun) {
        $main = (Invoke-ReviewerGit "$Stage - verify main SHA" @('-C',$script:repositoryPath,'rev-parse','refs/remotes/origin/main') $normalizedRun).StdOut.Trim().ToLowerInvariant()
        $head = (Invoke-ReviewerGit "$Stage - verify PR SHA" @('-C',$script:repositoryPath,'rev-parse','refs/remotes/origin/sashimi-review-pinned') $normalizedRun).StdOut.Trim().ToLowerInvariant()
        if ($main -cne $script:pinnedMainSha) { throw 'origin/main advanced after validation; no review publication or transition is allowed.' }
        if ($head -cne $script:pinnedHeadSha) { throw 'The exact PR ref advanced after validation; no review publication or transition is allowed.' }
    }
    Assert-ReviewerPin "$Stage - live PR pin"
    Assert-ReviewerIssuePin "$Stage - live Issue pin"
}

function ConvertTo-ReviewerMarkdownLine {
    param([AllowNull()][object]$Value)
    $text = Protect-SashimiText ([string]$Value)
    $text = [regex]::Replace($text, '[\r\n\t]+', ' ')
    $text = [regex]::Replace($text, '\s{2,}', ' ').Trim()
    return $text.Replace('<','&lt;').Replace('>','&gt;')
}

function Write-ReviewArtifact {
    param([string]$Name, [string]$Content)
    $path = Join-Path $script:artifactsPath $Name
    if (-not $DryRun) {
        if (Test-ReviewerTextContainsSensitiveContent -Text $Content) { throw 'Review artifact contains recognizable sensitive content; retention was refused.' }
        Write-SashimiUtf8File $path $Content
    }
    return $path
}

try {
    $script:reviewerConfig = Import-SashimiHostConfig -ConfigPath $ConfigPath
    $selection = Read-SashimiJsonFile $SelectionPath
    if (-not [bool](Get-SashimiPropertyValue $selection 'Success' $false) -or -not [bool](Get-SashimiPropertyValue $selection 'Selected' $false) -or
        [string]$selection.Role -cne 'Reviewer' -or [string]$selection.Mode -cne 'Review' -or [int]$selection.DispatchCount -ne 1) {
        throw 'Selection is not one valid Reviewer item.'
    }
    if (@(20,26,30) -contains [int]$selection.IssueNumber -and (Test-SashimiHarnessMode)) { throw 'Fixtures may not target live product Issues #20, #26, or #30.' }
    foreach ($fixture in @($CodexFixturePath,$UnityFixturePath,$PublishFixturePath)) { if ($fixture) { [void](Assert-SashimiFixtureAllowed $fixture -DryRun:$DryRun) } }
    $script:pinnedHeadSha = ([string](Get-SashimiPropertyValue $selection 'PullRequestHeadSha' '')).ToLowerInvariant()
    $script:pinnedHeadRef = [string](Get-SashimiPropertyValue $selection 'PullRequestHeadRef' '')
    if ($script:pinnedHeadSha -notmatch '^[0-9a-f]{40}$' -or [string]::IsNullOrWhiteSpace($script:pinnedHeadRef) -or [int](Get-SashimiPropertyValue $selection 'PullRequestNumber' 0) -lt 1) { throw 'Reviewer selection lacks an exact PR number/SHA/ref.' }
    if ([string](Get-SashimiPropertyValue $selection 'PullRequestHeadRepository' '') -cne [string]$script:reviewerConfig.Repository) { throw 'Reviewer refuses a fork or unpinned PR head repository.' }
    if ([string](Get-SashimiPropertyValue $selection 'Status' '') -cne 'Review' -or [string]::IsNullOrWhiteSpace([string](Get-SashimiPropertyValue $selection 'ProjectItemId' ''))) { throw 'Reviewer selection is not pinned to one Project item in Review.' }

    $script:issueUpdatedAt = [string](Get-SashimiPropertyValue $selection 'IssueUpdatedAt' '')
    $script:issueBodySha256 = [string](Get-SashimiPropertyValue $selection 'IssueBodySha256' '')
    if ($script:issueUpdatedAt -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$') { throw 'Reviewer selection lacks an exact UTC Issue updatedAt pin.' }
    try { [void][DateTimeOffset]::Parse($script:issueUpdatedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal) } catch { throw 'Reviewer selection contains an invalid Issue updatedAt pin.' }
    if ($script:issueBodySha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Reviewer selection lacks a lowercase SHA-256 Issue body pin.' }
    $script:conversationSha256 = [string](Get-SashimiPropertyValue $selection 'ConversationSha256' '')
    if ($script:conversationSha256 -cnotmatch '^[0-9a-f]{64}$' -and ($DryRun -or (Test-SashimiHarnessMode))) {
        $script:conversationSha256 = Get-SashimiConversationSha256 -Records @((Get-SashimiPropertyValue $selection 'Conversation' @()))
    }
    if ($script:conversationSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Reviewer selection lacks a lowercase SHA-256 conversation pin.' }
    $script:pullRequestContentSha256 = [string](Get-SashimiPropertyValue $selection 'PullRequestContentSha256' '')
    if ($script:pullRequestContentSha256 -cnotmatch '^[0-9a-f]{64}$' -and ($DryRun -or (Test-SashimiHarnessMode))) {
        $script:pullRequestContentSha256 = Get-SashimiPullRequestContentSha256 `
            -Title ([string](Get-SashimiPropertyValue $selection 'PullRequestTitle' '')) `
            -Body ([string](Get-SashimiPropertyValue $selection 'PullRequestBody' ''))
    }
    if ($script:pullRequestContentSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Reviewer selection lacks a lowercase SHA-256 PR title/body content pin.' }

    if ($DryRun) {
        $normalizedRun = ConvertTo-SashimiPath $RunPath -AllowMissing -Lexical; $runId = Split-Path -Leaf $normalizedRun
    }
    else {
        $owned = Get-SashimiOwnedRun -RunPath $RunPath -RunRoot ([string]$script:reviewerConfig.RunRoot); $normalizedRun = $owned.RunPath; $runId = $owned.RunId
    }
    $script:repositoryPath = Join-Path $normalizedRun 'Repository'; $script:artifactsPath = Join-Path $normalizedRun 'Artifacts'
    $script:cancellationMarkerPath = Join-Path $normalizedRun 'cancel.requested'
    $script:ownedHostPidPath = Join-Path $normalizedRun 'State\OwnedHostPids.json'
    if (-not $DryRun -and @(Get-ChildItem -LiteralPath $script:repositoryPath -Force).Count -gt 0) { throw 'Reviewer Repository directory is not fresh and empty.' }
    Assert-ReviewerNotCancelled

    Assert-NoReviewerGitUrlRewrite
    [void](Invoke-ReviewerGit 'Fresh standalone review clone' @('clone','--no-checkout','--origin','origin',[string]$script:reviewerConfig.RemoteUrl,$script:repositoryPath) $normalizedRun @{ GIT_LFS_SKIP_SMUDGE='1' })
    $origin = Invoke-ReviewerGit 'Verify canonical origin' @('-C',$script:repositoryPath,'remote','get-url','origin') $normalizedRun
    if (-not $DryRun -and $origin.StdOut.Trim() -cne [string]$script:reviewerConfig.RemoteUrl) { throw 'Fresh clone origin does not equal the canonical repository URL.' }
    $pushOrigin = Invoke-ReviewerGit 'Verify canonical push origin' @('-C',$script:repositoryPath,'remote','get-url','--push','origin') $normalizedRun
    if (-not $DryRun -and $pushOrigin.StdOut.Trim() -cne [string]$script:reviewerConfig.RemoteUrl) { throw 'Fresh clone push URL does not equal the canonical repository URL.' }
    $repositoryReady = $true
    [void](Invoke-ReviewerGit 'Validate exact PR head ref' @('-C',$script:repositoryPath,'check-ref-format',"refs/heads/$($script:pinnedHeadRef)") $normalizedRun)
    [void](Invoke-ReviewerGit 'Fetch latest main' @('-C',$script:repositoryPath,'fetch','--no-tags','origin','+refs/heads/main:refs/remotes/origin/main') $normalizedRun)
    [void](Invoke-ReviewerGit 'Fetch exact PR ref' @('-C',$script:repositoryPath,'fetch','--no-tags','origin',"+refs/heads/$($script:pinnedHeadRef):refs/remotes/origin/sashimi-review-pinned") $normalizedRun)
    if (-not $DryRun) {
        $fetched = Invoke-ReviewerGit 'Verify fetched PR SHA' @('-C',$script:repositoryPath,'rev-parse','refs/remotes/origin/sashimi-review-pinned') $normalizedRun
        if ($fetched.StdOut.Trim().ToLowerInvariant() -cne $script:pinnedHeadSha) { throw 'Fetched PR head no longer matches the selected SHA.' }
        $script:pinnedMainSha = (Invoke-ReviewerGit 'Pin fetched latest main SHA' @('-C',$script:repositoryPath,'rev-parse','refs/remotes/origin/main') $normalizedRun).StdOut.Trim().ToLowerInvariant()
        if ($script:pinnedMainSha -notmatch '^[0-9a-f]{40}$') { throw 'Fetched origin/main did not resolve to an exact SHA.' }
    }
    else { $script:pinnedMainSha = '0000000000000000000000000000000000000000' }
    [void](Invoke-ReviewerGit 'Set explicit local Git author name' @('-C',$script:repositoryPath,'config','--local','user.name',[string]$script:reviewerConfig.GitAuthorName) $normalizedRun)
    [void](Invoke-ReviewerGit 'Set explicit local Git author email' @('-C',$script:repositoryPath,'config','--local','user.email',[string]$script:reviewerConfig.GitAuthorEmail) $normalizedRun)
    [void](Invoke-ReviewerGit 'Checkout latest main detached' @('-C',$script:repositoryPath,'switch','--detach',$script:pinnedMainSha) $normalizedRun)
    [void](Invoke-ReviewerGit 'Normal synthetic merge' @('-C',$script:repositoryPath,'merge','--no-ff','--no-edit',$script:pinnedHeadSha) $normalizedRun)
    [void](Invoke-ReviewerGitLfs 'Install Git LFS locally' @('install','--local') $script:repositoryPath)
    [void](Invoke-ReviewerGit 'Disable repository hooks' @('-C',$script:repositoryPath,'config','core.hooksPath','NUL') $normalizedRun)
    [void](Invoke-ReviewerGitLfs 'Materialize LFS content' @('pull','origin') $script:repositoryPath)
    Assert-ReviewerPin 'Pre-review exact PR pin recheck'
    Assert-ReviewerIssuePin 'Pre-review exact Issue pin recheck'

    $prompt = @"
You are the independent read-only Reviewer for SASHIMI BOY Issue #$($selection.IssueNumber), PR #$($selection.PullRequestNumber), pinned head $($script:pinnedHeadSha), head ref $($script:pinnedHeadRef), PR title/body SHA-256 $($script:pullRequestContentSha256), Issue updatedAt $($script:issueUpdatedAt), Issue body SHA-256 $($script:issueBodySha256), and latest-main SHA $($script:pinnedMainSha).
Read AGENTS.md and Docs/Automation/SPEC_VERSION, WORKFLOW.md, and REVIEWER.md completely. Review the synthetic merge against latest main and the full issue acceptance criteria below.
---
$($selection.IssueTitle)
$($selection.IssueBody)
$([string](Get-SashimiPropertyValue $selection 'PullRequestBody' ''))
$(ConvertTo-SashimiJson (Get-SashimiPropertyValue $selection 'Conversation' @()) -Pretty)
---
Do not edit any file. Do not run gh, clone/fetch/pull, commit, push, create/update PRs, mutate Project state, merge, close Issues, or access credentials/profile/save data. Report focused Blocker/Major/Minor findings with evidence. Populate manualVerification with exact human-only checks and the evidence the Owner must attach. Return only the required structured result. The Host runs every validation and performs authorized publication.
"@
    $promptPath = Join-Path $normalizedRun 'State\ReviewerCodexPrompt.txt'
    $codexArgs = @(
        '-ConfigPath',$ConfigPath,'-RepositoryPath',$script:repositoryPath,'-Role','Reviewer','-Mode','Review','-PromptPath',$promptPath,
        '-ArtifactsPath',(Join-Path $script:artifactsPath 'Codex'),'-IssueNumber',[string]$selection.IssueNumber,
        '-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-RunId',$runId,
        '-CancellationMarkerPath',$script:cancellationMarkerPath)
    if ($CodexFixturePath) { $codexArgs += @('-FixturePath',$CodexFixturePath) }; if ($DryRun) { $codexArgs += '-DryRun' }
    try {
        if (-not $DryRun) {
            Assert-SashimiNoReparsePoint -Path $promptPath
            if (Test-ReviewerTextContainsSensitiveContent -Text $prompt) { throw 'Reviewer prompt contains recognizable sensitive content; retention was refused.' }
            Write-SashimiUtf8File -Path $promptPath -Content $prompt
        }
        $beforeCodex = Get-ReviewerGitSnapshot
        if (-not $DryRun -and [string]$beforeCodex.Status -match '\S') { throw 'Synthetic review checkout is not clean before read-only Codex analysis.' }
        $codex = Invoke-ReviewerScriptJson 'Codex read-only independent review' (Join-Path $PSScriptRoot 'Invoke-SashimiCodexExec.ps1') $codexArgs ([int]$script:reviewerConfig.Timeouts.CodexSeconds + 300)
    }
    finally {
        if (-not $DryRun -and (Test-Path -LiteralPath $promptPath -PathType Leaf)) {
            Assert-SashimiNoReparsePoint -Path $promptPath
            Remove-Item -LiteralPath $promptPath -Force -ErrorAction Stop
        }
    }
    Assert-ReviewerGitSnapshotUnchanged -Before $beforeCodex -Boundary 'Codex'
    Assert-ReviewerNotCancelled
    $codexPayload = Get-SashimiPropertyValue $codex 'Result' $codex

    $validationArgs = @('-ConfigPath',$ConfigPath,'-ProjectPath',$script:repositoryPath,'-ArtifactsPath',(Join-Path $script:artifactsPath 'Unity'),'-IssueNumber',[string]$selection.IssueNumber,'-BaselineRef','origin/main','-OwnedUnityPidPath',(Join-Path $normalizedRun 'State\OwnedUnityPids.json'),'-CancellationMarkerPath',$script:cancellationMarkerPath)
    if ($UnityFixturePath) { $validationArgs += @('-ValidationFixturePath',$UnityFixturePath) }; if ($DryRun) { $validationArgs += '-DryRun' }
    $validationTimeout = (3 * [int]$script:reviewerConfig.Timeouts.UnityStageSeconds) + (2 * [int]$script:reviewerConfig.Timeouts.GeneratorSeconds) + 600
    $beforeUnity = Get-ReviewerGitSnapshot
    $validationResult = Invoke-ReviewerScriptJson 'Host full Unity validation' (Join-Path $PSScriptRoot 'Invoke-SashimiUnityValidation.ps1') $validationArgs $validationTimeout
    Assert-ReviewerGitSnapshotUnchanged -Before $beforeUnity -Boundary 'Unity validation'
    [void](Invoke-ReviewerGit 'Git whitespace validation' @('-C',$script:repositoryPath,'diff','--check','origin/main...HEAD') $normalizedRun)
    Assert-ReviewerNotCancelled

    $findings = @((Get-SashimiPropertyValue $codexPayload 'findings' (Get-SashimiPropertyValue $codexPayload 'Findings' @())))
    $blocking = @($findings | Where-Object { @('Blocker','Major') -ccontains [string](Get-SashimiPropertyValue $_ 'severity' (Get-SashimiPropertyValue $_ 'Severity' '')) })
    $findingCount = $findings.Count
    if ($blocking.Count -gt 0) {
        $finding = $blocking[0]
        $severity = [string](Get-SashimiPropertyValue $finding 'severity' (Get-SashimiPropertyValue $finding 'Severity' 'Major'))
        $findingTitle = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $finding 'title' (Get-SashimiPropertyValue $finding 'Title' 'Finding'))
        $findingEvidence = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $finding 'evidence' (Get-SashimiPropertyValue $finding 'Evidence' 'See retained artifacts.'))
        $findingFile = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $finding 'file' (Get-SashimiPropertyValue $finding 'File' ''))
        $findingLine = [int](Get-SashimiPropertyValue $finding 'line' (Get-SashimiPropertyValue $finding 'Line' 0))
        $findingRecommendation = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $finding 'recommendation' (Get-SashimiPropertyValue $finding 'Recommendation' 'Resolve the finding and rerun the full host validation.'))
        $location = if ($findingFile) { "`n`nLocation: $findingFile$(if ($findingLine -gt 0) { ":$findingLine" } else { '' })" } else { '' }
        $findingText = "## Independent Review — $severity`n`n$findingTitle$location`n`nEvidence: $findingEvidence`n`nRequired correction: $findingRecommendation`n`nPinned PR evidence: #$($selection.PullRequestNumber), $($script:pinnedHeadRef), $($script:pinnedHeadSha).`nPinned latest main: $($script:pinnedMainSha)."
        $findingPath = Write-ReviewArtifact 'ReviewFinding.md' $findingText
        Assert-ReviewerPublicationFreshness 'Pre-finding publication freshness'
        $posted = Invoke-ReviewerPublish 'Post focused review finding' @('-Action','Comment','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-CommentTarget','PullRequest','-BodyPath',$findingPath)
        $findingUrl = if ($DryRun) { 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/0#issuecomment-0' } else { [string](Get-SashimiPropertyValue $posted.Result 'Url' '') }
        if (-not $DryRun -and -not [Uri]::IsWellFormedUriString($findingUrl,[UriKind]::Absolute)) { throw 'Focused finding publication did not return an absolute URL.' }
        $reason = if ($severity -ceq 'Blocker') { 'review-blocker' } else { 'review-major' }
        $handoff = "<!-- sashimi-boy-automation-handoff:v1`nmode: ReviewFix`nissue: $($selection.IssueNumber)`npr: $($selection.PullRequestNumber)`nhead: $($script:pinnedHeadSha)`nsourceRole: Reviewer`nreason: $reason`nfindingUrl: $findingUrl`npendingCommand: `n-->"
        $handoffPath = Write-ReviewArtifact 'ReviewFixHandoff.md' $handoff
        Assert-ReviewerPublicationFreshness 'Pre-handoff publication freshness'
        [void](Invoke-ReviewerPublish 'Post current ReviewFix handoff' @('-Action','Comment','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-CommentTarget','PullRequest','-BodyPath',$handoffPath))
        Assert-ReviewerPublicationFreshness 'Pre-In Progress transition freshness'
        [void](Invoke-ReviewerPublish 'Review to In Progress' @('-Action','Transition','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-ProjectItemId',[string]$selection.ProjectItemId,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-FromStatus','Review','-ToStatus','In Progress'))
        $transition = 'Review->In Progress'
    }
    else {
        $manualVerification = @((Get-SashimiPropertyValue $codexPayload 'manualVerification' (Get-SashimiPropertyValue $codexPayload 'ManualVerification' @())) | ForEach-Object { ConvertTo-ReviewerMarkdownLine $_ } | Where-Object { $_ })
        if (-not $DryRun -and $manualVerification.Count -eq 0) { throw 'Codex returned no exact Owner manualVerification checklist; Verification publication is blocked.' }
        if ($DryRun -and $manualVerification.Count -eq 0) { $manualVerification = @('Planned: use the exact Codex manualVerification items returned by the live review.') }

        $validationChecks = @((Get-SashimiPropertyValue $validationResult 'Checks' @()))
        if (-not $DryRun -and $validationChecks.Count -eq 0) { throw 'Unity validation returned no named checks; Verification publication is blocked.' }
        $failedValidationChecks = @($validationChecks | Where-Object { -not [bool](Get-SashimiPropertyValue $_ 'Passed' $false) })
        if (-not $DryRun -and $failedValidationChecks.Count -gt 0) { throw 'Unity validation contains a failed named check despite its process result.' }
        $checkLines = @($validationChecks | ForEach-Object {
            $name = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $_ 'Name' 'Unnamed host check')
            "- ${name}: PASS"
        })
        if ($checkLines.Count -eq 0) { $checkLines = @('- Planned host validation checks: exact results are emitted only by a live run.') }

        $stageLines = New-Object 'System.Collections.Generic.List[string]'
        $stageObject = Get-SashimiPropertyValue $validationResult 'Stages' $null
        if ($null -ne $stageObject) {
            foreach ($property in $stageObject.PSObject.Properties) {
                $stage = $property.Value
                $stageName = ConvertTo-ReviewerMarkdownLine (Get-SashimiPropertyValue $stage 'Name' $property.Name)
                $nativeExit = [int](Get-SashimiPropertyValue $stage 'NativeExitCode' 0)
                $stageLines.Add("- ${stageName}: PASS (native exit $nativeExit)")
            }
        }
        if ($stageLines.Count -eq 0) { $stageLines.Add('- Planned Unity stages: CompileImport, EditMode, and PlayMode.') }

        $codexDigest = 'planned'
        $unityDigest = 'planned'
        if (-not $DryRun) {
            $codexResultPath = Join-Path $script:artifactsPath 'Codex\CodexResult.json'
            $unitySummaryPath = Join-Path $script:artifactsPath 'Unity\UnityValidation.Summary.json'
            if (-not (Test-Path -LiteralPath $codexResultPath -PathType Leaf) -or -not (Test-Path -LiteralPath $unitySummaryPath -PathType Leaf)) { throw 'Exact Codex or Unity summary evidence is missing; Verification publication is blocked.' }
            $codexDigest = (Get-FileHash -LiteralPath $codexResultPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $unityDigest = (Get-FileHash -LiteralPath $unitySummaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $manualLines = @($manualVerification | ForEach-Object { "- [ ] $_" })
        $checklist = @"
## Independent Verification PASS

The independent read-only review found zero Blocker/Major findings and every required host check passed. Human Owner verification remains required.

## Exact reviewed pins

- Run ID: $runId
- Issue: #$($selection.IssueNumber), updatedAt $($script:issueUpdatedAt), body SHA-256 $($script:issueBodySha256)
- Pull request: #$($selection.PullRequestNumber), ref $($script:pinnedHeadRef), head $($script:pinnedHeadSha), title/body SHA-256 $($script:pullRequestContentSha256)
- Latest main used for the synthetic merge: $($script:pinnedMainSha)
- Codex result: outcome Succeeded, Artifacts/Codex/CodexResult.json SHA-256 $codexDigest
- Unity summary: Artifacts/Unity/UnityValidation.Summary.json SHA-256 $unityDigest

## Unity stages

$([string]::Join("`n", $stageLines.ToArray()))

## Host validation checks

$([string]::Join("`n", $checkLines))

## Owner manual verification from the independent review

$([string]::Join("`n", $manualLines))
"@
        $checklistPath = Write-ReviewArtifact 'OwnerVerificationChecklist.md' $checklist
        Assert-ReviewerPublicationFreshness 'Pre-checklist publication freshness'
        [void](Invoke-ReviewerPublish 'Post Owner verification checklist' @('-Action','Comment','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-CommentTarget','PullRequest','-BodyPath',$checklistPath))
        Assert-ReviewerPublicationFreshness 'Pre-Verification transition freshness'
        [void](Invoke-ReviewerPublish 'Review to Verification' @('-Action','Transition','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-ProjectItemId',[string]$selection.ProjectItemId,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-FromStatus','Review','-ToStatus','Verification'))
        $transition = 'Review->Verification'
    }
}
catch {
    $exitCode = 1; $failure = Get-SafeReviewerDiagnostic -Text ([string]$_.Exception.Message)
    if (-not $DryRun -and $repositoryReady -and $null -ne (Get-Variable selection -ErrorAction SilentlyContinue) -and
        -not [string]::IsNullOrWhiteSpace($script:issueUpdatedAt) -and $script:issueBodySha256 -cmatch '^[0-9a-f]{64}$' -and
        $script:pullRequestContentSha256 -cmatch '^[0-9a-f]{64}$') {
        try {
            $publishedFailure = ConvertTo-ReviewerMarkdownLine $failure
            $failureBody = @"
## Host Reviewer run failed safely

The run stopped safely for Issue #$([int](Get-SashimiPropertyValue $selection 'IssueNumber' 0)) and PR #$([int](Get-SashimiPropertyValue $selection 'PullRequestNumber' 0)). Failure handling does not push the PR branch, merge, transition to Done, or infer a Project transition from this failure. Confirm live state before resuming.

- Run ID: $runId
- Pinned PR ref: $($script:pinnedHeadRef)
- Pinned PR head: $($script:pinnedHeadSha)
- Pinned PR title/body SHA-256: $($script:pullRequestContentSha256)
- Pinned Issue updatedAt: $($script:issueUpdatedAt)
- Pinned Issue body SHA-256: $($script:issueBodySha256)
- Failure: $publishedFailure

Sanitized run-local evidence is retained under Artifacts/ for Owner diagnosis.
"@
            $failurePath = Write-ReviewArtifact 'ReviewerFailure.md' $failureBody
            Assert-ReviewerPublicationFreshness 'Pre-failure-evidence publication freshness'
            [void](Invoke-ReviewerPublish 'Publish sanitized failure evidence without transition' @('-Action','Comment','-Role','Reviewer','-IssueNumber',[string]$selection.IssueNumber,'-PullRequestNumber',[string]$selection.PullRequestNumber,'-PinnedHeadSha',$script:pinnedHeadSha,'-PinnedHeadRef',$script:pinnedHeadRef,'-CommentTarget','PullRequest','-BodyPath',$failurePath))
            $events.Add([pscustomobject]@{ Name='FailureEvidence'; Value='Published' })
        }
        catch {
            $events.Add([pscustomobject]@{ Name='FailureEvidence'; Value=(Get-SafeReviewerDiagnostic -Text ([string]$_.Exception.Message)) })
        }
    }
}

$output = [ordered]@{
    SchemaVersion=1; Tool='Invoke-SashimiReviewerRun'; Success=($exitCode -eq 0); ExitCode=$exitCode; DryRun=[bool]$DryRun
    IssueNumber=if ($null -ne (Get-Variable selection -ErrorAction SilentlyContinue)) { [int](Get-SashimiPropertyValue $selection 'IssueNumber' 0) } else { 0 }
    PullRequestNumber=if ($null -ne (Get-Variable selection -ErrorAction SilentlyContinue)) { [int](Get-SashimiPropertyValue $selection 'PullRequestNumber' 0) } else { 0 }
    FindingCount=$findingCount; Transition=$transition; ReviewerPushAttempted=$false; Commands=$commands.ToArray(); Events=$events.ToArray(); Error=$failure
}
[Console]::Out.WriteLine((ConvertTo-SashimiJson $output))
exit $exitCode
