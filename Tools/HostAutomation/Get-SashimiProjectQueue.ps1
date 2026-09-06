#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [string]$FixturePath,
    [Parameter(DontShow = $true)][string]$GraphQLFixturePath,
    [string]$OutputPath,
    [string]$CancellationMarkerPath,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path $PSScriptRoot 'HostAutomation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Missing host automation helper: $commonPath")
    exit 10
}
. $commonPath

$graphQLFixture = $null
$graphQLFixtureIndex = 0
$script:queueSensitiveValues = @(
    Get-SashimiSensitiveEnvironmentEntries |
        ForEach-Object { [string]$_.Value } |
        Where-Object { $_.Length -ge 8 -and $_.Length -le 4096 } |
        Sort-Object -Unique
)

function Stop-QueueFailure {
    param([Parameter(Mandatory = $true)][string]$Message, [int]$Code = 1)
    $safeMessage = if (Test-SashimiRecognizableSensitiveText -Text $Message -SensitiveValues $script:queueSensitiveValues) {
        'Queue processing failed because sensitive source content was detected and suppressed.'
    }
    else { Protect-SashimiTextWithExactValues -Text $Message -ExactValues $script:queueSensitiveValues }
    $failure = [ordered]@{
        SchemaVersion = 1; Tool = 'Get-SashimiProjectQueue'; Success = $false; Selected = $false
        Role = 'None'; Mode = 'None'; DataSource = if ($FixturePath) { 'Fixture' } else { 'Live' }
        Encoding = 'UTF-8'; MutationAttempted = $false; Error = $safeMessage
    }
    [Console]::Out.WriteLine((ConvertTo-SashimiJson $failure))
    exit $Code
}

function Assert-QueuePayloadContainsNoSensitiveContent {
    param([Parameter(Mandatory = $true)][object]$QueueData)

    # The selected Issue/PR/conversation is persisted to State and then enters
    # Codex prompts. Reject the complete queue payload before candidate
    # selection so credentials cannot be retained merely by changing priority.
    $serialized = ConvertTo-SashimiJson -InputObject $QueueData
    if (Test-SashimiRecognizableSensitiveText -Text $serialized -SensitiveValues $script:queueSensitiveValues) {
        throw 'Queue source contains recognizable sensitive content; selection and persistence were refused.'
    }
}

function Invoke-QueueGraphQL {
    param([Parameter(Mandatory = $true)][string]$Query, [hashtable]$Variables = @{}, [string]$Operation = 'GraphQL')

    if ($null -ne $script:graphQLFixture) {
        $responses = @((Get-SashimiPropertyValue $script:graphQLFixture 'Responses' @()))
        if ($script:graphQLFixtureIndex -ge $responses.Count) { throw "GraphQL fixture has no response for $Operation." }
        $entry = $responses[$script:graphQLFixtureIndex]
        $script:graphQLFixtureIndex++
        $expectedOperation = [string](Get-SashimiPropertyValue $entry 'Operation' '')
        if ($expectedOperation -and $expectedOperation -cne $Operation) { throw "GraphQL fixture expected '$expectedOperation' but received '$Operation'." }
        $response = Get-SashimiPropertyValue $entry 'Response' $entry
        if (@((Get-SashimiPropertyValue $response 'errors' @())).Count -gt 0) {
            throw "$Operation returned GraphQL errors: $(Protect-SashimiText (ConvertTo-SashimiJson (Get-SashimiPropertyValue $response 'errors' @())))"
        }
        return $response
    }
    $arguments = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in @('api', 'graphql')) { $arguments.Add($part) }
    foreach ($entry in $Variables.GetEnumerator() | Sort-Object Key) {
        $prefix = if ($entry.Value -is [int] -or $entry.Value -is [long]) { '-F' } else { '-f' }
        $arguments.Add($prefix)
        $arguments.Add("$($entry.Key)=$($entry.Value)")
    }
    $arguments.Add('-f'); $arguments.Add("query=$Query")
    $result = Invoke-SashimiHostProcess -FilePath ([string]$script:queueConfig.GitHubCli) -ArgumentList $arguments.ToArray() -TimeoutSeconds ([int]$script:queueConfig.Timeouts.GitHubSeconds) -Kind GitHub -CancellationMarkerPath $CancellationMarkerPath -Environment @{ GH_PROMPT_DISABLED='1'; GIT_TERMINAL_PROMPT='0' }
    if (-not $result.Succeeded) {
        throw "$Operation failed. Command: $($result.Command); exit code: $($result.ExitCode); stderr: $($result.StdErr)"
    }
    try { $response = $result.StdOut | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw "$Operation returned invalid UTF-8 JSON: $($_.Exception.Message)" }
    if (@((Get-SashimiPropertyValue $response 'errors' @())).Count -gt 0) {
        throw "$Operation returned GraphQL errors: $(Protect-SashimiText (ConvertTo-SashimiJson (Get-SashimiPropertyValue $response 'errors' @())))"
    }
    return $response
}

function Assert-ProjectSchema {
    param([Parameter(Mandatory = $true)][object[]]$Fields)

    foreach ($name in @('Status', 'Priority', 'Area', 'Size')) {
        $matches = @($Fields | Where-Object { [string](Get-SashimiPropertyValue $_ 'Name' (Get-SashimiPropertyValue $_ 'name' '')) -ceq $name })
        if ($matches.Count -ne 1) { throw "Project schema must contain exactly one '$name' field." }
    }
    foreach ($contract in @(
        [pscustomobject]@{ Name = 'Status'; Options = @('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done') },
        [pscustomobject]@{ Name = 'Priority'; Options = @('P0', 'P1', 'P2', 'P3') }
    )) {
        $field = @($Fields | Where-Object { [string](Get-SashimiPropertyValue $_ 'Name' (Get-SashimiPropertyValue $_ 'name' '')) -ceq $contract.Name })[0]
        $options = @((Get-SashimiPropertyValue $field 'Options' (Get-SashimiPropertyValue $field 'options' @())) | ForEach-Object {
            if ($_ -is [string]) { $_ } else { [string](Get-SashimiPropertyValue $_ 'Name' (Get-SashimiPropertyValue $_ 'name' '')) }
        })
        if ($options.Count -ne $contract.Options.Count -or @($contract.Options | Where-Object { $options -cnotcontains $_ }).Count -gt 0 -or @($options | Select-Object -Unique).Count -ne $options.Count) {
            throw "Project '$($contract.Name)' options do not exactly match: $($contract.Options -join ', ')."
        }
    }
}

