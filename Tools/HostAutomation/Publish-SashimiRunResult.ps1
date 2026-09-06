#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [Parameter(Mandatory = $true)][ValidateSet('RevalidatePin', 'RevalidateIssue', 'Transition', 'Comment', 'CreateDraftPullRequest')][string]$Action,
    [Parameter(Mandatory = $true)][ValidateSet('Developer', 'Reviewer')][string]$Role,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$IssueNumber,
    [string]$ProjectItemId,
    [ValidateRange(0, 2147483647)][int]$PullRequestNumber = 0,
    [string]$PinnedHeadSha,
    [string]$PinnedHeadRef,
    [string]$PinnedPullRequestContentSha256,
    [string]$PinnedIssueUpdatedAt,
    [string]$PinnedIssueBodySha256,
    [string]$PinnedConversationSha256,
    [string]$FromStatus,
    [string]$ToStatus,
    [ValidateSet('Issue', 'PullRequest')][string]$CommentTarget = 'PullRequest',
    [string]$BodyPath,
    [string]$Branch,
    [string]$Title,
    [string]$FixturePath,
    [string]$CancellationMarkerPath,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')

$commands = New-Object 'System.Collections.Generic.List[object]'
$mutationAttempted = $false
$pinCurrent = $true
$script:publishSensitiveValues = @(
    Get-SashimiSensitiveEnvironmentEntries |
        ForEach-Object { [string]$_.Value } |
        Where-Object { $_.Length -ge 8 -and $_.Length -le 4096 } |
        Sort-Object -Unique
)

function Assert-PublishTextContainsNoSensitiveContent {
    param([AllowEmptyString()][string]$Text, [Parameter(Mandatory = $true)][string]$Context)

    if (Test-SashimiRecognizableSensitiveText -Text $Text -SensitiveValues $script:publishSensitiveValues) {
        throw "$Context contains recognizable sensitive content; publication was refused."
    }
}

function Invoke-PublishGh {
    param([string[]]$Arguments, [string]$Operation, [switch]$Mutation)
    [void](Assert-SashimiSafeCommand -FilePath ([string]$script:publishConfig.GitHubCli) -ArgumentList $Arguments -Kind GitHub)
    $commandRecord = [pscustomobject]@{ Operation = $Operation; FilePath = Protect-SashimiText ([string]$script:publishConfig.GitHubCli); Arguments = @($Arguments | ForEach-Object { Protect-SashimiText $_ }); Mutation = [bool]$Mutation }
    if ($DryRun) {
        $commands.Add($commandRecord)
        return [pscustomobject]@{ Succeeded = $true; ExitCode = 0; StdOut = ''; StdErr = ''; DryRun = $true }
    }
    if ($Mutation) {
        if ($CancellationMarkerPath -and (Test-Path -LiteralPath $CancellationMarkerPath -PathType Leaf)) {
            throw "Cancellation was requested before $Operation; no mutation was attempted."
        }
        [void](Assert-PublishAuthenticatedActor -ImmediatelyBeforeMutation)
        $commands.Add($commandRecord)
        $script:mutationAttempted = $true
    }
    else { $commands.Add($commandRecord) }
    $result = Invoke-SashimiHostProcess -FilePath ([string]$script:publishConfig.GitHubCli) -ArgumentList $Arguments -TimeoutSeconds ([int]$script:publishConfig.Timeouts.GitHubSeconds) -Kind GitHub -CancellationMarkerPath $CancellationMarkerPath -Environment @{ GH_PROMPT_DISABLED='1'; GIT_TERMINAL_PROMPT='0' }
    if (-not $result.Succeeded) { throw "$Operation failed; exit=$($result.ExitCode); stderr=$($result.StdErr); command=$($result.Command)" }
    return $result
}

