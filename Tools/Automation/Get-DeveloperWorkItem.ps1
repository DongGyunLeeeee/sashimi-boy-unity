#requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Repository = 'DongGyunLeeeee/sashimi-boy-unity',

    [ValidateNotNullOrEmpty()]
    [string]$ProjectOwner = 'DongGyunLeeeee',

    [ValidateRange(1, 2147483647)]
    [int]$ProjectNumber = 1,

    [ValidateNotNullOrEmpty()]
    [string]$GitHubCliPath = 'gh',

    [Parameter(DontShow = $true)]
    [string]$FixturePath,

    [Parameter(DontShow = $true)]
    [string]$FixtureTestRoot,

    [Parameter(DontShow = $true)]
    [string]$FixtureTestRunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
$handoffPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Handoff.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Automation common helpers are missing: $commonPath")
    exit 1
}
if (-not (Test-Path -LiteralPath $handoffPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Automation handoff helpers are missing: $handoffPath")
    exit 1
}
. $commonPath
. $handoffPath

function Get-AutomationPropertyValue {
    [CmdletBinding()]
    param(
        [Parameter()]
        [AllowNull()]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter()]
        [AllowNull()]
        [object]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }
    return $property.Value
}

function ConvertFrom-RequiredAutomationJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Json,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    try {
        return ($Json | ConvertFrom-Json -ErrorAction Stop)
    }
    catch {
        throw "$Operation returned invalid JSON: $($_.Exception.Message)"
    }
}

function Invoke-RequiredAutomationGitHubJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $invocation = Invoke-AutomationNativeCommand -FilePath $GitHubCliPath -ArgumentList $ArgumentList
    if (-not $invocation.Succeeded) {
        $detail = $invocation.StdErr
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $invocation.StdOut
        }
        $command = Format-AutomationCommand -FilePath $GitHubCliPath -ArgumentList $ArgumentList
        throw "$Operation failed. Command: $command; exit code: $($invocation.ExitCode); stderr: $detail"
    }
    return ConvertFrom-RequiredAutomationJson -Json $invocation.StdOut -Operation $Operation
}

function Test-AutomationExactStringSet {
    [CmdletBinding()]
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
    foreach ($expectedValue in $Expected) {
        if ($Actual -cnotcontains $expectedValue) {
            return $false
        }
    }
    return $true
}

function Assert-DeveloperProjectSchema {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Fields
    )

    foreach ($requiredName in @('Status', 'Priority', 'Area', 'Size')) {
        $matches = @($Fields | Where-Object { [string](Get-AutomationPropertyValue -Object $_ -Name 'name' -DefaultValue (Get-AutomationPropertyValue -Object $_ -Name 'Name')) -ceq $requiredName })
        if ($matches.Count -ne 1) {
            throw "GitHub Project must contain exactly one field named $requiredName."
        }
    }

    $expectedOptions = [ordered]@{
        Status   = @('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done')
        Priority = @('P0', 'P1', 'P2', 'P3')
    }
    foreach ($entry in $expectedOptions.GetEnumerator()) {
        $field = @($Fields | Where-Object { [string](Get-AutomationPropertyValue -Object $_ -Name 'name' -DefaultValue (Get-AutomationPropertyValue -Object $_ -Name 'Name')) -ceq [string]$entry.Key })[0]
        $options = @(Get-AutomationPropertyValue -Object $field -Name 'options' -DefaultValue (Get-AutomationPropertyValue -Object $field -Name 'Options' -DefaultValue @()))
        $actualNames = @($options | ForEach-Object { [string](Get-AutomationPropertyValue -Object $_ -Name 'name' -DefaultValue (Get-AutomationPropertyValue -Object $_ -Name 'Name')) })
        if (-not (Test-AutomationExactStringSet -Actual $actualNames -Expected ([string[]]$entry.Value))) {
            throw "GitHub Project $($entry.Key) options must match the repository contract exactly."
        }
    }
}

function Get-AutomationTimestamp {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            $Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$parsed)) {
        throw "$Name is not a valid timestamp: $Value"
    }
    return $parsed.ToUniversalTime()
}

function New-AutomationContentSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Body,

        [Parameter(Mandatory = $true)]
        [string]$Timestamp,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$AuthorLogin,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$AuthorAssociation,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [AllowEmptyString()]
        [string]$ReviewState = '',

        [AllowEmptyString()]
        [string]$ReviewCommitSha = '',

        [AllowEmptyString()]
        [string]$EventId = '',

        [bool]$WasEdited = $false,

        [int]$PullRequestNumber = 0
    )

    return [pscustomobject][ordered]@{
        Body              = $Body
        Timestamp         = $Timestamp
        Url               = $Url
        AuthorLogin       = $AuthorLogin
        AuthorAssociation = $AuthorAssociation
        Kind              = $Kind
        ReviewState       = $ReviewState
        ReviewCommitSha   = $ReviewCommitSha
        EventId           = $EventId
        WasEdited         = $WasEdited
        PullRequestNumber = $PullRequestNumber
    }
}

function Get-AutomationRelevantSources {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [int]$PullRequestNumber = 0
    )

    return @((Get-AutomationPropertyValue -Object $Candidate -Name 'Sources' -DefaultValue @()) | Where-Object {
            $kind = [string](Get-AutomationPropertyValue -Object $_ -Name 'Kind' -DefaultValue '')
            $sourcePullRequestNumber = [int](Get-AutomationPropertyValue -Object $_ -Name 'PullRequestNumber' -DefaultValue 0)
            if ($kind -ceq 'IssueComment') {
                return $sourcePullRequestNumber -eq 0
            }
            if (@('PullRequestComment', 'PullRequestReview') -ccontains $kind) {
                return $PullRequestNumber -gt 0 -and $sourcePullRequestNumber -eq $PullRequestNumber
            }
            return $false
        })
}

function Get-AutomationAuthorLogin {
    [CmdletBinding()]
    param([AllowNull()][object]$Object)

    $author = Get-AutomationPropertyValue -Object $Object -Name 'author'
    return [string](Get-AutomationPropertyValue -Object $author -Name 'login' -DefaultValue '')
}

function Get-DeveloperLeaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$PullRequestNumber,

        [Parameter(Mandatory = $true)]
        [string]$HeadSha
    )

    $result = [ordered]@{ Active = $false; Invalid = $false }
    $leaseDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation\DeveloperLeases'
    if (-not (Test-Path -LiteralPath $leaseDirectory -PathType Container)) {
        return [pscustomobject]$result
    }

    Assert-AutomationPathHasNoReparsePoint -Path $leaseDirectory
    $leasePath = Join-Path -Path $leaseDirectory -ChildPath ("pr-{0}.json" -f $PullRequestNumber)
    Assert-AutomationPathHasNoReparsePoint -Path $leasePath
    if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf)) {
        return [pscustomobject]$result
    }

    try {
        $lease = Get-Content -Raw -LiteralPath $leasePath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        $expiresAt = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $lease -Name 'ExpiresAt')) -Name 'lease ExpiresAt'
        [void](Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $lease -Name 'AcquiredAt')) -Name 'lease AcquiredAt')
        $leaseId = [Guid]::Empty
        if ([int](Get-AutomationPropertyValue -Object $lease -Name 'SchemaVersion' -DefaultValue 0) -ne 1 -or
            [int](Get-AutomationPropertyValue -Object $lease -Name 'PullRequestNumber' -DefaultValue 0) -ne $PullRequestNumber -or
            [string](Get-AutomationPropertyValue -Object $lease -Name 'HeadSha' -DefaultValue '') -notmatch '^[0-9a-fA-F]{40}$' -or
            -not [Guid]::TryParse([string](Get-AutomationPropertyValue -Object $lease -Name 'LeaseId' -DefaultValue ''), [ref]$leaseId) -or
            $leaseId -eq [Guid]::Empty) {
            throw 'lease fields do not match the lease contract.'
        }
        $leaseHead = [string](Get-AutomationPropertyValue -Object $lease -Name 'HeadSha')
        if ($expiresAt -gt [DateTimeOffset]::UtcNow) {
            $result.Active = $true
        }
    }
    catch {
        $result.Invalid = $true
    }
    return [pscustomobject]$result
}