function Get-FixtureQueueData {
    param([Parameter(Mandatory = $true)][string]$Path)

    $allowed = Assert-SashimiFixtureAllowed -FixturePath $Path -DryRun:$DryRun
    $fixture = Read-SashimiJsonFile $allowed
    if ([int](Get-SashimiPropertyValue $fixture 'SchemaVersion' 0) -ne 1) { throw 'Queue fixture SchemaVersion must be 1.' }
    if ([string](Get-SashimiPropertyValue $fixture 'Encoding' '') -cne 'UTF-8') { throw 'Queue fixture must declare Encoding=UTF-8.' }
    $fields = @((Get-SashimiPropertyValue $fixture 'Fields' @()))
    Assert-ProjectSchema -Fields $fields
    $pages = @((Get-SashimiPropertyValue $fixture 'Pages' @()))
    if ($pages.Count -lt 1) { throw 'Queue fixture has no ProjectV2 pages.' }
    $nodes = New-Object 'System.Collections.Generic.List[object]'
    $ids = @{}; $cursors = @{}; $expectedTotal = -1
    for ($index = 0; $index -lt $pages.Count; $index++) {
        $page = $pages[$index]
        $total = [int](Get-SashimiPropertyValue $page 'TotalCount' -1)
        if ($total -lt 0) { throw "Fixture page $index has invalid TotalCount." }
        if ($expectedTotal -lt 0) { $expectedTotal = $total } elseif ($expectedTotal -ne $total) { throw 'ProjectV2 totalCount changed during pagination.' }
        foreach ($node in @((Get-SashimiPropertyValue $page 'Nodes' @()))) {
            $id = [string](Get-SashimiPropertyValue $node 'ProjectItemId' '')
            if ([string]::IsNullOrWhiteSpace($id) -or $ids.ContainsKey($id)) { throw 'ProjectV2 pagination returned a missing or duplicate item ID.' }
            $ids[$id] = $true; $nodes.Add($node)
        }
        $pageInfo = Get-SashimiPropertyValue $page 'PageInfo' $null
        if ($null -eq $pageInfo -or $null -eq $pageInfo.PSObject.Properties['HasNextPage'] -or $pageInfo.HasNextPage -isnot [bool]) { throw 'ProjectV2 pageInfo is incomplete.' }
        $hasNext = [bool]$pageInfo.HasNextPage
        $cursor = [string](Get-SashimiPropertyValue $pageInfo 'EndCursor' '')
        if ($hasNext) {
            if ([string]::IsNullOrWhiteSpace($cursor) -or $cursors.ContainsKey($cursor) -or $index -eq ($pages.Count - 1)) { throw 'ProjectV2 pagination has an empty, repeated, or unfulfilled cursor.' }
            $cursors[$cursor] = $true
        }
        elseif ($index -ne ($pages.Count - 1)) { throw 'Queue fixture contains pages after hasNextPage=false.' }
    }
    if ($nodes.Count -ne $expectedTotal) { throw "ProjectV2 pagination is incomplete ($($nodes.Count)/$expectedTotal)." }
    return [pscustomobject]@{ Fields = $fields; Items = $nodes.ToArray(); PageCount = $pages.Count }
}

function Get-LiveProjectFields {
    $query = 'query HostProjectFields($login:String!,$number:Int!,$cursor:String){user(login:$login){projectV2(number:$number){id fields(first:100,after:$cursor){totalCount nodes{__typename ... on ProjectV2Field{id name dataType} ... on ProjectV2IterationField{id name} ... on ProjectV2SingleSelectField{id name options{id name}} ... on ProjectV2RepositoryField{id name}} pageInfo{hasNextPage endCursor}}}}}'
    $nodes = New-Object 'System.Collections.Generic.List[object]'; $cursor = ''; $seen = @{}; $expected = -1
    while ($true) {
        $vars = @{ login = [string]$script:queueConfig.ProjectOwner; number = [int]$script:queueConfig.ProjectNumber }
        if ($cursor) { $vars.cursor = $cursor }
        $response = Invoke-QueueGraphQL -Query $query -Variables $vars -Operation 'ProjectV2 field pagination'
        $connection = $response.data.user.projectV2.fields
        if ($null -eq $connection) { throw 'ProjectV2 field response is missing its connection.' }
        $total = [int]$connection.totalCount
        if ($expected -lt 0) { $expected = $total } elseif ($expected -ne $total) { throw 'ProjectV2 field totalCount changed during pagination.' }
        foreach ($node in @($connection.nodes)) {
            $id = [string]$node.id; if (-not $id -or $seen.ContainsKey($id)) { throw 'ProjectV2 field pagination returned a missing or duplicate ID.' }
            $seen[$id] = $true
            $nodes.Add([pscustomobject]@{ Name = [string]$node.name; Options = @((Get-SashimiPropertyValue $node 'options' @()) | ForEach-Object { [string]$_.name }) })
        }
        if (-not [bool]$connection.pageInfo.hasNextPage) { break }
        $next = [string]$connection.pageInfo.endCursor
        if (-not $next -or $next -eq $cursor) { throw 'ProjectV2 field pagination returned an invalid cursor.' }
        $cursor = $next
    }
    if ($nodes.Count -ne $expected) { throw 'ProjectV2 field pagination ended before totalCount.' }
    return $nodes.ToArray()
}