function Assert-PublishAuthenticatedActor {
    param([switch]$ImmediatelyBeforeMutation)

    # ProjectOwner is an exact, immutable repository contract validated by
    # Import-SashimiHostConfig. AuthorizedPrAuthors controls incoming PR trust;
    # it must not implicitly grant an account authority to mutate this Project.
    $authorizedLogin = [string]$script:publishConfig.ProjectOwner
    if ($null -ne $script:publishFixture) {
        $authenticatedLogin = [string](Get-SashimiPropertyValue $script:publishFixture 'AuthenticatedLogin' $authorizedLogin)
    }
    elseif ($DryRun) {
        # A no-mutation dry run cannot meaningfully authenticate a future
        # mutation. Config validation still proves the exact authorized actor
        # contract, and every real mutation re-runs this check fail-closed.
        $authenticatedLogin = $authorizedLogin
    }
    else {
        $operation = if ($ImmediatelyBeforeMutation) { 'Authenticated actor mutation preflight' } else { 'Authenticated actor preflight' }
        $actorResult = Invoke-PublishGh -Operation $operation -Arguments @('api', 'user', '--jq', '.login')
        $authenticatedLogin = $actorResult.StdOut.Trim()
    }

    if ($authenticatedLogin -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$' -or
        -not [string]::Equals($authenticatedLogin, $authorizedLogin, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The authenticated GitHub actor is not authorized for Host publication; no mutation was attempted.'
    }
    return $authenticatedLogin
}

function ConvertFrom-PublishJson {
    param([string]$Text, [string]$Operation)
    try { $value = $Text | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw "$Operation returned invalid JSON: $($_.Exception.Message)" }
    if (@((Get-SashimiPropertyValue $value 'errors' @())).Count -gt 0) {
        throw "$Operation returned GraphQL errors: $(Protect-SashimiText (ConvertTo-SashimiJson (Get-SashimiPropertyValue $value 'errors' @())))"
    }
    return $value
}

function Convert-PullRequestShape {
    param([object]$Value)
    $headRepositoryValue = Get-SashimiPropertyValue $Value 'HeadRepository' (Get-SashimiPropertyValue $Value 'headRepository' '')
    $headRepositoryName = if ($headRepositoryValue -is [string]) { [string]$headRepositoryValue } else { [string](Get-SashimiPropertyValue $headRepositoryValue 'nameWithOwner' '') }
    $baseRepositoryValue = Get-SashimiPropertyValue $Value 'BaseRepository' ([string]$script:publishConfig.Repository)
    $baseRepositoryName = if ($baseRepositoryValue -is [string]) { [string]$baseRepositoryValue } else { [string](Get-SashimiPropertyValue $baseRepositoryValue 'nameWithOwner' ([string]$script:publishConfig.Repository)) }
    $hasTitle = $null -ne $Value.PSObject.Properties['Title'] -or $null -ne $Value.PSObject.Properties['title']
    $hasBody = $null -ne $Value.PSObject.Properties['Body'] -or $null -ne $Value.PSObject.Properties['body']
    $contentSha256 = [string](Get-SashimiPropertyValue $Value 'ContentSha256' (Get-SashimiPropertyValue $Value 'contentSha256' ''))
    if ($hasTitle -and $hasBody) {
        $contentSha256 = Get-SashimiPullRequestContentSha256 `
            -Title ([string](Get-SashimiPropertyValue $Value 'Title' (Get-SashimiPropertyValue $Value 'title' ''))) `
            -Body ([string](Get-SashimiPropertyValue $Value 'Body' (Get-SashimiPropertyValue $Value 'body' '')))
    }
    return [pscustomobject][ordered]@{
        Number = [int](Get-SashimiPropertyValue $Value 'Number' (Get-SashimiPropertyValue $Value 'number' 0))
        State = ([string](Get-SashimiPropertyValue $Value 'State' (Get-SashimiPropertyValue $Value 'state' ''))).ToUpperInvariant()
        IsDraft = [bool](Get-SashimiPropertyValue $Value 'IsDraft' (Get-SashimiPropertyValue $Value 'isDraft' $false))
        BaseRefName = [string](Get-SashimiPropertyValue $Value 'BaseRefName' (Get-SashimiPropertyValue $Value 'baseRefName' ''))
        BaseRepository = $baseRepositoryName
        HeadRef = [string](Get-SashimiPropertyValue $Value 'HeadRef' (Get-SashimiPropertyValue $Value 'headRefName' ''))
        HeadSha = ([string](Get-SashimiPropertyValue $Value 'HeadSha' (Get-SashimiPropertyValue $Value 'headRefOid' ''))).ToLowerInvariant()
        HeadRepository = $headRepositoryName
        AuthorLogin = [string](Get-SashimiPropertyValue $Value 'AuthorLogin' (Get-SashimiPropertyValue (Get-SashimiPropertyValue $Value 'author' $null) 'login' ''))
        IsCrossRepository = [bool](Get-SashimiPropertyValue $Value 'IsCrossRepository' (Get-SashimiPropertyValue $Value 'isCrossRepository' $false))
        Url = [string](Get-SashimiPropertyValue $Value 'Url' (Get-SashimiPropertyValue $Value 'url' ''))
        ContentSha256 = $contentSha256.ToLowerInvariant()
    }
}

function Get-LivePullRequest {
    param([int]$Number)
    if ($null -ne $script:publishFixture) {
        $value = Get-SashimiPropertyValue $script:publishFixture 'LivePullRequest' $null
        if ($null -eq $value) { throw 'Publish fixture has no LivePullRequest.' }
        $shape = Convert-PullRequestShape $value
        if ([string]$shape.ContentSha256 -cnotmatch '^[0-9a-f]{64}$' -and $PinnedPullRequestContentSha256 -cmatch '^[0-9a-f]{64}$') {
            # Compatibility for older self-contained fixtures that intentionally
            # omit prose. Live gh responses never use this fallback.
            $shape.ContentSha256 = $PinnedPullRequestContentSha256
        }
        elseif ([string]$shape.ContentSha256 -cnotmatch '^[0-9a-f]{64}$') {
            $shape.ContentSha256 = Get-SashimiPullRequestContentSha256 -Title '' -Body ''
        }
        return $shape
    }
    $result = Invoke-PublishGh -Operation "Pull Request #$Number live pin query" -Arguments @(
        'pr', 'view', [string]$Number, '--repo', [string]$script:publishConfig.Repository,
        '--json', 'number,title,body,state,isDraft,baseRefName,headRefName,headRefOid,headRepository,isCrossRepository,author,url'
    )
    $shape = Convert-PullRequestShape (ConvertFrom-PublishJson $result.StdOut 'Pull Request live pin query')
    if ([string]$shape.ContentSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Pull Request live pin query omitted exact title/body content.' }
    return $shape
}

function Get-PublishConversationConnection {
    param(
        [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory = $true)][ValidateSet('IssueComments','PullRequestComments','PullRequestReviews')][string]$Kind
    )

    $repoParts = ([string]$script:publishConfig.Repository).Split('/')
    if ($repoParts.Count -ne 2) { throw 'Canonical repository name is not owner/name.' }
    switch -CaseSensitive ($Kind) {
        'IssueComments' {
            $query = 'query HostPinIssueComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){issue(number:$number){comments(first:100,after:$cursor){totalCount nodes{body createdAt updatedAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
            $parentName = 'issue'; $connectionName = 'comments'; $canonicalKind = 'IssueComment'
        }
        'PullRequestComments' {
            $query = 'query HostPinPrComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){comments(first:100,after:$cursor){totalCount nodes{body createdAt updatedAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
            $parentName = 'pullRequest'; $connectionName = 'comments'; $canonicalKind = 'PullRequestComment'
        }
        'PullRequestReviews' {
            $query = 'query HostPinPrReviews($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviews(first:100,after:$cursor){totalCount nodes{body url createdAt updatedAt submittedAt includesCreatedEdit state author{login} authorAssociation commit{oid}} pageInfo{hasNextPage endCursor}}}}}'
            $parentName = 'pullRequest'; $connectionName = 'reviews'; $canonicalKind = 'PullRequestReview'
        }
    }

    $records = [Collections.Generic.List[object]]::new()
    $seenUrls = @{}; $seenCursors = @{}; $cursor = ''; $expectedCount = -1
    while ($true) {
        $args = @('api','graphql','-f',"owner=$($repoParts[0])",'-f',"name=$($repoParts[1])",'-F',"number=$Number")
        if ($cursor) { $args += @('-f',"cursor=$cursor") }
        $args += @('-f',"query=$query")
        $native = Invoke-PublishGh -Operation "$Kind pagination for #$Number" -Arguments $args
        $json = ConvertFrom-PublishJson $native.StdOut "$Kind pagination for #$Number"
        $repository = Get-SashimiPropertyValue (Get-SashimiPropertyValue $json 'data' $null) 'repository' $null
        $parent = Get-SashimiPropertyValue $repository $parentName $null
        if ($null -eq $parent) { throw "$Kind target #$Number was not found in the canonical repository." }
        $connection = Get-SashimiPropertyValue $parent $connectionName $null
        if ($null -eq $connection) { throw "$Kind response omitted its connection." }
        $totalCount = [int](Get-SashimiPropertyValue $connection 'totalCount' -1)
        if ($totalCount -lt 0) { throw "$Kind response has an invalid totalCount." }
        if ($expectedCount -lt 0) { $expectedCount = $totalCount }
        elseif ($expectedCount -ne $totalCount) { throw "$Kind totalCount changed during pagination." }
        foreach ($node in @((Get-SashimiPropertyValue $connection 'nodes' @()))) {
            $url = [string](Get-SashimiPropertyValue $node 'url' '')
            if ([string]::IsNullOrWhiteSpace($url) -or $seenUrls.ContainsKey($url)) { throw "$Kind returned a missing or duplicate URL." }
            $seenUrls[$url] = $true
            $records.Add((ConvertTo-SashimiCanonicalConversationRecord -Record $node -Kind $canonicalKind))
        }
        $pageInfo = Get-SashimiPropertyValue $connection 'pageInfo' $null
        if ($null -eq $pageInfo -or $null -eq $pageInfo.PSObject.Properties['hasNextPage']) { throw "$Kind response omitted pageInfo." }
        if (-not [bool]$pageInfo.hasNextPage) { break }
        $next = [string](Get-SashimiPropertyValue $pageInfo 'endCursor' '')
        if ([string]::IsNullOrWhiteSpace($next) -or $next -ceq $cursor -or $seenCursors.ContainsKey($next)) { throw "$Kind returned an invalid pagination cursor." }
        $seenCursors[$next] = $true; $cursor = $next
    }
    if ($records.Count -ne $expectedCount) { throw "$Kind pagination is incomplete ($($records.Count)/$expectedCount)." }
    return $records.ToArray()
}

function Get-LiveConversationState {
    param(
        [Parameter(Mandatory = $true)][ValidateRange(0, 2147483647)][int]$Number,
        [switch]$AfterHostMutation
    )

    if ($null -ne $script:publishFixture) {
        $recordsName = if ($AfterHostMutation) { 'ConversationRecordsAfterMutation' } else { 'LiveConversationRecords' }
        $digestName = if ($AfterHostMutation) { 'ConversationSha256AfterMutation' } else { 'LiveConversationSha256' }
        $recordsProperty = $script:publishFixture.PSObject.Properties[$recordsName]
        $digestProperty = $script:publishFixture.PSObject.Properties[$digestName]
        if ($null -ne $recordsProperty) {
            $records = @($recordsProperty.Value)
            $computed = Get-SashimiConversationSha256 -Records $records
            if ($null -ne $digestProperty -and [string]$digestProperty.Value -cne $computed) { throw "Publish fixture $digestName does not match its canonical records." }
            return [pscustomobject]@{ Records=$records; Sha256=$computed; Fixture=$true }
        }
        if ($null -ne $digestProperty) {
            $declared = [string]$digestProperty.Value
            if ($declared -cnotmatch '^[0-9a-f]{64}$') { throw "Publish fixture $digestName must be a lowercase SHA-256 digest." }
            return [pscustomobject]@{ Records=@(); Sha256=$declared; Fixture=$true }
        }
        # Fixture mutations do not alter an external conversation. Preserve old
        # fixture behavior unless a test explicitly supplies before/after data.
        if ($AfterHostMutation -and $PinnedConversationSha256 -cmatch '^[0-9a-f]{64}$') {
            return [pscustomobject]@{ Records=@(); Sha256=$PinnedConversationSha256; Fixture=$true }
        }
        $currentRecords = $script:publishFixture.PSObject.Properties['LiveConversationRecords']
        if ($null -ne $currentRecords) {
            $records = @($currentRecords.Value)
            return [pscustomobject]@{ Records=$records; Sha256=(Get-SashimiConversationSha256 -Records $records); Fixture=$true }
        }
        $currentDigest = [string](Get-SashimiPropertyValue $script:publishFixture 'LiveConversationSha256' '')
        if ($currentDigest -cmatch '^[0-9a-f]{64}$') { return [pscustomobject]@{ Records=@(); Sha256=$currentDigest; Fixture=$true } }
        if ($PinnedConversationSha256 -cmatch '^[0-9a-f]{64}$') { return [pscustomobject]@{ Records=@(); Sha256=$PinnedConversationSha256; Fixture=$true } }
        return [pscustomobject]@{ Records=@(); Sha256=(Get-SashimiConversationSha256 -Records @()); Fixture=$true }
    }

    $records = [Collections.Generic.List[object]]::new()
    foreach ($record in @(Get-PublishConversationConnection -Number $IssueNumber -Kind IssueComments)) { $records.Add($record) }
    if ($Number -gt 0) {
        foreach ($record in @(Get-PublishConversationConnection -Number $Number -Kind PullRequestComments)) { $records.Add($record) }
        foreach ($record in @(Get-PublishConversationConnection -Number $Number -Kind PullRequestReviews)) { $records.Add($record) }
    }
    return [pscustomobject]@{ Records=$records.ToArray(); Sha256=(Get-SashimiConversationSha256 -Records $records.ToArray()); Fixture=$false }
}

function Convert-RemoteBranchShape {
    param([Parameter(Mandatory = $true)][object]$Value)

    $objectValue = Get-SashimiPropertyValue $Value 'Object' (Get-SashimiPropertyValue $Value 'object' $null)
    return [pscustomobject][ordered]@{
        Ref = [string](Get-SashimiPropertyValue $Value 'Ref' (Get-SashimiPropertyValue $Value 'ref' ''))
        ObjectType = [string](Get-SashimiPropertyValue $Value 'ObjectType' (Get-SashimiPropertyValue $objectValue 'type' ''))
        Sha = ([string](Get-SashimiPropertyValue $Value 'Sha' (Get-SashimiPropertyValue $objectValue 'sha' ''))).ToLowerInvariant()
    }
}

function Get-LiveRemoteBranch {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$ImmediatelyBeforeMutation
    )

    if ($null -ne $script:publishFixture) {
        $fixtureProperty = if ($ImmediatelyBeforeMutation) { 'LiveRemoteBranchImmediatelyBeforeMutation' } else { 'LiveRemoteBranch' }
        $fixtureValue = Get-SashimiPropertyValue $script:publishFixture $fixtureProperty $null
        if ($null -eq $fixtureValue -and $ImmediatelyBeforeMutation) {
            $fixtureValue = Get-SashimiPropertyValue $script:publishFixture 'LiveRemoteBranch' $null
        }
        if ($null -eq $fixtureValue) {
            return [pscustomobject][ordered]@{
                Ref = "refs/heads/$Name"
                ObjectType = 'commit'
                Sha = $PinnedHeadSha.ToLowerInvariant()
            }
        }
        return Convert-RemoteBranchShape -Value $fixtureValue
    }

    # The generated branch format contains only an Issue number, ASCII letters,
    # hyphens, hexadecimal digits, and one slash, so it is safe as the GitHub
    # REST ref suffix and cannot inject a query string or path traversal.
    $branchResult = Invoke-PublishGh -Operation "Remote branch '$Name' exact pin query" -Arguments @(
        'api', "repos/$($script:publishConfig.Repository)/git/ref/heads/$Name"
    )
    return Convert-RemoteBranchShape (ConvertFrom-PublishJson $branchResult.StdOut 'Remote branch exact pin query')
}