function New-LiveAutomationCommentSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Comment,
        [Parameter(Mandatory = $true)][ValidateSet('IssueComment', 'PullRequestComment')][string]$Kind,
        [int]$PullRequestNumber = 0
    )

    $editedProperty = $Comment.PSObject.Properties['includesCreatedEdit']
    $urlProperty = $Comment.PSObject.Properties['url']
    $createdAtProperty = $Comment.PSObject.Properties['createdAt']
    $associationProperty = $Comment.PSObject.Properties['authorAssociation']
    if ($null -eq $editedProperty -or $editedProperty.Value -isnot [bool] -or
        $null -eq $urlProperty -or [string]::IsNullOrWhiteSpace([string]$urlProperty.Value) -or
        $null -eq $createdAtProperty -or [string]::IsNullOrWhiteSpace([string]$createdAtProperty.Value) -or
        $null -eq $associationProperty) {
        throw "$Kind response is missing immutable marker-source metadata."
    }
    $commentUrl = [string]$urlProperty.Value
    $parsedCommentUrl = $null
    if (-not [Uri]::TryCreate($commentUrl, [UriKind]::Absolute, [ref]$parsedCommentUrl) -or
        $parsedCommentUrl.Scheme -cne 'https') {
        throw "$Kind response has an invalid comment URL."
    }
    [void](Get-AutomationTimestamp -Value ([string]$createdAtProperty.Value) -Name "$Kind createdAt")

    return New-AutomationContentSource `
        -Body ([string](Get-AutomationPropertyValue -Object $Comment -Name 'body' -DefaultValue '')) `
        -Timestamp ([string]$createdAtProperty.Value) `
        -Url $commentUrl `
        -AuthorLogin (Get-AutomationAuthorLogin -Object $Comment) `
        -AuthorAssociation ([string]$associationProperty.Value) `
        -Kind $Kind `
        -WasEdited ([bool]$editedProperty.Value) `
        -PullRequestNumber $PullRequestNumber
}

function Get-CompleteAutomationGraphQLConnection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][string]$RepositoryOwner,
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][ValidateCount(2, 2)][string[]]$ConnectionPath
    )

    $allNodes = New-Object 'System.Collections.Generic.List[object]'
    $seenNodeIds = @{}
    $seenCursors = @{}
    $cursor = ''
    $expectedTotalCount = -1
    while ($true) {
        $arguments = @(
            'api', 'graphql',
            '-f', "owner=$RepositoryOwner",
            '-f', "name=$RepositoryName",
            '-F', "number=$Number"
        )
        if (-not [string]::IsNullOrWhiteSpace($cursor)) {
            $arguments += @('-f', "cursor=$cursor")
        }
        $arguments += @('-f', "query=$Query")
        $response = Invoke-RequiredAutomationGitHubJson -Operation $Operation -ArgumentList $arguments

        $data = Get-AutomationPropertyValue -Object $response -Name 'data'
        $repositoryNode = Get-AutomationPropertyValue -Object $data -Name 'repository'
        $nameWithOwner = [string](Get-AutomationPropertyValue -Object $repositoryNode -Name 'nameWithOwner' -DefaultValue '')
        if (-not [string]::Equals($nameWithOwner, $Repository, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Operation returned a missing or mismatched repository node."
        }
        $parentNode = Get-AutomationPropertyValue -Object $repositoryNode -Name $ConnectionPath[0]
        $connection = Get-AutomationPropertyValue -Object $parentNode -Name $ConnectionPath[1]
        if ($null -eq $connection) {
            throw "$Operation returned no $($ConnectionPath -join '.') connection."
        }
        foreach ($requiredConnectionProperty in @('totalCount', 'nodes', 'pageInfo')) {
            if ($null -eq $connection.PSObject.Properties[$requiredConnectionProperty]) {
                throw "$Operation connection is missing $requiredConnectionProperty."
            }
        }
        $pageInfo = Get-AutomationPropertyValue -Object $connection -Name 'pageInfo'
        $hasNextPageProperty = if ($null -ne $pageInfo) { $pageInfo.PSObject.Properties['hasNextPage'] } else { $null }
        $endCursorProperty = if ($null -ne $pageInfo) { $pageInfo.PSObject.Properties['endCursor'] } else { $null }
        if ($null -eq $hasNextPageProperty -or $hasNextPageProperty.Value -isnot [bool] -or $null -eq $endCursorProperty) {
            throw "$Operation connection has invalid pageInfo."
        }

        $pageTotalCount = 0
        if (-not [int]::TryParse([string]$connection.totalCount, [ref]$pageTotalCount) -or $pageTotalCount -lt 0) {
            throw "$Operation connection has an invalid totalCount."
        }
        if ($expectedTotalCount -lt 0) {
            $expectedTotalCount = $pageTotalCount
        }
        elseif ($pageTotalCount -ne $expectedTotalCount) {
            throw "$Operation totalCount changed during pagination."
        }

        foreach ($node in @(Get-AutomationPropertyValue -Object $connection -Name 'nodes' -DefaultValue @())) {
            $nodeId = [string](Get-AutomationPropertyValue -Object $node -Name 'id' -DefaultValue '')
            if ([string]::IsNullOrWhiteSpace($nodeId)) {
                throw "$Operation returned a node without an id."
            }
            if ($seenNodeIds.ContainsKey($nodeId)) {
                throw "$Operation returned a duplicate node while paginating."
            }
            $seenNodeIds[$nodeId] = $true
            $allNodes.Add($node)
            if ($allNodes.Count -gt $expectedTotalCount) {
                throw "$Operation returned more nodes than totalCount."
            }
        }

        if (-not [bool]$hasNextPageProperty.Value) {
            if ($allNodes.Count -ne $expectedTotalCount) {
                throw "$Operation returned an incomplete connection ($($allNodes.Count)/$expectedTotalCount)."
            }
            break
        }
        $nextCursor = [string]$endCursorProperty.Value
        if ([string]::IsNullOrWhiteSpace($nextCursor) -or
            [string]::Equals($nextCursor, $cursor, [StringComparison]::Ordinal) -or
            $seenCursors.ContainsKey($nextCursor)) {
            throw "$Operation returned an empty or repeated pagination cursor."
        }
        $seenCursors[$nextCursor] = $true
        $cursor = $nextCursor
    }

    return $allNodes.ToArray()
}

function Get-CompleteProjectLinkedPullRequests {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][string]$ProjectItemId
    )

    $allNodes = New-Object 'System.Collections.Generic.List[object]'
    $seenNodeIds = @{}
    $seenCursors = @{}
    $cursor = ''
    $expectedTotalCount = -1
    while ($true) {
        $arguments = @(
            'api', 'graphql',
            '-F', "itemId=$ProjectItemId",
            '-f', 'fieldName=Linked pull requests'
        )
        if (-not [string]::IsNullOrWhiteSpace($cursor)) {
            $arguments += @('-f', "cursor=$cursor")
        }
        $arguments += @('-f', "query=$Query")
        $response = Invoke-RequiredAutomationGitHubJson -Operation $Operation -ArgumentList $arguments

        $data = Get-AutomationPropertyValue -Object $response -Name 'data'
        $itemNode = Get-AutomationPropertyValue -Object $data -Name 'node'
        $returnedItemId = [string](Get-AutomationPropertyValue -Object $itemNode -Name 'id' -DefaultValue '')
        if (-not [string]::Equals($returnedItemId, $ProjectItemId, [StringComparison]::Ordinal)) {
            throw "$Operation returned a missing or mismatched Project item node."
        }
        $linkedValueProperty = $itemNode.PSObject.Properties['linkedPullRequests']
        if ($null -eq $linkedValueProperty) {
            throw "$Operation response is missing linkedPullRequests."
        }
        $linkedValue = $linkedValueProperty.Value
        if ($null -eq $linkedValue) {
            if (-not [string]::IsNullOrWhiteSpace($cursor)) {
                throw "$Operation removed its linked pull request field during pagination."
            }
            return @()
        }
        if ([string](Get-AutomationPropertyValue -Object $linkedValue -Name '__typename' -DefaultValue '') -cne 'ProjectV2ItemFieldPullRequestValue') {
            throw "$Operation returned an unexpected linked pull request field type."
        }
        $connection = Get-AutomationPropertyValue -Object $linkedValue -Name 'pullRequests'
        if ($null -eq $connection) {
            throw "$Operation returned no linked pull request connection."
        }
        foreach ($requiredConnectionProperty in @('totalCount', 'nodes', 'pageInfo')) {
            if ($null -eq $connection.PSObject.Properties[$requiredConnectionProperty]) {
                throw "$Operation connection is missing $requiredConnectionProperty."
            }
        }
        $pageInfo = Get-AutomationPropertyValue -Object $connection -Name 'pageInfo'
        $hasNextPageProperty = if ($null -ne $pageInfo) { $pageInfo.PSObject.Properties['hasNextPage'] } else { $null }
        $endCursorProperty = if ($null -ne $pageInfo) { $pageInfo.PSObject.Properties['endCursor'] } else { $null }
        if ($null -eq $hasNextPageProperty -or $hasNextPageProperty.Value -isnot [bool] -or $null -eq $endCursorProperty) {
            throw "$Operation connection has invalid pageInfo."
        }

        $pageTotalCount = 0
        if (-not [int]::TryParse([string]$connection.totalCount, [ref]$pageTotalCount) -or $pageTotalCount -lt 0) {
            throw "$Operation connection has an invalid totalCount."
        }
        if ($expectedTotalCount -lt 0) {
            $expectedTotalCount = $pageTotalCount
        }
        elseif ($pageTotalCount -ne $expectedTotalCount) {
            throw "$Operation totalCount changed during pagination."
        }

        foreach ($node in @(Get-AutomationPropertyValue -Object $connection -Name 'nodes' -DefaultValue @())) {
            $nodeId = [string](Get-AutomationPropertyValue -Object $node -Name 'id' -DefaultValue '')
            if ([string]::IsNullOrWhiteSpace($nodeId)) {
                throw "$Operation returned a node without an id."
            }
            if ($seenNodeIds.ContainsKey($nodeId)) {
                throw "$Operation returned a duplicate node while paginating."
            }
            $seenNodeIds[$nodeId] = $true
            $allNodes.Add($node)
            if ($allNodes.Count -gt $expectedTotalCount) {
                throw "$Operation returned more nodes than totalCount."
            }
        }

        if (-not [bool]$hasNextPageProperty.Value) {
            if ($allNodes.Count -ne $expectedTotalCount) {
                throw "$Operation returned an incomplete connection ($($allNodes.Count)/$expectedTotalCount)."
            }
            break
        }
        $nextCursor = [string]$endCursorProperty.Value
        if ([string]::IsNullOrWhiteSpace($nextCursor) -or
            [string]::Equals($nextCursor, $cursor, [StringComparison]::Ordinal) -or
            $seenCursors.ContainsKey($nextCursor)) {
            throw "$Operation returned an empty or repeated pagination cursor."
        }
        $seenCursors[$nextCursor] = $true
        $cursor = $nextCursor
    }

    return $allNodes.ToArray()
}