function Convert-LiveProjectItem {
    param([Parameter(Mandatory = $true)][object]$Node)

    $content = $Node.content
    if ($null -eq $content -or [string]$content.__typename -cne 'Issue') { return $null }
    $status = [string](Get-SashimiPropertyValue $Node.statusValue 'name' '')
    $priority = [string](Get-SashimiPropertyValue $Node.priorityValue 'name' '')
    $pullRequests = New-Object 'System.Collections.Generic.List[object]'
    if ($null -eq $Node.PSObject.Properties['linkedValue']) {
        throw "Project item $($Node.id) response omitted the exact 'Linked pull requests' field."
    }
    $linked = Get-SashimiPropertyValue $Node 'linkedValue' $null
    if ($null -ne $linked) {
        $connection = Get-SashimiPropertyValue $linked 'pullRequests' $null
        if ($null -eq $connection) { throw "Project item $($Node.id) has an invalid linked PR field." }
        if ([bool]$connection.pageInfo.hasNextPage -or [int]$connection.totalCount -ne @($connection.nodes).Count) { throw "Linked PR pagination for item $($Node.id) is incomplete." }
        foreach ($pr in @($connection.nodes)) {
            $pullRequests.Add([pscustomobject][ordered]@{
                Number = [int]$pr.number; Url = [string]$pr.url; State = [string]$pr.state; IsDraft = [bool]$pr.isDraft
                Title = [string](Get-SashimiPropertyValue $pr 'title' ''); Body = [string](Get-SashimiPropertyValue $pr 'body' '')
                ContentSha256 = Get-SashimiPullRequestContentSha256 -Title ([string](Get-SashimiPropertyValue $pr 'title' '')) -Body ([string](Get-SashimiPropertyValue $pr 'body' ''))
                BaseRefName = [string]$pr.baseRefName; BaseRepository = [string]$pr.baseRepository.nameWithOwner
                HeadRef = [string]$pr.headRefName; HeadSha = [string]$pr.headRefOid
                HeadRepository = [string]$pr.headRepository.nameWithOwner; AuthorLogin = [string]$pr.author.login
                IsCrossRepository = ([string]$pr.headRepository.nameWithOwner -cne [string]$pr.baseRepository.nameWithOwner)
            })
        }
    }
    $labelsConnection = Get-SashimiPropertyValue $content 'labels' $null
    if ($null -ne $labelsConnection -and ([bool]$labelsConnection.pageInfo.hasNextPage -or [int]$labelsConnection.totalCount -ne @($labelsConnection.nodes).Count)) {
        throw "Issue #$($content.number) label pagination is incomplete."
    }
    return [pscustomobject][ordered]@{
        ProjectItemId = [string]$Node.id; UpdatedAt = [string]$Node.updatedAt; Status = $status; Priority = $priority
        IssueNumber = [int]$content.number; IssueTitle = [string]$content.title; IssueBody = [string]$content.body; IssueUpdatedAt = [string]$content.updatedAt
        IssueUrl = [string]$content.url; IssueState = [string]$content.state; IssueRepository = [string]$content.repository.nameWithOwner
        Labels = @($labelsConnection.nodes | ForEach-Object { [string]$_.name }); PullRequests = $pullRequests.ToArray(); Comments = @(); Reviews = @()
    }
}

function Get-LiveProjectItems {
    $query = 'query HostProjectItems($login:String!,$number:Int!,$cursor:String){user(login:$login){projectV2(number:$number){id items(first:50,after:$cursor){totalCount pageInfo{hasNextPage endCursor} nodes{id updatedAt content{__typename ... on Issue{number title body updatedAt url state repository{nameWithOwner} labels(first:100){totalCount nodes{name} pageInfo{hasNextPage endCursor}}}} statusValue:fieldValueByName(name:"Status"){... on ProjectV2ItemFieldSingleSelectValue{name}} priorityValue:fieldValueByName(name:"Priority"){... on ProjectV2ItemFieldSingleSelectValue{name}} linkedValue:fieldValueByName(name:"Linked pull requests"){... on ProjectV2ItemFieldPullRequestValue{pullRequests(first:100){totalCount pageInfo{hasNextPage endCursor} nodes{number title body url state isDraft baseRefName headRefName headRefOid author{login} baseRepository{nameWithOwner} headRepository{nameWithOwner}}}}}}}}}}'
    $items = New-Object 'System.Collections.Generic.List[object]'; $seen = @{}; $cursors = @{}; $cursor = ''; $expected = -1; $pageCount = 0
    while ($true) {
        $vars = @{ login = [string]$script:queueConfig.ProjectOwner; number = [int]$script:queueConfig.ProjectNumber }
        if ($cursor) { $vars.cursor = $cursor }
        $response = Invoke-QueueGraphQL -Query $query -Variables $vars -Operation 'ProjectV2 item pagination'
        $connection = $response.data.user.projectV2.items
        if ($null -eq $connection) { throw 'ProjectV2 item response is missing its connection.' }
        $pageCount++
        $total = [int]$connection.totalCount
        if ($expected -lt 0) { $expected = $total } elseif ($expected -ne $total) { throw 'ProjectV2 item totalCount changed during pagination.' }
        foreach ($node in @($connection.nodes)) {
            $id = [string]$node.id; if (-not $id -or $seen.ContainsKey($id)) { throw 'ProjectV2 item pagination returned a missing or duplicate ID.' }
            $seen[$id] = $true
            $converted = Convert-LiveProjectItem $node
            if ($null -ne $converted) { $items.Add($converted) }
        }
        if (-not [bool]$connection.pageInfo.hasNextPage) { break }
        $next = [string]$connection.pageInfo.endCursor
        if (-not $next -or $next -eq $cursor -or $cursors.ContainsKey($next)) { throw 'ProjectV2 item pagination returned an invalid cursor.' }
        $cursors[$next] = $true; $cursor = $next
    }
    if ($seen.Count -ne $expected) { throw "ProjectV2 item pagination is incomplete ($($seen.Count)/$expected)." }
    return [pscustomobject]@{ Items = $items.ToArray(); PageCount = $pageCount }
}