function Assert-LiveRemoteBranchPin {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$ImmediatelyBeforeMutation
    )

    $liveRemoteBranch = Get-LiveRemoteBranch -Name $Name -ImmediatelyBeforeMutation:$ImmediatelyBeforeMutation
    if ([string]$liveRemoteBranch.Ref -cne "refs/heads/$Name" -or
        [string]$liveRemoteBranch.ObjectType -cne 'commit' -or
        [string]$liveRemoteBranch.Sha -cne $PinnedHeadSha.ToLowerInvariant()) {
        $script:pinCurrent = $false
        throw 'The canonical remote branch no longer resolves to the exact pushed head SHA; no PR mutation was attempted.'
    }
    return $liveRemoteBranch
}

function Assert-LivePullRequestIdentityPin {
    if ($PullRequestNumber -lt 1 -or $PinnedHeadSha -notmatch '^[0-9a-fA-F]{40}$' -or [string]::IsNullOrWhiteSpace($PinnedHeadRef)) {
        throw 'A PR mutation requires its exact number, 40-character head SHA, and head ref.'
    }
    $live = Get-LivePullRequest -Number $PullRequestNumber
    $effectiveContentPin = $PinnedPullRequestContentSha256
    if ($effectiveContentPin -cnotmatch '^[0-9a-f]{64}$') {
        if ($null -eq $script:publishFixture) { throw 'A PR mutation requires its exact lowercase SHA-256 title/body content pin.' }
        $effectiveContentPin = [string]$live.ContentSha256
    }
    $pinned = [pscustomobject]@{
        Number = $PullRequestNumber; State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'; BaseRepository = [string]$script:publishConfig.Repository
        HeadRef = $PinnedHeadRef; HeadSha = $PinnedHeadSha.ToLowerInvariant(); HeadRepository = [string]$script:publishConfig.Repository
        ContentSha256 = $effectiveContentPin
    }
    $comparison = Test-SashimiPinnedPullRequest -Pinned $pinned -Live $live
    if (-not $comparison.Current) {
        $script:pinCurrent = $false
        return [pscustomobject]@{ Current = $false; ChangedField = $comparison.ChangedField; Live = $live }
    }
    $trust = Test-SashimiPullRequestTrust -PullRequest $live -Repository ([string]$script:publishConfig.Repository) -AuthorizedAuthors @($script:publishConfig.Security.AuthorizedPrAuthors)
    if (-not $trust.Trusted) {
        $script:pinCurrent = $false
        return [pscustomobject]@{ Current = $false; ChangedField = [string]::Join(',', $trust.Reasons); Live = $live }
    }
    return [pscustomobject]@{ Current=$true; ChangedField=''; Live=$live }
}