function Get-LiveDeveloperQueueData {
    [CmdletBinding()]
    param()

    $repositoryMatch = [regex]::Match($Repository, '^(?<owner>[A-Za-z0-9_.-]+)/(?<name>[A-Za-z0-9_.-]+)$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $repositoryMatch.Success) {
        throw 'Repository must use the exact owner/name form.'
    }
    $repositoryOwner = $repositoryMatch.Groups['owner'].Value
    $repositoryName = $repositoryMatch.Groups['name'].Value
    $projectLinkedPullRequestsQuery = 'query AutomationProjectItemLinkedPullRequests($itemId:ID!,$fieldName:String!,$cursor:String){node(id:$itemId){... on ProjectV2Item{id linkedPullRequests:fieldValueByName(name:$fieldName){__typename ... on ProjectV2ItemFieldPullRequestValue{pullRequests(first:100,after:$cursor){totalCount nodes{id number url state repository{nameWithOwner}} pageInfo{hasNextPage endCursor}}}}}}}'
    $issueLabelsQuery = 'query AutomationIssueLabels($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){nameWithOwner issue(number:$number){labels(first:100,after:$cursor){totalCount nodes{id name} pageInfo{hasNextPage endCursor}}}}}'
    $issueCommentsQuery = 'query AutomationIssueComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){nameWithOwner issue(number:$number){comments(first:100,after:$cursor){totalCount nodes{id body createdAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
    $pullRequestCommentsQuery = 'query AutomationPullRequestComments($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){nameWithOwner pullRequest(number:$number){comments(first:100,after:$cursor){totalCount nodes{id body createdAt url author{login} authorAssociation includesCreatedEdit} pageInfo{hasNextPage endCursor}}}}}'
    $pullRequestReviewsQuery = 'query AutomationPullRequestReviews($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){nameWithOwner pullRequest(number:$number){reviews(first:100,after:$cursor){totalCount nodes{id body submittedAt author{login} authorAssociation state commit{oid}} pageInfo{hasNextPage endCursor}}}}}'

    $fieldResponse = Invoke-RequiredAutomationGitHubJson -Operation 'GitHub Project field lookup' -ArgumentList @(
        'project', 'field-list', [string]$ProjectNumber,
        '--owner', $ProjectOwner,
        '--format', 'json',
        '--limit', '10000'
    )
    $fields = @(Get-AutomationPropertyValue -Object $fieldResponse -Name 'fields' -DefaultValue @())
    $fieldTotalCount = [int](Get-AutomationPropertyValue -Object $fieldResponse -Name 'totalCount' -DefaultValue -1)
    if ($fieldTotalCount -lt 0 -or $fields.Count -ne $fieldTotalCount) {
        throw "GitHub Project field lookup returned a partial or uncounted page ($($fields.Count)/$fieldTotalCount)."
    }
    Assert-DeveloperProjectSchema -Fields $fields

    $itemResponse = Invoke-RequiredAutomationGitHubJson -Operation 'GitHub Project item lookup' -ArgumentList @(
        'project', 'item-list', [string]$ProjectNumber,
        '--owner', $ProjectOwner,
        '--format', 'json',
        '--limit', '10000'
    )
    $items = @(Get-AutomationPropertyValue -Object $itemResponse -Name 'items' -DefaultValue @())
    $itemTotalCount = [int](Get-AutomationPropertyValue -Object $itemResponse -Name 'totalCount' -DefaultValue -1)
    if ($itemTotalCount -lt 0 -or $items.Count -ne $itemTotalCount) {
        throw "GitHub Project item lookup returned a partial or uncounted page ($($items.Count)/$itemTotalCount)."
    }

    $candidates = New-Object 'System.Collections.Generic.List[object]'
    foreach ($item in $items) {
        $status = [string](Get-AutomationPropertyValue -Object $item -Name 'status' -DefaultValue '')
        if (@('Ready', 'In Progress') -cnotcontains $status) {
            continue
        }

        $content = Get-AutomationPropertyValue -Object $item -Name 'content'
        if ([string](Get-AutomationPropertyValue -Object $content -Name 'type' -DefaultValue '') -cne 'Issue' -or
            [string](Get-AutomationPropertyValue -Object $content -Name 'repository' -DefaultValue '') -cne $Repository) {
            continue
        }
        $issueNumber = [int](Get-AutomationPropertyValue -Object $content -Name 'number' -DefaultValue 0)
        if ($issueNumber -lt 1) {
            continue
        }

        $itemId = [string](Get-AutomationPropertyValue -Object $item -Name 'id' -DefaultValue '')
        if ([string]::IsNullOrWhiteSpace($itemId)) {
            throw "Project item for Issue #$issueNumber has no id."
        }
        $itemUpdatedQuery = 'query($itemId:ID!){node(id:$itemId){... on ProjectV2Item{updatedAt}}}'
        $itemUpdatedResponse = Invoke-RequiredAutomationGitHubJson -Operation "Project Updated lookup for Issue #$issueNumber" -ArgumentList @(
            'api', 'graphql',
            '-F', "itemId=$itemId",
            '-f', "query=$itemUpdatedQuery"
        )
        $data = Get-AutomationPropertyValue -Object $itemUpdatedResponse -Name 'data'
        $node = Get-AutomationPropertyValue -Object $data -Name 'node'
        $updatedAt = [string](Get-AutomationPropertyValue -Object $node -Name 'updatedAt' -DefaultValue '')
        [void](Get-AutomationTimestamp -Value $updatedAt -Name "Project Updated for Issue #$issueNumber")

        $issue = Invoke-RequiredAutomationGitHubJson -Operation "Issue #$issueNumber lookup" -ArgumentList @(
            'issue', 'view', [string]$issueNumber,
            '--repo', $Repository,
            '--json', 'number,title,state,url'
        )
        foreach ($requiredIssueProperty in @('number', 'state', 'url')) {
            if ($null -eq $issue.PSObject.Properties[$requiredIssueProperty]) {
                throw "Issue #$issueNumber response is missing $requiredIssueProperty."
            }
        }
        $expectedIssueUrl = "https://github.com/$Repository/issues/$issueNumber"
        if ([int]$issue.number -ne $issueNumber -or
            -not [string]::Equals([string]$issue.url, $expectedIssueUrl, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Issue #$issueNumber lookup returned a mismatched number or repository URL."
        }
        $labelNodes = @(Get-CompleteAutomationGraphQLConnection `
                -Operation "Issue #$issueNumber labels lookup" `
                -Query $issueLabelsQuery `
                -RepositoryOwner $repositoryOwner `
                -RepositoryName $repositoryName `
                -Number $issueNumber `
                -ConnectionPath @('issue', 'labels'))
        $issueCommentNodes = @(Get-CompleteAutomationGraphQLConnection `
                -Operation "Issue #$issueNumber comments lookup" `
                -Query $issueCommentsQuery `
                -RepositoryOwner $repositoryOwner `
                -RepositoryName $repositoryName `
                -Number $issueNumber `
                -ConnectionPath @('issue', 'comments'))
        $sources = New-Object 'System.Collections.Generic.List[object]'
        foreach ($comment in $issueCommentNodes) {
            $sources.Add((New-LiveAutomationCommentSource -Comment $comment -Kind IssueComment))
        }

        $linkedPullRequestNodes = @(Get-CompleteProjectLinkedPullRequests `
                -Operation "Project linked pull requests lookup for Issue #$issueNumber" `
                -Query $projectLinkedPullRequestsQuery `
                -ProjectItemId $itemId)
        $pullRequests = New-Object 'System.Collections.Generic.List[object]'
        foreach ($linkedPullRequestNode in $linkedPullRequestNodes) {
            foreach ($requiredLinkedPullRequestProperty in @('number', 'url', 'state', 'repository')) {
                if ($null -eq $linkedPullRequestNode.PSObject.Properties[$requiredLinkedPullRequestProperty]) {
                    throw "Project linked pull request response for Issue #$issueNumber is missing $requiredLinkedPullRequestProperty."
                }
            }
            $linkedPullRequestNumber = 0
            if (-not [int]::TryParse([string]$linkedPullRequestNode.number, [ref]$linkedPullRequestNumber) -or $linkedPullRequestNumber -lt 1) {
                throw "Project linked pull request response for Issue #$issueNumber has an invalid number."
            }
            $linkedPullRequestState = [string]$linkedPullRequestNode.state
            if (@('OPEN', 'CLOSED', 'MERGED') -cnotcontains $linkedPullRequestState) {
                throw "Project linked pull request #$linkedPullRequestNumber for Issue #$issueNumber has an unknown state: $linkedPullRequestState"
            }
            $linkedRepository = Get-AutomationPropertyValue -Object $linkedPullRequestNode -Name 'repository'
            $linkedRepositoryName = [string](Get-AutomationPropertyValue -Object $linkedRepository -Name 'nameWithOwner' -DefaultValue '')
            if ($linkedRepositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
                throw "Project linked pull request #$linkedPullRequestNumber for Issue #$issueNumber has an invalid repository identity."
            }
            $linkedPullRequestUrl = [string]$linkedPullRequestNode.url
            $expectedLinkedPullRequestUrl = "https://github.com/$linkedRepositoryName/pull/$linkedPullRequestNumber"
            if (-not [string]::Equals($linkedPullRequestUrl, $expectedLinkedPullRequestUrl, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Project linked pull request #$linkedPullRequestNumber for Issue #$issueNumber has a mismatched URL."
            }
            $baseRepositoryMatches = [string]::Equals($linkedRepositoryName, $Repository, [StringComparison]::OrdinalIgnoreCase)
            $pullRequests.Add([pscustomobject][ordered]@{
                    Number                = $linkedPullRequestNumber
                    Url                   = $linkedPullRequestUrl
                    State                 = $linkedPullRequestState
                    IsDraft               = $false
                    BaseRef               = ''
                    HeadSha               = ''
                    HeadRef               = ''
                    IsCrossRepository     = $false
                    BaseRepositoryMatches = $baseRepositoryMatches
                })
        }

        $linkedOpenPullRequests = @($pullRequests | Where-Object { $_.State -ceq 'OPEN' })
        $targetRepositoryOpenPullRequests = @($linkedOpenPullRequests | Where-Object { [bool]$_.BaseRepositoryMatches })
        if ($linkedOpenPullRequests.Count -eq 1 -and $targetRepositoryOpenPullRequests.Count -eq 1) {
            $linkedPullRequestSummary = $targetRepositoryOpenPullRequests[0]
            $linkedPullRequestNumber = [int]$linkedPullRequestSummary.Number
            $pullRequest = Invoke-RequiredAutomationGitHubJson -Operation "linked pull request lookup for Issue #$issueNumber" -ArgumentList @(
                'pr', 'view', [string]$linkedPullRequestNumber,
                '--repo', $Repository,
                '--json', 'number,url,state,isDraft,baseRefName,headRefName,headRefOid,headRepositoryOwner,isCrossRepository,updatedAt'
            )
            foreach ($requiredPullRequestProperty in @('number', 'url', 'state', 'isDraft', 'baseRefName', 'headRefName', 'headRefOid', 'isCrossRepository')) {
                if ($null -eq $pullRequest.PSObject.Properties[$requiredPullRequestProperty]) {
                    throw "Pull request response for Issue #$issueNumber is missing $requiredPullRequestProperty."
                }
            }
            if ($pullRequest.isDraft -isnot [bool] -or $pullRequest.isCrossRepository -isnot [bool]) {
                throw "Pull request response for Issue #$issueNumber has a non-Boolean repository or Draft flag."
            }
            $livePullRequestNumber = [int](Get-AutomationPropertyValue -Object $pullRequest -Name 'number')
            $livePullRequestUrl = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'url')
            $livePullRequestState = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'state')
            $expectedLivePullRequestUrl = "https://github.com/$Repository/pull/$linkedPullRequestNumber"
            if ($livePullRequestNumber -ne $linkedPullRequestNumber -or
                -not [string]::Equals($livePullRequestUrl, $expectedLivePullRequestUrl, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Pull request #$linkedPullRequestNumber lookup returned a mismatched number or repository URL."
            }
            if (@('OPEN', 'CLOSED', 'MERGED') -cnotcontains $livePullRequestState) {
                throw "Pull request #$linkedPullRequestNumber lookup returned an unknown state: $livePullRequestState"
            }
            if ($livePullRequestState -cne [string]$linkedPullRequestSummary.State) {
                throw "Pull request #$linkedPullRequestNumber state changed during Developer selection."
            }
            $linkedPullRequestSummary.IsDraft = [bool](Get-AutomationPropertyValue -Object $pullRequest -Name 'isDraft' -DefaultValue $false)
            $linkedPullRequestSummary.BaseRef = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'baseRefName' -DefaultValue '')
            $linkedPullRequestSummary.HeadSha = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'headRefOid' -DefaultValue '')
            $linkedPullRequestSummary.HeadRef = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'headRefName' -DefaultValue '')
            $linkedPullRequestSummary.IsCrossRepository = [bool]$pullRequest.isCrossRepository
        }

        $openPullRequestsForEvidence = @($linkedOpenPullRequests | Where-Object {
                $_.State -ceq 'OPEN' -and
                [bool]$_.BaseRepositoryMatches -and
                [int]$_.Number -gt 0 -and
                [string]$_.HeadSha -match '^[0-9a-fA-F]{40}$' -and
                -not [string]::IsNullOrWhiteSpace([string]$_.HeadRef)
            })
        if ($status -ceq 'In Progress' -and $linkedOpenPullRequests.Count -eq 1 -and $openPullRequestsForEvidence.Count -eq 1) {
            $evidencePullRequest = $openPullRequestsForEvidence[0]
            $evidencePullRequestNumber = [int]$evidencePullRequest.Number
            $pullRequestCommentNodes = @(Get-CompleteAutomationGraphQLConnection `
                    -Operation "Pull request #$evidencePullRequestNumber comments lookup" `
                    -Query $pullRequestCommentsQuery `
                    -RepositoryOwner $repositoryOwner `
                    -RepositoryName $repositoryName `
                    -Number $evidencePullRequestNumber `
                    -ConnectionPath @('pullRequest', 'comments'))
            $pullRequestReviewNodes = @(Get-CompleteAutomationGraphQLConnection `
                    -Operation "Pull request #$evidencePullRequestNumber reviews lookup" `
                    -Query $pullRequestReviewsQuery `
                    -RepositoryOwner $repositoryOwner `
                    -RepositoryName $repositoryName `
                    -Number $evidencePullRequestNumber `
                    -ConnectionPath @('pullRequest', 'reviews'))
            foreach ($comment in $pullRequestCommentNodes) {
                $sources.Add((New-LiveAutomationCommentSource -Comment $comment -Kind PullRequestComment -PullRequestNumber $evidencePullRequestNumber))
            }
            foreach ($review in $pullRequestReviewNodes) {
                foreach ($requiredReviewProperty in @('state', 'id')) {
                    if ($null -eq $review.PSObject.Properties[$requiredReviewProperty]) {
                        throw "Pull request review response for Issue #$issueNumber is missing $requiredReviewProperty."
                    }
                }
                $reviewState = [string](Get-AutomationPropertyValue -Object $review -Name 'state' -DefaultValue '')
                if ([string]::IsNullOrWhiteSpace($reviewState)) {
                    throw "Pull request review response for Issue #$issueNumber has an empty state."
                }
                if (@('APPROVED', 'CHANGES_REQUESTED', 'COMMENTED', 'DISMISSED', 'PENDING') -cnotcontains $reviewState) {
                    throw "Pull request review response for Issue #$issueNumber has an unknown state: $reviewState"
                }
                if (@('APPROVED', 'CHANGES_REQUESTED', 'DISMISSED') -cnotcontains $reviewState) {
                    continue
                }
                foreach ($requiredDecisiveReviewProperty in @('submittedAt', 'authorAssociation', 'commit')) {
                    if ($null -eq $review.PSObject.Properties[$requiredDecisiveReviewProperty]) {
                        throw "Decisive pull request review response for Issue #$issueNumber is missing $requiredDecisiveReviewProperty."
                    }
                }
                $reviewSubmittedAt = [string](Get-AutomationPropertyValue -Object $review -Name 'submittedAt' -DefaultValue '')
                $reviewAssociation = [string](Get-AutomationPropertyValue -Object $review -Name 'authorAssociation' -DefaultValue '')
                $reviewId = [string](Get-AutomationPropertyValue -Object $review -Name 'id' -DefaultValue '')
                if ([string]::IsNullOrWhiteSpace($reviewSubmittedAt) -or
                    [string]::IsNullOrWhiteSpace($reviewAssociation) -or
                    [string]::IsNullOrWhiteSpace($reviewId)) {
                    throw "Pull request review response for Issue #$issueNumber has empty immutable review metadata."
                }
                [void](Get-AutomationTimestamp -Value $reviewSubmittedAt -Name "pull request review submittedAt for Issue #$issueNumber")
                $reviewLogin = Get-AutomationAuthorLogin -Object $review
                $reviewCommit = Get-AutomationPropertyValue -Object $review -Name 'commit'
                $reviewCommitSha = [string](Get-AutomationPropertyValue -Object $reviewCommit -Name 'oid' -DefaultValue '')
                if ($reviewCommitSha -notmatch '^[0-9a-fA-F]{40}$') {
                    throw "Decisive pull request review for Issue #$issueNumber has no valid commit oid."
                }
                $sources.Add((New-AutomationContentSource `
                            -Body ([string](Get-AutomationPropertyValue -Object $review -Name 'body' -DefaultValue '')) `
                            -Timestamp $reviewSubmittedAt `
                            -Url ([string]$evidencePullRequest.Url) `
                            -AuthorLogin $reviewLogin `
                            -AuthorAssociation $reviewAssociation `
                            -Kind 'PullRequestReview' `
                            -ReviewState $reviewState `
                            -ReviewCommitSha $reviewCommitSha `
                            -EventId $reviewId `
                            -PullRequestNumber $evidencePullRequestNumber))
            }
        }

        $labels = New-Object 'System.Collections.Generic.List[string]'
        foreach ($labelNode in $labelNodes) {
            $labelNameProperty = $labelNode.PSObject.Properties['name']
            if ($null -eq $labelNameProperty -or [string]::IsNullOrWhiteSpace([string]$labelNameProperty.Value)) {
                throw "Issue #$issueNumber label connection contains a node without a name."
            }
            $labels.Add([string]$labelNameProperty.Value)
        }
        $leaseActive = $false
        $leaseInvalid = $false
        foreach ($openPullRequest in $openPullRequestsForEvidence) {
            $lease = Get-DeveloperLeaseState -PullRequestNumber $openPullRequest.Number -HeadSha $openPullRequest.HeadSha
            $leaseActive = $leaseActive -or [bool]$lease.Active
            $leaseInvalid = $leaseInvalid -or [bool]$lease.Invalid
        }

        $candidates.Add([pscustomobject][ordered]@{
                ProjectItemId          = $itemId
                Status                 = $status
                Priority               = [string](Get-AutomationPropertyValue -Object $item -Name 'priority' -DefaultValue '')
                UpdatedAt              = $updatedAt
                IssueNumber            = $issueNumber
                IssueUrl               = [string](Get-AutomationPropertyValue -Object $issue -Name 'url')
                IssueState             = [string](Get-AutomationPropertyValue -Object $issue -Name 'state')
                Labels                 = $labels.ToArray()
                LeaseActive            = $leaseActive
                LeaseStateInvalid      = $leaseInvalid
                PullRequests           = $pullRequests.ToArray()
                Sources                = $sources.ToArray()
            })
    }

    return [pscustomobject][ordered]@{
        ProjectFields = $fields
        Candidates    = $candidates.ToArray()
    }
}

function Test-AutomationPullRequestHeadRef {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value.StartsWith('-') -or
        $Value -ceq '@' -or
        $Value.StartsWith('.') -or
        $Value.EndsWith('/') -or
        $Value.EndsWith('.') -or
        $Value.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase) -or
        $Value.Contains('..') -or
        $Value.Contains('@{') -or
        $Value.Contains('//') -or
        $Value -match '[\x00-\x20~^:?*\[\\]') {
        return $false
    }
    foreach ($component in @($Value -split '/')) {
        if ([string]::IsNullOrWhiteSpace($component) -or
            $component.StartsWith('.') -or
            $component.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }
    return $true
}

function Test-AutomationTrustedMarkerAuthor {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AuthorLogin,
        [Parameter(Mandatory = $true)][string]$AuthorAssociation
    )

    if ([string]::Equals($AuthorLogin, $ProjectOwner, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    return @('OWNER', 'MEMBER', 'COLLABORATOR') -ccontains $AuthorAssociation
}

function Test-AutomationDedicatedMarkerComment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content,
        [Parameter(Mandatory = $true)][string]$MarkerName
    )

    $pattern = '\A\s*<!--[ \t]*' + [regex]::Escape($MarkerName) + '[ \t]*\r?\n'
    return [regex]::IsMatch($Content, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Get-LatestOwnerQueueDecision {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [int]$PullRequestNumber = 0
    )

    $events = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in @(Get-AutomationRelevantSources -Candidate $Candidate -PullRequestNumber $PullRequestNumber)) {
        if (@('IssueComment', 'PullRequestComment') -cnotcontains [string](Get-AutomationPropertyValue -Object $source -Name 'Kind' -DefaultValue '')) {
            continue
        }
        $body = [string](Get-AutomationPropertyValue -Object $source -Name 'Body' -DefaultValue '')
        if (-not (Test-AutomationDedicatedMarkerComment -Content $body -MarkerName 'sashimi-boy-automation-owner-decision:v1')) {
            continue
        }
        $authorAssociation = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorAssociation' -DefaultValue '')
        $authorLogin = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorLogin' -DefaultValue '')
        if ($authorAssociation -cne 'OWNER' -and
            -not [string]::Equals($authorLogin, $ProjectOwner, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $timestamp = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $source -Name 'Timestamp')) -Name 'Owner queue decision timestamp'
        try {
            if ([bool](Get-AutomationPropertyValue -Object $source -Name 'WasEdited' -DefaultValue $false)) {
                throw 'edited comments cannot carry Owner queue decisions; post a new comment.'
            }
            $markers = @(Get-AutomationOwnerQueueDecisionMarkers -Content $body)
            if ($markers.Count -eq 0) {
                throw 'Owner queue decision header did not form a complete marker.'
            }
            $matchingMarkers = @($markers | Where-Object { [int]$_.IssueNumber -eq $IssueNumber })
            if ($matchingMarkers.Count -eq 0) {
                throw "Owner queue decision does not target Issue #$IssueNumber."
            }
            foreach ($marker in $matchingMarkers) {
                $events.Add([pscustomobject][ordered]@{
                        Valid       = $true
                        Error       = $null
                        Queue       = [string]$marker.Queue
                        Timestamp   = $timestamp
                        SourceUrl   = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                        MarkerIndex = [int]$marker.MarkerIndex
                    })
            }
        }
        catch {
            $events.Add([pscustomobject][ordered]@{
                    Valid       = $false
                    Error       = $_.Exception.Message
                    Queue       = $null
                    Timestamp   = $timestamp
                    SourceUrl   = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                    MarkerIndex = [int]::MaxValue
                })
        }
    }
    if ($events.Count -eq 0) {
        return $null
    }
    $latestTicks = [long](@($events | Sort-Object -Property @{ Expression = { $_.Timestamp.UtcTicks }; Descending = $true })[0].Timestamp.UtcTicks)
    $latestEvents = @($events | Where-Object { $_.Timestamp.UtcTicks -eq $latestTicks })
    if (@($latestEvents | ForEach-Object { [string]$_.SourceUrl } | Sort-Object -Unique).Count -ne 1) {
        throw 'Owner queue decision events have an ambiguous latest timestamp; post a new authoritative comment.'
    }
    $latestEvent = @($latestEvents | Sort-Object -Property @{ Expression = { $_.MarkerIndex }; Ascending = $true })[-1]
    if (-not [bool]$latestEvent.Valid) {
        throw [string]$latestEvent.Error
    }
    return $latestEvent
}

function Get-LatestAutomationHandoff {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][int]$PullRequestNumber
    )

    $events = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in @(Get-AutomationRelevantSources -Candidate $Candidate -PullRequestNumber $PullRequestNumber)) {
        if (@('IssueComment', 'PullRequestComment') -cnotcontains [string](Get-AutomationPropertyValue -Object $source -Name 'Kind' -DefaultValue '')) {
            continue
        }
        $body = [string](Get-AutomationPropertyValue -Object $source -Name 'Body' -DefaultValue '')
        if (-not (Test-AutomationDedicatedMarkerComment -Content $body -MarkerName 'sashimi-boy-automation-handoff:v1')) {
            continue
        }
        $authorLogin = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorLogin' -DefaultValue '')
        $authorAssociation = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorAssociation' -DefaultValue '')
        if (-not (Test-AutomationTrustedMarkerAuthor -AuthorLogin $authorLogin -AuthorAssociation $authorAssociation)) {
            continue
        }
        $timestamp = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $source -Name 'Timestamp')) -Name 'handoff timestamp'
        try {
            if ([bool](Get-AutomationPropertyValue -Object $source -Name 'WasEdited' -DefaultValue $false)) {
                throw 'edited comments cannot carry automation handoff markers; post a new comment.'
            }
            $markers = @(Get-AutomationHandoffMarkers -Content $body)
            if ($markers.Count -eq 0) {
                throw 'handoff header did not form a complete marker.'
            }
            foreach ($marker in $markers) {
                $events.Add([pscustomobject][ordered]@{
                        Valid             = $true
                        Error             = $null
                        Timestamp         = $timestamp
                        MarkerIndex       = [int]$marker.MarkerIndex
                        Marker            = $marker
                        SourceUrl         = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                        AuthorLogin       = $authorLogin
                        AuthorAssociation = $authorAssociation
                    })
            }
        }
        catch {
            $events.Add([pscustomobject][ordered]@{
                    Valid             = $false
                    Error             = $_.Exception.Message
                    Timestamp         = $timestamp
                    MarkerIndex       = [int]::MaxValue
                    Marker            = $null
                    SourceUrl         = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                    AuthorLogin       = $authorLogin
                    AuthorAssociation = $authorAssociation
                })
        }
    }
    if ($events.Count -eq 0) {
        return $null
    }
    $latestTicks = [long](@($events | Sort-Object -Property @{ Expression = { $_.Timestamp.UtcTicks }; Descending = $true })[0].Timestamp.UtcTicks)
    $latestEvents = @($events | Where-Object { $_.Timestamp.UtcTicks -eq $latestTicks })
    if (@($latestEvents | ForEach-Object { [string]$_.SourceUrl } | Sort-Object -Unique).Count -ne 1) {
        return [pscustomobject][ordered]@{
            Valid             = $false
            Error             = 'handoff events have an ambiguous latest timestamp; post a new comment.'
            Timestamp         = $latestEvents[0].Timestamp
            MarkerIndex       = [int]::MaxValue
            Marker            = $null
            SourceUrl         = ''
            AuthorLogin       = ''
            AuthorAssociation = ''
        }
    }
    return @($latestEvents | Sort-Object -Property @{ Expression = { $_.MarkerIndex }; Ascending = $true })[-1]
}

function Compare-AutomationEventOrder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$CandidateEvent,
        [Parameter(Mandatory = $true)][object]$ReferenceEvent
    )

    if ($CandidateEvent.Timestamp.UtcTicks -ne $ReferenceEvent.Timestamp.UtcTicks) {
        if ($CandidateEvent.Timestamp.UtcTicks -gt $ReferenceEvent.Timestamp.UtcTicks) {
            return 'Later'
        }
        return 'Earlier'
    }
    if (-not [string]::Equals([string]$CandidateEvent.SourceUrl, [string]$ReferenceEvent.SourceUrl, [StringComparison]::Ordinal)) {
        return 'Ambiguous'
    }
    if ([int]$CandidateEvent.MarkerIndex -gt [int]$ReferenceEvent.MarkerIndex) {
        return 'Later'
    }
    if ([int]$CandidateEvent.MarkerIndex -lt [int]$ReferenceEvent.MarkerIndex) {
        return 'Earlier'
    }
    return 'Same'
}

function Get-AutomationHandoffCompletionsAfter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [Parameter(Mandatory = $true)][object]$HandoffEvent,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][int]$PullRequestNumber
    )

    $events = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in @(Get-AutomationRelevantSources -Candidate $Candidate -PullRequestNumber $PullRequestNumber)) {
        if (@('IssueComment', 'PullRequestComment') -cnotcontains [string](Get-AutomationPropertyValue -Object $source -Name 'Kind' -DefaultValue '')) {
            continue
        }
        $body = [string](Get-AutomationPropertyValue -Object $source -Name 'Body' -DefaultValue '')
        if (-not (Test-AutomationDedicatedMarkerComment -Content $body -MarkerName 'sashimi-boy-automation-handoff-completion:v1')) {
            continue
        }
        $authorLogin = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorLogin' -DefaultValue '')
        $authorAssociation = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorAssociation' -DefaultValue '')
        if (-not (Test-AutomationTrustedMarkerAuthor -AuthorLogin $authorLogin -AuthorAssociation $authorAssociation)) {
            continue
        }
        $timestamp = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $source -Name 'Timestamp')) -Name 'handoff completion timestamp'
        try {
            if ([bool](Get-AutomationPropertyValue -Object $source -Name 'WasEdited' -DefaultValue $false)) {
                throw 'edited comments cannot carry automation handoff completion markers; post a new comment.'
            }
            $markers = @(Get-AutomationHandoffCompletionMarkers -Content $body)
            if ($markers.Count -eq 0) {
                throw 'handoff completion header did not form a complete marker.'
            }
            foreach ($marker in $markers) {
                $events.Add([pscustomobject][ordered]@{
                        Valid       = $true
                        Error       = $null
                        Timestamp   = $timestamp
                        SourceUrl   = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                        MarkerIndex = [int]$marker.MarkerIndex
                        Marker      = $marker
                    })
            }
        }
        catch {
            $events.Add([pscustomobject][ordered]@{
                    Valid       = $false
                    Error       = $_.Exception.Message
                    Timestamp   = $timestamp
                    SourceUrl   = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
                    MarkerIndex = [int]::MaxValue
                    Marker      = $null
                })
        }
    }

    $laterEvents = New-Object 'System.Collections.Generic.List[object]'
    foreach ($event in $events) {
        $order = Compare-AutomationEventOrder -CandidateEvent $event -ReferenceEvent $HandoffEvent
        if ($order -ceq 'Later') {
            $laterEvents.Add($event)
        }
        elseif ($order -ceq 'Ambiguous') {
            $laterEvents.Add([pscustomobject][ordered]@{
                    Valid       = $false
                    Error       = 'handoff and completion events have an ambiguous timestamp; post a new completion comment.'
                    Timestamp   = $event.Timestamp
                    SourceUrl   = $event.SourceUrl
                    MarkerIndex = $event.MarkerIndex
                    Marker      = $null
                })
        }
    }
    return $laterEvents.ToArray()
}

function Get-AutomationReviewPassStateAfterHandoff {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [Parameter(Mandatory = $true)][object]$HandoffEvent,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][int]$PullRequestNumber
    )

    $decisions = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in @(Get-AutomationRelevantSources -Candidate $Candidate -PullRequestNumber $PullRequestNumber)) {
        if ([string](Get-AutomationPropertyValue -Object $source -Name 'Kind' -DefaultValue '') -cne 'PullRequestReview') {
            continue
        }
        $reviewState = [string](Get-AutomationPropertyValue -Object $source -Name 'ReviewState' -DefaultValue '')
        if (@('APPROVED', 'CHANGES_REQUESTED', 'DISMISSED') -cnotcontains $reviewState) {
            continue
        }
        $reviewCommitSha = [string](Get-AutomationPropertyValue -Object $source -Name 'ReviewCommitSha' -DefaultValue '')
        if ($reviewCommitSha -notmatch '^[0-9a-fA-F]{40}$' -or
            -not [string]::Equals($reviewCommitSha, $HeadSha, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $authorLogin = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorLogin' -DefaultValue '')
        $authorAssociation = [string](Get-AutomationPropertyValue -Object $source -Name 'AuthorAssociation' -DefaultValue '')
        if (-not (Test-AutomationTrustedMarkerAuthor -AuthorLogin $authorLogin -AuthorAssociation $authorAssociation)) {
            continue
        }
        $event = [pscustomobject][ordered]@{
            Timestamp   = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $source -Name 'Timestamp')) -Name 'pull request review timestamp'
            SourceUrl   = [string](Get-AutomationPropertyValue -Object $source -Name 'Url' -DefaultValue '')
            MarkerIndex = 0
        }
        $order = Compare-AutomationEventOrder -CandidateEvent $event -ReferenceEvent $HandoffEvent
        if ($order -ceq 'Ambiguous') {
            if ($reviewState -ceq 'APPROVED') {
                return 'Ambiguous'
            }
            continue
        }
        if ($order -ceq 'Later') {
            $decisions.Add([pscustomobject][ordered]@{
                    State     = $reviewState
                    Timestamp = $event.Timestamp
                })
        }
    }
    if ($decisions.Count -eq 0) {
        return 'None'
    }
    $latestTicks = [long](@($decisions | Sort-Object -Property @{ Expression = { $_.Timestamp.UtcTicks }; Descending = $true })[0].Timestamp.UtcTicks)
    $latestStates = @($decisions | Where-Object { $_.Timestamp.UtcTicks -eq $latestTicks } | ForEach-Object { [string]$_.State } | Sort-Object -Unique)
    if ($latestStates.Count -ne 1) {
        return 'Ambiguous'
    }
    if ($latestStates[0] -ceq 'APPROVED') {
        return 'Approved'
    }
    return 'None'
}

function New-DeveloperCandidateResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Candidate,
        [Parameter(Mandatory = $true)][bool]$Eligible,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Mode = 'None',
        [int]$Rank = [int]::MaxValue,
        [AllowNull()][object]$PullRequest = $null,
        [AllowNull()][object]$HandoffEvent = $null,
        [AllowNull()][object]$UpdatedAtValue = $null
    )

    return [pscustomobject][ordered]@{
        Candidate      = $Candidate
        Eligible       = $Eligible
        Reason         = $Reason
        Mode           = $Mode
        Rank           = $Rank
        PullRequest    = $PullRequest
        HandoffEvent   = $HandoffEvent
        UpdatedAtValue = $UpdatedAtValue
    }
}

function Test-DeveloperCandidate {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Candidate)

    $issueNumber = [int](Get-AutomationPropertyValue -Object $Candidate -Name 'IssueNumber' -DefaultValue 0)
    $status = [string](Get-AutomationPropertyValue -Object $Candidate -Name 'Status' -DefaultValue '')
    $priority = [string](Get-AutomationPropertyValue -Object $Candidate -Name 'Priority' -DefaultValue '')
    if (@('Ready', 'In Progress') -cnotcontains $status) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'IneligibleStatus'
    }
    if (@('P0', 'P1', 'P2', 'P3') -cnotcontains $priority) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidPriority'
    }
    if ([string](Get-AutomationPropertyValue -Object $Candidate -Name 'IssueState' -DefaultValue '') -cne 'OPEN') {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'IssueNotOpen'
    }

    try {
        $updatedAtValue = Get-AutomationTimestamp -Value ([string](Get-AutomationPropertyValue -Object $Candidate -Name 'UpdatedAt' -DefaultValue '')) -Name "Project Updated for Issue #$issueNumber"
    }
    catch {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidProjectUpdatedAt'
    }

    if ([bool](Get-AutomationPropertyValue -Object $Candidate -Name 'LeaseStateInvalid' -DefaultValue $false)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidDeveloperLease' -UpdatedAtValue $updatedAtValue
    }
    if ([bool](Get-AutomationPropertyValue -Object $Candidate -Name 'LeaseActive' -DefaultValue $false)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'ActiveDeveloperLease' -UpdatedAtValue $updatedAtValue
    }

    $pullRequests = @(Get-AutomationPropertyValue -Object $Candidate -Name 'PullRequests' -DefaultValue @())
    $openPullRequests = @($pullRequests | Where-Object { [string](Get-AutomationPropertyValue -Object $_ -Name 'State' -DefaultValue '') -ceq 'OPEN' })
    $pullRequest = $null
    $targetPullRequestNumber = 0
    if ($status -ceq 'Ready') {
        if ($openPullRequests.Count -ne 0) {
            return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'ReadyHasLinkedOpenPullRequest' -UpdatedAtValue $updatedAtValue
        }
    }
    else {
        if ($openPullRequests.Count -eq 0) {
            return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'NoLinkedDraftPullRequest' -UpdatedAtValue $updatedAtValue
        }
        if ($openPullRequests.Count -gt 1) {
            return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'MultipleLinkedDraftPullRequests' -UpdatedAtValue $updatedAtValue
        }
        $pullRequest = $openPullRequests[0]
        $targetPullRequestNumber = [int](Get-AutomationPropertyValue -Object $pullRequest -Name 'Number' -DefaultValue 0)
    }

    try {
        $ownerDecision = Get-LatestOwnerQueueDecision `
            -Candidate $Candidate `
            -IssueNumber $issueNumber `
            -PullRequestNumber $targetPullRequestNumber
    }
    catch {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidOwnerQueueDecision' -UpdatedAtValue $updatedAtValue
    }
    if ($null -ne $ownerDecision -and [string]$ownerDecision.Queue -ceq 'block') {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'UnresolvedProductAssetOrExternalBlocker' -UpdatedAtValue $updatedAtValue
    }

    $labels = @((Get-AutomationPropertyValue -Object $Candidate -Name 'Labels' -DefaultValue @()) | ForEach-Object { ([string]$_).ToLowerInvariant() })
    if ($labels -ccontains 'blocked') {
        if ($null -eq $ownerDecision -or [string]$ownerDecision.Queue -cne 'unblock') {
            return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'BlockedLabelUnresolved' -UpdatedAtValue $updatedAtValue
        }
    }

    if ($status -ceq 'Ready') {
        $rank = if (@('P0', 'P1') -ccontains $priority) { 5 } else { 6 }
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $true -Reason 'ready-new-work' -Mode 'NewWork' -Rank $rank -UpdatedAtValue $updatedAtValue
    }

    $crossRepositoryProperty = $pullRequest.PSObject.Properties['IsCrossRepository']
    $baseRepositoryProperty = $pullRequest.PSObject.Properties['BaseRepositoryMatches']
    if ($null -eq $crossRepositoryProperty -or $crossRepositoryProperty.Value -isnot [bool] -or
        $null -eq $baseRepositoryProperty -or $baseRepositoryProperty.Value -isnot [bool]) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'PullRequestRepositoryIdentityUnknown' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    if (-not [bool](Get-AutomationPropertyValue -Object $pullRequest -Name 'BaseRepositoryMatches' -DefaultValue $true)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'PullRequestRepositoryMismatch' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    if (-not [bool](Get-AutomationPropertyValue -Object $pullRequest -Name 'IsDraft' -DefaultValue $false)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'PullRequestNotDraft' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    if ([bool](Get-AutomationPropertyValue -Object $pullRequest -Name 'IsCrossRepository' -DefaultValue $false)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'CrossRepositoryPullRequest' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    if ([string](Get-AutomationPropertyValue -Object $pullRequest -Name 'BaseRef' -DefaultValue '') -cne 'main') {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'PullRequestBaseIsNotMain' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    $headSha = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'HeadSha' -DefaultValue '')
    $headRef = [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'HeadRef' -DefaultValue '')
    if ($headSha -notmatch '^[0-9a-fA-F]{40}$' -or -not (Test-AutomationPullRequestHeadRef -Value $headRef)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'MissingOrInvalidPullRequestHead' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }

    $handoffEvent = Get-LatestAutomationHandoff `
        -Candidate $Candidate `
        -IssueNumber $issueNumber `
        -PullRequestNumber $targetPullRequestNumber
    if ($null -eq $handoffEvent) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'NoCurrentHandoff' -PullRequest $pullRequest -UpdatedAtValue $updatedAtValue
    }
    if (-not [bool]$handoffEvent.Valid) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidHandoffMarker' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    $handoff = $handoffEvent.Marker
    if ([int]$handoff.IssueNumber -ne $issueNumber -or
        [int]$handoff.PullRequestNumber -ne [int](Get-AutomationPropertyValue -Object $pullRequest -Name 'Number')) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'HandoffTargetMismatch' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    if (-not [string]::Equals([string]$handoff.HeadSha, $headSha, [StringComparison]::OrdinalIgnoreCase)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'StaleHandoffHead' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    if ([string]$handoff.SourceRole -ceq 'Owner' -and
        [string]$handoffEvent.AuthorAssociation -cne 'OWNER' -and
        -not [string]::Equals([string]$handoffEvent.AuthorLogin, $ProjectOwner, [StringComparison]::OrdinalIgnoreCase)) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'OwnerHandoffNotAuthoritative' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    $reviewPassState = Get-AutomationReviewPassStateAfterHandoff `
        -Candidate $Candidate `
        -HandoffEvent $handoffEvent `
        -HeadSha $headSha `
        -PullRequestNumber $targetPullRequestNumber
    if ($reviewPassState -ceq 'Ambiguous') {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'AmbiguousReviewDecisionAfterHandoff' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    if ($reviewPassState -ceq 'Approved') {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'ReviewPassAfterHandoff' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }

    $completionEvents = @(Get-AutomationHandoffCompletionsAfter `
            -Candidate $Candidate `
            -HandoffEvent $handoffEvent `
            -IssueNumber $issueNumber `
            -PullRequestNumber $targetPullRequestNumber)
    foreach ($completionEvent in @($completionEvents | Where-Object { $_.Valid })) {
        $completion = $completionEvent.Marker
        if ([int]$completion.IssueNumber -eq $issueNumber -and
            [int]$completion.PullRequestNumber -eq [int](Get-AutomationPropertyValue -Object $pullRequest -Name 'Number') -and
            [string]::Equals([string]$completion.HeadSha, $headSha, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$completion.HandoffUrl, [string]$handoffEvent.SourceUrl, [StringComparison]::Ordinal)) {
            return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'HandoffCompleted' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
        }
    }
    if (@($completionEvents | Where-Object { -not $_.Valid }).Count -gt 0) {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'InvalidHandoffCompletionMarker' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }

    $mode = [string]$handoff.Mode
    if ($mode -ceq 'ReviewFix') {
        $rank = if (@('P0', 'P1') -ccontains $priority) { 1 } else { 3 }
    }
    elseif ($mode -ceq 'DeliveryResume') {
        $rank = if (@('P0', 'P1') -ccontains $priority) { 2 } else { 4 }
    }
    else {
        return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $false -Reason 'UnsupportedHandoffMode' -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
    }
    return New-DeveloperCandidateResult -Candidate $Candidate -Eligible $true -Reason ([string]$handoff.Reason) -Mode $mode -Rank $rank -PullRequest $pullRequest -HandoffEvent $handoffEvent -UpdatedAtValue $updatedAtValue
}

function New-DeveloperSelectorOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][bool]$Succeeded,
        [AllowNull()][object]$SelectedResult,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$ExcludedCandidates,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Errors,
        [Parameter(Mandatory = $true)][ValidateSet('Live', 'Fixture')][string]$DataSource
    )

    $selected = $null -ne $SelectedResult
    $candidate = if ($selected) { $SelectedResult.Candidate } else { $null }
    $pullRequest = if ($selected) { $SelectedResult.PullRequest } else { $null }
    $handoff = if ($selected -and $null -ne $SelectedResult.HandoffEvent) { $SelectedResult.HandoffEvent.Marker } else { $null }
    $mode = if ($selected) { [string]$SelectedResult.Mode } else { 'None' }
    $headRef = if ($null -ne $pullRequest) { [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'HeadRef' -DefaultValue '') } else { $null }
    $isResume = $mode -eq 'ReviewFix' -or $mode -eq 'DeliveryResume'

    return [pscustomobject][ordered]@{
        Succeeded                 = $Succeeded
        DataSource                = $DataSource
        Selected                  = $selected
        Mode                      = $mode
        IssueNumber               = if ($selected) { [int](Get-AutomationPropertyValue -Object $candidate -Name 'IssueNumber') } else { $null }
        IssueUrl                  = if ($selected) { [string](Get-AutomationPropertyValue -Object $candidate -Name 'IssueUrl' -DefaultValue '') } else { $null }
        Status                    = if ($selected) { [string](Get-AutomationPropertyValue -Object $candidate -Name 'Status') } else { $null }
        Priority                  = if ($selected) { [string](Get-AutomationPropertyValue -Object $candidate -Name 'Priority') } else { $null }
        UpdatedAt                 = if ($selected) { $SelectedResult.UpdatedAtValue.ToString('o') } else { $null }
        PullRequestNumber         = if ($null -ne $pullRequest) { [int](Get-AutomationPropertyValue -Object $pullRequest -Name 'Number') } else { $null }
        PullRequestUrl            = if ($null -ne $pullRequest) { [string](Get-AutomationPropertyValue -Object $pullRequest -Name 'Url' -DefaultValue '') } else { $null }
        PullRequestHeadSha        = if ($null -ne $pullRequest) { ([string](Get-AutomationPropertyValue -Object $pullRequest -Name 'HeadSha')).ToLowerInvariant() } else { $null }
        PullRequestHeadRef        = $headRef
        Reason                    = if ($selected) { [string]$SelectedResult.Reason } else { 'NoEligibleDeveloperWorkItem' }
        LatestHandoffUrl          = if ($selected -and $null -ne $SelectedResult.HandoffEvent) { [string]$SelectedResult.HandoffEvent.SourceUrl } else { $null }
        FindingUrl                = if ($null -ne $handoff) { [string]$handoff.FindingUrl } else { $null }
        PendingCommand            = if ($null -ne $handoff) { [string]$handoff.PendingCommand } else { $null }
        ResumePushRefSpec         = if ($isResume) { "HEAD:$headRef" } else { $null }
        MayCreateIssue            = $false
        MayCreatePullRequest      = ($selected -and -not $isResume)
        MayCreateRemoteBranch     = ($selected -and -not $isResume)
        ValidationOnlyAllowed     = ($mode -eq 'DeliveryResume')
        AllowedCompletionStatus   = if ($selected) { 'Review' } else { $null }
        MaximumIssuesThisRun      = 1
        ExcludedCandidates        = @($ExcludedCandidates)
        Errors                    = @($Errors)
    }
}

$scriptExitCode = 0
$dataSource = if ([string]::IsNullOrWhiteSpace($FixturePath)) { 'Live' } else { 'Fixture' }
try {
    if ([string]::IsNullOrWhiteSpace($FixturePath)) {
        $queueData = Get-LiveDeveloperQueueData
    }
    else {
        if ($env:SASHIMI_BOY_AUTOMATION_TEST_HARNESS -cne '1' -or
            [string]::IsNullOrWhiteSpace($FixtureTestRoot) -or [string]::IsNullOrWhiteSpace($FixtureTestRunId)) {
            throw 'Fixture mode is reserved for the owned PowerShell smoke harness.'
        }
        Assert-AutomationPathHasNoReparsePoint -Path $FixtureTestRoot
        $normalizedTestRoot = ConvertTo-AutomationPath -Path $FixtureTestRoot
        $normalizedFixturePath = ConvertTo-AutomationPath -Path $FixturePath
        $testRootPrefix = $normalizedTestRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $normalizedFixturePath.StartsWith($testRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Selector fixture must be a child of the owned smoke-test root.'
        }
        $testOwnerMarkerPath = Join-Path -Path $normalizedTestRoot -ChildPath '.automation-script-tests-owner'
        Assert-AutomationPathHasNoReparsePoint -Path $testOwnerMarkerPath
        $testOwnerMarker = Get-Content -Raw -LiteralPath $testOwnerMarkerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ([int](Get-AutomationPropertyValue -Object $testOwnerMarker -Name 'SchemaVersion' -DefaultValue 0) -ne 1 -or
            -not [string]::Equals([string](Get-AutomationPropertyValue -Object $testOwnerMarker -Name 'RunId' -DefaultValue ''), $FixtureTestRunId, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-AutomationPathEqual -Left ([string](Get-AutomationPropertyValue -Object $testOwnerMarker -Name 'WorkspaceRoot' -DefaultValue '')) -Right $normalizedTestRoot) -or
            [System.IO.Path]::GetFileName([string](Get-AutomationPropertyValue -Object $testOwnerMarker -Name 'Script' -DefaultValue '')) -cne 'Test-AutomationScripts.ps1') {
            throw 'Selector fixture test ownership marker is invalid or mismatched.'
        }
        Assert-AutomationPathHasNoReparsePoint -Path $FixturePath
        if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) {
            throw "Selector fixture does not exist: $FixturePath"
        }
        $queueData = Get-Content -Raw -LiteralPath $FixturePath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        Assert-DeveloperProjectSchema -Fields @(Get-AutomationPropertyValue -Object $queueData -Name 'ProjectFields' -DefaultValue @())
    }

    $evaluated = New-Object 'System.Collections.Generic.List[object]'
    $seenIssueNumbers = @{}
    foreach ($candidate in @(Get-AutomationPropertyValue -Object $queueData -Name 'Candidates' -DefaultValue @())) {
        $candidateIssueNumber = [int](Get-AutomationPropertyValue -Object $candidate -Name 'IssueNumber' -DefaultValue 0)
        if ($candidateIssueNumber -lt 1) {
            throw 'Every Developer queue candidate must have a positive IssueNumber.'
        }
        if ($seenIssueNumbers.ContainsKey($candidateIssueNumber)) {
            throw "GitHub Project contains duplicate items for Issue #$candidateIssueNumber."
        }
        $seenIssueNumbers[$candidateIssueNumber] = $true
        $evaluated.Add((Test-DeveloperCandidate -Candidate $candidate))
    }

    $eligible = @($evaluated | Where-Object { $_.Eligible } | Sort-Object -Property `
            @{ Expression = { $_.Rank }; Ascending = $true }, `
            @{ Expression = { $_.UpdatedAtValue.UtcTicks }; Ascending = $true }, `
            @{ Expression = { [int](Get-AutomationPropertyValue -Object $_.Candidate -Name 'IssueNumber') }; Ascending = $true })
    $selectedResult = if ($eligible.Count -gt 0) { $eligible[0] } else { $null }

    $excluded = New-Object 'System.Collections.Generic.List[object]'
    foreach ($result in $evaluated) {
        if ($null -ne $selectedResult -and [object]::ReferenceEquals($result, $selectedResult)) {
            continue
        }
        $exclusionReason = if ($result.Eligible) { 'LowerQueuePriority' } else { [string]$result.Reason }
        $excluded.Add([pscustomobject][ordered]@{
                IssueNumber = [int](Get-AutomationPropertyValue -Object $result.Candidate -Name 'IssueNumber' -DefaultValue 0)
                IssueUrl    = [string](Get-AutomationPropertyValue -Object $result.Candidate -Name 'IssueUrl' -DefaultValue '')
                Status      = [string](Get-AutomationPropertyValue -Object $result.Candidate -Name 'Status' -DefaultValue '')
                Priority    = [string](Get-AutomationPropertyValue -Object $result.Candidate -Name 'Priority' -DefaultValue '')
                Reason      = $exclusionReason
            })
    }

    $output = New-DeveloperSelectorOutput `
        -Succeeded $true `
        -SelectedResult $selectedResult `
        -ExcludedCandidates @($excluded | Sort-Object -Property IssueNumber) `
        -Errors @() `
        -DataSource $dataSource
    $output | ConvertTo-AutomationJson
}
catch {
    $scriptExitCode = 1
    $output = New-DeveloperSelectorOutput `
        -Succeeded $false `
        -SelectedResult $null `
        -ExcludedCandidates @() `
        -Errors @($_.Exception.Message) `
        -DataSource $dataSource
    $output.Reason = 'SelectorError'
    $output | ConvertTo-AutomationJson
}

exit $scriptExitCode