function Get-LiveConversationConnection {
    param([int]$Number, [ValidateSet('IssueComments', 'PullRequestComments', 'PullRequestReviews')][string]$Kind)

    $repoParts = ([string]$script:queueConfig.Repository).Split('/')
    if ($Kind -eq 'IssueComments') {
        $query = 'query HostIssueComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){issue(number:$number){comments(first:100,after:$cursor){totalCount nodes{body createdAt updatedAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
        $parentName = 'issue'; $connectionName = 'comments'
    }
    elseif ($Kind -eq 'PullRequestComments') {
        $query = 'query HostPrComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){comments(first:100,after:$cursor){totalCount nodes{body createdAt updatedAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
        $parentName = 'pullRequest'; $connectionName = 'comments'
    }
    else {
        $query = 'query HostPrReviews($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviews(first:100,after:$cursor){totalCount nodes{body url createdAt updatedAt submittedAt includesCreatedEdit state author{login} authorAssociation commit{oid}} pageInfo{hasNextPage endCursor}}}}}'
        $parentName = 'pullRequest'; $connectionName = 'reviews'
    }
    $all = New-Object 'System.Collections.Generic.List[object]'; $cursor = ''; $expected = -1; $seenCursors=@{}; $seenUrls=@{}
    while ($true) {
        $vars = @{ owner = $repoParts[0]; name = $repoParts[1]; number = $Number }; if ($cursor) { $vars.cursor = $cursor }
        $response = Invoke-QueueGraphQL -Query $query -Variables $vars -Operation "$Kind pagination for #$Number"
        $parent = $response.data.repository.$parentName
        if ($null -eq $parent) { throw "$Kind target #$Number was not found." }
        $connection = $parent.$connectionName; $total = [int]$connection.totalCount
        if ($expected -lt 0) { $expected = $total } elseif ($expected -ne $total) { throw "$Kind totalCount changed during pagination." }
        foreach ($node in @($connection.nodes)) {
            $nodeUrl = [string](Get-SashimiPropertyValue $node 'url' '')
            if (-not $nodeUrl -or $seenUrls.ContainsKey($nodeUrl)) { throw "$Kind returned a missing or duplicate URL." }
            $seenUrls[$nodeUrl]=$true; $all.Add($node)
        }
        if (-not [bool]$connection.pageInfo.hasNextPage) { break }
        $next = [string]$connection.pageInfo.endCursor; if (-not $next -or $next -eq $cursor -or $seenCursors.ContainsKey($next)) { throw "$Kind returned an invalid cursor." }; $seenCursors[$next]=$true; $cursor = $next
    }
    if ($all.Count -ne $expected) { throw "$Kind pagination is incomplete." }
    return $all.ToArray()
}

function Get-LiveQueueData {
    $fields = @(Get-LiveProjectFields); Assert-ProjectSchema -Fields $fields
    $data = Get-LiveProjectItems
    foreach ($item in @($data.Items | Where-Object { @('Ready','In Progress','Review') -ccontains [string]$_.Status -or @($_.Labels) -ccontains 'blocked' })) {
        $comments = New-Object 'System.Collections.Generic.List[object]'
        foreach ($comment in @(Get-LiveConversationConnection -Number $item.IssueNumber -Kind IssueComments)) {
            $comments.Add((ConvertTo-SashimiCanonicalConversationRecord -Record $comment -Kind IssueComment))
        }
        $open = @($item.PullRequests | Where-Object { [string]$_.State -ceq 'OPEN' })
        if ($open.Count -eq 1) {
            foreach ($comment in @(Get-LiveConversationConnection -Number $open[0].Number -Kind PullRequestComments)) {
                $comments.Add((ConvertTo-SashimiCanonicalConversationRecord -Record $comment -Kind PullRequestComment))
            }
            $item.Reviews = @((Get-LiveConversationConnection -Number $open[0].Number -Kind PullRequestReviews) | ForEach-Object {
                ConvertTo-SashimiCanonicalConversationRecord -Record $_ -Kind PullRequestReview
            })
        }
        $item.Comments = $comments.ToArray()
        $decision = Get-LatestOwnerDecision -Item $item
        $item | Add-Member -NotePropertyName LatestOwnerDecision -NotePropertyValue $decision -Force
        $item | Add-Member -NotePropertyName OwnerUnblocked -NotePropertyValue ([string]$decision.State -ceq 'Unblock') -Force
    }
    return [pscustomobject]@{ Fields = $fields; Items = $data.Items; PageCount = $data.PageCount }
}