function Assert-LivePin {
    $identity = Assert-LivePullRequestIdentityPin
    if (-not $identity.Current) {
        return [pscustomobject]@{ Current=$false; ChangedField=[string]$identity.ChangedField; Live=$identity.Live; PullRequestContentSha256=[string]$identity.Live.ContentSha256; ConversationSha256=''; ConversationRecords=@() }
    }
    $conversation = Get-LiveConversationState -Number $PullRequestNumber
    $effectivePinnedDigest = $PinnedConversationSha256
    if ($effectivePinnedDigest -cnotmatch '^[0-9a-f]{64}$') {
        if ($null -eq $script:publishFixture) { throw 'An existing-PR action requires a lowercase SHA-256 conversation pin.' }
        $effectivePinnedDigest = [string]$conversation.Sha256
    }
    if ([string]$conversation.Sha256 -cne $effectivePinnedDigest) {
        $script:pinCurrent = $false
        return [pscustomobject]@{ Current=$false; ChangedField='ConversationSha256'; Live=$identity.Live; PullRequestContentSha256=[string]$identity.Live.ContentSha256; ConversationSha256=[string]$conversation.Sha256; ConversationRecords=@($conversation.Records) }
    }
    return [pscustomobject]@{ Current = $true; ChangedField = ''; Live = $identity.Live; PullRequestContentSha256=[string]$identity.Live.ContentSha256; ConversationSha256=[string]$conversation.Sha256; ConversationRecords=@($conversation.Records) }
}

function Assert-LiveIssueConversationPin {
    $conversation = Get-LiveConversationState -Number 0
    $effectivePinnedDigest = $PinnedConversationSha256
    if ($effectivePinnedDigest -cnotmatch '^[0-9a-f]{64}$') {
        if ($null -eq $script:publishFixture) { throw 'An Issue action requires a lowercase SHA-256 conversation pin.' }
        $effectivePinnedDigest = [string]$conversation.Sha256
    }
    if ([string]$conversation.Sha256 -cne $effectivePinnedDigest) {
        $script:pinCurrent = $false
        return [pscustomobject]@{ Current=$false; ChangedField='ConversationSha256'; ConversationSha256=[string]$conversation.Sha256; ConversationRecords=@($conversation.Records) }
    }
    return [pscustomobject]@{ Current=$true; ChangedField=''; ConversationSha256=[string]$conversation.Sha256; ConversationRecords=@($conversation.Records) }
}

function Assert-VerifiedCommentConversationAdvance {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$BeforeRecords,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$AfterRecords,
        [Parameter(Mandatory = $true)][object]$VerifiedComment
    )

    $before = @($BeforeRecords | ForEach-Object { ConvertTo-SashimiCanonicalConversationRecord -Record $_ })
    $after = @($AfterRecords | ForEach-Object { ConvertTo-SashimiCanonicalConversationRecord -Record $_ })
    $verified = ConvertTo-SashimiCanonicalConversationRecord -Record $VerifiedComment
    if ($after.Count -ne ($before.Count + 1)) { throw 'Conversation changed by more than the one verified Host comment.' }
    $afterByUrl = @{}
    foreach ($record in $after) {
        if ([string]::IsNullOrWhiteSpace([string]$record.Url) -or $afterByUrl.ContainsKey([string]$record.Url)) { throw 'Post-comment conversation contains a missing or duplicate URL.' }
        $afterByUrl[[string]$record.Url] = $record
    }
    foreach ($record in $before) {
        if (-not $afterByUrl.ContainsKey([string]$record.Url) -or
            (ConvertTo-SashimiJson $record) -cne (ConvertTo-SashimiJson $afterByUrl[[string]$record.Url])) {
            throw 'An existing conversation record changed during Host comment publication.'
        }
    }
    if (@($before | Where-Object { [string]$_.Url -ceq [string]$verified.Url }).Count -gt 0 -or
        -not $afterByUrl.ContainsKey([string]$verified.Url) -or
        (ConvertTo-SashimiJson $verified) -cne (ConvertTo-SashimiJson $afterByUrl[[string]$verified.Url])) {
        throw 'The sole new conversation record is not the immutable Host comment that was read back.'
    }
    return Get-SashimiConversationSha256 -Records $after
}

function Assert-PublishProjectSchema {
    param([Parameter(Mandatory = $true)][object[]]$Fields)
    foreach ($requiredName in @('Status','Priority','Area','Size')) {
        if (@($Fields | Where-Object { [string](Get-SashimiPropertyValue $_ 'name' (Get-SashimiPropertyValue $_ 'Name' '')) -ceq $requiredName }).Count -ne 1) {
            throw "Project schema no longer contains exactly one '$requiredName' field."
        }
    }
    foreach ($expected in @(
        [pscustomobject]@{ Name='Status'; Options=@('Backlog','Ready','In Progress','Review','Verification','Done') },
        [pscustomobject]@{ Name='Priority'; Options=@('P0','P1','P2','P3') }
    )) {
        $field = @($Fields | Where-Object { [string](Get-SashimiPropertyValue $_ 'name' (Get-SashimiPropertyValue $_ 'Name' '')) -ceq $expected.Name })[0]
        $options = @((Get-SashimiPropertyValue $field 'options' (Get-SashimiPropertyValue $field 'Options' @())) | ForEach-Object {
            if ($_ -is [string]) { [string]$_ } else { [string](Get-SashimiPropertyValue $_ 'name' (Get-SashimiPropertyValue $_ 'Name' '')) }
        })
        if ($options.Count -ne $expected.Options.Count -or @($expected.Options | Where-Object { $options -cnotcontains $_ }).Count -gt 0 -or @($options | Select-Object -Unique).Count -ne $options.Count) {
            throw "Project '$($expected.Name)' options changed from the exact host contract."
        }
    }
}

function Get-ProjectContract {
    if ($null -ne $script:publishFixture) {
        $fixtureFields = Get-SashimiPropertyValue $script:publishFixture 'Fields' @(
            [pscustomobject]@{ name='Status'; options=@('Backlog','Ready','In Progress','Review','Verification','Done') },
            [pscustomobject]@{ name='Priority'; options=@('P0','P1','P2','P3') },
            [pscustomobject]@{ name='Area'; options=@() }, [pscustomobject]@{ name='Size'; options=@() }
        )
        Assert-PublishProjectSchema -Fields @($fixtureFields)
        return [pscustomobject]@{
            ProjectId = [string](Get-SashimiPropertyValue $script:publishFixture 'ProjectId' 'fixture-project')
            StatusFieldId = [string](Get-SashimiPropertyValue $script:publishFixture 'StatusFieldId' 'fixture-status')
            CurrentStatus = [string](Get-SashimiPropertyValue $script:publishFixture 'CurrentStatus' $FromStatus)
            OpenPullRequestCount = [int](Get-SashimiPropertyValue $script:publishFixture 'OpenPullRequestCount' 0)
            OpenPullRequestNumbers = @((Get-SashimiPropertyValue $script:publishFixture 'OpenPullRequestNumbers' $(if ([int](Get-SashimiPropertyValue $script:publishFixture 'OpenPullRequestCount' 0) -eq 1 -and $PullRequestNumber -gt 0) { @($PullRequestNumber) } else { @() })) | ForEach-Object { [int]$_ })
            IssueUpdatedAt = [string](Get-SashimiPropertyValue $script:publishFixture 'IssueUpdatedAt' $PinnedIssueUpdatedAt)
            IssueBodySha256 = [string](Get-SashimiPropertyValue $script:publishFixture 'IssueBodySha256' $PinnedIssueBodySha256)
            Options = Get-SashimiPropertyValue $script:publishFixture 'StatusOptions' ([pscustomobject]@{ Backlog='backlog'; Ready='ready'; 'In Progress'='in-progress'; Review='review'; Verification='verification'; Done='done' })
        }
    }
    $query = 'query HostPublishContract($login:String!,$number:Int!,$itemId:ID!){user(login:$login){projectV2(number:$number){id fields(first:100){totalCount pageInfo{hasNextPage endCursor} nodes{__typename ... on ProjectV2Field{id name dataType} ... on ProjectV2IterationField{id name} ... on ProjectV2SingleSelectField{id name options{id name}} ... on ProjectV2RepositoryField{id name}}}}} node(id:$itemId){... on ProjectV2Item{id project{id} content{... on Issue{number state updatedAt body repository{nameWithOwner}}} statusValue:fieldValueByName(name:"Status"){... on ProjectV2ItemFieldSingleSelectValue{name}} linkedValue:fieldValueByName(name:"Linked pull requests"){... on ProjectV2ItemFieldPullRequestValue{pullRequests(first:100){totalCount pageInfo{hasNextPage endCursor} nodes{number state}}}}}}}'
    $result = Invoke-PublishGh -Operation 'Project status contract query' -Arguments @('api','graphql','-f',"login=$($script:publishConfig.ProjectOwner)",'-F',"number=$($script:publishConfig.ProjectNumber)",'-f',"itemId=$ProjectItemId",'-f',"query=$query")
    $json = ConvertFrom-PublishJson $result.StdOut 'Project status contract query'
    $project = $json.data.user.projectV2; $node = $json.data.node
    if ($null -eq $project -or $null -eq $node -or [string]$node.project.id -cne [string]$project.id -or [int]$node.content.number -ne $IssueNumber -or
        [string]$node.content.state -cne 'OPEN' -or [string]$node.content.repository.nameWithOwner -cne [string]$script:publishConfig.Repository) {
        throw 'Project item identity changed or does not match the pinned Issue.'
    }
    if ([bool]$project.fields.pageInfo.hasNextPage -or [int]$project.fields.totalCount -ne @($project.fields.nodes).Count) { throw 'Project field pagination exceeds the fail-closed publish query.' }
    Assert-PublishProjectSchema -Fields @($project.fields.nodes)
    $statusField = @($project.fields.nodes | Where-Object { [string]$_.name -ceq 'Status' })[0]
    if ($null -eq $node.PSObject.Properties['linkedValue']) { throw "Project response omitted the exact 'Linked pull requests' field." }
    $linkedConnection = Get-SashimiPropertyValue (Get-SashimiPropertyValue $node 'linkedValue' $null) 'pullRequests' $null
    if ($null -ne $linkedConnection -and ([bool]$linkedConnection.pageInfo.hasNextPage -or [int]$linkedConnection.totalCount -ne @($linkedConnection.nodes).Count)) {
        throw 'Project linked PR connection is incomplete during pin revalidation.'
    }
    $openCount = if ($null -eq $linkedConnection) { 0 } else { @($linkedConnection.nodes | Where-Object { [string]$_.state -ceq 'OPEN' }).Count }
    $openNumbers = if ($null -eq $linkedConnection) { @() } else { @($linkedConnection.nodes | Where-Object { [string]$_.state -ceq 'OPEN' } | ForEach-Object { [int]$_.number }) }
    $options = [ordered]@{}; foreach ($option in @($statusField.options)) { $options[[string]$option.name] = [string]$option.id }
    return [pscustomobject]@{
        ProjectId = [string]$project.id; StatusFieldId = [string]$statusField.id; CurrentStatus = [string]$node.statusValue.name
        OpenPullRequestCount = $openCount; OpenPullRequestNumbers=$openNumbers; Options = [pscustomobject]$options; IssueUpdatedAt = [string]$node.content.updatedAt
        IssueBodySha256 = Get-SashimiTextSha256 -Text ([string]$node.content.body)
    }
}

function Test-IssuePin {
    param([Parameter(Mandatory = $true)][object]$Contract)
    if ($PinnedIssueUpdatedAt -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$' -or $PinnedIssueBodySha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'An Issue mutation or revalidation requires the pinned Issue updatedAt and lowercase SHA-256 body digest.'
    }
    $current = ([string]$Contract.IssueUpdatedAt -ceq $PinnedIssueUpdatedAt -and [string]$Contract.IssueBodySha256 -ceq $PinnedIssueBodySha256)
    if (-not $current) { $script:pinCurrent = $false }
    return $current
}

function Assert-CommentReadBack {
    param([Parameter(Mandatory = $true)][string]$Url, [Parameter(Mandatory = $true)][string]$ExpectedBody)
    if ($Url -notmatch '^https://github\.com/DongGyunLeeeee/sashimi-boy-unity/(?<kind>issues|pull)/(?<number>\d+)#issuecomment-(?<id>\d+)$') {
        throw 'Comment mutation did not return an exact repository issue-comment URL.'
    }
    $expectedKind = if ($CommentTarget -ceq 'PullRequest') { 'pull' } else { 'issues' }
    $expectedNumber = if ($CommentTarget -ceq 'PullRequest') { $PullRequestNumber } else { $IssueNumber }
    if ([string]$Matches.kind -cne $expectedKind -or [int]$Matches.number -ne $expectedNumber) { throw 'Comment read-back URL targets a different Issue or PR.' }
    $commentId = [string]$Matches.id
    $read = Invoke-PublishGh -Operation 'Comment immutable read-back' -Arguments @('api',"repos/$($script:publishConfig.Repository)/issues/comments/$commentId")
    $comment = ConvertFrom-PublishJson $read.StdOut 'Comment immutable read-back'
    $author = [string](Get-SashimiPropertyValue (Get-SashimiPropertyValue $comment 'user' $null) 'login' '')
    $actualBody = ([string](Get-SashimiPropertyValue $comment 'body' '')) -replace "`r`n", "`n"
    $normalizedExpectedBody = $ExpectedBody -replace "`r`n", "`n"
    if ([string](Get-SashimiPropertyValue $comment 'html_url' '') -cne $Url -or
        $actualBody -cne $normalizedExpectedBody -or
        @($script:publishConfig.Security.AuthorizedPrAuthors) -cnotcontains $author -or
        [string](Get-SashimiPropertyValue $comment 'created_at' '') -cne [string](Get-SashimiPropertyValue $comment 'updated_at' '')) {
        throw 'Comment read-back did not match exact URL, body, authorized author, or immutable timestamp.'
    }
    $association = [string](Get-SashimiPropertyValue $comment 'author_association' '')
    if ([string]::IsNullOrWhiteSpace($association)) { throw 'Comment read-back omitted author association required for exact conversation pinning.' }
    return [pscustomobject][ordered]@{
        Kind=if ($CommentTarget -ceq 'PullRequest') { 'PullRequestComment' } else { 'IssueComment' }
        Url=$Url; CreatedAt=[string]$comment.created_at; UpdatedAt=[string]$comment.updated_at; SubmittedAt=''
        AuthorLogin=$author; AuthorAssociation=$association; WasEdited=$false; Body=$actualBody; ReviewState=''; CommitOid=''
    }
}