function Get-LatestOwnerDecision {
    param([Parameter(Mandatory = $true)][object]$Item)

    $decisions = New-Object 'System.Collections.Generic.List[object]'
    foreach ($comment in @((Get-SashimiPropertyValue $Item 'Comments' @()))) {
        $author = [string](Get-SashimiPropertyValue $comment 'AuthorLogin' (Get-SashimiPropertyValue (Get-SashimiPropertyValue $comment 'author' $null) 'login' ''))
        if ($author -cne [string]$script:queueConfig.ProjectOwner) { continue }
        $body = [string](Get-SashimiPropertyValue $comment 'Body' (Get-SashimiPropertyValue $comment 'body' ''))
        if ($body -notmatch 'sashimi-boy-automation-owner-decision:v1') { continue }
        $createdText = [string](Get-SashimiPropertyValue $comment 'CreatedAt' (Get-SashimiPropertyValue $comment 'createdAt' ''))
        $created = [datetime]::MinValue
        if (-not [datetime]::TryParse($createdText, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$created)) {
            return [pscustomobject]@{ State='Ambiguous'; Reason='InvalidOwnerDecisionTimestamp'; CreatedAt='' }
        }
        if ([bool](Get-SashimiPropertyValue $comment 'WasEdited' (Get-SashimiPropertyValue $comment 'includesCreatedEdit' $false))) {
            $decisions.Add([pscustomobject]@{ State='Invalid'; Reason='EditedOwnerDecision'; CreatedAt=$created }); continue
        }
        $match = [regex]::Match($body, '(?s)^\s*<!-- sashimi-boy-automation-owner-decision:v1\r?\nissue: (?<issue>\d+)\r?\nqueue: (?<queue>block|unblock)\r?\nreason: (?<reason>product-decision|source-asset-missing|external-blocker|resolved)\r?\n-->\s*$')
        if (-not $match.Success -or [int]$match.Groups['issue'].Value -ne [int]$Item.IssueNumber) {
            $decisions.Add([pscustomobject]@{ State='Invalid'; Reason='MalformedOwnerDecision'; CreatedAt=$created }); continue
        }
        $queue = $match.Groups['queue'].Value; $reason = $match.Groups['reason'].Value
        if (($queue -ceq 'unblock' -and $reason -cne 'resolved') -or ($queue -ceq 'block' -and $reason -ceq 'resolved')) {
            $decisions.Add([pscustomobject]@{ State='Invalid'; Reason='InvalidOwnerDecisionReason'; CreatedAt=$created }); continue
        }
        $decisions.Add([pscustomobject]@{ State=$(if ($queue -ceq 'block') {'Block'} else {'Unblock'}); Reason=$reason; CreatedAt=$created })
    }
    if ($decisions.Count -eq 0) { return [pscustomobject]@{ State='None'; Reason=''; CreatedAt='' } }
    $ordered = @($decisions | Sort-Object CreatedAt -Descending)
    if ($ordered.Count -gt 1 -and $ordered[0].CreatedAt -eq $ordered[1].CreatedAt) { return [pscustomobject]@{ State='Ambiguous'; Reason='EqualTimeOwnerDecisions'; CreatedAt=$ordered[0].CreatedAt.ToString('o') } }
    if ($ordered[0].State -ceq 'Invalid') { return [pscustomobject]@{ State='Ambiguous'; Reason=$ordered[0].Reason; CreatedAt=$ordered[0].CreatedAt.ToString('o') } }
    return [pscustomobject]@{ State=$ordered[0].State; Reason=$ordered[0].Reason; CreatedAt=$ordered[0].CreatedAt.ToString('o') }
}

function Test-LatestOwnerUnblock {
    param([Parameter(Mandatory = $true)][object]$Item)
    return ([string](Get-LatestOwnerDecision -Item $Item).State -ceq 'Unblock')
}

function ConvertFrom-HandoffCompletion {
    param([Parameter(Mandatory = $true)][string]$Body)
    $match = [regex]::Match($Body, '(?s)^\s*<!-- sashimi-boy-automation-handoff-completion:v1\r?\nissue: (?<issue>\d+)\r?\npr: (?<pr>\d+)\r?\nhead: (?<head>[0-9a-fA-F]{40})\r?\nsourceRole: Developer\r?\nhandoffUrl: (?<url>https://[^\r\n]+)\r?\n-->\s*$')
    if (-not $match.Success) { return $null }
    return [pscustomobject]@{ IssueNumber=[int]$match.Groups['issue'].Value; PullRequestNumber=[int]$match.Groups['pr'].Value; HeadSha=$match.Groups['head'].Value.ToLowerInvariant(); HandoffUrl=$match.Groups['url'].Value }
}

function Get-CurrentHandoff {
    param([Parameter(Mandatory = $true)][object]$Item, [Parameter(Mandatory = $true)][object]$PullRequest, [Parameter(Mandatory = $true)][string[]]$AuthorizedAuthors)

    $explicit = Get-SashimiPropertyValue $Item 'CurrentHandoff' $null
    if ($null -ne $explicit) {
        if ([bool](Get-SashimiPropertyValue $Item 'HandoffResolved' $false)) { return $null }
        if (-not (Test-SashimiHandoffContract -Handoff $explicit -IssueNumber ([int]$Item.IssueNumber) -PullRequest $PullRequest)) { return $null }
        return $explicit
    }
    $markers = New-Object 'System.Collections.Generic.List[object]'
    foreach ($comment in @((Get-SashimiPropertyValue $Item 'Comments' @()))) {
        if ([bool](Get-SashimiPropertyValue $comment 'WasEdited' (Get-SashimiPropertyValue $comment 'includesCreatedEdit' $false))) { continue }
        $author = [string](Get-SashimiPropertyValue $comment 'AuthorLogin' (Get-SashimiPropertyValue (Get-SashimiPropertyValue $comment 'author' $null) 'login' ''))
        if ($AuthorizedAuthors -cnotcontains $author) { continue }
        $parsed = ConvertFrom-SashimiHandoffMarker -Body ([string](Get-SashimiPropertyValue $comment 'Body' (Get-SashimiPropertyValue $comment 'body' '')))
        if ($null -eq $parsed) { continue }
        if ([string]$parsed.SourceRole -ceq 'Owner' -and $author -cne [string]$script:queueConfig.ProjectOwner) { continue }
        if (-not (Test-SashimiHandoffContract -Handoff $parsed -IssueNumber ([int]$Item.IssueNumber) -PullRequest $PullRequest)) { continue }
        $markers.Add([pscustomobject]@{ Handoff = $parsed; CreatedAt = [datetime](Get-SashimiPropertyValue $comment 'CreatedAt' (Get-SashimiPropertyValue $comment 'createdAt' '')); Url = [string](Get-SashimiPropertyValue $comment 'Url' (Get-SashimiPropertyValue $comment 'url' '')) })
    }
    if ($markers.Count -eq 0) { return $null }
    $ordered = @($markers | Sort-Object CreatedAt -Descending)
    if ($ordered.Count -gt 1 -and $ordered[0].CreatedAt -eq $ordered[1].CreatedAt) { return $null }
    $handoff = $ordered[0]
    foreach ($comment in @((Get-SashimiPropertyValue $Item 'Comments' @()))) {
        if ([bool](Get-SashimiPropertyValue $comment 'WasEdited' (Get-SashimiPropertyValue $comment 'includesCreatedEdit' $false))) { continue }
        $author = [string](Get-SashimiPropertyValue $comment 'AuthorLogin' (Get-SashimiPropertyValue (Get-SashimiPropertyValue $comment 'author' $null) 'login' ''))
        if ($AuthorizedAuthors -cnotcontains $author) { continue }
        $created = [datetime](Get-SashimiPropertyValue $comment 'CreatedAt' (Get-SashimiPropertyValue $comment 'createdAt' ''))
        if ($created -le $handoff.CreatedAt) { continue }
        $completion = ConvertFrom-HandoffCompletion -Body ([string](Get-SashimiPropertyValue $comment 'Body' (Get-SashimiPropertyValue $comment 'body' '')))
        if ($null -ne $completion -and $completion.IssueNumber -eq [int]$Item.IssueNumber -and $completion.PullRequestNumber -eq [int]$PullRequest.Number -and
            $completion.HeadSha -ceq ([string]$PullRequest.HeadSha).ToLowerInvariant() -and $completion.HandoffUrl -ceq $handoff.Url) { return $null }
    }
    $decisiveReviews = New-Object 'System.Collections.Generic.List[object]'
    foreach ($review in @((Get-SashimiPropertyValue $Item 'Reviews' @()))) {
        $author = [string](Get-SashimiPropertyValue (Get-SashimiPropertyValue $review 'author' $null) 'login' (Get-SashimiPropertyValue $review 'AuthorLogin' ''))
        $timeText = [string](Get-SashimiPropertyValue $review 'submittedAt' (Get-SashimiPropertyValue $review 'SubmittedAt' (Get-SashimiPropertyValue $review 'CreatedAt' '')))
        $commit = [string](Get-SashimiPropertyValue (Get-SashimiPropertyValue $review 'commit' $null) 'oid' (Get-SashimiPropertyValue $review 'CommitOid' (Get-SashimiPropertyValue $review 'HeadSha' '')))
        $reviewState = ([string](Get-SashimiPropertyValue $review 'state' (Get-SashimiPropertyValue $review 'State' (Get-SashimiPropertyValue $review 'ReviewState' '')))).ToUpperInvariant()
        if ($AuthorizedAuthors -ccontains $author -and @('APPROVED','CHANGES_REQUESTED','DISMISSED') -ccontains $reviewState -and
            $commit -ceq [string]$PullRequest.HeadSha -and [datetime]$timeText -gt $handoff.CreatedAt) {
            $decisiveReviews.Add([pscustomobject]@{ State=$reviewState; SubmittedAt=[datetime]$timeText })
        }
    }
    if ($decisiveReviews.Count -gt 0) {
        $reviews = @($decisiveReviews | Sort-Object SubmittedAt -Descending)
        if ($reviews.Count -gt 1 -and $reviews[0].SubmittedAt -eq $reviews[1].SubmittedAt) { return $null }
        if ($reviews[0].State -ceq 'APPROVED') { return $null }
    }
    $handoff.Handoff | Add-Member -NotePropertyName LatestHandoffUrl -NotePropertyValue $handoff.Url -Force
    return $handoff.Handoff
}