try {
    $script:publishConfig = Import-SashimiHostConfig -ConfigPath $ConfigPath
    $script:publishFixture = $null
    if ($FixturePath) {
        $fixture = Assert-SashimiFixtureAllowed -FixturePath $FixturePath -DryRun:$DryRun
        $script:publishFixture = Read-SashimiJsonFile $fixture
    }
    [void](Assert-PublishAuthenticatedActor)

    $resultData = $null
    switch ($Action) {
        'RevalidatePin' {
            $pin = Assert-LivePin
            $resultData = [ordered]@{ Current = [bool]$pin.Current; ChangedField = [string]$pin.ChangedField; LivePullRequest = $pin.Live; PullRequestContentSha256=[string]$pin.PullRequestContentSha256; ConversationSha256=[string]$pin.ConversationSha256 }
        }
        'RevalidateIssue' {
            if ([string]::IsNullOrWhiteSpace($ProjectItemId) -or [string]::IsNullOrWhiteSpace($FromStatus)) { throw 'RevalidateIssue requires ProjectItemId and the pinned current status in FromStatus.' }
            $contract = Get-ProjectContract
            $current = (Test-IssuePin -Contract $contract) -and ([string]$contract.CurrentStatus -ceq $FromStatus)
            if ($FromStatus -ceq 'Ready') { $current = $current -and [int]$contract.OpenPullRequestCount -eq 0 }
            elseif ($FromStatus -ceq 'In Progress') {
                $expectedOpenCount = if ($PullRequestNumber -gt 0) { 1 } else { 0 }
                $current = $current -and [int]$contract.OpenPullRequestCount -eq $expectedOpenCount
            }
            elseif ($FromStatus -ceq 'Review') { $current = $current -and [int]$contract.OpenPullRequestCount -eq 1 }
            if ($PullRequestNumber -gt 0) { $current = $current -and @($contract.OpenPullRequestNumbers).Count -eq 1 -and [int]$contract.OpenPullRequestNumbers[0] -eq $PullRequestNumber }
            $conversationDigest = $PinnedConversationSha256
            if ($PullRequestNumber -gt 0) {
                $pin = Assert-LivePin
                $conversationDigest = [string]$pin.ConversationSha256
                $current = $current -and [bool]$pin.Current
            }
            else {
                $pin = Assert-LiveIssueConversationPin
                $conversationDigest = [string]$pin.ConversationSha256
                $current = $current -and [bool]$pin.Current
            }
            if (-not $current) { $script:pinCurrent = $false }
            $resultData = [ordered]@{ Current = $current; CurrentStatus = [string]$contract.CurrentStatus; OpenPullRequestCount = [int]$contract.OpenPullRequestCount; IssueUpdatedAt=[string]$contract.IssueUpdatedAt; IssueBodySha256=[string]$contract.IssueBodySha256; PullRequestContentSha256=if ($PullRequestNumber -gt 0) { [string]$pin.PullRequestContentSha256 } else { '' }; ConversationSha256=$conversationDigest }
        }
        'Transition' {
            [void](Assert-SashimiTransition -Role $Role -From $FromStatus -To $ToStatus)
            if ([string]::IsNullOrWhiteSpace($ProjectItemId)) { throw 'Transition requires ProjectItemId.' }
            $pin = $null
            if ($PullRequestNumber -gt 0) {
                $pin = Assert-LivePin
                if (-not $pin.Current) { throw "Stale PR pin; changed field: $($pin.ChangedField). No status mutation was attempted." }
            }
            else {
                $pin = Assert-LiveIssueConversationPin
                if (-not $pin.Current) { throw 'Stale Issue conversation pin; no status mutation was attempted.' }
            }
            $contract = Get-ProjectContract
            if (-not (Test-IssuePin -Contract $contract)) { throw 'The Issue body or updatedAt changed; no status mutation was attempted.' }
            if ($PullRequestNumber -gt 0 -and (@($contract.OpenPullRequestNumbers).Count -ne 1 -or [int]$contract.OpenPullRequestNumbers[0] -ne $PullRequestNumber)) { throw 'The pinned PR is no longer the Issue''s exact linked open PR; no status mutation was attempted.' }
            if ([string]$contract.CurrentStatus -cne $FromStatus) { throw "Project status is '$($contract.CurrentStatus)', not pinned '$FromStatus'." }
            if ($FromStatus -ceq 'Ready' -and [int]$contract.OpenPullRequestCount -ne 0) { throw 'Ready item gained a linked open PR; no status mutation was attempted.' }
            $optionProperty = $contract.Options.PSObject.Properties[$ToStatus]
            if ($null -eq $optionProperty -or [string]::IsNullOrWhiteSpace([string]$optionProperty.Value)) { throw "Project Status option '$ToStatus' was not found exactly." }
            $mutation = 'mutation HostSetStatus($project:ID!,$item:ID!,$field:ID!,$option:String!){updateProjectV2ItemFieldValue(input:{projectId:$project,itemId:$item,fieldId:$field,value:{singleSelectOptionId:$option}}){projectV2Item{id}}}'
            $args = @('api','graphql','-f',"project=$($contract.ProjectId)",'-f',"item=$ProjectItemId",'-f',"field=$($contract.StatusFieldId)",'-f',"option=$($optionProperty.Value)",'-f',"query=$mutation")
            if ($PullRequestNumber -gt 0) {
                $pin = Assert-LivePin
                if (-not $pin.Current) { throw "Conversation or PR pin changed immediately before status mutation ($($pin.ChangedField))." }
            }
            else {
                $pin = Assert-LiveIssueConversationPin
                if (-not $pin.Current) { throw 'Issue conversation changed immediately before status mutation.' }
            }
            $afterContract = $contract
            if ($null -ne $script:publishFixture) { [void](Assert-PublishAuthenticatedActor -ImmediatelyBeforeMutation) }
            if ($null -eq $script:publishFixture) {
                $mutationResult = Invoke-PublishGh -Operation "Project transition $FromStatus -> $ToStatus" -Arguments $args -Mutation
                $mutationJson = ConvertFrom-PublishJson $mutationResult.StdOut 'Project status mutation'
                if ([string]$mutationJson.data.updateProjectV2ItemFieldValue.projectV2Item.id -cne $ProjectItemId) { throw 'Project status mutation did not return the pinned item ID.' }
                $readBack = Get-ProjectContract
                if ([string]$readBack.CurrentStatus -cne $ToStatus) { throw "Project status mutation read-back is '$($readBack.CurrentStatus)', expected '$ToStatus'." }
                $afterContract = $readBack
            }
            else { $commands.Add([pscustomobject]@{ Operation = "Project transition $FromStatus -> $ToStatus"; FilePath = Protect-SashimiText ([string]$script:publishConfig.GitHubCli); Arguments = @($args | ForEach-Object { Protect-SashimiText $_ }); Mutation = $true }) }
            $resultData = [ordered]@{ From = $FromStatus; To = $ToStatus; Planned = $DryRun -or $null -ne $script:publishFixture; IssueUpdatedAt=[string]$afterContract.IssueUpdatedAt; IssueBodySha256=[string]$afterContract.IssueBodySha256; PullRequestContentSha256=if ($PullRequestNumber -gt 0 -and $null -ne $pin) { [string]$pin.PullRequestContentSha256 } else { '' }; ConversationSha256=if ($null -ne $pin) { [string]$pin.ConversationSha256 } else { $PinnedConversationSha256 } }
        }
        'Comment' {
            if (-not (Test-Path -LiteralPath $BodyPath -PathType Leaf)) { throw "Comment BodyPath does not exist: $BodyPath" }
            if ([string]::IsNullOrWhiteSpace($ProjectItemId)) { throw 'Comment requires the exact pinned ProjectItemId.' }
            $resolvedBodyPath = ConvertTo-SashimiPath -Path $BodyPath
            if (-not (Test-SashimiPathWithin -Path $resolvedBodyPath -Root ([string]$script:publishConfig.RunRoot))) { throw 'Comment BodyPath must be inside the configured run root.' }
            Assert-SashimiNoReparsePoint -Path $resolvedBodyPath
            if ((Get-Item -LiteralPath $resolvedBodyPath).Length -gt 1048576) { throw 'Comment body exceeds the 1 MiB host limit.' }
            $body = [IO.File]::ReadAllText($resolvedBodyPath, [Text.Encoding]::UTF8)
            Assert-PublishTextContainsNoSensitiveContent -Text $body -Context 'Comment body'
            if (-not [string]::Equals($body,(Protect-SashimiText $body),[StringComparison]::Ordinal)) { throw 'Comment body contains secret, save-data, or user-profile content forbidden from publication.' }
            $contract = Get-ProjectContract
            if (-not (Test-IssuePin -Contract $contract)) { throw 'The Issue body or updatedAt changed; no comment mutation was attempted.' }
            if ($PullRequestNumber -gt 0 -and (@($contract.OpenPullRequestNumbers).Count -ne 1 -or [int]$contract.OpenPullRequestNumbers[0] -ne $PullRequestNumber)) { throw 'The pinned PR is no longer the Issue''s exact linked open PR; no comment mutation was attempted.' }
            if ($PullRequestNumber -gt 0) {
                $pin = Assert-LivePin
                if (-not $pin.Current) { throw "Stale PR pin; no comment mutation was attempted ($($pin.ChangedField))." }
            }
            else {
                $pin = Assert-LiveIssueConversationPin
                if (-not $pin.Current) { throw 'Stale Issue conversation pin; no comment mutation was attempted.' }
            }
            $beforeConversationRecords = @($pin.ConversationRecords)
            if ($CommentTarget -ceq 'PullRequest') {
                $args = @('pr','comment',[string]$PullRequestNumber,'--repo',[string]$script:publishConfig.Repository,'--body-file',$resolvedBodyPath)
            }
            else {
                $args = @('issue','comment',[string]$IssueNumber,'--repo',[string]$script:publishConfig.Repository,'--body-file',$resolvedBodyPath)
            }
            $verifiedComment = $null
            if ($null -ne $script:publishFixture) { [void](Assert-PublishAuthenticatedActor -ImmediatelyBeforeMutation) }
            if ($null -eq $script:publishFixture) {
                $commentResult = Invoke-PublishGh -Operation "$CommentTarget comment" -Arguments $args -Mutation
                $commentUrl = $commentResult.StdOut.Trim()
                $verifiedComment = Assert-CommentReadBack -Url $commentUrl -ExpectedBody $body
                $afterCommentContract = Get-ProjectContract
            }
            else {
                $commands.Add([pscustomobject]@{ Operation = "$CommentTarget comment"; FilePath = Protect-SashimiText ([string]$script:publishConfig.GitHubCli); Arguments = @($args | ForEach-Object { Protect-SashimiText $_ }); Mutation = $true })
                $commentUrl = [string](Get-SashimiPropertyValue $script:publishFixture 'CommentUrl' 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/0#issuecomment-0')
                $afterCommentContract = $contract
            }
            if ($null -ne $script:publishFixture -and
                $null -eq $script:publishFixture.PSObject.Properties['ConversationRecordsAfterMutation'] -and
                $null -eq $script:publishFixture.PSObject.Properties['ConversationSha256AfterMutation']) {
                # A fixture has no external comment store. Advance a deterministic
                # opaque fixture pin so existing harness scenarios stay stateful.
                $afterConversationDigest = Get-SashimiTextSha256 -Text ("fixture-host-comment-v1`n$PinnedConversationSha256`n$commentUrl`n$body")
            }
            else {
                $afterConversation = Get-LiveConversationState -Number $PullRequestNumber -AfterHostMutation
                if ($null -eq $verifiedComment) {
                    $verifiedComment = Get-SashimiPropertyValue $script:publishFixture 'VerifiedCommentAfterMutation' $null
                    if ($null -eq $verifiedComment) { throw 'Exact post-mutation conversation fixtures require VerifiedCommentAfterMutation.' }
                }
                $afterConversationDigest = Assert-VerifiedCommentConversationAdvance -BeforeRecords $beforeConversationRecords -AfterRecords @($afterConversation.Records) -VerifiedComment $verifiedComment
            }
            if ($PullRequestNumber -gt 0) {
                $postIdentity = Assert-LivePullRequestIdentityPin
                if (-not $postIdentity.Current) { throw 'PR head/ref changed during comment publication; no later transition is allowed.' }
            }
            $resultData = [ordered]@{ Target = $CommentTarget; Url = $commentUrl; Planned = $DryRun -or $null -ne $script:publishFixture; ReadBack=($null -eq $script:publishFixture -and -not $DryRun); IssueUpdatedAt=[string]$afterCommentContract.IssueUpdatedAt; IssueBodySha256=[string]$afterCommentContract.IssueBodySha256; PullRequestContentSha256=if ($PullRequestNumber -gt 0) { [string]$postIdentity.Live.ContentSha256 } else { '' }; ConversationSha256=$afterConversationDigest }
        }
        'CreateDraftPullRequest' {
            if ($Role -cne 'Developer' -or $PullRequestNumber -ne 0) { throw 'Only Developer NewWork without an existing PR may create a Draft PR.' }
            if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -eq 'main' -or $Branch -notmatch '^issue/\d+-host-[0-9a-f]{32}$') { throw 'CreateDraftPullRequest requires the exact Host-generated non-main branch format.' }
            Assert-PublishTextContainsNoSensitiveContent -Text $Title -Context 'Draft PR title'
            if ([string]::IsNullOrWhiteSpace($Title) -or $Title.Length -gt 256 -or $Title -match '[\r\n]' -or -not [string]::Equals($Title,(Protect-SashimiText $Title),[StringComparison]::Ordinal)) { throw 'Draft PR title is empty, oversized, multiline, or contains forbidden content.' }
            if (-not (Test-Path -LiteralPath $BodyPath -PathType Leaf)) { throw 'Draft PR body file is missing.' }
            $resolvedPrBodyPath = ConvertTo-SashimiPath -Path $BodyPath
            if (-not (Test-SashimiPathWithin -Path $resolvedPrBodyPath -Root ([string]$script:publishConfig.RunRoot))) { throw 'Draft PR BodyPath must be inside the configured run root.' }
            Assert-SashimiNoReparsePoint -Path $resolvedPrBodyPath
            $prBody = [IO.File]::ReadAllText($resolvedPrBodyPath,[Text.Encoding]::UTF8)
            Assert-PublishTextContainsNoSensitiveContent -Text $prBody -Context 'Draft PR body'
            if ((Get-Item -LiteralPath $resolvedPrBodyPath).Length -gt 1048576 -or -not [string]::Equals($prBody,(Protect-SashimiText $prBody),[StringComparison]::Ordinal)) { throw 'Draft PR body is too large or contains forbidden secret/profile/save content.' }
            if ([string]::IsNullOrWhiteSpace($ProjectItemId)) { throw 'CreateDraftPullRequest requires the pinned ProjectItemId.' }
            $createdContentSha256 = Get-SashimiPullRequestContentSha256 -Title $Title -Body $prBody
            $contract = Get-ProjectContract
            if (-not (Test-IssuePin -Contract $contract)) { throw 'The Issue body or updatedAt changed; no PR mutation was attempted.' }
            if ([string]$contract.CurrentStatus -cne 'In Progress' -or [int]$contract.OpenPullRequestCount -ne 0) { throw 'NewWork Issue is stale or already linked to an open PR; no PR was created.' }
            if ($PinnedHeadSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'CreateDraftPullRequest requires the exact pushed head SHA.' }
            [void](Assert-LiveRemoteBranchPin -Name $Branch)
            $issueConversationPin = Assert-LiveIssueConversationPin
            if (-not $issueConversationPin.Current) { throw 'Issue conversation changed immediately before Draft PR creation.' }
            $args = @('pr','create','--repo',[string]$script:publishConfig.Repository,'--draft','--base','main','--head',$Branch,'--title',$Title,'--body-file',$resolvedPrBodyPath)
            # Re-read the canonical ref after every other potentially lengthy
            # preflight. A branch move at the same name now fails before gh can
            # create a PR for a head that was not the Host-validated commit.
            $liveRemoteBranch = Assert-LiveRemoteBranchPin -Name $Branch -ImmediatelyBeforeMutation
            if ($null -ne $script:publishFixture) { [void](Assert-PublishAuthenticatedActor -ImmediatelyBeforeMutation) }
            if ($null -eq $script:publishFixture) {
                $created = Invoke-PublishGh -Operation 'Create Draft PR' -Arguments $args -Mutation; $createdUrl = $created.StdOut.Trim()
                if ($createdUrl -notmatch '^https://github\.com/DongGyunLeeeee/sashimi-boy-unity/pull/(?<number>\d+)$') { throw 'Draft PR creation did not return an exact repository PR URL.' }
                $createdNumber = [int]$Matches.number
                $liveCreated = Get-LivePullRequest -Number $createdNumber
                $trust = Test-SashimiPullRequestTrust -PullRequest $liveCreated -Repository ([string]$script:publishConfig.Repository) -AuthorizedAuthors @($script:publishConfig.Security.AuthorizedPrAuthors)
                if (-not $trust.Trusted -or [string]$liveCreated.HeadRef -cne $Branch -or [string]$liveCreated.HeadSha -cne $PinnedHeadSha.ToLowerInvariant() -or
                    [string]$liveCreated.ContentSha256 -cne $createdContentSha256) { throw 'Created Draft PR failed exact trust, ref, SHA, or title/body read-back.' }
                $afterCreateContract = Get-ProjectContract
                if (@($afterCreateContract.OpenPullRequestNumbers).Count -ne 1 -or [int]$afterCreateContract.OpenPullRequestNumbers[0] -ne $createdNumber) { throw 'Created Draft PR is not the Issue''s exact linked open PR.' }
            }
            else {
                $commands.Add([pscustomobject]@{ Operation = 'Create Draft PR'; FilePath = Protect-SashimiText ([string]$script:publishConfig.GitHubCli); Arguments = @($args | ForEach-Object { Protect-SashimiText $_ }); Mutation = $true })
                $createdUrl = [string](Get-SashimiPropertyValue $script:publishFixture 'CreatedPullRequestUrl' 'https://github.com/example/example/pull/1')
                if ($createdUrl -match '/pull/(?<number>\d+)$') { $createdNumber = [int]$Matches.number } else { $createdNumber = 1 }
                $afterCreateContract=$contract
            }
            $afterCreateConversation = Get-LiveConversationState -Number $createdNumber -AfterHostMutation
            if ([string]$afterCreateConversation.Sha256 -cne [string]$issueConversationPin.ConversationSha256 -or
                @($afterCreateConversation.Records | Where-Object { [string](Get-SashimiPropertyValue $_ 'Kind' '') -in @('PullRequestComment','PullRequestReview') }).Count -ne 0) {
                $script:pinCurrent = $false
                throw 'Issue conversation changed or the new Draft PR started with comments/reviews; pin advancement was refused.'
            }
            $resultData = [ordered]@{ Url = $createdUrl; Planned = $DryRun -or $null -ne $script:publishFixture; RemoteBranchRef=[string]$liveRemoteBranch.Ref; RemoteBranchSha=[string]$liveRemoteBranch.Sha; IssueUpdatedAt=[string]$afterCreateContract.IssueUpdatedAt; IssueBodySha256=[string]$afterCreateContract.IssueBodySha256; PullRequestContentSha256=$createdContentSha256; ConversationSha256=[string]$afterCreateConversation.Sha256 }
        }
    }

    $output = [ordered]@{
        SchemaVersion = 1; Tool = 'Publish-SashimiRunResult'; Success = $true; Action = $Action; Role = $Role
        IssueNumber = $IssueNumber; PinCurrent = $pinCurrent; DryRun = [bool]$DryRun; MutationAttempted = $mutationAttempted
        Commands = $commands.ToArray(); Result = $resultData; Error = ''
    }
    [Console]::Out.WriteLine((ConvertTo-SashimiJson $output))
    exit 0
}
catch {
    $rawError = [string]$_.Exception.Message
    $safeError = if (Test-SashimiRecognizableSensitiveText -Text $rawError -SensitiveValues $script:publishSensitiveValues) {
        'Publication failed because sensitive diagnostic content was detected and suppressed.'
    }
    else { Protect-SashimiTextWithExactValues -Text $rawError -ExactValues $script:publishSensitiveValues }
    $output = [ordered]@{
        SchemaVersion = 1; Tool = 'Publish-SashimiRunResult'; Success = $false; Action = $Action; Role = $Role
        IssueNumber = $IssueNumber; PinCurrent = $pinCurrent; DryRun = [bool]$DryRun; MutationAttempted = $mutationAttempted
        Commands = $commands.ToArray(); Result = $null; Error = $safeError
    }
    [Console]::Out.WriteLine((ConvertTo-SashimiJson $output))
    exit 1
}