function New-QueueCandidate {
    param([object]$Item, [string[]]$AuthorizedAuthors)

    $number = [int](Get-SashimiPropertyValue $Item 'IssueNumber' 0)
    $base = [ordered]@{ Item = $Item; Eligible = $false; Reason = ''; ClassRank = 999; Role = 'None'; Mode = 'None'; PullRequest = $null; Handoff = $null }
    if ($number -lt 1 -or [string](Get-SashimiPropertyValue $Item 'IssueState' '') -cne 'OPEN' -or [string](Get-SashimiPropertyValue $Item 'IssueRepository' '') -cne [string]$script:queueConfig.Repository) {
        $base.Reason = 'IssueIdentityOrStateMismatch'; return [pscustomobject]$base
    }
    $priority = [string](Get-SashimiPropertyValue $Item 'Priority' '')
    if (@('P0', 'P1', 'P2', 'P3') -cnotcontains $priority) { $base.Reason = 'InvalidPriority'; return [pscustomobject]$base }
    try { [void][datetime]::Parse([string]$Item.UpdatedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal) } catch { $base.Reason = 'InvalidUpdatedAt'; return [pscustomobject]$base }
    $issueUpdatedAt = [string](Get-SashimiPropertyValue $Item 'IssueUpdatedAt' (Get-SashimiPropertyValue $Item 'UpdatedAt' ''))
    if ($issueUpdatedAt -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$') { $base.Reason = 'InvalidIssueUpdatedAt'; return [pscustomobject]$base }
    $status = [string]$Item.Status
    if (@('Verification', 'Done', 'Backlog') -ccontains $status) { $base.Reason = "StatusNotSelectable:$status"; return [pscustomobject]$base }
    $ownerDecision = Get-SashimiPropertyValue $Item 'LatestOwnerDecision' $null
    if ($null -eq $ownerDecision -and $null -ne $Item.PSObject.Properties['Comments']) { $ownerDecision = Get-LatestOwnerDecision -Item $Item }
    $ownerDecisionState = [string](Get-SashimiPropertyValue $ownerDecision 'State' 'None')
    if (@('Block','Ambiguous') -ccontains $ownerDecisionState) { $base.Reason = "OwnerDecision:$ownerDecisionState"; return [pscustomobject]$base }
    if (@((Get-SashimiPropertyValue $Item 'Labels' @())) -ccontains 'blocked' -and $ownerDecisionState -cne 'Unblock') {
        $base.Reason = 'Blocked'; return [pscustomobject]$base
    }
    $openPrs = @((Get-SashimiPropertyValue $Item 'PullRequests' @()) | Where-Object { [string](Get-SashimiPropertyValue $_ 'State' '') -ceq 'OPEN' })
    if ($status -ceq 'Ready') {
        if ($openPrs.Count -ne 0) { $base.Reason = 'ReadyHasLinkedOpenPullRequest'; return [pscustomobject]$base }
        $base.Eligible = $true; $base.ClassRank = 4; $base.Role = 'Developer'; $base.Mode = 'NewWork'; $base.Reason = 'ReadyNewWork'; return [pscustomobject]$base
    }
    if (@('Review', 'In Progress') -cnotcontains $status) { $base.Reason = 'UnknownOrIneligibleStatus'; return [pscustomobject]$base }
    if ($openPrs.Count -ne 1) { $base.Reason = 'RequiresExactlyOneLinkedOpenPullRequest'; return [pscustomobject]$base }
    $pr = $openPrs[0]
    $trust = Test-SashimiPullRequestTrust -PullRequest $pr -Repository ([string]$script:queueConfig.Repository) -AuthorizedAuthors $AuthorizedAuthors
    if (-not $trust.Trusted) { $base.Reason = [string]::Join(',', $trust.Reasons); return [pscustomobject]$base }
    $base.PullRequest = $pr
    if ($status -ceq 'Review') {
        $base.Eligible = $true; $base.ClassRank = 1; $base.Role = 'Reviewer'; $base.Mode = 'Review'; $base.Reason = 'ReviewDraftPullRequest'; return [pscustomobject]$base
    }
    $handoff = Get-CurrentHandoff -Item $Item -PullRequest $pr -AuthorizedAuthors $AuthorizedAuthors
    if ($null -eq $handoff) { $base.Reason = 'NoCurrentValidHandoff'; return [pscustomobject]$base }
    $base.Handoff = $handoff
    if ([string]$handoff.Mode -ceq 'ReviewFix') { $base.ClassRank = 2; $base.Mode = 'ReviewFix' }
    elseif ([string]$handoff.Mode -ceq 'DeliveryResume') { $base.ClassRank = 3; $base.Mode = 'DeliveryResume' }
    else { $base.Reason = 'UnsupportedHandoffMode'; return [pscustomobject]$base }
    $base.Eligible = $true; $base.Role = 'Developer'; $base.Reason = 'CurrentHandoff'; return [pscustomobject]$base
}

try {
    $script:queueConfig = Import-SashimiHostConfig -ConfigPath $ConfigPath
    if ($FixturePath -and $GraphQLFixturePath) { throw 'Use either FixturePath or GraphQLFixturePath, never both.' }
    if ($GraphQLFixturePath) {
        $graphFixtureFile = Assert-SashimiFixtureAllowed -FixturePath $GraphQLFixturePath -DryRun:$DryRun
        $script:graphQLFixture = Read-SashimiJsonFile $graphFixtureFile
        if ([int](Get-SashimiPropertyValue $script:graphQLFixture 'SchemaVersion' 0) -ne 1 -or [string](Get-SashimiPropertyValue $script:graphQLFixture 'Encoding' '') -cne 'UTF-8') { throw 'GraphQL fixture must use SchemaVersion 1 and Encoding UTF-8.' }
        $queueData = Get-LiveQueueData; $dataSource = 'GraphQLFixture'
        if ($script:graphQLFixtureIndex -ne @((Get-SashimiPropertyValue $script:graphQLFixture 'Responses' @())).Count) { throw 'GraphQL fixture contains unused responses; request pagination did not match the fixture.' }
    }
    elseif ($FixturePath) { $queueData = Get-FixtureQueueData -Path $FixturePath; $dataSource = 'Fixture' }
    else {
        if ($DryRun) { throw 'Live queue access is disabled in -DryRun; provide -FixturePath for a no-mutation plan.' }
        $queueData = Get-LiveQueueData; $dataSource = 'Live'
    }
    Assert-QueuePayloadContainsNoSensitiveContent -QueueData $queueData
    $authorized = @($script:queueConfig.Security.AuthorizedPrAuthors | ForEach-Object { [string]$_ })
    if ($authorized.Count -lt 1 -or @($authorized | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) { throw 'Security.AuthorizedPrAuthors must be non-empty.' }
    $candidates = @($queueData.Items | ForEach-Object { New-QueueCandidate -Item $_ -AuthorizedAuthors $authorized })
    $eligible = @($candidates | Where-Object Eligible | Sort-Object `
        @{ Expression = { [int]$_.ClassRank }; Ascending = $true },
        @{ Expression = { [array]::IndexOf(@('P0', 'P1', 'P2', 'P3'), [string]$_.Item.Priority) }; Ascending = $true },
        @{ Expression = { [datetime]$_.Item.UpdatedAt }; Ascending = $true },
        @{ Expression = { [int]$_.Item.IssueNumber }; Ascending = $true })
    $excluded = @($candidates | Where-Object { -not $_.Eligible } | ForEach-Object { [ordered]@{ IssueNumber = [int](Get-SashimiPropertyValue $_.Item 'IssueNumber' 0); Reason = $_.Reason } })
    if ($eligible.Count -eq 0) {
        $output = [ordered]@{
            SchemaVersion = 1; Tool = 'Get-SashimiProjectQueue'; Success = $true; Selected = $false; Role = 'None'; Mode = 'None'
            Reason = 'QueueEmpty'; DataSource = $dataSource; Encoding = 'UTF-8'; ProjectPageCount = [int]$queueData.PageCount
            CandidateCount = 0; DispatchCount = 0; ExcludedCandidates = $excluded; MutationAttempted = $false
        }
    }
    else {
        $selected = $eligible[0]; $item = $selected.Item; $pr = $selected.PullRequest; $handoff = $selected.Handoff
        $canonicalConversation = New-Object 'System.Collections.Generic.List[object]'
        foreach ($comment in @((Get-SashimiPropertyValue $item 'Comments' @()))) {
            $kind = [string](Get-SashimiPropertyValue $comment 'Kind' '')
            if ([string]::IsNullOrWhiteSpace($kind) -or $kind -ceq 'Comment') {
                $commentUrl = [string](Get-SashimiPropertyValue $comment 'Url' (Get-SashimiPropertyValue $comment 'url' ''))
                $kind = if ($commentUrl -match '/pull/') { 'PullRequestComment' } else { 'IssueComment' }
            }
            $canonicalConversation.Add((ConvertTo-SashimiCanonicalConversationRecord -Record $comment -Kind $kind))
        }
        foreach ($review in @((Get-SashimiPropertyValue $item 'Reviews' @()))) {
            $canonicalConversation.Add((ConvertTo-SashimiCanonicalConversationRecord -Record $review -Kind PullRequestReview))
        }
        $conversationSha256 = Get-SashimiConversationSha256 -Records $canonicalConversation.ToArray()
        $conversation = @($canonicalConversation.ToArray() | ForEach-Object {
            [ordered]@{
                Kind=[string]$_.Kind; Url=[string]$_.Url; CreatedAt=[string]$_.CreatedAt; UpdatedAt=[string]$_.UpdatedAt
                SubmittedAt=[string]$_.SubmittedAt; AuthorLogin=[string]$_.AuthorLogin; AuthorAssociation=[string]$_.AuthorAssociation
                WasEdited=[bool]$_.WasEdited; Body=Protect-SashimiText ([string]$_.Body); ReviewState=[string]$_.ReviewState; CommitOid=[string]$_.CommitOid
            }
        })
        $findingUrl = if ($null -eq $handoff) { '' } else { [string](Get-SashimiPropertyValue $handoff 'FindingUrl' '') }
        $findingBody = ''
        if ($findingUrl) {
            $findingComment = @($conversation | Where-Object { [string]$_.Url -ceq $findingUrl }) | Select-Object -First 1
            if ($null -ne $findingComment) { $findingBody = [string]$findingComment.Body }
        }
        $output = [ordered]@{
            SchemaVersion = 1; Tool = 'Get-SashimiProjectQueue'; Success = $true; Selected = $true; Role = $selected.Role; Mode = $selected.Mode
            DataSource = $dataSource; Encoding = 'UTF-8'; ProjectPageCount = [int]$queueData.PageCount; CandidateCount = $eligible.Count; DispatchCount = 1
            ProjectItemId = [string]$item.ProjectItemId; Status = [string]$item.Status; Priority = [string]$item.Priority; UpdatedAt = [string]$item.UpdatedAt
            IssueNumber = [int]$item.IssueNumber; IssueTitle = Protect-SashimiText ([string]$item.IssueTitle); IssueBody = Protect-SashimiText ([string]$item.IssueBody); IssueUrl = [string]$item.IssueUrl
            IssueUpdatedAt = [string](Get-SashimiPropertyValue $item 'IssueUpdatedAt' (Get-SashimiPropertyValue $item 'UpdatedAt' ''))
            IssueBodySha256 = Get-SashimiTextSha256 -Text ([string]$item.IssueBody)
            ConversationSha256 = $conversationSha256
            PullRequestNumber = if ($null -eq $pr) { $null } else { [int]$pr.Number }
            PullRequestUrl = if ($null -eq $pr) { '' } else { [string]$pr.Url }
            PullRequestTitle = if ($null -eq $pr) { '' } else { Protect-SashimiText ([string](Get-SashimiPropertyValue $pr 'Title' '')) }
            PullRequestBody = if ($null -eq $pr) { '' } else { Protect-SashimiText ([string](Get-SashimiPropertyValue $pr 'Body' '')) }
            PullRequestContentSha256 = if ($null -eq $pr) { '' } else { Get-SashimiPullRequestContentSha256 -Title ([string](Get-SashimiPropertyValue $pr 'Title' '')) -Body ([string](Get-SashimiPropertyValue $pr 'Body' '')) }
            PullRequestHeadSha = if ($null -eq $pr) { '' } else { ([string]$pr.HeadSha).ToLowerInvariant() }
            PullRequestHeadRef = if ($null -eq $pr) { '' } else { [string]$pr.HeadRef }
            PullRequestHeadRepository = if ($null -eq $pr) { '' } else { [string]$pr.HeadRepository }
            PendingCommand = if ($null -eq $handoff) { '' } else { Protect-SashimiText ([string]$handoff.PendingCommand) }
            HandoffReason = if ($null -eq $handoff) { '' } else { [string]$handoff.Reason }
            LatestHandoffUrl = if ($null -eq $handoff) { '' } else { [string](Get-SashimiPropertyValue $handoff 'LatestHandoffUrl' (Get-SashimiPropertyValue $handoff 'Url' (Get-SashimiPropertyValue $item 'LatestHandoffUrl' ''))) }
            FindingUrl = $findingUrl; FindingBody = $findingBody; Conversation = @($conversation | Sort-Object CreatedAt,Kind,Url)
            ExcludedCandidates = $excluded; MutationAttempted = $false
        }
    }
    $json = ConvertTo-SashimiJson $output
    if ($OutputPath) { Write-SashimiUtf8File -Path $OutputPath -Content $json }
    [Console]::Out.WriteLine($json)
    exit 0
}
catch {
    Stop-QueueFailure -Message $_.Exception.Message
}
