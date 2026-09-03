#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot,

    [Parameter()]
    [string]$WindowsPowerShellPath = (Join-Path -Path $env:SystemRoot -ChildPath 'System32\WindowsPowerShell\v1.0\powershell.exe'),

    [switch]$KeepTemporaryFiles,

    [Parameter(DontShow = $true)]
    [ValidateSet('None', 'MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')]
    [string]$InternalLifecycleFailure = 'None'
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

$handoffLibraryPath = Join-Path -Path $RepositoryRoot -ChildPath 'Tools\Automation\Automation.Handoff.ps1'
if (-not (Test-Path -LiteralPath $handoffLibraryPath -PathType Leaf)) {
    throw "Automation handoff helpers do not exist: $handoffLibraryPath"
}
. $handoffLibraryPath

$script:testResults = New-Object System.Collections.Generic.List[object]
$script:temporaryRoot = $null
$script:fixture = $null
$script:unityProjectPath = $null
$script:testRunId = $null
$script:ownedTestRootCreated = $false
$script:ownerMarkerWritten = $false
$script:suiteCompleted = $false
$script:suiteSucceeded = $false
$script:testRootPreserved = $false

if ($InternalLifecycleFailure -ne 'None' -and
    $env:SASHIMI_BOY_AUTOMATION_TEST_HARNESS -cne '1') {
    throw 'Internal lifecycle failure injection is available only to this script smoke harness.'
}

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

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # A lifecycle regression intentionally lets a child fail before it can
        # emit JSON. Capture native stderr as evidence without turning that
        # expected child failure into a terminating error in the parent suite.
        $ErrorActionPreference = 'Continue'
        $output = @(& $WindowsPowerShellPath -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    return [pscustomobject][ordered]@{
        ExitCode = [int]$exitCode
        Output   = [string]::Join([Environment]::NewLine, [string[]]$output)
    }
}

function Invoke-SmokeRunnerLifecycleProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemporaryPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')]
        [string]$Failure,

        [switch]$KeepTemporaryFiles
    )

    $previousTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
    $previousTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
    $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('TEMP', $TemporaryPath, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $TemporaryPath, 'Process')
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
        return Invoke-AutomationChildScript -ScriptPath $PSCommandPath -WorkingDirectory $repository -Parameters @{
            RepositoryRoot          = $repository
            WindowsPowerShellPath   = $WindowsPowerShellPath
            InternalLifecycleFailure = $Failure
            KeepTemporaryFiles      = [bool]$KeepTemporaryFiles
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('TEMP', $previousTemp, 'Process')
        [Environment]::SetEnvironmentVariable('TMP', $previousTmp, 'Process')
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
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

function Get-TestDeveloperProjectFields {
    [CmdletBinding()]
    param()

    return @(
        [pscustomobject][ordered]@{
            name    = 'Status'
            options = @(
                [pscustomobject]@{ name = 'Backlog' },
                [pscustomobject]@{ name = 'Ready' },
                [pscustomobject]@{ name = 'In Progress' },
                [pscustomobject]@{ name = 'Review' },
                [pscustomobject]@{ name = 'Verification' },
                [pscustomobject]@{ name = 'Done' }
            )
        },
        [pscustomobject][ordered]@{
            name    = 'Priority'
            options = @(
                [pscustomobject]@{ name = 'P0' },
                [pscustomobject]@{ name = 'P1' },
                [pscustomobject]@{ name = 'P2' },
                [pscustomobject]@{ name = 'P3' }
            )
        },
        [pscustomobject][ordered]@{ name = 'Area'; options = @() },
        [pscustomobject][ordered]@{ name = 'Size'; options = @() }
    )
}

function New-TestDeveloperPullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$HeadRef,
        [string]$State = 'OPEN',
        [bool]$IsDraft = $true,
        [string]$BaseRef = 'main',
        [bool]$IsCrossRepository = $false,
        [bool]$BaseRepositoryMatches = $true
    )

    return [pscustomobject][ordered]@{
        Number            = $Number
        Url               = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$Number"
        State             = $State
        IsDraft           = $IsDraft
        BaseRef           = $BaseRef
        HeadSha           = $HeadSha
        HeadRef           = $HeadRef
        IsCrossRepository = $IsCrossRepository
        BaseRepositoryMatches = $BaseRepositoryMatches
    }
}

function New-TestAutomationSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Body,
        [Parameter(Mandatory = $true)][string]$Timestamp,
        [Parameter(Mandatory = $true)][string]$Url,
        [string]$AuthorLogin = 'automation-agent',
        [string]$AuthorAssociation = 'MEMBER',
        [string]$Kind = 'IssueComment',
        [string]$ReviewState = '',
        [string]$ReviewCommitSha = '',
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

function New-TestDeveloperCandidate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Priority,
        [Parameter(Mandatory = $true)][string]$UpdatedAt,
        [string]$IssueState = 'OPEN',
        [string[]]$Labels = @(),
        [object[]]$PullRequests = @(),
        [object[]]$Sources = @(),
        [bool]$LeaseActive = $false,
        [bool]$LeaseStateInvalid = $false
    )

    return [pscustomobject][ordered]@{
        ProjectItemId          = "project-item-$IssueNumber"
        Status                 = $Status
        Priority               = $Priority
        UpdatedAt              = $UpdatedAt
        IssueNumber            = $IssueNumber
        IssueUrl               = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/$IssueNumber"
        IssueState             = $IssueState
        Labels                 = @($Labels)
        LeaseActive            = $LeaseActive
        LeaseStateInvalid      = $LeaseStateInvalid
        PullRequests           = @($PullRequests)
        Sources                = @($Sources)
    }
}

function Invoke-TestDeveloperSelector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Candidates,
        [object[]]$ProjectFields = $(Get-TestDeveloperProjectFields),
        [string]$Name = 'selector-fixture'
    )

    $fixturePath = Join-Path -Path $script:temporaryRoot -ChildPath ($Name + '-' + [Guid]::NewGuid().ToString('N') + '.json')
    $fixture = [pscustomobject][ordered]@{
        ProjectFields = @($ProjectFields)
        Candidates    = @($Candidates)
    }
    New-TestFile -Path $fixturePath -Content (($fixture | ConvertTo-Json -Depth 16) + "`n")
    $selectorPath = Join-Path $repository 'Tools\Automation\Get-DeveloperWorkItem.ps1'
    $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
        return Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters @{
            Repository   = 'DongGyunLeeeee/sashimi-boy-unity'
            ProjectOwner = 'DongGyunLeeeee'
            ProjectNumber = 1
            FixturePath  = $fixturePath
            FixtureTestRoot = $script:temporaryRoot
            FixtureTestRunId = $script:testRunId
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
    }
}

function New-TestGraphQLConnectionResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('issue', 'pullRequest')][string]$ParentName,
        [Parameter(Mandatory = $true)][ValidateSet('labels', 'comments', 'reviews')][string]$ConnectionName,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Nodes,
        [int]$TotalCount = -1,
        [bool]$HasNextPage = $false,
        [AllowNull()][string]$EndCursor = $null
    )

    if ($TotalCount -lt 0) {
        $TotalCount = $Nodes.Count
    }
    $connection = [pscustomobject][ordered]@{
        totalCount = $TotalCount
        nodes = @($Nodes)
        pageInfo = [pscustomobject][ordered]@{
            hasNextPage = $HasNextPage
            endCursor = $EndCursor
        }
    }
    $parent = [ordered]@{}
    $parent[$ConnectionName] = $connection
    $repositoryNode = [ordered]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
    $repositoryNode[$ParentName] = [pscustomobject]$parent
    return [pscustomobject][ordered]@{
        data = [pscustomobject][ordered]@{
            repository = [pscustomobject]$repositoryNode
        }
    }
}

function New-TestProjectLinkedPullRequestsResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Nodes,
        [string]$ProjectItemId = 'project-item-690',
        [int]$TotalCount = -1,
        [bool]$HasNextPage = $false,
        [AllowNull()][string]$EndCursor = $null
    )

    if ($TotalCount -lt 0) {
        $TotalCount = $Nodes.Count
    }
    return [pscustomobject][ordered]@{
        data = [pscustomobject][ordered]@{
            node = [pscustomobject][ordered]@{
                id = $ProjectItemId
                linkedPullRequests = [pscustomobject][ordered]@{
                    __typename = 'ProjectV2ItemFieldPullRequestValue'
                    pullRequests = [pscustomobject][ordered]@{
                        totalCount = $TotalCount
                        nodes = @($Nodes)
                        pageInfo = [pscustomobject][ordered]@{
                            hasNextPage = $HasNextPage
                            endCursor = $EndCursor
                        }
                    }
                }
            }
        }
    }
}

function New-TestDeveloperSelectorGitHubFixture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$HandoffMarker,
        [Parameter(Mandatory = $true)][string]$HeadSha
    )

    $fixtureRoot = Join-Path -Path $Root -ChildPath ('selector-live-gh-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixtureRoot -ErrorAction Stop | Out-Null
    $logPath = Join-Path $fixtureRoot 'invocations.jsonl'
    $mutationSentinel = Join-Path $fixtureRoot 'mutation-attempted.txt'
    $fieldPath = Join-Path $fixtureRoot 'fields.json'
    $itemPath = Join-Path $fixtureRoot 'items.json'
    $updatedPath = Join-Path $fixtureRoot 'updated.json'
    $issuePath = Join-Path $fixtureRoot 'issue.json'
    $pullRequestPath = Join-Path $fixtureRoot 'pull-request.json'
    $linkedPullRequestsPath = Join-Path $fixtureRoot 'linked-pull-requests.json'
    $linkedPullRequestsNextPath = Join-Path $fixtureRoot 'linked-pull-requests-next.json'
    $issueLabelsPath = Join-Path $fixtureRoot 'issue-labels.json'
    $issueLabelsNextPath = Join-Path $fixtureRoot 'issue-labels-next.json'
    $issueCommentsPath = Join-Path $fixtureRoot 'issue-comments.json'
    $issueCommentsNextPath = Join-Path $fixtureRoot 'issue-comments-next.json'
    $pullRequestCommentsPath = Join-Path $fixtureRoot 'pull-request-comments.json'
    $pullRequestCommentsNextPath = Join-Path $fixtureRoot 'pull-request-comments-next.json'
    $pullRequestReviewsPath = Join-Path $fixtureRoot 'pull-request-reviews.json'
    $pullRequestReviewsNextPath = Join-Path $fixtureRoot 'pull-request-reviews-next.json'
    $fakeScriptPath = Join-Path $fixtureRoot 'gh.ps1'
    $fakeCommandPath = Join-Path $fixtureRoot 'gh.cmd'

    $fields = [pscustomobject][ordered]@{
        fields = @(Get-TestDeveloperProjectFields)
        totalCount = 4
    }
    $items = [pscustomobject][ordered]@{
        items = @([pscustomobject][ordered]@{
                id = 'project-item-690'
                status = 'In Progress'
                priority = 'P1'
                'linked pull requests' = @('https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790')
                content = [pscustomobject][ordered]@{
                    type = 'Issue'
                    repository = 'DongGyunLeeeee/sashimi-boy-unity'
                    number = 690
                }
            })
        totalCount = 1
    }
    $updated = [pscustomobject][ordered]@{
        data = [pscustomobject][ordered]@{
            node = [pscustomobject][ordered]@{ updatedAt = '2026-08-01T00:00:00Z' }
        }
    }
    $issue = [pscustomobject][ordered]@{
        number = 690
        title = 'Live selector fixture'
        state = 'OPEN'
        labels = @()
        url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690'
        comments = @([pscustomobject][ordered]@{
                body = $HandoffMarker
                createdAt = '2026-08-01T00:01:00Z'
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-1'
                author = [pscustomobject]@{ login = 'automation-agent' }
                authorAssociation = 'MEMBER'
                includesCreatedEdit = $false
            })
    }
    $pullRequest = [pscustomobject][ordered]@{
        number = 790
        url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790'
        state = 'OPEN'
        isDraft = $true
        baseRefName = 'main'
        headRefName = 'codex/existing-live-fixture'
        headRefOid = $HeadSha
        headRepositoryOwner = [pscustomobject]@{ login = 'DongGyunLeeeee' }
        isCrossRepository = $false
        comments = @()
        reviews = @()
        updatedAt = '2026-08-01T00:01:00Z'
    }
    $linkedPullRequestNode = [pscustomobject][ordered]@{
        id = 'linked-pr-790'
        number = 790
        url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790'
        state = 'OPEN'
        repository = [pscustomobject]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
    }
    $linkedPullRequests = New-TestProjectLinkedPullRequestsResponse -Nodes @($linkedPullRequestNode)
    $emptyLinkedPullRequestsNext = New-TestProjectLinkedPullRequestsResponse -Nodes @()
    $issueLabels = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes @()
    $emptyIssueLabelsNext = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes @()
    $issueComments = New-TestGraphQLConnectionResponse `
        -ParentName issue `
        -ConnectionName comments `
        -Nodes @([pscustomobject][ordered]@{
                id = 'issue-comment-1'
                body = $HandoffMarker
                createdAt = '2026-08-01T00:01:00Z'
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-1'
                author = [pscustomobject]@{ login = 'automation-agent' }
                authorAssociation = 'MEMBER'
                includesCreatedEdit = $false
            })
    $emptyIssueCommentsNext = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName comments -Nodes @()
    $pullRequestComments = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName comments -Nodes @()
    $emptyPullRequestCommentsNext = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName comments -Nodes @()
    $pullRequestReviews = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes @()
    $emptyPullRequestReviewsNext = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes @()
    New-TestFile -Path $fieldPath -Content (($fields | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $itemPath -Content (($items | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $updatedPath -Content (($updated | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $issuePath -Content (($issue | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $pullRequestPath -Content (($pullRequest | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $linkedPullRequestsPath -Content (($linkedPullRequests | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $linkedPullRequestsNextPath -Content (($emptyLinkedPullRequestsNext | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $issueLabelsPath -Content (($issueLabels | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $issueLabelsNextPath -Content (($emptyIssueLabelsNext | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $issueCommentsPath -Content (($issueComments | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $issueCommentsNextPath -Content (($emptyIssueCommentsNext | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $pullRequestCommentsPath -Content (($pullRequestComments | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $pullRequestCommentsNextPath -Content (($emptyPullRequestCommentsNext | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $pullRequestReviewsPath -Content (($pullRequestReviews | ConvertTo-Json -Depth 16 -Compress) + "`n")
    New-TestFile -Path $pullRequestReviewsNextPath -Content (($emptyPullRequestReviewsNext | ConvertTo-Json -Depth 16 -Compress) + "`n")

    $fakeLines = @(
        '$ErrorActionPreference = ''Stop''',
        '$arguments = @($args)',
        ('$logPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $logPath)),
        ('$mutationSentinel = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $mutationSentinel)),
        ('$fieldPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $fieldPath)),
        ('$itemPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $itemPath)),
        ('$updatedPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $updatedPath)),
        ('$issuePath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $issuePath)),
        ('$pullRequestPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $pullRequestPath)),
        ('$linkedPullRequestsPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $linkedPullRequestsPath)),
        ('$linkedPullRequestsNextPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $linkedPullRequestsNextPath)),
        ('$issueLabelsPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $issueLabelsPath)),
        ('$issueLabelsNextPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $issueLabelsNextPath)),
        ('$issueCommentsPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $issueCommentsPath)),
        ('$issueCommentsNextPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $issueCommentsNextPath)),
        ('$pullRequestCommentsPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $pullRequestCommentsPath)),
        ('$pullRequestCommentsNextPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $pullRequestCommentsNextPath)),
        ('$pullRequestReviewsPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $pullRequestReviewsPath)),
        ('$pullRequestReviewsNextPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $pullRequestReviewsNextPath)),
        '$joined = [string]::Join('' '', [string[]]$arguments)',
        '[System.IO.File]::AppendAllText($logPath, ($joined + [Environment]::NewLine))',
        '$isWrite = $joined -match ''(?i)(^|[ =:])mutation([ ({]|$)'' -or $joined -match ''(?i)^project item-edit '' -or $joined -match ''(?i)^(issue|pr) (create|edit|close|reopen|merge|ready) ''',
        'if ($isWrite) { [System.IO.File]::WriteAllText($mutationSentinel, $joined); [Console]::Error.WriteLine(''mutation forbidden''); exit 97 }',
        'if ($arguments.Count -ge 2 -and $arguments[0] -ceq ''project'' -and $arguments[1] -ceq ''field-list'') { [Console]::Out.Write([System.IO.File]::ReadAllText($fieldPath)); exit 0 }',
        'if ($arguments.Count -ge 2 -and $arguments[0] -ceq ''project'' -and $arguments[1] -ceq ''item-list'') { [Console]::Out.Write([System.IO.File]::ReadAllText($itemPath)); exit 0 }',
        'if ($arguments.Count -ge 2 -and $arguments[0] -ceq ''api'' -and $arguments[1] -ceq ''graphql'') {',
        '    if ($joined -match ''AutomationProjectItemLinkedPullRequests'') { if ($joined -match ''cursor=project-linked-prs-page-2'') { [Console]::Out.Write([System.IO.File]::ReadAllText($linkedPullRequestsNextPath)) } else { [Console]::Out.Write([System.IO.File]::ReadAllText($linkedPullRequestsPath)) }; exit 0 }',
        '    if ($joined -match ''AutomationIssueLabels'') { if ($joined -match ''cursor=issue-labels-page-2'') { [Console]::Out.Write([System.IO.File]::ReadAllText($issueLabelsNextPath)) } else { [Console]::Out.Write([System.IO.File]::ReadAllText($issueLabelsPath)) }; exit 0 }',
        '    if ($joined -match ''AutomationIssueComments'') { if ($joined -match ''cursor=issue-comments-page-2'') { [Console]::Out.Write([System.IO.File]::ReadAllText($issueCommentsNextPath)) } else { [Console]::Out.Write([System.IO.File]::ReadAllText($issueCommentsPath)) }; exit 0 }',
        '    if ($joined -match ''AutomationPullRequestComments'') { if ($joined -match ''cursor=pr-comments-page-2'') { [Console]::Out.Write([System.IO.File]::ReadAllText($pullRequestCommentsNextPath)) } else { [Console]::Out.Write([System.IO.File]::ReadAllText($pullRequestCommentsPath)) }; exit 0 }',
        '    if ($joined -match ''AutomationPullRequestReviews'') { if ($joined -match ''cursor=pr-reviews-page-2'') { [Console]::Out.Write([System.IO.File]::ReadAllText($pullRequestReviewsNextPath)) } else { [Console]::Out.Write([System.IO.File]::ReadAllText($pullRequestReviewsPath)) }; exit 0 }',
        '    [Console]::Out.Write([System.IO.File]::ReadAllText($updatedPath)); exit 0',
        '}',
        'if ($arguments.Count -ge 2 -and $arguments[0] -ceq ''issue'' -and $arguments[1] -ceq ''view'') { [Console]::Out.Write([System.IO.File]::ReadAllText($issuePath)); exit 0 }',
        'if ($arguments.Count -ge 2 -and $arguments[0] -ceq ''pr'' -and $arguments[1] -ceq ''view'') { [Console]::Out.Write([System.IO.File]::ReadAllText($pullRequestPath)); exit 0 }',
        '[Console]::Error.WriteLine((''unexpected fake gh invocation: '' + $joined))',
        'exit 98'
    )
    New-TestFile -Path $fakeScriptPath -Content ([string]::Join("`r`n", $fakeLines) + "`r`n")
    New-TestFile -Path $fakeCommandPath -Content ([string]::Join("`r`n", @(
                '@echo off',
                '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0gh.ps1" %*',
                'exit /b %ERRORLEVEL%'
            )) + "`r`n")

    return [pscustomobject][ordered]@{
        GitHubPath = $fakeCommandPath
        LogPath = $logPath
        MutationSentinelPath = $mutationSentinel
        LeaseDirectory = (Join-Path $Root 'SashimiBoyAutomation\DeveloperLeases')
        FieldPath = $fieldPath
        PullRequestPath = $pullRequestPath
        LinkedPullRequestsPath = $linkedPullRequestsPath
        LinkedPullRequestsNextPath = $linkedPullRequestsNextPath
        IssueLabelsPath = $issueLabelsPath
        IssueLabelsNextPath = $issueLabelsNextPath
        IssueCommentsPath = $issueCommentsPath
        IssueCommentsNextPath = $issueCommentsNextPath
        PullRequestCommentsPath = $pullRequestCommentsPath
        PullRequestCommentsNextPath = $pullRequestCommentsNextPath
        PullRequestReviewsPath = $pullRequestReviewsPath
        PullRequestReviewsNextPath = $pullRequestReviewsNextPath
    }
}

function New-DisposableDriftUnityFixture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Exact', 'NoFinalNewline', 'HeaderLikeContent', 'RelocatedApprovedField', 'ExtraField', 'SecondProjectSettingsFile', 'AssetsFile', 'PackagesFile')]
        [string]$Mode
    )

    $editorDirectory = Join-Path $Root ("$Mode\6000.4.0f1\Editor")
    $fakeUnityPath = Join-Path $editorDirectory 'Unity.cmd'
    $fakeUnityScriptPath = Join-Path $editorDirectory 'Unity.ps1'
    $postLines = @(
        'PlayerSettings:',
        '  targetPixelDensity: 30',
        '  fixtureGapAfterPixel: 0',
        '  buildNumber:',
        '    Standalone: 0',
        '    VisionOS: 0',
        '    iPhone: 0',
        '    tvOS: 0',
        '  fixtureGapAfterBuildNumber: 0',
        '  iOSTargetOSVersionString: 15.0',
        '  fixtureGapAfterIOS: 0',
        '  tvOSTargetOSVersionString: 15.0',
        '  fixtureGapAfterTvOS: 0',
        '  VisionOSTargetOSVersionString: 1.0',
        '  fixtureGapAfterVisionOS: 0',
        '  macOSTargetOSVersion: 12.0'
    )
    if ($Mode -eq 'ExtraField') {
        $postLines += '  scriptingBackend: 1'
    }
    if ($Mode -eq 'HeaderLikeContent') {
        $postLines += '++ b/ProjectSettings/ProjectSettings.asset'
    }
    if ($Mode -eq 'RelocatedApprovedField') {
        $postLines = @(
            'PlayerSettings:',
            '  fixtureGapAfterPixel: 0',
            '  targetPixelDensity: 30'
        ) + @($postLines | Select-Object -Skip 3)
    }
    $postContent = [string]::Join("`n", $postLines)
    if ($Mode -ne 'NoFinalNewline') {
        $postContent += "`n"
    }
    $scriptLines = @(
        '$arguments = @($args)',
        '$projectPath = $null',
        '$logFile = $null',
        '$resultFile = $null',
        'for ($index = 0; $index -lt $arguments.Count; $index++) {',
        '    if ($arguments[$index] -ieq ''-projectPath'') { $projectPath = [string]$arguments[++$index]; continue }',
        '    if ($arguments[$index] -ieq ''-logFile'') { $logFile = [string]$arguments[++$index]; continue }',
        '    if ($arguments[$index] -ieq ''-testResults'') { $resultFile = [string]$arguments[++$index]; continue }',
        '}',
        'if ([string]::IsNullOrWhiteSpace($projectPath) -or [string]::IsNullOrWhiteSpace($logFile)) { exit 64 }',
        ('$mutationContent = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $postContent)),
        '$utf8NoBom = New-Object System.Text.UTF8Encoding($false)',
        '[System.IO.File]::WriteAllText((Join-Path $projectPath ''ProjectSettings\ProjectSettings.asset''), $mutationContent, $utf8NoBom)'
    )
    switch ($Mode) {
        'SecondProjectSettingsFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''ProjectSettings\Unexpected.asset''), ''unexpected'', $utf8NoBom)'
        }
        'AssetsFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''Assets\Unexpected.txt''), ''unexpected'', $utf8NoBom)'
        }
        'PackagesFile' {
            $scriptLines += '[System.IO.File]::WriteAllText((Join-Path $projectPath ''Packages\Unexpected.json''), ''{}'', $utf8NoBom)'
        }
    }
    $scriptLines += @(
        'if ($resultFile) {',
        '    [System.IO.File]::WriteAllText($logFile, "Running tests for ExecutionSettings with details:`r`nTest run completed. Exiting with code 0`r`n", $utf8NoBom)',
        '    [System.IO.File]::WriteAllText($resultFile, ''<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" />'', $utf8NoBom)',
        '}',
        'else {',
        '    [System.IO.File]::WriteAllText($logFile, "Clean compile/import fixture`r`n", $utf8NoBom)',
        '}',
        'exit 0'
    )
    New-TestFile -Path $fakeUnityScriptPath -Content ([string]::Join("`r`n", $scriptLines) + "`r`n")
    $cmdLines = @(
        '@echo off',
        '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Unity.ps1" %*',
        'exit /b %ERRORLEVEL%'
    )
    New-TestFile -Path $fakeUnityPath -Content ([string]::Join("`r`n", $cmdLines) + "`r`n")
    return $fakeUnityPath
}

function Remove-OwnedTestRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$AutomationRoot,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9a-fA-F]{32}$')]
        [string]$ExpectedRunId,

        [Parameter(Mandatory = $true)]
        [bool]$MarkerWasWritten
    )

    Assert-AutomationPathHasNoReparsePoint -Path $AutomationRoot
    Assert-AutomationPathHasNoReparsePoint -Path $Path
    $normalizedAutomationRoot = ConvertTo-AutomationLexicalPath -Path $AutomationRoot
    $normalizedPath = ConvertTo-AutomationLexicalPath -Path $Path
    $expectedPath = Join-Path -Path $normalizedAutomationRoot -ChildPath ('script-tests-' + $ExpectedRunId.ToLowerInvariant())
    $markerPath = Join-Path -Path $normalizedPath -ChildPath '.automation-script-tests-owner'
    if (-not (Test-AutomationPathEqual -Left $normalizedPath -Right $expectedPath) -or
        -not (Test-AutomationPathWithin -Path $normalizedPath -Root $normalizedAutomationRoot) -or
        (Test-AutomationPathEqual -Left $normalizedPath -Right $normalizedAutomationRoot)) {
        throw "Refusing to remove unowned test path: $normalizedPath"
    }

    if (-not (Test-Path -LiteralPath $normalizedPath)) {
        return
    }

    if (-not (Test-Path -LiteralPath $normalizedPath -PathType Container)) {
        throw "Owned test root is not a directory: $normalizedPath"
    }

    Assert-AutomationTreeHasNoReparsePoint -Root $normalizedPath
    if (-not $MarkerWasWritten) {
        $unexpectedEntries = @(Get-ChildItem -LiteralPath $normalizedPath -Force -ErrorAction Stop |
            Where-Object { -not (Test-AutomationPathEqual -Left $_.FullName -Right $markerPath) })
        if ($unexpectedEntries.Count -gt 0) {
            throw "Refusing marker-failure cleanup because the root contains an unexpected entry: $($unexpectedEntries[0].FullName)"
        }
        if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
            (Get-Item -LiteralPath $markerPath -Force).Attributes = [System.IO.FileAttributes]::Normal
            [System.IO.File]::Delete($markerPath)
        }
        [System.IO.Directory]::Delete($normalizedPath, $false)
        return
    }
    else {
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            throw "Owned test marker disappeared before cleanup: $markerPath"
        }
        try {
            $marker = Get-Content -Raw -LiteralPath $markerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Owned test marker is invalid: $($_.Exception.Message)"
        }
        if ([int]$marker.SchemaVersion -ne 1 -or
            -not [string]::Equals([string]$marker.RunId, $ExpectedRunId, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-AutomationPathEqual -Left ([string]$marker.WorkspaceRoot) -Right $normalizedPath) -or
            [System.IO.Path]::GetFileName([string]$marker.Script) -cne 'Test-AutomationScripts.ps1') {
            throw "Owned test marker does not match this run: $markerPath"
        }
    }

    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $directories = New-Object 'System.Collections.Generic.List[string]'
    $pending.Push($normalizedPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $directoryItem = Get-Item -LiteralPath $directory -Force -ErrorAction Stop
        if (-not $directoryItem.PSIsContainer -or
            ($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing cleanup because a queued test directory changed type: $directory"
        }
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
    $projectSettingsFixture = @(
        'PlayerSettings:',
        '  targetPixelDensity: 0',
        '  fixtureGapAfterPixel: 0',
        '  buildNumber: {}',
        '  fixtureGapAfterBuildNumber: 0',
        '  iOSTargetOSVersionString: ',
        '  fixtureGapAfterIOS: 0',
        '  tvOSTargetOSVersionString: ',
        '  fixtureGapAfterTvOS: 0',
        '  VisionOSTargetOSVersionString: ',
        '  fixtureGapAfterVisionOS: 0',
        '  macOSTargetOSVersion: '
    )
    New-TestFile -Path (Join-Path $basePath 'ProjectSettings\ProjectSettings.asset') -Content ([string]::Join("`n", $projectSettingsFixture) + "`n")
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
        'if defined resultFile echo Running tests for ExecutionSettings with details:>>"!logFile!"',
        'if defined resultFile echo LogAssert.Expect matched the expected negative-path error.>>"!logFile!"',
        'if defined resultFile echo UnityEngine.Debug:LogError ^(object^)>>"!logFile!"',
        'if defined resultFile echo Test run completed. Exiting with code 0 >>"!logFile!"',
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
$automationTempRoot = $null
$script:testRunId = [Guid]::NewGuid().ToString('N')

try {
    $automationTempRootInput = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation'
    # This must precede the first write. It inspects the lexical path and every
    # existing ancestor without resolving a junction to its target.
    Assert-AutomationPathHasNoReparsePoint -Path $automationTempRootInput
    $automationTempRoot = ConvertTo-AutomationLexicalPath -Path $automationTempRootInput
    $testRootCandidate = Join-Path -Path $automationTempRoot -ChildPath ('script-tests-' + $script:testRunId)
    Assert-AutomationPathHasNoReparsePoint -Path $testRootCandidate
    if (-not (Test-Path -LiteralPath $automationTempRoot)) {
        New-Item -ItemType Directory -Path $automationTempRoot -ErrorAction Stop | Out-Null
    }
    Assert-AutomationPathHasNoReparsePoint -Path $automationTempRoot

    if (Test-Path -LiteralPath $testRootCandidate) {
        throw "Refusing to reuse a pre-existing test root: $testRootCandidate"
    }
    Assert-AutomationPathHasNoReparsePoint -Path $testRootCandidate
    New-Item -ItemType Directory -Path $testRootCandidate -ErrorAction Stop | Out-Null
    $script:temporaryRoot = ConvertTo-AutomationLexicalPath -Path $testRootCandidate
    $script:ownedTestRootCreated = $true
    Assert-AutomationPathHasNoReparsePoint -Path $script:temporaryRoot

    if ($InternalLifecycleFailure -eq 'MarkerWrite') {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:temporaryRoot '.automation-script-tests-owner'),
            '{"partial":',
            (New-Object System.Text.UTF8Encoding($false)))
        throw 'Injected ownership-marker write failure.'
    }
    $markerData = [ordered]@{
        SchemaVersion = 1
        RunId          = $script:testRunId
        WorkspaceRoot  = $script:temporaryRoot
        Script         = $PSCommandPath
    }
    $testMarkerPath = Join-Path $script:temporaryRoot '.automation-script-tests-owner'
    New-TestFile -Path $testMarkerPath -Content ((ConvertTo-AutomationJson -InputObject $markerData) + "`n")
    $writtenMarker = Get-Content -Raw -LiteralPath $testMarkerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ([int]$writtenMarker.SchemaVersion -ne 1 -or
        -not [string]::Equals([string]$writtenMarker.RunId, $script:testRunId, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-AutomationPathEqual -Left ([string]$writtenMarker.WorkspaceRoot) -Right $script:temporaryRoot) -or
        [System.IO.Path]::GetFileName([string]$writtenMarker.Script) -cne 'Test-AutomationScripts.ps1') {
        throw 'Ownership marker readback did not match this smoke run.'
    }
    $script:ownerMarkerWritten = $true

    if ($InternalLifecycleFailure -eq 'ChildExitOne') {
        & $WindowsPowerShellPath -NoProfile -NonInteractive -Command 'exit 1'
        $injectedChildExitCode = $LASTEXITCODE
        if ($injectedChildExitCode -ne 1) {
            throw "Injected child returned unexpected exit code $injectedChildExitCode."
        }
        throw 'Injected child process exit 1.'
    }
    if ($InternalLifecycleFailure -eq 'Assertion') {
        Assert-AutomationTest -Condition $false -Message 'Injected assertion failure.'
    }

    if ($InternalLifecycleFailure -eq 'RecordedAssertion') {
        Invoke-AutomationTestCase -Name 'InjectedRecordedAssertion' -Body {
            Assert-AutomationTest -Condition $false -Message 'Injected recorded assertion failure.'
        }
    }
    else {
    Invoke-AutomationTestCase -Name 'RequiredFilesExist' -Body {
        $requiredFiles = @(
            'AGENTS.md',
            'Docs\Automation\SPEC_VERSION',
            'Docs\Automation\WORKFLOW.md',
            'Docs\Automation\DEVELOPER.md',
            'Docs\Automation\REVIEWER.md',
            'Docs\Automation\BOOTSTRAP.md',
            'Tools\Automation\Automation.Common.ps1',
            'Tools\Automation\Automation.Handoff.ps1',
            'Tools\Automation\Get-DeveloperWorkItem.ps1',
            'Tools\Automation\Invoke-AutomationPreflight.ps1',
            'Tools\Automation\New-AutomationHandoff.ps1',
            'Tools\Automation\New-AutomationHandoffCompletion.ps1',
            'Tools\Automation\New-AutomationOwnerQueueDecision.ps1',
            'Tools\Automation\Use-DeveloperLease.ps1',
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

    Invoke-AutomationTestCase -Name 'SmokeRunnerRejectsJunctionBeforeFirstWrite' -Body {
        $probeTemp = Join-Path $script:temporaryRoot 'runner junction probe temp'
        $junctionTarget = Join-Path $script:temporaryRoot 'runner junction probe target'
        $junctionRoot = Join-Path $probeTemp 'SashimiBoyAutomation'
        New-Item -ItemType Directory -Path $probeTemp -ErrorAction Stop | Out-Null
        New-Item -ItemType Directory -Path $junctionTarget -ErrorAction Stop | Out-Null
        $sentinel = Join-Path $junctionTarget 'target-sentinel.txt'
        New-TestFile -Path $sentinel -Content "must survive`n"
        New-Item -ItemType Junction -Path $junctionRoot -Target $junctionTarget -ErrorAction Stop | Out-Null
        try {
            $probe = Invoke-SmokeRunnerLifecycleProbe -TemporaryPath $probeTemp -Failure Assertion
            Assert-AutomationTest -Condition ($probe.ExitCode -ne 0) -Message 'Smoke runner accepted a junction canonical temp root.'
            Assert-AutomationTest -Condition ($probe.Output -match 'Reparse points are not allowed') -Message "Smoke runner junction rejection was not explicit: $($probe.Output)"
            $targetEntries = @(Get-ChildItem -LiteralPath $junctionTarget -Force -ErrorAction Stop)
            Assert-AutomationTest -Condition ($targetEntries.Count -eq 1 -and (Test-AutomationPathEqual -Left $targetEntries[0].FullName -Right $sentinel)) -Message 'Smoke runner wrote fixture data through the junction before validation.'
        }
        finally {
            if (Test-Path -LiteralPath $junctionRoot) {
                [System.IO.Directory]::Delete($junctionRoot, $false)
            }
        }
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinel -PathType Leaf) -Message 'Junction rejection modified the external target sentinel.'
    }

    Invoke-AutomationTestCase -Name 'SmokeRunnerCleansEveryInjectedFailure' -Body {
        foreach ($failure in @('MarkerWrite', 'ChildExitOne', 'Assertion', 'RecordedAssertion')) {
            foreach ($keepRequested in @($false, $true)) {
                $probeTemp = Join-Path $script:temporaryRoot ('runner lifecycle ' + $failure + ' keep-' + $keepRequested)
                New-Item -ItemType Directory -Path $probeTemp -ErrorAction Stop | Out-Null
                $probe = Invoke-SmokeRunnerLifecycleProbe `
                    -TemporaryPath $probeTemp `
                    -Failure $failure `
                    -KeepTemporaryFiles:$keepRequested
                Assert-AutomationTest -Condition ($probe.ExitCode -ne 0) -Message "Injected runner failure unexpectedly succeeded: $failure (KeepTemporaryFiles=$keepRequested)"
                $automationRoot = Join-Path $probeTemp 'SashimiBoyAutomation'
                $ownedRootRemainders = @(
                    if (Test-Path -LiteralPath $automationRoot -PathType Container) {
                        Get-ChildItem -LiteralPath $automationRoot -Directory -Filter 'script-tests-*' -Force -ErrorAction Stop
                    }
                )
                Assert-AutomationTest -Condition ($ownedRootRemainders.Count -eq 0) -Message "Injected $failure left an owned test root behind when KeepTemporaryFiles=$keepRequested."
            }
        }
    }

    Invoke-AutomationTestCase -Name 'SmokeRunnerNeverDeletesUnownedPath' -Body {
        $localAutomationRoot = Join-Path $script:temporaryRoot 'nonowned cleanup boundary'
        $actualRunId = [Guid]::NewGuid().ToString('N')
        $differentRunId = [Guid]::NewGuid().ToString('N')
        $nonOwnedPath = Join-Path $localAutomationRoot ('script-tests-' + $actualRunId)
        $sentinel = Join-Path $nonOwnedPath 'nonowned-sentinel.txt'
        New-TestFile -Path $sentinel -Content "must survive refusal`n"
        $rejected = $false
        try {
            Remove-OwnedTestRoot `
                -Path $nonOwnedPath `
                -AutomationRoot $localAutomationRoot `
                -ExpectedRunId $differentRunId `
                -MarkerWasWritten $false
        }
        catch {
            $rejected = $_.Exception.Message -match 'unowned test path'
        }
        Assert-AutomationTest -Condition $rejected -Message 'Smoke runner cleanup accepted an unrecorded path.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinel -PathType Leaf) -Message 'Smoke runner cleanup deleted an unowned path.'
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
        $preflightContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Invoke-AutomationPreflight.ps1'))
        Assert-AutomationTest -Condition ($preflightContent -notmatch 'SkipUnityProcessCheck') -Message 'Production preflight still exposes SkipUnityProcessCheck.'
        $unityContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'))
        Assert-AutomationTest -Condition ($unityContent -notmatch 'AllowSkipped') -Message 'Unity wrapper still exposes an AllowSkipped bypass.'
        Assert-AutomationTest -Condition ($unityContent -notmatch '\$ProtectedProjectPath\b') -Message 'Unity wrapper still exposes a public protected-worktree override.'
        Assert-AutomationTest -Condition ($unityContent -match "ValidatedUnityVersion, '6000\.4\.0f1'") -Message 'Known drift is not bound to literal Unity 6000.4.0f1.'
        Assert-AutomationTest -Condition ($unityContent -match '\$protectedCheckoutPaths' -and $unityContent -match '\$persistentEvidencePaths') -Message 'Immutable overlap protection and test-injectable evidence paths are not separated.'
        Assert-AutomationTest -Condition (@([regex]::Matches($unityContent, "'-buildTarget', 'StandaloneWindows64'")).Count -eq 3) -Message 'Unity wrapper must use StandaloneWindows64 for compile, EditMode, and PlayMode.'
        $commonContent = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Automation.Common.ps1'))
        Assert-AutomationTest -Condition (@([regex]::Matches($commonContent, '(?is)Remove-Item[^\r\n]*-Recurse')).Count -eq 1) -Message 'The vetted common helper must contain the sole production recursive delete.'
        Assert-AutomationTest -Condition ($commonContent -match 'Assert-AutomationTreeHasNoReparsePoint') -Message 'Recursive cleanup lacks a full-tree reparse-point guard.'
    }

    Invoke-AutomationTestCase -Name 'SourceOfTruthHierarchyAndStaleCommentPolicyAreExact' -Body {
        $documents = [ordered]@{
            'AGENTS.md'                      = 1
            'Docs\Automation\WORKFLOW.md'  = 1
            'Docs\Automation\DEVELOPER.md' = 1
            'Docs\Automation\REVIEWER.md'  = 1
            'Docs\Automation\BOOTSTRAP.md' = 2
        }
        $rankPatterns = @(
            '^\s*1\.\s+the current Issue''s latest Owner Decision\s*$',
            '^\s*2\.\s+the current Issue''s latest body and Acceptance Criteria\s*$',
            '^\s*3\.\s+repository-wide safety rules',
            '^\s*4\.\s+the role-specific',
            '^\s*5\.\s+the latest independent Review finding on the linked PR\s*$',
            '^\s*6\.\s+older Issue/PR comments and older Reviews\s*$',
            '^\s*7\.\s+previous chat summaries and Automation memory\s*$'
        )
        $nonRelaxablePatterns = @(
            "destroy the user's checkout",
            'reset --hard.*git clean.*force push',
            'never merge a PR.*move an Issue to.*Done',
            '(?:exactly )?one Issue per run|One run handles exactly one Issue',
            'never report an unexecuted test as PASS'
        )

        foreach ($documentEntry in $documents.GetEnumerator()) {
            $relativePath = [string]$documentEntry.Key
            $expectedBlockCount = [int]$documentEntry.Value
            $content = [System.IO.File]::ReadAllText((Join-Path $repository $relativePath))
            $lines = @($content -split "`r?`n")
            $blockStarts = @()
            for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
                if ($lines[$lineIndex] -match $rankPatterns[0]) {
                    $blockStarts += [int]$lineIndex
                }
            }
            Assert-AutomationTest -Condition ($blockStarts.Count -eq $expectedBlockCount) -Message "$relativePath must contain exactly $expectedBlockCount Source of Truth block(s)."

            for ($blockIndex = 0; $blockIndex -lt $blockStarts.Count; $blockIndex++) {
                $start = $blockStarts[$blockIndex]
                $end = if ($blockIndex + 1 -lt $blockStarts.Count) { $blockStarts[$blockIndex + 1] - 1 } else { $lines.Count - 1 }
                $cursor = $start - 1
                foreach ($rankPattern in $rankPatterns) {
                    $rankMatches = @()
                    for ($candidateIndex = $start; $candidateIndex -le $end; $candidateIndex++) {
                        if ($lines[$candidateIndex] -match $rankPattern) {
                            $rankMatches += [int]$candidateIndex
                        }
                    }
                    Assert-AutomationTest -Condition ($rankMatches.Count -eq 1) -Message "$relativePath Source of Truth block has a missing or duplicate rank: $rankPattern"
                    Assert-AutomationTest -Condition ($rankMatches[0] -gt $cursor) -Message "$relativePath Source of Truth ranks are out of order."
                    $cursor = $rankMatches[0]
                }

                $segment = [string]::Join(' ', [string[]]$lines[$start..$end]) -replace '\s+', ' '
                Assert-AutomationTest -Condition ($segment -match 'Only the latest Owner Decision and (?:the )?current Acceptance Criteria are authoritative comments') -Message "$relativePath does not limit authoritative comments to the latest Owner Decision/current Acceptance Criteria."
                Assert-AutomationTest -Condition ($segment -match 'Older or non-Owner general comments.*(?:yield|conflict)') -Message "$relativePath does not make stale/non-Owner comments yield on conflict."
                foreach ($prohibitionPattern in $nonRelaxablePatterns) {
                    Assert-AutomationTest -Condition ($segment -match $prohibitionPattern) -Message "$relativePath Source of Truth entry point can relax a required safety prohibition: $prohibitionPattern"
                }
            }
        }
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
        Assert-AutomationTest -Condition ($bootstrap -match '(?s)Developer bootstrap.*Never\s+merge\s+a\s+PR\s+or\s+move\s+an\s+Issue\s+to\s+Verification\s+or\s+Done') -Message 'Developer bootstrap exceeds its state authority.'
        Assert-AutomationTest -Condition ($bootstrap -match '(?s)Reviewer bootstrap.*Never push an\s+integration result.*move an Issue to Done') -Message 'Reviewer bootstrap exceeds its state authority.'
        Assert-AutomationTest -Condition ($statusScript -match "'Ready'\s*=\s*@\('In Progress'\)") -Message 'Status script Developer Ready transition differs from the canonical machine.'
        Assert-AutomationTest -Condition ($statusScript -match "'In Progress'\s*=\s*@\('Review'\)") -Message 'Status script Developer Review transition differs from the canonical machine.'
        Assert-AutomationTest -Condition ($statusScript -match "'Review'\s*=\s*@\('In Progress', 'Verification'\)") -Message 'Status script Reviewer transitions differ from the canonical machine.'
    }

    Invoke-AutomationTestCase -Name 'HandoffMarkersRoundTripAndRejectUnsafeValues' -Body {
        $head = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        $findingUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/602#issuecomment-1'
        $marker = New-AutomationHandoffMarker `
            -Mode ReviewFix `
            -IssueNumber 502 `
            -PullRequestNumber 602 `
            -HeadSha $head `
            -SourceRole Reviewer `
            -Reason review-major `
            -FindingUrl $findingUrl
        $parsed = @(Get-AutomationHandoffMarkers -Content (" `r`n$marker`r`n "))
        Assert-AutomationTest -Condition ($parsed.Count -eq 1) -Message 'Handoff parser did not return exactly one marker.'
        Assert-AutomationTest -Condition ([string]$parsed[0].Mode -ceq 'ReviewFix') -Message 'Handoff mode changed during round-trip.'
        Assert-AutomationTest -Condition ([int]$parsed[0].IssueNumber -eq 502 -and [int]$parsed[0].PullRequestNumber -eq 602) -Message 'Handoff target changed during round-trip.'
        Assert-AutomationTest -Condition ([string]$parsed[0].FindingUrl -ceq $findingUrl) -Message 'Handoff finding URL changed during round-trip.'
        Assert-AutomationTest -Condition (@(Get-AutomationHandoffMarkers -Content "Documentation mentions sashimi-boy-automation-handoff:v1 only.").Count -eq 0) -Message 'A prose marker-name mention was parsed as evidence.'
        $fencedMarkerExample = '```md' + "`n" + $marker + "`n" + '```'
        Assert-AutomationTest -Condition (@(Get-AutomationHandoffMarkers -Content $fencedMarkerExample).Count -eq 0) -Message 'A fenced marker example was parsed as evidence.'

        $completion = New-AutomationHandoffCompletionMarker `
            -IssueNumber 502 `
            -PullRequestNumber 602 `
            -HeadSha $head `
            -SourceRole Developer `
            -HandoffUrl $findingUrl
        $parsedCompletion = @(Get-AutomationHandoffCompletionMarkers -Content $completion)
        Assert-AutomationTest -Condition ($parsedCompletion.Count -eq 1 -and [string]$parsedCompletion[0].HandoffUrl -ceq $findingUrl) -Message 'Handoff completion did not round-trip.'

        $ownerDecision = New-AutomationOwnerQueueDecisionMarker -IssueNumber 502 -Queue unblock -Reason resolved
        $parsedOwnerDecision = @(Get-AutomationOwnerQueueDecisionMarkers -Content $ownerDecision)
        Assert-AutomationTest -Condition ($parsedOwnerDecision.Count -eq 1 -and [string]$parsedOwnerDecision[0].Queue -ceq 'unblock' -and [string]$parsedOwnerDecision[0].Reason -ceq 'resolved') -Message 'Owner queue decision did not round-trip.'

        $ownerWriter = Invoke-AutomationChildScript `
            -ScriptPath (Join-Path $repository 'Tools\Automation\New-AutomationOwnerQueueDecision.ps1') `
            -WorkingDirectory $repository `
            -Parameters @{ IssueNumber = 502; Queue = 'block'; Reason = 'product-decision' }
        Assert-AutomationTest -Condition ($ownerWriter.ExitCode -eq 0 -and $ownerWriter.Output -match 'sashimi-boy-automation-owner-decision:v1') -Message "Owner decision formatter failed under Windows PowerShell: $($ownerWriter.Output)"

        $writer = Invoke-AutomationChildScript `
            -ScriptPath (Join-Path $repository 'Tools\Automation\New-AutomationHandoff.ps1') `
            -WorkingDirectory $repository `
            -Parameters @{
                Mode              = 'ReviewFix'
                IssueNumber       = 502
                PullRequestNumber = 602
                HeadSha           = $head
                SourceRole        = 'Reviewer'
                Reason            = 'review-blocker'
                FindingUrl        = $findingUrl
            }
        Assert-AutomationTest -Condition ($writer.ExitCode -eq 0 -and $writer.Output -match 'sashimi-boy-automation-handoff:v1') -Message "Handoff formatter failed under Windows PowerShell: $($writer.Output)"

        $caseRejected = $false
        try {
            New-AutomationHandoffMarker -Mode reviewfix -IssueNumber 502 -PullRequestNumber 602 -HeadSha $head -SourceRole Reviewer -Reason review-major | Out-Null
        }
        catch {
            $caseRejected = $_.Exception.Message -match 'canonical casing'
        }
        Assert-AutomationTest -Condition $caseRejected -Message 'Handoff formatter accepted non-canonical mode casing.'

        $terminatorRejected = $false
        try {
            New-AutomationHandoffMarker `
                -Mode DeliveryResume `
                -IssueNumber 502 `
                -PullRequestNumber 602 `
                -HeadSha $head `
                -SourceRole Developer `
                -Reason network `
                -PendingCommand 'invoke-check --> hidden' | Out-Null
        }
        catch {
            $terminatorRejected = $_.Exception.Message -match 'safe single-line'
        }
        Assert-AutomationTest -Condition $terminatorRejected -Message 'Handoff formatter accepted an HTML-comment terminator.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorPrefersReviewFixAndPreservesResumeContract' -Body {
        $head = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        $handoffUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/610#issuecomment-10'
        $marker = New-AutomationHandoffMarker `
            -Mode ReviewFix `
            -IssueNumber 510 `
            -PullRequestNumber 610 `
            -HeadSha $head `
            -SourceRole Reviewer `
            -Reason review-major `
            -FindingUrl $handoffUrl
        $resume = New-TestDeveloperCandidate `
            -IssueNumber 510 `
            -Status 'In Progress' `
            -Priority P1 `
            -UpdatedAt '2026-01-02T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 610 -HeadSha $head -HeadRef 'codex/existing-review-fix')) `
            -Sources @((New-TestAutomationSource -Body $marker -Timestamp '2026-01-02T00:01:00Z' -Url $handoffUrl))
        $ready = New-TestDeveloperCandidate -IssueNumber 511 -Status Ready -Priority P0 -UpdatedAt '2025-01-01T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($ready, $resume) -Name 'review-fix-priority'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "ReviewFix selector fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([bool]$json.Succeeded -and [bool]$json.Selected) -Message 'ReviewFix selector did not report a selection.'
        Assert-AutomationTest -Condition ([string]$json.Mode -ceq 'ReviewFix' -and [int]$json.IssueNumber -eq 510) -Message 'ReviewFix did not outrank Ready NewWork.'
        Assert-AutomationTest -Condition ([string]$json.PullRequestHeadSha -ceq $head -and [string]$json.PullRequestHeadRef -ceq 'codex/existing-review-fix') -Message 'Live resume head/ref were not preserved.'
        Assert-AutomationTest -Condition ([string]$json.ResumePushRefSpec -ceq 'HEAD:codex/existing-review-fix') -Message 'Resume push refspec did not target the existing PR head ref.'
        Assert-AutomationTest -Condition (-not [bool]$json.MayCreateIssue -and -not [bool]$json.MayCreatePullRequest -and -not [bool]$json.MayCreateRemoteBranch) -Message 'ReviewFix allowed a new Issue, PR, or remote branch.'
        Assert-AutomationTest -Condition ([int]$json.MaximumIssuesThisRun -eq 1 -and [string]$json.AllowedCompletionStatus -ceq 'Review') -Message 'ReviewFix output broadened run or transition authority.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorAcceptsOwnerFailReviewFix' -Body {
        $head = 'cccccccccccccccccccccccccccccccccccccccc'
        $handoffUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/520#issuecomment-20'
        $findingUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/620#issuecomment-21'
        $marker = New-AutomationHandoffMarker `
            -Mode ReviewFix `
            -IssueNumber 520 `
            -PullRequestNumber 620 `
            -HeadSha $head `
            -SourceRole Owner `
            -Reason owner-verification-fail `
            -FindingUrl $findingUrl
        $candidate = New-TestDeveloperCandidate `
            -IssueNumber 520 `
            -Status 'In Progress' `
            -Priority P1 `
            -UpdatedAt '2026-01-03T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 620 -HeadSha $head -HeadRef 'codex/existing-owner-fail')) `
            -Sources @((New-TestAutomationSource -Body $marker -Timestamp '2026-01-03T00:01:00Z' -Url $handoffUrl -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER))
        $selector = Invoke-TestDeveloperSelector -Candidates @($candidate) -Name 'owner-review-fix'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Owner ReviewFix fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([string]$json.Mode -ceq 'ReviewFix' -and [string]$json.Reason -ceq 'owner-verification-fail') -Message 'Owner manual FAIL was not selected as ReviewFix.'
        Assert-AutomationTest -Condition ([string]$json.LatestHandoffUrl -ceq $handoffUrl -and [string]$json.FindingUrl -ceq $findingUrl) -Message 'Owner handoff evidence URLs were not preserved.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorPrefersDeliveryResumeAndAllowsValidationOnly' -Body {
        $head = 'dddddddddddddddddddddddddddddddddddddddd'
        $handoffUrl = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/630#issuecomment-30'
        $pendingCommand = "& 'C:\Tools\Run Check.ps1' -Fresh"
        $marker = New-AutomationHandoffMarker `
            -Mode DeliveryResume `
            -IssueNumber 530 `
            -PullRequestNumber 630 `
            -HeadSha $head `
            -SourceRole Developer `
            -Reason runner-failure `
            -PendingCommand $pendingCommand
        $resume = New-TestDeveloperCandidate `
            -IssueNumber 530 `
            -Status 'In Progress' `
            -Priority P2 `
            -UpdatedAt '2026-01-04T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 630 -HeadSha $head -HeadRef 'codex/existing-delivery')) `
            -Sources @((New-TestAutomationSource -Body $marker -Timestamp '2026-01-04T00:01:00Z' -Url $handoffUrl))
        $ready = New-TestDeveloperCandidate -IssueNumber 531 -Status Ready -Priority P0 -UpdatedAt '2025-01-01T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($ready, $resume) -Name 'delivery-priority'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "DeliveryResume fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([string]$json.Mode -ceq 'DeliveryResume' -and [int]$json.IssueNumber -eq 530) -Message 'DeliveryResume did not outrank Ready NewWork.'
        Assert-AutomationTest -Condition ([string]$json.PendingCommand -ceq $pendingCommand) -Message 'DeliveryResume pending command was not preserved exactly.'
        Assert-AutomationTest -Condition ([bool]$json.ValidationOnlyAllowed) -Message 'DeliveryResume did not allow validation-only completion.'
        Assert-AutomationTest -Condition (-not [bool]$json.MayCreatePullRequest -and -not [bool]$json.MayCreateRemoteBranch) -Message 'DeliveryResume allowed new delivery state.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorUsesExactBandsAndOldestProjectUpdate' -Body {
        $headA = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
        $headB = 'ffffffffffffffffffffffffffffffffffffffff'
        $markerA = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 540 -PullRequestNumber 640 -HeadSha $headA -SourceRole Reviewer -Reason review-blocker -FindingUrl 'https://example.invalid/finding/540'
        $markerB = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 541 -PullRequestNumber 641 -HeadSha $headB -SourceRole Reviewer -Reason review-blocker -FindingUrl 'https://example.invalid/finding/541'
        $p0Newer = New-TestDeveloperCandidate `
            -IssueNumber 540 -Status 'In Progress' -Priority P0 -UpdatedAt '2026-02-02T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 640 -HeadSha $headA -HeadRef 'codex/p0-review')) `
            -Sources @((New-TestAutomationSource -Body $markerA -Timestamp '2026-02-02T00:01:00Z' -Url 'https://example.invalid/handoff/540'))
        $p1Older = New-TestDeveloperCandidate `
            -IssueNumber 541 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-02-01T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 641 -HeadSha $headB -HeadRef 'codex/p1-review')) `
            -Sources @((New-TestAutomationSource -Body $markerB -Timestamp '2026-02-01T00:01:00Z' -Url 'https://example.invalid/handoff/541'))
        $selector = Invoke-TestDeveloperSelector -Candidates @($p0Newer, $p1Older) -Name 'same-band-oldest'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Same-band fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 541) -Message 'P0 incorrectly outranked an older P1 inside the shared band.'

        $deliveryHead = '1111111111111111111111111111111111111111'
        $deliveryMarker = New-AutomationHandoffMarker `
            -Mode DeliveryResume -IssueNumber 542 -PullRequestNumber 642 -HeadSha $deliveryHead `
            -SourceRole Developer -Reason network -PendingCommand 'Invoke-NetworkValidation'
        $p2Delivery = New-TestDeveloperCandidate `
            -IssueNumber 542 -Status 'In Progress' -Priority P2 -UpdatedAt '2026-02-03T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 642 -HeadSha $deliveryHead -HeadRef 'codex/p2-delivery')) `
            -Sources @((New-TestAutomationSource -Body $deliveryMarker -Timestamp '2026-02-03T00:01:00Z' -Url 'https://example.invalid/handoff/542'))
        $p0Ready = New-TestDeveloperCandidate -IssueNumber 543 -Status Ready -Priority P0 -UpdatedAt '2025-01-01T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($p0Ready, $p2Delivery) -Name 'cross-band-order'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Cross-band fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 542 -and [string]$json.Mode -ceq 'DeliveryResume') -Message 'P2/P3 DeliveryResume did not outrank P0/P1 NewWork.'

        $bandHeads = @(
            'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
            'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
            'cccccccccccccccccccccccccccccccccccccccc',
            'dddddddddddddddddddddddddddddddddddddddd'
        )
        $bandMarkers = @(
            (New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 544 -PullRequestNumber 644 -HeadSha $bandHeads[0] -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/544'),
            (New-AutomationHandoffMarker -Mode DeliveryResume -IssueNumber 545 -PullRequestNumber 645 -HeadSha $bandHeads[1] -SourceRole Developer -Reason network -PendingCommand 'Invoke-BandTwo'),
            (New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 546 -PullRequestNumber 646 -HeadSha $bandHeads[2] -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/546'),
            (New-AutomationHandoffMarker -Mode DeliveryResume -IssueNumber 547 -PullRequestNumber 647 -HeadSha $bandHeads[3] -SourceRole Developer -Reason network -PendingCommand 'Invoke-BandFour')
        )
        $bandCandidates = @(
            (New-TestDeveloperCandidate -IssueNumber 544 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-06-06T00:00:00Z' -PullRequests @((New-TestDeveloperPullRequest -Number 644 -HeadSha $bandHeads[0] -HeadRef 'codex/band-one')) -Sources @((New-TestAutomationSource -Body $bandMarkers[0] -Timestamp '2026-06-06T00:01:00Z' -Url 'https://example.invalid/handoff/544'))),
            (New-TestDeveloperCandidate -IssueNumber 545 -Status 'In Progress' -Priority P0 -UpdatedAt '2020-01-01T00:00:00Z' -PullRequests @((New-TestDeveloperPullRequest -Number 645 -HeadSha $bandHeads[1] -HeadRef 'codex/band-two')) -Sources @((New-TestAutomationSource -Body $bandMarkers[1] -Timestamp '2020-01-01T00:01:00Z' -Url 'https://example.invalid/handoff/545'))),
            (New-TestDeveloperCandidate -IssueNumber 546 -Status 'In Progress' -Priority P3 -UpdatedAt '2019-01-01T00:00:00Z' -PullRequests @((New-TestDeveloperPullRequest -Number 646 -HeadSha $bandHeads[2] -HeadRef 'codex/band-three')) -Sources @((New-TestAutomationSource -Body $bandMarkers[2] -Timestamp '2019-01-01T00:01:00Z' -Url 'https://example.invalid/handoff/546'))),
            (New-TestDeveloperCandidate -IssueNumber 547 -Status 'In Progress' -Priority P2 -UpdatedAt '2018-01-01T00:00:00Z' -PullRequests @((New-TestDeveloperPullRequest -Number 647 -HeadSha $bandHeads[3] -HeadRef 'codex/band-four')) -Sources @((New-TestAutomationSource -Body $bandMarkers[3] -Timestamp '2018-01-01T00:01:00Z' -Url 'https://example.invalid/handoff/547'))),
            (New-TestDeveloperCandidate -IssueNumber 548 -Status Ready -Priority P1 -UpdatedAt '2017-01-01T00:00:00Z'),
            (New-TestDeveloperCandidate -IssueNumber 549 -Status Ready -Priority P3 -UpdatedAt '2016-01-01T00:00:00Z')
        )
        $expectedBandIssues = @(544, 545, 546, 547, 548, 549)
        $remainingBandCandidates = @($bandCandidates)
        for ($bandIndex = 0; $bandIndex -lt $expectedBandIssues.Count; $bandIndex++) {
            $bandSelector = Invoke-TestDeveloperSelector -Candidates $remainingBandCandidates -Name ("exact-band-$bandIndex")
            Assert-AutomationTest -Condition ($bandSelector.ExitCode -eq 0) -Message "Exact queue-band fixture $bandIndex failed: $($bandSelector.Output)"
            $bandJson = ConvertFrom-LastAutomationJson -Output $bandSelector.Output
            Assert-AutomationTest -Condition ([int]$bandJson.IssueNumber -eq $expectedBandIssues[$bandIndex]) -Message "Exact queue-band order diverged at band $($bandIndex + 1)."
            if ($remainingBandCandidates.Count -gt 1) {
                $remainingBandCandidates = @($remainingBandCandidates[1..($remainingBandCandidates.Count - 1)])
            }
            else {
                $remainingBandCandidates = @()
            }
        }

        $higherIssueTie = New-TestDeveloperCandidate -IssueNumber 591 -Status Ready -Priority P2 -UpdatedAt '2026-06-07T00:00:00Z'
        $lowerIssueTie = New-TestDeveloperCandidate -IssueNumber 590 -Status Ready -Priority P3 -UpdatedAt '2026-06-07T00:00:00Z'
        $tieSelector = Invoke-TestDeveloperSelector -Candidates @($higherIssueTie, $lowerIssueTie) -Name 'issue-number-tie'
        Assert-AutomationTest -Condition ($tieSelector.ExitCode -eq 0) -Message "Issue-number tie fixture failed: $($tieSelector.Output)"
        $tieJson = ConvertFrom-LastAutomationJson -Output $tieSelector.Output
        Assert-AutomationTest -Condition ([int]$tieJson.IssueNumber -eq 590) -Message 'Issue number was not the final deterministic ascending tie-break.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorScopesEvidenceToCurrentOpenPullRequest' -Body {
        $currentHead = 'abababababababababababababababababababab'
        $closedHead = 'cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd'
        $currentMarker = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 592 -PullRequestNumber 693 -HeadSha $currentHead -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/592-current'
        $closedMarker = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 592 -PullRequestNumber 692 -HeadSha $closedHead -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/592-closed'
        $closedOwnerBlock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 592 -Queue block -Reason external-blocker
        $candidate = New-TestDeveloperCandidate `
            -IssueNumber 592 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-06-08T00:00:00Z' `
            -PullRequests @(
                (New-TestDeveloperPullRequest -Number 692 -HeadSha $closedHead -HeadRef 'codex/closed-history' -State CLOSED),
                (New-TestDeveloperPullRequest -Number 693 -HeadSha $currentHead -HeadRef 'codex/current-resume')
            ) `
            -Sources @(
                (New-TestAutomationSource -Body $currentMarker -Timestamp '2026-06-08T00:02:00Z' -Url 'https://example.invalid/handoff/592-current' -Kind PullRequestComment -PullRequestNumber 693),
                (New-TestAutomationSource -Body $closedOwnerBlock -Timestamp '2026-06-08T00:03:00Z' -Url 'https://example.invalid/owner/592-closed' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER -Kind PullRequestComment -PullRequestNumber 692),
                (New-TestAutomationSource -Body "<!-- sashimi-boy-automation-handoff:v1`nmode: ReviewFix`n-->" -Timestamp '2026-06-08T00:04:00Z' -Url 'https://example.invalid/malformed/592-closed' -Kind PullRequestComment -PullRequestNumber 692),
                (New-TestAutomationSource -Body 'Approved on closed PR source' -Timestamp '2026-06-08T00:05:00Z' -Url 'https://example.invalid/review/592-closed' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $currentHead -PullRequestNumber 692),
                (New-TestAutomationSource -Body $closedMarker -Timestamp '2026-06-08T00:06:00Z' -Url 'https://example.invalid/handoff/592-closed' -Kind PullRequestComment -PullRequestNumber 692)
            )
        $selector = Invoke-TestDeveloperSelector -Candidates @($candidate) -Name 'current-pr-source-scope'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Current PR source-scope fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([bool]$json.Selected -and [int]$json.PullRequestNumber -eq 693 -and [string]$json.LatestHandoffUrl -ceq 'https://example.invalid/handoff/592-current') -Message 'Closed-PR comments or reviews poisoned the current open PR handoff.'

        $completionHandoffUrl = 'https://example.invalid/handoff/593-current'
        $completionCurrentMarker = New-AutomationHandoffMarker -Mode DeliveryResume -IssueNumber 593 -PullRequestNumber 694 -HeadSha $currentHead -SourceRole Developer -Reason runner-failure -PendingCommand 'Invoke-CurrentValidation'
        $closedSourceCompletion = New-AutomationHandoffCompletionMarker -IssueNumber 593 -PullRequestNumber 694 -HeadSha $currentHead -SourceRole Developer -HandoffUrl $completionHandoffUrl
        $completionCandidate = New-TestDeveloperCandidate `
            -IssueNumber 593 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-06-09T00:00:00Z' `
            -PullRequests @(
                (New-TestDeveloperPullRequest -Number 694 -HeadSha $currentHead -HeadRef 'codex/current-completion-scope'),
                (New-TestDeveloperPullRequest -Number 695 -HeadSha $closedHead -HeadRef 'codex/closed-completion-scope' -State CLOSED)
            ) `
            -Sources @(
                (New-TestAutomationSource -Body $completionCurrentMarker -Timestamp '2026-06-09T00:01:00Z' -Url $completionHandoffUrl -Kind PullRequestComment -PullRequestNumber 694),
                (New-TestAutomationSource -Body $closedSourceCompletion -Timestamp '2026-06-09T00:02:00Z' -Url 'https://example.invalid/completion/593-closed-source' -Kind PullRequestComment -PullRequestNumber 695)
            )
        $completionSelector = Invoke-TestDeveloperSelector -Candidates @($completionCandidate) -Name 'current-pr-completion-source-scope'
        Assert-AutomationTest -Condition ($completionSelector.ExitCode -eq 0) -Message "Current PR completion source-scope fixture failed: $($completionSelector.Output)"
        $completionJson = ConvertFrom-LastAutomationJson -Output $completionSelector.Output
        Assert-AutomationTest -Condition ([bool]$completionJson.Selected -and [int]$completionJson.PullRequestNumber -eq 694) -Message 'A completion comment from a closed PR resolved the current open PR handoff.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorExcludesInvalidLinkedPullRequests' -Body {
        $head = '2222222222222222222222222222222222222222'
        $noPr = New-TestDeveloperCandidate -IssueNumber 550 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-01T00:00:00Z'
        $twoPrs = New-TestDeveloperCandidate `
            -IssueNumber 551 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-02T00:00:00Z' `
            -PullRequests @(
                (New-TestDeveloperPullRequest -Number 651 -HeadSha $head -HeadRef 'codex/first'),
                (New-TestDeveloperPullRequest -Number 652 -HeadSha $head -HeadRef 'codex/second')
            )
        $notDraft = New-TestDeveloperCandidate `
            -IssueNumber 552 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-03T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 653 -HeadSha $head -HeadRef 'codex/not-draft' -IsDraft $false))
        $wrongBase = New-TestDeveloperCandidate `
            -IssueNumber 553 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-04T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 654 -HeadSha $head -HeadRef 'codex/wrong-base' -BaseRef develop))
        $readyWithPr = New-TestDeveloperCandidate `
            -IssueNumber 554 -Status Ready -Priority P1 -UpdatedAt '2026-03-05T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 655 -HeadSha $head -HeadRef 'codex/ready-pr'))
        $crossRepository = New-TestDeveloperCandidate `
            -IssueNumber 556 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T01:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 656 -HeadSha $head -HeadRef 'codex/cross-repository' -IsCrossRepository $true))
        $wrongRepository = New-TestDeveloperCandidate `
            -IssueNumber 557 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T02:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 657 -HeadSha $head -HeadRef 'codex/wrong-repository' -BaseRepositoryMatches $false))
        $unknownRepositoryPullRequest = New-TestDeveloperPullRequest -Number 661 -HeadSha $head -HeadRef 'codex/unknown-repository'
        $unknownRepositoryPullRequest.PSObject.Properties.Remove('IsCrossRepository')
        $unknownRepository = New-TestDeveloperCandidate `
            -IssueNumber 561 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T02:30:00Z' `
            -PullRequests @($unknownRepositoryPullRequest)
        $invalidHead = New-TestDeveloperCandidate `
            -IssueNumber 558 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T03:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 658 -HeadSha 'not-a-sha' -HeadRef 'codex/invalid-head'))
        $activeLease = New-TestDeveloperCandidate `
            -IssueNumber 559 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T04:00:00Z' -LeaseActive $true `
            -PullRequests @((New-TestDeveloperPullRequest -Number 659 -HeadSha $head -HeadRef 'codex/active-lease'))
        $invalidLease = New-TestDeveloperCandidate `
            -IssueNumber 560 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-03-05T05:00:00Z' -LeaseStateInvalid $true `
            -PullRequests @((New-TestDeveloperPullRequest -Number 660 -HeadSha $head -HeadRef 'codex/invalid-lease'))
        $ready = New-TestDeveloperCandidate -IssueNumber 555 -Status Ready -Priority P1 -UpdatedAt '2026-03-06T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($noPr, $twoPrs, $notDraft, $wrongBase, $readyWithPr, $crossRepository, $wrongRepository, $unknownRepository, $invalidHead, $activeLease, $invalidLease, $ready) -Name 'invalid-linked-prs'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Invalid PR fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 555 -and [string]$json.Mode -ceq 'NewWork') -Message 'Ready NewWork regression failed after invalid PR exclusions.'
        $reasons = @($json.ExcludedCandidates | ForEach-Object { [string]$_.Reason })
        foreach ($expectedReason in @('NoLinkedDraftPullRequest', 'MultipleLinkedDraftPullRequests', 'PullRequestNotDraft', 'PullRequestBaseIsNotMain', 'ReadyHasLinkedOpenPullRequest', 'CrossRepositoryPullRequest', 'PullRequestRepositoryMismatch', 'PullRequestRepositoryIdentityUnknown', 'MissingOrInvalidPullRequestHead', 'ActiveDeveloperLease', 'InvalidDeveloperLease')) {
            Assert-AutomationTest -Condition ($reasons -ccontains $expectedReason) -Message "Missing linked-PR exclusion reason: $expectedReason"
        }
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorRejectsStaleMalformedAndCompletedHandoffs' -Body {
        $liveHead = '3333333333333333333333333333333333333333'
        $oldHead = '4444444444444444444444444444444444444444'
        $staleMarker = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 560 -PullRequestNumber 660 -HeadSha $oldHead -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/560'
        $stale = New-TestDeveloperCandidate `
            -IssueNumber 560 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-01T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 660 -HeadSha $liveHead -HeadRef 'codex/stale')) `
            -Sources @((New-TestAutomationSource -Body $staleMarker -Timestamp '2026-04-01T00:01:00Z' -Url 'https://example.invalid/handoff/560'))

        $completedUrl = 'https://example.invalid/handoff/561'
        $currentMarker = New-AutomationHandoffMarker `
            -Mode DeliveryResume -IssueNumber 561 -PullRequestNumber 661 -HeadSha $liveHead `
            -SourceRole Developer -Reason runner-failure -PendingCommand 'Invoke-Validation'
        $completionMarker = New-AutomationHandoffCompletionMarker `
            -IssueNumber 561 -PullRequestNumber 661 -HeadSha $liveHead -SourceRole Developer -HandoffUrl $completedUrl
        $completed = New-TestDeveloperCandidate `
            -IssueNumber 561 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-02T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 661 -HeadSha $liveHead -HeadRef 'codex/completed')) `
            -Sources @(
                (New-TestAutomationSource -Body $currentMarker -Timestamp '2026-04-02T00:01:00Z' -Url $completedUrl),
                (New-TestAutomationSource -Body $completionMarker -Timestamp '2026-04-02T00:02:00Z' -Url 'https://example.invalid/completion/561'),
                (New-TestAutomationSource `
                    -Body (New-AutomationHandoffCompletionMarker -IssueNumber 561 -PullRequestNumber 661 -HeadSha $liveHead -SourceRole Developer -HandoffUrl 'https://example.invalid/handoff/not-current') `
                    -Timestamp '2026-04-02T00:03:00Z' `
                    -Url 'https://example.invalid/completion/561-nonmatching')
            )

        $validOld = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 562 -PullRequestNumber 662 -HeadSha $liveHead -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/562'
        $malformedNew = "<!-- sashimi-boy-automation-handoff:v1`nmode: ReviewFix`n-->"
        $malformed = New-TestDeveloperCandidate `
            -IssueNumber 562 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 662 -HeadSha $liveHead -HeadRef 'codex/malformed')) `
            -Sources @(
                (New-TestAutomationSource -Body $validOld -Timestamp '2026-04-03T00:01:00Z' -Url 'https://example.invalid/handoff/562-old'),
                (New-TestAutomationSource -Body $malformedNew -Timestamp '2026-04-03T00:02:00Z' -Url 'https://example.invalid/handoff/562-new')
            )
        $approvedMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 564 -PullRequestNumber 664 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/564'
        $approved = New-TestDeveloperCandidate `
            -IssueNumber 564 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T12:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 664 -HeadSha $liveHead -HeadRef 'codex/approved')) `
            -Sources @(
                (New-TestAutomationSource -Body $approvedMarker -Timestamp '2026-04-03T12:01:00Z' -Url 'https://example.invalid/handoff/564'),
                (New-TestAutomationSource -Body 'Native approval' -Timestamp '2026-04-03T12:02:00Z' -Url 'https://example.invalid/review/564' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $liveHead -PullRequestNumber 664)
            )

        $editedHandoffMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 565 -PullRequestNumber 665 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/565'
        $editedHandoff = New-TestDeveloperCandidate `
            -IssueNumber 565 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T13:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 665 -HeadSha $liveHead -HeadRef 'codex/edited-handoff')) `
            -Sources @((New-TestAutomationSource -Body $editedHandoffMarker -Timestamp '2026-04-03T13:01:00Z' -Url 'https://example.invalid/handoff/565' -WasEdited $true))

        $editedCompletionHandoffUrl = 'https://example.invalid/handoff/566'
        $editedCompletionHandoff = New-AutomationHandoffMarker `
            -Mode DeliveryResume -IssueNumber 566 -PullRequestNumber 666 -HeadSha $liveHead `
            -SourceRole Developer -Reason runner-failure -PendingCommand 'Invoke-Validation'
        $editedCompletionMarker = New-AutomationHandoffCompletionMarker `
            -IssueNumber 566 -PullRequestNumber 666 -HeadSha $liveHead -SourceRole Developer -HandoffUrl $editedCompletionHandoffUrl
        $editedCompletion = New-TestDeveloperCandidate `
            -IssueNumber 566 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T14:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 666 -HeadSha $liveHead -HeadRef 'codex/edited-completion')) `
            -Sources @(
                (New-TestAutomationSource -Body $editedCompletionHandoff -Timestamp '2026-04-03T14:01:00Z' -Url $editedCompletionHandoffUrl),
                (New-TestAutomationSource -Body $editedCompletionMarker -Timestamp '2026-04-03T14:02:00Z' -Url 'https://example.invalid/completion/566' -WasEdited $true)
            )

        $ambiguousHandoffA = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 567 -PullRequestNumber 667 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/567-a'
        $ambiguousHandoffB = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 567 -PullRequestNumber 667 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-blocker -FindingUrl 'https://example.invalid/finding/567-b'
        $ambiguousHandoff = New-TestDeveloperCandidate `
            -IssueNumber 567 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T15:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 667 -HeadSha $liveHead -HeadRef 'codex/ambiguous-handoff')) `
            -Sources @(
                (New-TestAutomationSource -Body $ambiguousHandoffA -Timestamp '2026-04-03T15:01:00Z' -Url 'https://example.invalid/handoff/567-a'),
                (New-TestAutomationSource -Body $ambiguousHandoffB -Timestamp '2026-04-03T15:01:00Z' -Url 'https://example.invalid/handoff/567-b')
            )

        $ambiguousCompletionHandoffUrl = 'https://example.invalid/handoff/568'
        $ambiguousCompletionHandoff = New-AutomationHandoffMarker `
            -Mode DeliveryResume -IssueNumber 568 -PullRequestNumber 668 -HeadSha $liveHead `
            -SourceRole Developer -Reason runner-failure -PendingCommand 'Invoke-Validation'
        $ambiguousCompletionMarker = New-AutomationHandoffCompletionMarker `
            -IssueNumber 568 -PullRequestNumber 668 -HeadSha $liveHead -SourceRole Developer -HandoffUrl $ambiguousCompletionHandoffUrl
        $ambiguousCompletion = New-TestDeveloperCandidate `
            -IssueNumber 568 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T16:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 668 -HeadSha $liveHead -HeadRef 'codex/ambiguous-completion')) `
            -Sources @(
                (New-TestAutomationSource -Body $ambiguousCompletionHandoff -Timestamp '2026-04-03T16:01:00Z' -Url $ambiguousCompletionHandoffUrl),
                (New-TestAutomationSource -Body $ambiguousCompletionMarker -Timestamp '2026-04-03T16:01:00Z' -Url 'https://example.invalid/completion/568')
            )
        $matchingOlderMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 569 -PullRequestNumber 669 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/569-current'
        $wrongTargetNewerMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 999 -PullRequestNumber 998 -HeadSha $liveHead `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/569-wrong-target'
        $wrongTargetNewer = New-TestDeveloperCandidate `
            -IssueNumber 569 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-03T17:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 669 -HeadSha $liveHead -HeadRef 'codex/wrong-target')) `
            -Sources @(
                (New-TestAutomationSource -Body $matchingOlderMarker -Timestamp '2026-04-03T17:01:00Z' -Url 'https://example.invalid/handoff/569-current'),
                (New-TestAutomationSource -Body $wrongTargetNewerMarker -Timestamp '2026-04-03T17:02:00Z' -Url 'https://example.invalid/handoff/569-wrong-target')
            )
        $ready = New-TestDeveloperCandidate -IssueNumber 563 -Status Ready -Priority P1 -UpdatedAt '2026-04-04T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($stale, $completed, $malformed, $approved, $editedHandoff, $editedCompletion, $ambiguousHandoff, $ambiguousCompletion, $wrongTargetNewer, $ready) -Name 'handoff-staleness'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Handoff staleness fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 563) -Message 'A stale, completed, or malformed handoff was selected.'
        $reasons = @($json.ExcludedCandidates | ForEach-Object { [string]$_.Reason })
        foreach ($expectedReason in @('StaleHandoffHead', 'HandoffCompleted', 'InvalidHandoffMarker', 'InvalidHandoffCompletionMarker', 'ReviewPassAfterHandoff', 'HandoffTargetMismatch')) {
            Assert-AutomationTest -Condition ($reasons -ccontains $expectedReason) -Message "Missing handoff exclusion reason: $expectedReason"
        }
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorUsesLatestTrustedReviewDecisionForLiveHead' -Body {
        $head = '7777777777777777777777777777777777777777'
        $oldHead = '8888888888888888888888888888888888888888'

        $changedMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 577 -PullRequestNumber 677 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/577'
        $changesRequestedAfterApproval = New-TestDeveloperCandidate `
            -IssueNumber 577 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-10T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 677 -HeadSha $head -HeadRef 'codex/latest-review-decision')) `
            -Sources @(
                (New-TestAutomationSource -Body $changedMarker -Timestamp '2026-04-10T00:01:00Z' -Url 'https://example.invalid/handoff/577'),
                (New-TestAutomationSource -Body 'Approved current head' -Timestamp '2026-04-10T00:02:00Z' -Url 'https://example.invalid/review/577-a' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $head -PullRequestNumber 677),
                (New-TestAutomationSource -Body 'Changes requested later' -Timestamp '2026-04-10T00:03:00Z' -Url 'https://example.invalid/review/577-b' -Kind PullRequestReview -ReviewState CHANGES_REQUESTED -ReviewCommitSha $head -PullRequestNumber 677)
            )

        $staleApprovalMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 578 -PullRequestNumber 678 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/578'
        $staleApproval = New-TestDeveloperCandidate `
            -IssueNumber 578 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-11T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 678 -HeadSha $head -HeadRef 'codex/stale-approval')) `
            -Sources @(
                (New-TestAutomationSource -Body $staleApprovalMarker -Timestamp '2026-04-11T00:01:00Z' -Url 'https://example.invalid/handoff/578'),
                (New-TestAutomationSource -Body 'Approved old head' -Timestamp '2026-04-11T00:02:00Z' -Url 'https://example.invalid/review/578' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $oldHead -PullRequestNumber 678)
            )

        $approvedMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 579 -PullRequestNumber 679 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/579'
        $approved = New-TestDeveloperCandidate `
            -IssueNumber 579 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-12T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 679 -HeadSha $head -HeadRef 'codex/current-approval')) `
            -Sources @(
                (New-TestAutomationSource -Body $approvedMarker -Timestamp '2026-04-12T00:01:00Z' -Url 'https://example.invalid/handoff/579'),
                (New-TestAutomationSource -Body 'Approved current head' -Timestamp '2026-04-12T00:02:00Z' -Url 'https://example.invalid/review/579' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $head -PullRequestNumber 679)
            )

        $ambiguousMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 580 -PullRequestNumber 680 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/580'
        $ambiguous = New-TestDeveloperCandidate `
            -IssueNumber 580 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-13T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 680 -HeadSha $head -HeadRef 'codex/ambiguous-review')) `
            -Sources @(
                (New-TestAutomationSource -Body $ambiguousMarker -Timestamp '2026-04-13T00:01:00Z' -Url 'https://example.invalid/handoff/580'),
                (New-TestAutomationSource -Body 'Approved current head' -Timestamp '2026-04-13T00:02:00Z' -Url 'https://example.invalid/review/580-a' -Kind PullRequestReview -ReviewState APPROVED -ReviewCommitSha $head -PullRequestNumber 680),
                (New-TestAutomationSource -Body 'Changes requested at same instant' -Timestamp '2026-04-13T00:02:00Z' -Url 'https://example.invalid/review/580-b' -Kind PullRequestReview -ReviewState CHANGES_REQUESTED -ReviewCommitSha $head -PullRequestNumber 680)
            )

        $sameSecondFindingMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 581 -PullRequestNumber 681 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/581'
        $sameSecondFinding = New-TestDeveloperCandidate `
            -IssueNumber 581 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-04-09T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 681 -HeadSha $head -HeadRef 'codex/same-second-finding')) `
            -Sources @(
                (New-TestAutomationSource -Body $sameSecondFindingMarker -Timestamp '2026-04-09T00:01:00Z' -Url 'https://example.invalid/handoff/581'),
                (New-TestAutomationSource -Body 'Changes requested in the same second' -Timestamp '2026-04-09T00:01:00Z' -Url 'https://example.invalid/review/581' -Kind PullRequestReview -ReviewState CHANGES_REQUESTED -ReviewCommitSha $head -PullRequestNumber 681)
            )

        $selector = Invoke-TestDeveloperSelector -Candidates @($approved, $ambiguous, $staleApproval, $changesRequestedAfterApproval, $sameSecondFinding) -Name 'review-pass-ordering'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Review PASS ordering fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 581) -Message 'A same-second non-PASS finding or stale/superseded approval incorrectly resolved a handoff.'
        $reasons = @($json.ExcludedCandidates | ForEach-Object { [string]$_.Reason })
        Assert-AutomationTest -Condition ($reasons -ccontains 'ReviewPassAfterHandoff') -Message 'Current-head APPROVED review did not resolve a handoff.'
        Assert-AutomationTest -Condition ($reasons -ccontains 'AmbiguousReviewDecisionAfterHandoff') -Message 'Equal-time conflicting review decisions did not fail closed.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorIgnoresUntrustedMarkersAndRequiresReviewerFinding' -Body {
        $head = '6666666666666666666666666666666666666666'
        $missingFindingRejected = $false
        try {
            New-AutomationHandoffMarker `
                -Mode ReviewFix -IssueNumber 575 -PullRequestNumber 675 -HeadSha $head `
                -SourceRole Reviewer -Reason review-major | Out-Null
        }
        catch {
            $missingFindingRejected = $_.Exception.Message -match 'findingUrl must not be empty'
        }
        Assert-AutomationTest -Condition $missingFindingRejected -Message 'Reviewer handoff accepted an empty finding URL.'

        $trustedUrl = 'https://example.invalid/handoff/575'
        $trustedMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 575 -PullRequestNumber 675 -HeadSha $head `
            -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/575'
        $untrustedCompletion = New-AutomationHandoffCompletionMarker `
            -IssueNumber 575 -PullRequestNumber 675 -HeadSha $head -SourceRole Developer -HandoffUrl $trustedUrl
        $trustedCandidate = New-TestDeveloperCandidate `
            -IssueNumber 575 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-03T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 675 -HeadSha $head -HeadRef 'codex/trusted-handoff')) `
            -Sources @(
                (New-TestAutomationSource -Body $trustedMarker -Timestamp '2026-05-03T00:01:00Z' -Url $trustedUrl),
                (New-TestAutomationSource -Body '<!-- sashimi-boy-automation-handoff:v1 malformed -->' -Timestamp '2026-05-03T00:02:00Z' -Url 'https://example.invalid/untrusted/handoff' -AuthorLogin stranger -AuthorAssociation CONTRIBUTOR),
                (New-TestAutomationSource -Body $untrustedCompletion -Timestamp '2026-05-03T00:03:00Z' -Url 'https://example.invalid/untrusted/completion' -AuthorLogin stranger -AuthorAssociation CONTRIBUTOR),
                (New-TestAutomationSource -Body '<!-- sashimi-boy-automation-handoff:v1 malformed -->' -Timestamp '2026-05-03T00:04:00Z' -Url 'https://example.invalid/review/no-comment-url' -Kind PullRequestReview -ReviewState COMMENTED -ReviewCommitSha $head -PullRequestNumber 675),
                (New-TestAutomationSource -Body 'Prose mentions sashimi-boy-automation-handoff:v1 without evidence.' -Timestamp '2026-05-03T00:05:00Z' -Url 'https://example.invalid/comment/mention'),
                (New-TestAutomationSource -Body ('```md' + "`n" + $trustedMarker + "`n" + '```') -Timestamp '2026-05-03T00:06:00Z' -Url 'https://example.invalid/comment/fenced-example')
            )

        $untrustedOnlyMarker = New-AutomationHandoffMarker `
            -Mode ReviewFix -IssueNumber 576 -PullRequestNumber 676 -HeadSha $head `
            -SourceRole Reviewer -Reason review-blocker -FindingUrl 'https://example.invalid/finding/576'
        $untrustedOnly = New-TestDeveloperCandidate `
            -IssueNumber 576 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-04T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 676 -HeadSha $head -HeadRef 'codex/untrusted-handoff')) `
            -Sources @((New-TestAutomationSource -Body $untrustedOnlyMarker -Timestamp '2026-05-04T00:01:00Z' -Url 'https://example.invalid/untrusted/only' -AuthorLogin stranger -AuthorAssociation NONE))

        $selector = Invoke-TestDeveloperSelector -Candidates @($untrustedOnly, $trustedCandidate) -Name 'trusted-marker-authors'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Trusted marker fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 575 -and [string]$json.Mode -ceq 'ReviewFix') -Message 'An untrusted marker or completion superseded the trusted handoff.'
        $reasons = @($json.ExcludedCandidates | ForEach-Object { [string]$_.Reason })
        Assert-AutomationTest -Condition ($reasons -ccontains 'NoCurrentHandoff') -Message 'An untrusted-only handoff was treated as current.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorRequiresOwnerUnblockAndRejectsProductBlocker' -Body {
        $head = '5555555555555555555555555555555555555555'
        $markerA = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 570 -PullRequestNumber 670 -HeadSha $head -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/570'
        $nonOwnerUnblock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 570 -Queue unblock -Reason resolved
        $stillBlocked = New-TestDeveloperCandidate `
            -IssueNumber 570 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-01T00:00:00Z' -Labels @('blocked') `
            -PullRequests @((New-TestDeveloperPullRequest -Number 670 -HeadSha $head -HeadRef 'codex/non-owner-unblock')) `
            -Sources @(
                (New-TestAutomationSource -Body $markerA -Timestamp '2026-05-01T00:01:00Z' -Url 'https://example.invalid/handoff/570'),
                (New-TestAutomationSource -Body $nonOwnerUnblock -Timestamp '2026-05-01T00:02:00Z' -Url 'https://example.invalid/unblock/570' -AuthorLogin stranger -AuthorAssociation CONTRIBUTOR)
            )

        $markerB = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 571 -PullRequestNumber 671 -HeadSha $head -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/571'
        $ownerUnblock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 571 -Queue unblock -Reason resolved
        $unblocked = New-TestDeveloperCandidate `
            -IssueNumber 571 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-02T00:00:00Z' -Labels @('blocked') `
            -PullRequests @((New-TestDeveloperPullRequest -Number 671 -HeadSha $head -HeadRef 'codex/owner-unblock')) `
            -Sources @(
                (New-TestAutomationSource -Body $markerB -Timestamp '2026-05-02T00:01:00Z' -Url 'https://example.invalid/handoff/571'),
                (New-TestAutomationSource -Body $ownerUnblock -Timestamp '2026-05-02T00:02:00Z' -Url 'https://example.invalid/unblock/571' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER)
            )

        $markerC = New-AutomationHandoffMarker -Mode ReviewFix -IssueNumber 572 -PullRequestNumber 672 -HeadSha $head -SourceRole Reviewer -Reason review-major -FindingUrl 'https://example.invalid/finding/572'
        $productBlock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 572 -Queue block -Reason source-asset-missing
        $productBlocked = New-TestDeveloperCandidate `
            -IssueNumber 572 -Status 'In Progress' -Priority P0 -UpdatedAt '2026-04-01T00:00:00Z' `
            -PullRequests @((New-TestDeveloperPullRequest -Number 672 -HeadSha $head -HeadRef 'codex/product-blocked')) `
            -Sources @(
                (New-TestAutomationSource -Body $markerC -Timestamp '2026-04-01T00:01:00Z' -Url 'https://example.invalid/handoff/572'),
                (New-TestAutomationSource -Body $productBlock -Timestamp '2026-04-01T00:02:00Z' -Url 'https://example.invalid/block/572' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER)
            )

        $olderUnblock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 573 -Queue unblock -Reason resolved
        $editedBlock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 573 -Queue block -Reason product-decision
        $editedOwnerDecision = New-TestDeveloperCandidate `
            -IssueNumber 573 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-03T00:00:00Z' -Labels @('blocked') `
            -PullRequests @((New-TestDeveloperPullRequest -Number 673 -HeadSha $head -HeadRef 'codex/edited-owner-decision')) `
            -Sources @(
                (New-TestAutomationSource -Body $olderUnblock -Timestamp '2026-05-03T00:01:00Z' -Url 'https://example.invalid/unblock/573' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER),
                (New-TestAutomationSource -Body $editedBlock -Timestamp '2026-05-03T00:02:00Z' -Url 'https://example.invalid/block/573' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER -WasEdited $true)
            )

        $equalUnblock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 574 -Queue unblock -Reason resolved
        $equalBlock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 574 -Queue block -Reason external-blocker
        $ambiguousOwnerDecision = New-TestDeveloperCandidate `
            -IssueNumber 574 -Status 'In Progress' -Priority P1 -UpdatedAt '2026-05-04T00:00:00Z' -Labels @('blocked') `
            -PullRequests @((New-TestDeveloperPullRequest -Number 674 -HeadSha $head -HeadRef 'codex/ambiguous-owner-decision')) `
            -Sources @(
                (New-TestAutomationSource -Body $equalUnblock -Timestamp '2026-05-04T00:01:00Z' -Url 'https://example.invalid/unblock/574' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER),
                (New-TestAutomationSource -Body $equalBlock -Timestamp '2026-05-04T00:01:00Z' -Url 'https://example.invalid/block/574' -AuthorLogin DongGyunLeeeee -AuthorAssociation OWNER)
            )

        $selector = Invoke-TestDeveloperSelector -Candidates @($stillBlocked, $unblocked, $productBlocked, $editedOwnerDecision, $ambiguousOwnerDecision) -Name 'blocked-owner-decision'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Blocked Owner decision fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 571) -Message 'Latest authoritative Owner unblock did not recover the stale blocked candidate.'
        $reasons = @($json.ExcludedCandidates | ForEach-Object { [string]$_.Reason })
        Assert-AutomationTest -Condition ($reasons -ccontains 'BlockedLabelUnresolved') -Message 'Non-Owner unblock incorrectly cleared blocked.'
        Assert-AutomationTest -Condition ($reasons -ccontains 'UnresolvedProductAssetOrExternalBlocker') -Message 'Product/asset blocker was not excluded.'
        Assert-AutomationTest -Condition (@($json.ExcludedCandidates | Where-Object { $_.Reason -ceq 'InvalidOwnerQueueDecision' }).Count -eq 2) -Message 'Edited or equal-time ambiguous Owner decisions did not fail closed.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorReturnsOneItemAndNeverExpandsStateAuthority' -Body {
        $readyOldest = New-TestDeveloperCandidate -IssueNumber 580 -Status Ready -Priority P1 -UpdatedAt '2026-06-01T00:00:00Z'
        $readyLater = New-TestDeveloperCandidate -IssueNumber 581 -Status Ready -Priority P1 -UpdatedAt '2026-06-02T00:00:00Z'
        $verification = New-TestDeveloperCandidate -IssueNumber 582 -Status Verification -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $done = New-TestDeveloperCandidate -IssueNumber 583 -Status Done -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $selector = Invoke-TestDeveloperSelector -Candidates @($readyLater, $done, $readyOldest, $verification) -Name 'one-issue-state-authority'
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "One-item fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([int]$json.IssueNumber -eq 580 -and [string]$json.Mode -ceq 'NewWork') -Message 'Oldest same-band Ready item was not selected deterministically.'
        Assert-AutomationTest -Condition ([int]$json.MaximumIssuesThisRun -eq 1) -Message 'Selector allowed more than one Issue per run.'
        Assert-AutomationTest -Condition ([string]$json.AllowedCompletionStatus -ceq 'Review') -Message 'Selector allowed Verification or Done completion.'
        Assert-AutomationTest -Condition (@($json.ExcludedCandidates).Count -eq 3) -Message 'Selector did not account for all non-selected candidates.'

        $emptySelector = Invoke-TestDeveloperSelector -Candidates @() -Name 'empty-queue'
        Assert-AutomationTest -Condition ($emptySelector.ExitCode -eq 0) -Message "Empty queue should be a successful no-op: $($emptySelector.Output)"
        $emptyJson = ConvertFrom-LastAutomationJson -Output $emptySelector.Output
        Assert-AutomationTest -Condition (-not [bool]$emptyJson.Selected -and [string]$emptyJson.Mode -ceq 'None') -Message 'Empty queue output contract is incorrect.'
        Assert-AutomationTest -Condition (-not [bool]$emptyJson.MayCreatePullRequest -and -not [bool]$emptyJson.MayCreateRemoteBranch) -Message 'Empty queue output allowed a PR or remote branch mutation.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorRequiresExactProjectSchema' -Body {
        $fields = @(Get-TestDeveloperProjectFields)
        $fields[0].options[1].name = 'ready'
        $selector = Invoke-TestDeveloperSelector `
            -Candidates @((New-TestDeveloperCandidate -IssueNumber 590 -Status Ready -Priority P1 -UpdatedAt '2026-07-01T00:00:00Z')) `
            -ProjectFields $fields `
            -Name 'invalid-project-schema'
        Assert-AutomationTest -Condition ($selector.ExitCode -ne 0) -Message 'Selector accepted a mis-cased required Project option.'
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition (-not [bool]$json.Succeeded -and [string]$json.Reason -ceq 'SelectorError') -Message 'Schema failure did not use the selector error contract.'
    }

    Invoke-AutomationTestCase -Name 'DeveloperSelectorLiveAdapterIsReadOnlyCompleteAndLeaseAware' -Body {
        $head = '9999999999999999999999999999999999999999'
        $handoff = New-AutomationHandoffMarker `
            -Mode DeliveryResume -IssueNumber 690 -PullRequestNumber 790 -HeadSha $head `
            -SourceRole Developer -Reason runner-failure -PendingCommand 'Invoke-LiveFixtureValidation'
        $fakeGitHub = New-TestDeveloperSelectorGitHubFixture -Root $script:temporaryRoot -HandoffMarker $handoff -HeadSha $head
        $selectorPath = Join-Path $repository 'Tools\Automation\Get-DeveloperWorkItem.ps1'
        $baseParameters = @{
            Repository = 'DongGyunLeeeee/sashimi-boy-unity'
            ProjectOwner = 'DongGyunLeeeee'
            ProjectNumber = 1
            GitHubCliPath = $fakeGitHub.GitHubPath
        }

        $previousTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
        $previousTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('TEMP', $script:temporaryRoot, 'Process')
            [Environment]::SetEnvironmentVariable('TMP', $script:temporaryRoot, 'Process')
            $selector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($selector.ExitCode -eq 0) -Message "Raw live-shaped selector fixture failed: $($selector.Output)"
        $json = ConvertFrom-LastAutomationJson -Output $selector.Output
        Assert-AutomationTest -Condition ([string]$json.DataSource -ceq 'Live' -and [int]$json.IssueNumber -eq 690 -and [string]$json.Mode -ceq 'DeliveryResume') -Message 'Live adapter did not normalize/select the raw GitHub response correctly.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $fakeGitHub.MutationSentinelPath)) -Message 'Live selector attempted a GitHub mutation.'
        $invocations = @(Get-Content -LiteralPath $fakeGitHub.LogPath)
        Assert-AutomationTest -Condition ($invocations.Count -eq 10) -Message "Live selector used an unexpected GitHub call count: $($invocations.Count)"
        $joinedInvocations = @($invocations | ForEach-Object { [string]$_ })
        Assert-AutomationTest -Condition (@($joinedInvocations | Where-Object { $_ -match '^project field-list .* --limit 10000$' }).Count -eq 1) -Message 'Field lookup did not request a complete high-limit page.'
        Assert-AutomationTest -Condition (@($joinedInvocations | Where-Object { $_ -match '^project item-list .* --limit 10000$' }).Count -eq 1) -Message 'Item lookup did not request a complete high-limit page.'
        Assert-AutomationTest -Condition (@($joinedInvocations | Where-Object { $_ -match '^pr view 790 ' }).Count -eq 1) -Message 'Live adapter did not pin the linked PR number in the target repository.'
        foreach ($connectionQueryName in @('AutomationProjectItemLinkedPullRequests', 'AutomationIssueLabels', 'AutomationIssueComments', 'AutomationPullRequestComments', 'AutomationPullRequestReviews')) {
            Assert-AutomationTest -Condition (@($joinedInvocations | Where-Object { $_ -match $connectionQueryName }).Count -eq 1) -Message "Live adapter did not query the complete $connectionQueryName connection."
        }
        Assert-AutomationTest -Condition (@($joinedInvocations | Where-Object { $_ -match '(?i)(^|[ =:])mutation([ ({]|$)|^project item-edit |^(issue|pr) (create|edit|close|reopen|merge|ready) ' }).Count -eq 0) -Message 'Live adapter command log contains a mutation.'

        $baselineIssueLabels = Get-Content -Raw -LiteralPath $fakeGitHub.IssueLabelsPath
        $baselineIssueLabelsNext = Get-Content -Raw -LiteralPath $fakeGitHub.IssueLabelsNextPath
        $baselineIssueComments = Get-Content -Raw -LiteralPath $fakeGitHub.IssueCommentsPath
        $baselineIssueCommentsNext = Get-Content -Raw -LiteralPath $fakeGitHub.IssueCommentsNextPath
        $baselinePullRequestComments = Get-Content -Raw -LiteralPath $fakeGitHub.PullRequestCommentsPath
        $baselinePullRequestCommentsNext = Get-Content -Raw -LiteralPath $fakeGitHub.PullRequestCommentsNextPath
        $baselinePullRequestReviews = Get-Content -Raw -LiteralPath $fakeGitHub.PullRequestReviewsPath
        $baselinePullRequestReviewsNext = Get-Content -Raw -LiteralPath $fakeGitHub.PullRequestReviewsNextPath
        $baselinePullRequest = Get-Content -Raw -LiteralPath $fakeGitHub.PullRequestPath
        $baselineLinkedPullRequests = Get-Content -Raw -LiteralPath $fakeGitHub.LinkedPullRequestsPath
        $baselineLinkedPullRequestsNext = Get-Content -Raw -LiteralPath $fakeGitHub.LinkedPullRequestsNextPath

        $linkedPullRequestPageOneNodes = @([pscustomobject][ordered]@{
                id = 'linked-pr-790'
                number = 790
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790'
                state = 'OPEN'
                repository = [pscustomobject]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
            })
        for ($linkedPullRequestIndex = 791; $linkedPullRequestIndex -le 799; $linkedPullRequestIndex++) {
            $linkedPullRequestPageOneNodes += [pscustomobject][ordered]@{
                id = "linked-pr-$linkedPullRequestIndex"
                number = $linkedPullRequestIndex
                url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$linkedPullRequestIndex"
                state = 'CLOSED'
                repository = [pscustomobject]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
            }
        }
        $linkedPullRequestPageTwoNodes = @([pscustomobject][ordered]@{
                id = 'linked-pr-800'
                number = 800
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/800'
                state = 'OPEN'
                repository = [pscustomobject]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
            })
        $linkedPullRequestsPageOne = New-TestProjectLinkedPullRequestsResponse -Nodes $linkedPullRequestPageOneNodes -TotalCount 11 -HasNextPage $true -EndCursor 'project-linked-prs-page-2'
        $linkedPullRequestsPageTwo = New-TestProjectLinkedPullRequestsResponse -Nodes $linkedPullRequestPageTwoNodes -TotalCount 11
        New-TestFile -Path $fakeGitHub.LinkedPullRequestsPath -Content (($linkedPullRequestsPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.LinkedPullRequestsNextPath -Content (($linkedPullRequestsPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $pagedLinkedPullRequestsSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($pagedLinkedPullRequestsSelector.ExitCode -eq 0) -Message "Paged linked-PR selector failed: $($pagedLinkedPullRequestsSelector.Output)"
        $pagedLinkedPullRequestsJson = ConvertFrom-LastAutomationJson -Output $pagedLinkedPullRequestsSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$pagedLinkedPullRequestsJson.Selected -and @($pagedLinkedPullRequestsJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'MultipleLinkedDraftPullRequests' }).Count -eq 1) -Message 'A second open PR after the first 10 linked PRs was not detected.'
        New-TestFile -Path $fakeGitHub.LinkedPullRequestsPath -Content $baselineLinkedPullRequests
        New-TestFile -Path $fakeGitHub.LinkedPullRequestsNextPath -Content $baselineLinkedPullRequestsNext

        $mismatchedPullRequestUrl = $baselinePullRequest | ConvertFrom-Json
        $mismatchedPullRequestUrl.url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/999'
        New-TestFile -Path $fakeGitHub.PullRequestPath -Content (($mismatchedPullRequestUrl | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $mismatchedPullRequestUrlSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($mismatchedPullRequestUrlSelector.ExitCode -ne 0) -Message 'Live adapter accepted a PR URL whose number differed from the requested PR.'
        $mismatchedPullRequestUrlJson = ConvertFrom-LastAutomationJson -Output $mismatchedPullRequestUrlSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$mismatchedPullRequestUrlJson.Succeeded -and @($mismatchedPullRequestUrlJson.Errors | Where-Object { $_ -match 'mismatched number or repository URL' }).Count -eq 1) -Message 'PR URL/number mismatch did not produce an explicit selector error.'
        New-TestFile -Path $fakeGitHub.PullRequestPath -Content $baselinePullRequest

        $unknownPullRequestState = $baselinePullRequest | ConvertFrom-Json
        $unknownPullRequestState.state = 'open'
        New-TestFile -Path $fakeGitHub.PullRequestPath -Content (($unknownPullRequestState | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $unknownPullRequestStateSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($unknownPullRequestStateSelector.ExitCode -ne 0) -Message 'Live adapter accepted a non-canonical PR state.'
        $unknownPullRequestStateJson = ConvertFrom-LastAutomationJson -Output $unknownPullRequestStateSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$unknownPullRequestStateJson.Succeeded -and @($unknownPullRequestStateJson.Errors | Where-Object { $_ -match 'unknown state' }).Count -eq 1) -Message 'Unknown PR state did not produce an explicit selector error.'
        New-TestFile -Path $fakeGitHub.PullRequestPath -Content $baselinePullRequest

        $labelPageOneNodes = @(for ($labelIndex = 0; $labelIndex -lt 100; $labelIndex++) {
                [pscustomobject][ordered]@{ id = "label-$labelIndex"; name = "label-$labelIndex" }
            })
        $labelPageTwoNodes = @([pscustomobject][ordered]@{ id = 'label-blocked'; name = 'blocked' })
        $labelPageOne = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes $labelPageOneNodes -TotalCount 101 -HasNextPage $true -EndCursor 'issue-labels-page-2'
        $labelPageTwo = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes $labelPageTwoNodes -TotalCount 101
        New-TestFile -Path $fakeGitHub.IssueLabelsPath -Content (($labelPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.IssueLabelsNextPath -Content (($labelPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $pagedLabelSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($pagedLabelSelector.ExitCode -eq 0) -Message "Paged label selector failed: $($pagedLabelSelector.Output)"
        $pagedLabelJson = ConvertFrom-LastAutomationJson -Output $pagedLabelSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$pagedLabelJson.Selected -and @($pagedLabelJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'BlockedLabelUnresolved' }).Count -eq 1) -Message 'A page-2 blocked label was not applied.'
        New-TestFile -Path $fakeGitHub.IssueLabelsPath -Content $baselineIssueLabels
        New-TestFile -Path $fakeGitHub.IssueLabelsNextPath -Content $baselineIssueLabelsNext

        $issueCommentPageOneNodes = @([pscustomobject][ordered]@{
                id = 'issue-comment-1'
                body = $handoff
                createdAt = '2026-08-01T00:01:00Z'
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-1'
                author = [pscustomobject]@{ login = 'automation-agent' }
                authorAssociation = 'MEMBER'
                includesCreatedEdit = $false
            })
        for ($commentIndex = 2; $commentIndex -le 100; $commentIndex++) {
            $issueCommentPageOneNodes += [pscustomobject][ordered]@{
                id = "issue-comment-$commentIndex"
                body = "Non-marker issue comment $commentIndex"
                createdAt = '2026-08-01T00:01:10Z'
                url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-$commentIndex"
                author = [pscustomobject]@{ login = 'automation-agent' }
                authorAssociation = 'MEMBER'
                includesCreatedEdit = $false
            }
        }
        $pageTwoOwnerBlock = New-AutomationOwnerQueueDecisionMarker -IssueNumber 690 -Queue block -Reason external-blocker
        $issueCommentPageTwoNodes = @([pscustomobject][ordered]@{
                id = 'issue-comment-101'
                body = $pageTwoOwnerBlock
                createdAt = '2026-08-01T00:02:00Z'
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-101'
                author = [pscustomobject]@{ login = 'DongGyunLeeeee' }
                authorAssociation = 'OWNER'
                includesCreatedEdit = $false
            })
        $issueCommentsPageOne = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName comments -Nodes $issueCommentPageOneNodes -TotalCount 101 -HasNextPage $true -EndCursor 'issue-comments-page-2'
        $issueCommentsPageTwo = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName comments -Nodes $issueCommentPageTwoNodes -TotalCount 101
        New-TestFile -Path $fakeGitHub.IssueCommentsPath -Content (($issueCommentsPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.IssueCommentsNextPath -Content (($issueCommentsPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $pagedIssueCommentSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($pagedIssueCommentSelector.ExitCode -eq 0) -Message "Paged Issue-comment selector failed: $($pagedIssueCommentSelector.Output)"
        $pagedIssueCommentJson = ConvertFrom-LastAutomationJson -Output $pagedIssueCommentSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$pagedIssueCommentJson.Selected -and @($pagedIssueCommentJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'UnresolvedProductAssetOrExternalBlocker' }).Count -eq 1) -Message 'A page-2 Owner block decision was not applied.'
        New-TestFile -Path $fakeGitHub.IssueCommentsPath -Content $baselineIssueComments
        New-TestFile -Path $fakeGitHub.IssueCommentsNextPath -Content $baselineIssueCommentsNext

        $pullRequestCommentPageOneNodes = @(for ($commentIndex = 1; $commentIndex -le 100; $commentIndex++) {
                [pscustomobject][ordered]@{
                    id = "pr-comment-$commentIndex"
                    body = "Non-marker PR comment $commentIndex"
                    createdAt = '2026-08-01T00:01:10Z'
                    url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790#issuecomment-$commentIndex"
                    author = [pscustomobject]@{ login = 'automation-agent' }
                    authorAssociation = 'MEMBER'
                    includesCreatedEdit = $false
                }
            })
        $pageTwoCompletion = New-AutomationHandoffCompletionMarker `
            -IssueNumber 690 `
            -PullRequestNumber 790 `
            -HeadSha $head `
            -SourceRole Developer `
            -HandoffUrl 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/690#issuecomment-1'
        $pullRequestCommentPageTwoNodes = @([pscustomobject][ordered]@{
                id = 'pr-comment-101'
                body = $pageTwoCompletion
                createdAt = '2026-08-01T00:02:00Z'
                url = 'https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/790#issuecomment-101'
                author = [pscustomobject]@{ login = 'automation-agent' }
                authorAssociation = 'MEMBER'
                includesCreatedEdit = $false
            })
        $pullRequestCommentsPageOne = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName comments -Nodes $pullRequestCommentPageOneNodes -TotalCount 101 -HasNextPage $true -EndCursor 'pr-comments-page-2'
        $pullRequestCommentsPageTwo = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName comments -Nodes $pullRequestCommentPageTwoNodes -TotalCount 101
        New-TestFile -Path $fakeGitHub.PullRequestCommentsPath -Content (($pullRequestCommentsPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.PullRequestCommentsNextPath -Content (($pullRequestCommentsPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $pagedPullRequestCommentSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($pagedPullRequestCommentSelector.ExitCode -eq 0) -Message "Paged PR-comment selector failed: $($pagedPullRequestCommentSelector.Output)"
        $pagedPullRequestCommentJson = ConvertFrom-LastAutomationJson -Output $pagedPullRequestCommentSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$pagedPullRequestCommentJson.Selected -and @($pagedPullRequestCommentJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'HandoffCompleted' }).Count -eq 1) -Message 'A page-2 PR completion did not resolve the current handoff.'
        New-TestFile -Path $fakeGitHub.PullRequestCommentsPath -Content $baselinePullRequestComments
        New-TestFile -Path $fakeGitHub.PullRequestCommentsNextPath -Content $baselinePullRequestCommentsNext

        $pullRequestReviewPageOneNodes = @(for ($reviewIndex = 1; $reviewIndex -le 100; $reviewIndex++) {
                [pscustomobject][ordered]@{
                    id = "pr-review-$reviewIndex"
                    body = "Non-decisive review $reviewIndex"
                    submittedAt = $null
                    author = [pscustomobject]@{ login = 'reviewer-agent' }
                    authorAssociation = 'MEMBER'
                    state = 'PENDING'
                    commit = $null
                }
            })
        $pullRequestReviewPageTwoNodes = @([pscustomobject][ordered]@{
                id = 'pr-review-101'
                body = 'Page-two approval'
                submittedAt = '2026-08-01T00:02:00Z'
                author = [pscustomobject]@{ login = 'reviewer-agent' }
                authorAssociation = 'MEMBER'
                state = 'APPROVED'
                commit = [pscustomobject]@{ oid = $head }
            })
        $pullRequestReviewsPageOne = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes $pullRequestReviewPageOneNodes -TotalCount 101 -HasNextPage $true -EndCursor 'pr-reviews-page-2'
        $pullRequestReviewsPageTwo = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes $pullRequestReviewPageTwoNodes -TotalCount 101
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content (($pullRequestReviewsPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.PullRequestReviewsNextPath -Content (($pullRequestReviewsPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $pagedPullRequestReviewSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($pagedPullRequestReviewSelector.ExitCode -eq 0) -Message "Paged PR-review selector failed: $($pagedPullRequestReviewSelector.Output)"
        $pagedPullRequestReviewJson = ConvertFrom-LastAutomationJson -Output $pagedPullRequestReviewSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$pagedPullRequestReviewJson.Selected -and @($pagedPullRequestReviewJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'ReviewPassAfterHandoff' }).Count -eq 1) -Message 'A page-2 native approval did not resolve the current handoff.'
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content $baselinePullRequestReviews
        New-TestFile -Path $fakeGitHub.PullRequestReviewsNextPath -Content $baselinePullRequestReviewsNext

        $driftLabelPageOne = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes @() -TotalCount 1 -HasNextPage $true -EndCursor 'issue-labels-page-2'
        $driftLabelPageTwo = New-TestGraphQLConnectionResponse -ParentName issue -ConnectionName labels -Nodes @([pscustomobject][ordered]@{ id = 'drift-label'; name = 'drift' }) -TotalCount 2
        New-TestFile -Path $fakeGitHub.IssueLabelsPath -Content (($driftLabelPageOne | ConvertTo-Json -Depth 16 -Compress) + "`n")
        New-TestFile -Path $fakeGitHub.IssueLabelsNextPath -Content (($driftLabelPageTwo | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $paginationDriftSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($paginationDriftSelector.ExitCode -ne 0) -Message 'Selector accepted a connection whose totalCount changed during pagination.'
        $paginationDriftJson = ConvertFrom-LastAutomationJson -Output $paginationDriftSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$paginationDriftJson.Succeeded -and @($paginationDriftJson.Errors | Where-Object { $_ -match 'totalCount changed' }).Count -eq 1) -Message 'Pagination drift did not produce an explicit selector error.'
        New-TestFile -Path $fakeGitHub.IssueLabelsPath -Content $baselineIssueLabels
        New-TestFile -Path $fakeGitHub.IssueLabelsNextPath -Content $baselineIssueLabelsNext

        New-Item -ItemType Directory -Path $fakeGitHub.LeaseDirectory -ErrorAction Stop | Out-Null
        $leasePath = Join-Path $fakeGitHub.LeaseDirectory 'pr-790.json'
        $activeLease = [pscustomobject][ordered]@{
            SchemaVersion = 1
            PullRequestNumber = 790
            HeadSha = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
            LeaseId = [Guid]::NewGuid().ToString('D')
            AcquiredAt = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
            ExpiresAt = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o')
        }
        New-TestFile -Path $leasePath -Content (($activeLease | ConvertTo-Json -Compress) + "`n")
        $leasedSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($leasedSelector.ExitCode -eq 0) -Message "Active-lease selector fixture failed unexpectedly: $($leasedSelector.Output)"
        $leasedJson = ConvertFrom-LastAutomationJson -Output $leasedSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$leasedJson.Selected -and @($leasedJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'ActiveDeveloperLease' }).Count -eq 1) -Message 'Live lease file did not exclude the active PR.'

        New-TestFile -Path $leasePath -Content "{`n"
        $invalidLeaseSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($invalidLeaseSelector.ExitCode -eq 0) -Message "Malformed-lease selector fixture failed the whole queue: $($invalidLeaseSelector.Output)"
        $invalidLeaseJson = ConvertFrom-LastAutomationJson -Output $invalidLeaseSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$invalidLeaseJson.Selected -and @($invalidLeaseJson.ExcludedCandidates | Where-Object { $_.Reason -ceq 'InvalidDeveloperLease' }).Count -eq 1) -Message 'Malformed live lease did not fail the candidate closed.'

        $invalidReviewResponse = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes @([pscustomobject][ordered]@{
                id = 'review-without-submitted-at'
                state = 'APPROVED'
                author = [pscustomobject]@{ login = 'reviewer-agent' }
                authorAssociation = 'MEMBER'
                commit = [pscustomobject]@{ oid = $head }
                body = 'Approval with a missing immutable timestamp'
            })
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content (($invalidReviewResponse | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $invalidReviewSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($invalidReviewSelector.ExitCode -ne 0) -Message 'Live adapter accepted a review without submittedAt.'
        $invalidReviewJson = ConvertFrom-LastAutomationJson -Output $invalidReviewSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$invalidReviewJson.Succeeded -and @($invalidReviewJson.Errors | Where-Object { $_ -match 'submittedAt' }).Count -eq 1) -Message 'Missing review submittedAt did not produce an explicit selector error.'
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content $baselinePullRequestReviews

        $unknownReviewStateResponse = New-TestGraphQLConnectionResponse -ParentName pullRequest -ConnectionName reviews -Nodes @([pscustomobject][ordered]@{
                id = 'review-with-unknown-state'
                state = 'FUTURE_STATE'
                author = [pscustomobject]@{ login = 'reviewer-agent' }
                authorAssociation = 'MEMBER'
                commit = [pscustomobject]@{ oid = $head }
                body = 'Review with an unknown future state'
            })
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content (($unknownReviewStateResponse | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $unknownReviewStateSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($unknownReviewStateSelector.ExitCode -ne 0) -Message 'Live adapter accepted an unknown review state.'
        $unknownReviewStateJson = ConvertFrom-LastAutomationJson -Output $unknownReviewStateSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$unknownReviewStateJson.Succeeded -and @($unknownReviewStateJson.Errors | Where-Object { $_ -match 'unknown state' }).Count -eq 1) -Message 'Unknown review state did not produce an explicit selector error.'
        New-TestFile -Path $fakeGitHub.PullRequestReviewsPath -Content $baselinePullRequestReviews

        $partialFields = Get-Content -Raw -LiteralPath $fakeGitHub.FieldPath | ConvertFrom-Json
        $partialFields.totalCount = 5
        New-TestFile -Path $fakeGitHub.FieldPath -Content (($partialFields | ConvertTo-Json -Depth 16 -Compress) + "`n")
        $partialSelector = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters $baseParameters
        Assert-AutomationTest -Condition ($partialSelector.ExitCode -ne 0) -Message 'Selector accepted a partial GitHub Project field page.'
        $partialJson = ConvertFrom-LastAutomationJson -Output $partialSelector.Output
        Assert-AutomationTest -Condition (-not [bool]$partialJson.Succeeded -and @($partialJson.Errors | Where-Object { $_ -match 'partial or uncounted page' }).Count -eq 1) -Message 'Partial page failure was not explicit.'

        $fixtureBypassPath = Join-Path $script:temporaryRoot 'ungated-selector-fixture.json'
        New-TestFile -Path $fixtureBypassPath -Content '{"ProjectFields":[],"Candidates":[]}'
        $fixtureBypass = Invoke-AutomationChildScript -ScriptPath $selectorPath -WorkingDirectory $repository -Parameters @{ FixturePath = $fixtureBypassPath }
        Assert-AutomationTest -Condition ($fixtureBypass.ExitCode -ne 0) -Message 'Production invocation accepted fixture mode without the owned harness gate.'
        $fixtureBypassJson = ConvertFrom-LastAutomationJson -Output $fixtureBypass.Output
        Assert-AutomationTest -Condition ([string]$fixtureBypassJson.DataSource -ceq 'Fixture') -Message 'Fixture attempt was not visibly distinguished from live data.'
        }
        finally {
            [Environment]::SetEnvironmentVariable('TEMP', $previousTemp, 'Process')
            [Environment]::SetEnvironmentVariable('TMP', $previousTmp, 'Process')
        }
    }

    Invoke-AutomationTestCase -Name 'DeveloperLeaseIsAtomicOwnerBoundAndWhatIfSafe' -Body {
        $leaseScript = Join-Path $repository 'Tools\Automation\Use-DeveloperLease.ps1'
        $head = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1'
        $leaseId = [Guid]::NewGuid().ToString('D')
        $otherLeaseId = [Guid]::NewGuid().ToString('D')
        $replacementLeaseId = [Guid]::NewGuid().ToString('D')
        $leasePath = Join-Path $script:temporaryRoot 'SashimiBoyAutomation\DeveloperLeases\pr-791.json'
        $baseParameters = @{
            PullRequestNumber = 791
            HeadSha = $head
            LeaseId = $leaseId
            LeaseMinutes = 30
        }
        $previousTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
        $previousTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
        $raceProcesses = @()
        try {
            [Environment]::SetEnvironmentVariable('TEMP', $script:temporaryRoot, 'Process')
            [Environment]::SetEnvironmentVariable('TMP', $script:temporaryRoot, 'Process')

            $whatIfAcquireParameters = @{} + $baseParameters
            $whatIfAcquireParameters.Action = 'Acquire'
            $whatIfAcquireParameters.WhatIf = $true
            $whatIfAcquire = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $whatIfAcquireParameters
            Assert-AutomationTest -Condition ($whatIfAcquire.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $leasePath)) -Message "Lease Acquire -WhatIf mutated state or failed: $($whatIfAcquire.Output)"
            $whatIfAcquireJson = ConvertFrom-LastAutomationJson -Output $whatIfAcquire.Output
            Assert-AutomationTest -Condition (-not [bool]$whatIfAcquireJson.Changed -and -not [bool]$whatIfAcquireJson.Cancelled -and -not [bool]$whatIfAcquireJson.Exists) -Message 'Lease Acquire -WhatIf reported a mutation or cancellation.'

            $wrongCaseParameters = @{} + $baseParameters
            $wrongCaseParameters.Action = 'acquire'
            $wrongCase = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $wrongCaseParameters
            Assert-AutomationTest -Condition ($wrongCase.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $leasePath)) -Message 'Lease helper accepted non-canonical action casing.'

            $declineCommand = '& ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $leaseScript) +
                ' -Action Acquire -PullRequestNumber 791 -HeadSha ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $head) +
                ' -LeaseId ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $leaseId) +
                ' -LeaseMinutes 30 -Confirm'
            $declineEncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($declineCommand))
            $declineStartInfo = New-Object System.Diagnostics.ProcessStartInfo
            $declineStartInfo.FileName = $WindowsPowerShellPath
            $declineStartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $declineEncodedCommand"
            $declineStartInfo.UseShellExecute = $false
            $declineStartInfo.CreateNoWindow = $true
            $declineStartInfo.RedirectStandardInput = $true
            $declineStartInfo.RedirectStandardOutput = $true
            $declineStartInfo.RedirectStandardError = $true
            $declineProcess = New-Object System.Diagnostics.Process
            $declineProcess.StartInfo = $declineStartInfo
            try {
                Assert-AutomationTest -Condition $declineProcess.Start() -Message 'Could not start the lease confirmation-decline probe.'
                $declineOutputTask = $declineProcess.StandardOutput.ReadToEndAsync()
                $declineErrorTask = $declineProcess.StandardError.ReadToEndAsync()
                $declineProcess.StandardInput.WriteLine('N')
                $declineProcess.StandardInput.Close()
                Assert-AutomationTest -Condition ($declineProcess.WaitForExit(30000)) -Message 'Lease confirmation-decline probe timed out.'
                $declineOutput = $declineOutputTask.Result
                $declineError = $declineErrorTask.Result
                $declineCombined = $declineOutput + [Environment]::NewLine + $declineError
                Assert-AutomationTest -Condition ($declineProcess.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $leasePath)) -Message "Declined lease acquisition mutated state or failed: $declineCombined"
                $declineJson = ConvertFrom-LastAutomationJson -Output $declineCombined
                Assert-AutomationTest -Condition ([bool]$declineJson.Succeeded -and -not [bool]$declineJson.Changed -and [bool]$declineJson.Cancelled -and -not [bool]$declineJson.Exists -and -not [bool]$declineJson.Active) -Message 'Declined lease acquisition did not report its unchanged cancelled state.'
            }
            finally {
                if (-not $declineProcess.HasExited) {
                    $declineProcess.Kill()
                    [void]$declineProcess.WaitForExit(5000)
                }
                $declineProcess.Dispose()
            }

            $acquireParameters = @{} + $baseParameters
            $acquireParameters.Action = 'Acquire'
            $acquire = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $acquireParameters
            Assert-AutomationTest -Condition ($acquire.ExitCode -eq 0 -and (Test-Path -LiteralPath $leasePath -PathType Leaf)) -Message "Lease acquisition failed: $($acquire.Output)"
            $acquireJson = ConvertFrom-LastAutomationJson -Output $acquire.Output
            Assert-AutomationTest -Condition ([bool]$acquireJson.Succeeded -and [bool]$acquireJson.Changed -and -not [bool]$acquireJson.Cancelled -and [string]$acquireJson.LeaseId -ceq $leaseId) -Message 'Lease acquisition did not preserve the unguessable lease identity or mutation state.'

            $inspect = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters @{ Action = 'Inspect'; PullRequestNumber = 791; HeadSha = $head }
            $inspectJson = ConvertFrom-LastAutomationJson -Output $inspect.Output
            Assert-AutomationTest -Condition ($inspect.ExitCode -eq 0 -and [bool]$inspectJson.Active -and [string]$inspectJson.LeaseId -ceq $leaseId) -Message "Lease inspection failed: $($inspect.Output)"
            $inspectOtherHead = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters @{ Action = 'Inspect'; PullRequestNumber = 791; HeadSha = 'cccccccccccccccccccccccccccccccccccccccc' }
            $inspectOtherHeadJson = ConvertFrom-LastAutomationJson -Output $inspectOtherHead.Output
            Assert-AutomationTest -Condition ($inspectOtherHead.ExitCode -eq 0 -and [bool]$inspectOtherHeadJson.Active -and [string]$inspectOtherHeadJson.HeadSha -ceq $head) -Message 'An active same-PR lease became invisible or reported the caller head after the PR head changed.'

            $validLeaseRecord = Get-Content -Raw -LiteralPath $leasePath
            $emptyLeaseIdRecord = $validLeaseRecord | ConvertFrom-Json
            $emptyLeaseIdRecord.LeaseId = [Guid]::Empty.ToString('D')
            New-TestFile -Path $leasePath -Content (($emptyLeaseIdRecord | ConvertTo-Json -Compress) + "`n")
            $inspectEmptyLeaseId = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters @{ Action = 'Inspect'; PullRequestNumber = 791; HeadSha = $head }
            $inspectEmptyLeaseIdJson = ConvertFrom-LastAutomationJson -Output $inspectEmptyLeaseId.Output
            Assert-AutomationTest -Condition ($inspectEmptyLeaseId.ExitCode -ne 0 -and -not [bool]$inspectEmptyLeaseIdJson.Succeeded -and [string]$inspectEmptyLeaseIdJson.Error -match 'malformed') -Message 'Lease inspection accepted Guid.Empty as a valid lease identity.'
            New-TestFile -Path $leasePath -Content $validLeaseRecord

            $secondAcquireParameters = @{} + $baseParameters
            $secondAcquireParameters.Action = 'Acquire'
            $secondAcquireParameters.LeaseId = $otherLeaseId
            $secondAcquire = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $secondAcquireParameters
            Assert-AutomationTest -Condition ($secondAcquire.ExitCode -ne 0) -Message 'A second run acquired an active lease for the same PR.'

            $wrongRenewParameters = @{} + $baseParameters
            $wrongRenewParameters.Action = 'Renew'
            $wrongRenewParameters.LeaseId = $otherLeaseId
            $wrongRenew = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $wrongRenewParameters
            Assert-AutomationTest -Condition ($wrongRenew.ExitCode -ne 0) -Message 'A different run renewed the active lease.'

            $renewParameters = @{} + $baseParameters
            $renewParameters.Action = 'Renew'
            $renew = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $renewParameters
            Assert-AutomationTest -Condition ($renew.ExitCode -eq 0) -Message "Lease renewal failed: $($renew.Output)"
            $renewJson = ConvertFrom-LastAutomationJson -Output $renew.Output
            Assert-AutomationTest -Condition ([bool]$renewJson.Changed -and -not [bool]$renewJson.Cancelled) -Message 'Lease renewal did not report an applied mutation.'

            $releaseWhatIfParameters = @{} + $baseParameters
            $releaseWhatIfParameters.Action = 'Release'
            $releaseWhatIfParameters.WhatIf = $true
            $releaseWhatIf = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $releaseWhatIfParameters
            Assert-AutomationTest -Condition ($releaseWhatIf.ExitCode -eq 0 -and (Test-Path -LiteralPath $leasePath -PathType Leaf)) -Message 'Lease Release -WhatIf removed the lease.'
            $releaseWhatIfJson = ConvertFrom-LastAutomationJson -Output $releaseWhatIf.Output
            Assert-AutomationTest -Condition (-not [bool]$releaseWhatIfJson.Changed -and -not [bool]$releaseWhatIfJson.Cancelled -and [bool]$releaseWhatIfJson.Exists -and [bool]$releaseWhatIfJson.Active) -Message 'Lease Release -WhatIf did not report unchanged active state.'

            $wrongReleaseParameters = @{} + $baseParameters
            $wrongReleaseParameters.Action = 'Release'
            $wrongReleaseParameters.LeaseId = $otherLeaseId
            $wrongRelease = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $wrongReleaseParameters
            Assert-AutomationTest -Condition ($wrongRelease.ExitCode -ne 0 -and (Test-Path -LiteralPath $leasePath -PathType Leaf)) -Message 'A different run released the active lease.'

            $releaseParameters = @{} + $baseParameters
            $releaseParameters.Action = 'Release'
            $release = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $releaseParameters
            Assert-AutomationTest -Condition ($release.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $leasePath)) -Message "Lease release failed: $($release.Output)"

            $expiredRecord = [pscustomobject][ordered]@{
                SchemaVersion = 1
                PullRequestNumber = 791
                HeadSha = $head
                LeaseId = $leaseId
                AcquiredAt = [DateTimeOffset]::UtcNow.AddHours(-2).ToString('o')
                ExpiresAt = [DateTimeOffset]::UtcNow.AddHours(-1).ToString('o')
                ProcessId = 1
                MachineName = 'expired-fixture'
            }
            New-TestFile -Path $leasePath -Content (($expiredRecord | ConvertTo-Json -Compress) + "`n")
            $reacquireParameters = @{} + $baseParameters
            $reacquireParameters.Action = 'Acquire'
            $reacquireParameters.LeaseId = $replacementLeaseId
            $reacquire = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $reacquireParameters
            $reacquireJson = ConvertFrom-LastAutomationJson -Output $reacquire.Output
            Assert-AutomationTest -Condition ($reacquire.ExitCode -eq 0 -and [string]$reacquireJson.LeaseId -ceq $replacementLeaseId) -Message "Expired lease was not safely reclaimed: $($reacquire.Output)"

            $finalReleaseParameters = @{} + $baseParameters
            $finalReleaseParameters.Action = 'Release'
            $finalReleaseParameters.LeaseId = $replacementLeaseId
            $finalRelease = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters $finalReleaseParameters
            Assert-AutomationTest -Condition ($finalRelease.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $leasePath)) -Message 'Replacement lease was not released.'

            $racePullRequest = 792
            $raceHead = 'dddddddddddddddddddddddddddddddddddddddd'
            $raceLeaseIds = @([Guid]::NewGuid().ToString('D'), [Guid]::NewGuid().ToString('D'))
            $raceReadyPaths = @(
                (Join-Path $script:temporaryRoot 'lease-race-ready-0'),
                (Join-Path $script:temporaryRoot 'lease-race-ready-1')
            )
            $raceResultPaths = @(
                (Join-Path $script:temporaryRoot 'lease-race-result-0.json'),
                (Join-Path $script:temporaryRoot 'lease-race-result-1.json')
            )
            $raceGoPath = Join-Path $script:temporaryRoot 'lease-race-go'
            for ($raceIndex = 0; $raceIndex -lt 2; $raceIndex++) {
                $raceCommand = @(
                    '$ErrorActionPreference = ''Continue''',
                    ('[System.IO.File]::WriteAllText(' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $raceReadyPaths[$raceIndex]) + ', ''ready'')'),
                    ('$goPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $raceGoPath)),
                    '$deadline = [DateTime]::UtcNow.AddSeconds(20)',
                    'while (-not (Test-Path -LiteralPath $goPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 10 }',
                    'if (-not (Test-Path -LiteralPath $goPath)) { exit 99 }',
                    ('$output = @(& ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $leaseScript) + ' -Action Acquire -PullRequestNumber ' + $racePullRequest + ' -HeadSha ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $raceHead) + ' -LeaseId ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $raceLeaseIds[$raceIndex]) + ' -LeaseMinutes 30 2>&1)'),
                    '$capturedExitCode = $LASTEXITCODE',
                    ('$resultPath = ' + (ConvertTo-SingleQuotedPowerShellLiteral -Value $raceResultPaths[$raceIndex])),
                    '$result = [pscustomobject]@{ ExitCode = [int]$capturedExitCode; Output = [string]::Join([Environment]::NewLine, [string[]]$output) }',
                    '[System.IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))'
                )
                $raceEncodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes([string]::Join('; ', $raceCommand)))
                $raceProcesses += Start-Process `
                    -FilePath $WindowsPowerShellPath `
                    -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $raceEncodedCommand) `
                    -PassThru `
                    -WindowStyle Hidden
            }
            $readyDeadline = [DateTime]::UtcNow.AddSeconds(20)
            while (@($raceReadyPaths | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 2 -and [DateTime]::UtcNow -lt $readyDeadline) {
                Start-Sleep -Milliseconds 10
            }
            Assert-AutomationTest -Condition (@($raceReadyPaths | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 2) -Message 'Concurrent lease competitors did not reach the barrier.'
            New-TestFile -Path $raceGoPath -Content 'go'
            foreach ($raceProcess in $raceProcesses) {
                Assert-AutomationTest -Condition ($raceProcess.WaitForExit(30000)) -Message 'Concurrent lease competitor timed out.'
            }
            $raceResults = @($raceResultPaths | ForEach-Object { Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json })
            $raceWinners = @($raceResults | Where-Object { [int]$_.ExitCode -eq 0 })
            $raceLosers = @($raceResults | Where-Object { [int]$_.ExitCode -ne 0 })
            Assert-AutomationTest -Condition ($raceWinners.Count -eq 1 -and $raceLosers.Count -eq 1) -Message 'Concurrent same-PR Acquire did not produce exactly one winner and one loser.'
            $raceWinnerJson = ConvertFrom-LastAutomationJson -Output ([string]$raceWinners[0].Output)
            $raceLeasePath = Join-Path $script:temporaryRoot 'SashimiBoyAutomation\DeveloperLeases\pr-792.json'
            $raceLeaseRecord = Get-Content -Raw -LiteralPath $raceLeasePath | ConvertFrom-Json
            Assert-AutomationTest -Condition ([string]$raceLeaseRecord.LeaseId -ceq [string]$raceWinnerJson.LeaseId) -Message 'Final lease file does not belong to the atomic Acquire winner.'
            $raceRelease = Invoke-AutomationChildScript -ScriptPath $leaseScript -WorkingDirectory $repository -Parameters @{
                Action = 'Release'
                PullRequestNumber = $racePullRequest
                HeadSha = $raceHead
                LeaseId = [string]$raceWinnerJson.LeaseId
            }
            Assert-AutomationTest -Condition ($raceRelease.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $raceLeasePath)) -Message 'Concurrent Acquire winner lease was not released.'
        }
        finally {
            foreach ($raceProcess in $raceProcesses) {
                if ($null -ne $raceProcess -and -not $raceProcess.HasExited) {
                    $raceProcess.Kill()
                    [void]$raceProcess.WaitForExit(5000)
                }
                if ($null -ne $raceProcess) {
                    $raceProcess.Dispose()
                }
            }
            [Environment]::SetEnvironmentVariable('TEMP', $previousTemp, 'Process')
            [Environment]::SetEnvironmentVariable('TMP', $previousTmp, 'Process')
        }
    }

    Invoke-AutomationTestCase -Name 'DeveloperResumeDocumentationAndReadOnlyContractIsExplicit' -Body {
        $workflow = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\WORKFLOW.md'))
        $developer = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\DEVELOPER.md'))
        $reviewer = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\REVIEWER.md'))
        $bootstrap = [System.IO.File]::ReadAllText((Join-Path $repository 'Docs\Automation\BOOTSTRAP.md'))
        $selector = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Get-DeveloperWorkItem.ps1'))
        $lease = [System.IO.File]::ReadAllText((Join-Path $repository 'Tools\Automation\Use-DeveloperLease.ps1'))
        $bootstrapNormalized = ($bootstrap -replace '\s+', ' ').Trim()

        $neutralBootstrap = 'Use the live Project and repository selector to choose exactly one eligible Developer item according to DEVELOPER.md.'
        Assert-AutomationTest -Condition ($bootstrapNormalized.Contains($neutralBootstrap)) -Message 'Developer bootstrap is missing the neutral repository-selector instruction.'
        Assert-AutomationTest -Condition ($bootstrap -notmatch 'select exactly one Ready Issue') -Message 'Developer bootstrap still hard-codes a Ready-only queue.'
        Assert-AutomationTest -Condition ($bootstrapNormalized.IndexOf($neutralBootstrap, [StringComparison]::Ordinal) -lt $bootstrapNormalized.IndexOf('including its preflight', [StringComparison]::Ordinal)) -Message 'Developer bootstrap runs preflight before pinning the selector result.'
        Assert-AutomationTest -Condition ($bootstrap -notmatch 'On any required-check failure, make no state change') -Message 'Developer bootstrap forbids its repository-defined safe failure handoff.'
        Assert-AutomationTest -Condition ($bootstrap -match '(?s)repository-defined safe failure handoff.*make no Project status transition') -Message 'Developer bootstrap does not bound failure handling to safe handoff without status advance.'

        foreach ($requiredWorkflowText in @(
                'P0/P1 `ReviewFix`',
                'P0/P1 `DeliveryResume`',
                'P2/P3 `ReviewFix`',
                'P2/P3 `DeliveryResume`',
                'P0/P1 `NewWork`',
                'P2/P3 `NewWork`',
                'ProjectV2Item.updatedAt',
                'sashimi-boy-automation-handoff:v1',
                'sashimi-boy-automation-handoff-completion:v1',
                'sashimi-boy-automation-owner-decision:v1',
                'DataSource=Live',
                'Use-DeveloperLease.ps1'
            )) {
            Assert-AutomationTest -Condition ($workflow.Contains($requiredWorkflowText)) -Message "WORKFLOW is missing Developer queue contract text: $requiredWorkflowText"
        }

        foreach ($requiredDeveloperText in @(
                'exactly one linked',
                'live `head.sha` and `head.ref`',
                'unique local-only agent',
                'latest `origin/main`',
                'git push origin HEAD:<existing-pr-head-ref>',
                'do not create an empty commit',
                'do not perform a start-of-run status mutation',
                'For `ReviewFix` or `DeliveryResume` only',
                'Immediately before every remote mutation'
            )) {
            Assert-AutomationTest -Condition ($developer.Contains($requiredDeveloperText)) -Message "DEVELOPER is missing resume contract text: $requiredDeveloperText"
        }
        Assert-AutomationTest -Condition ($developer -match '(?s)Do not create a new Issue, PR, or remote feature\s+branch') -Message 'DEVELOPER does not forbid new resume delivery state.'
        Assert-AutomationTest -Condition ($developer -match 'Do not rebase or force\s+push') -Message 'DEVELOPER does not forbid rebase/force push in resume mode.'
        Assert-AutomationTest -Condition ($reviewer -match '(?s)post the focused finding.*New-AutomationHandoff\.ps1.*Review -> In Progress') -Message 'REVIEWER does not enforce finding then handoff then status order.'
        Assert-AutomationTest -Condition ($reviewer -match 'non-empty absolute URL') -Message 'REVIEWER does not require deterministic finding evidence.'
        Assert-AutomationTest -Condition ($selector -match "SASHIMI_BOY_AUTOMATION_TEST_HARNESS.*-cne '1'" -and $selector -match 'DataSource') -Message 'Selector fixture mode is not visibly gated to the smoke harness.'
        Assert-AutomationTest -Condition ($lease -match 'SupportsShouldProcess' -and $lease -match 'System\.Threading\.Mutex' -and $lease -match 'FileMode\]::CreateNew') -Message 'Developer lease helper lacks atomic/WhatIf mechanics.'
        Assert-AutomationTest -Condition ($lease -match 'Developer lease is owned by another run or PR head') -Message 'Developer lease release/renew is not identity-bound.'

        foreach ($forbiddenSelectorPattern in @(
                '(?i)project\s+item-edit',
                "(?i)'issue'\s*,\s*'edit'",
                "(?i)'pr'\s*,\s*'create'",
                "(?i)'pr'\s*,\s*'edit'",
                "(?i)'pr'\s*,\s*'merge'",
                "(?i)'git'\s*,\s*'push'"
            )) {
            Assert-AutomationTest -Condition (-not [regex]::IsMatch($selector, $forbiddenSelectorPattern)) -Message "Read-only selector contains a mutation path: $forbiddenSelectorPattern"
        }
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

    Invoke-AutomationTestCase -Name 'SyntheticMergeNeverAdoptsPreExistingWorkspace' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $integrationTemp = Join-Path $script:temporaryRoot 'exclusive integration collision'
        $fixedLeaf = 'ReviewIntegration-20000101T000000Z-0123456789abcdef0123456789abcdef'
        $preExistingWorkspace = Join-Path $integrationTemp $fixedLeaf
        $sentinelPath = Join-Path $preExistingWorkspace 'non-owner-sentinel.txt'
        New-TestFile -Path $sentinelPath -Content "must survive`n"
        $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber     = 23
                RepositoryUrl         = $script:fixture.OriginPath
                TempRoot              = $integrationTemp
                InternalWorkspaceLeaf = $fixedLeaf
            }
        }
        finally {
            [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
        }

        Assert-AutomationTest -Condition ($integration.ExitCode -ne 0) -Message 'Synthetic merge adopted a pre-existing unowned workspace.'
        Assert-AutomationTest -Condition (Test-Path -LiteralPath $sentinelPath -PathType Leaf) -Message 'Synthetic merge deleted or overwrote a pre-existing sentinel.'
        Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath (Join-Path $preExistingWorkspace '.sashimi-boy-automation-owned.json'))) -Message 'Synthetic merge wrote an ownership marker into a pre-existing workspace.'
        $json = ConvertFrom-LastAutomationJson -Output $integration.Output
        Assert-AutomationTest -Condition (-not [bool]$json.Success -and -not [bool]$json.WorkspaceCleaned) -Message 'Pre-existing workspace collision was not reported as a non-cleanup failure.'
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
        try {
            New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
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

        $workspace = $null
        try {
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = 'https://example.invalid/repository.git'
                TempRoot          = $integrationTemp
                GitExecutable     = $fakeGitPath
            }
            $json = ConvertFrom-LastAutomationJson -Output $integration.Output
            $workspace = [string]$json.WorkspaceRoot
            Assert-AutomationTest -Condition ($integration.ExitCode -eq 12) -Message "Dual primary/cleanup failure did not return cleanup exit 12: $($integration.Output)"
            Assert-AutomationTest -Condition ([string]$json.PrimaryError.Message -match 'exit code 7.*intentional clone exit 7') -Message "Primary clone failure was not preserved: $($integration.Output)"
            Assert-AutomationTest -Condition ([string]$json.Error.Message -eq [string]$json.PrimaryError.Message) -Message 'Backward-compatible Error no longer reports the primary failure.'
            Assert-AutomationTest -Condition ([string]$json.CleanupError.Message -match 'reparse point') -Message "Cleanup failure was not recorded separately: $($integration.Output)"
        }
        finally {
            if (Test-Path -LiteralPath $integrationTemp -PathType Container) {
                foreach ($retainedWorkspace in @(Get-ChildItem -LiteralPath $integrationTemp -Directory -Filter 'ReviewIntegration-*' -Force -ErrorAction SilentlyContinue)) {
                    $retainedRepository = Join-Path $retainedWorkspace.FullName 'Repository'
                    if (Test-Path -LiteralPath $retainedRepository) {
                        $retainedRepositoryItem = Get-Item -LiteralPath $retainedRepository -Force -ErrorAction Stop
                        if (($retainedRepositoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                            [System.IO.Directory]::Delete($retainedRepository, $false)
                        }
                    }
                }
            }
        }

        Assert-AutomationTest -Condition (-not [string]::IsNullOrWhiteSpace($workspace)) -Message 'Dual-failure test did not report its retained workspace.'
        $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
            WorkspaceRoot = $workspace
            TempRoot      = $integrationTemp
        }
        Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Dual-failure retained workspace could not be safely cleaned: $($cleanup.Output)"
        Assert-AutomationTest -Condition (Test-Path -LiteralPath (Join-Path $junctionTarget 'target-sentinel.txt') -PathType Leaf) -Message 'Dual-failure cleanup modified the junction target.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperAllowsExpectedLogAssertAndHandlesArguments' -Body {
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
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { [string]$_.Category -match 'Console|LogError' }).Count -eq 0) -Message 'Expected LogAssert output was reclassified as a new Console error.'
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

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsUnexpectedErrorViaFailedNUnitResult' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Console Error Unity\6000.4.0f1\Editor\Unity.cmd'
        $diagnosticUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            'set "resultFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
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
            ':writeLog',
            'if not defined resultFile if defined logFile echo Clean compile/import fixture>"%logFile%"',
            'if defined resultFile if defined logFile echo Error: unexpected test error>"%logFile%"',
            'if defined resultFile if defined logFile echo UnityEngine.Debug:LogError ^(object^)>>"%logFile%"',
            'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Failed" total="1" passed="0" failed="1" inconclusive="0" skipped="0" duration="0.1" /^>"%resultFile%"',
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
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted an unexpected test error with failed NUnit XML.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        $nonAuthoritativeStages = @($json.Stages.EditMode, $json.Stages.PlayMode | Where-Object { -not [bool]$_.AuthoritativePass })
        Assert-AutomationTest -Condition ($nonAuthoritativeStages.Count -ge 1) -Message 'Unexpected Error regression was not rejected through authoritative failed NUnit XML.'
    }

    Invoke-AutomationTestCase -Name 'UnityWrapperRejectsOutOfRunConsoleErrorWithPassedNUnitXml' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:unityProjectPath) -Message 'Unity success smoke did not create a project clone.'
        $diagnosticUnityPath = Join-Path $script:temporaryRoot 'Out Of Run Console Unity\6000.4.0f1\Editor\Unity.cmd'
        $diagnosticUnity = @(
            '@echo off',
            'setlocal EnableExtensions',
            'set "logFile="',
            'set "resultFile="',
            ':parse',
            'if "%~1"=="" goto writeLog',
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
            ':writeLog',
            'if not defined resultFile if defined logFile echo Clean compile/import fixture>"%logFile%"',
            'if defined resultFile if defined logFile echo Error: outside NUnit execution>"%logFile%"',
            'if defined resultFile if defined logFile echo Running tests for ExecutionSettings with details:>>"%logFile%"',
            'if defined resultFile if defined logFile echo LogAssert.Expect matched an expected in-test error.>>"%logFile%"',
            'if defined resultFile if defined logFile echo UnityEngine.Debug:LogError ^(object^)>>"%logFile%"',
            'if defined resultFile if defined logFile echo Test run completed. Exiting with code 0 >>"%logFile%"',
            'if defined resultFile echo ^<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1" /^>>"%resultFile%"',
            'exit /b 0'
        )
        New-TestFile -Path $diagnosticUnityPath -Content ([string]::Join("`r`n", $diagnosticUnity) + "`r`n")
        $artifactsPath = Join-Path $script:temporaryRoot 'Unity Artifacts\out of run console error'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
            ProjectPath          = $script:unityProjectPath
            ArtifactsPath        = $artifactsPath
            UnityExecutable      = $diagnosticUnityPath
            ExpectedUnityVersion = '6000.4.0f1'
        }
        Assert-AutomationTest -Condition ($unity.ExitCode -ne 0) -Message 'Unity wrapper accepted an out-of-run Console error with Passed NUnit XML.'
        $json = ConvertFrom-LastAutomationJson -Output $unity.Output
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'OutOfRunConsoleError' }).Count -ge 1) -Message 'Out-of-run Console error was not reported as structured diagnostic evidence.'
        Assert-AutomationTest -Condition (@($json.Diagnostics | Where-Object { $_.Category -eq 'OutOfRunConsoleLogError' }).Count -eq 0) -Message 'Expected in-run LogAssert output was reclassified as out-of-run.'
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
            },
            [ordered]@{
                Name = 'skipped strict result'
                Xml  = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="0" failed="0" inconclusive="0" skipped="1" duration="0.1" />'
                ErrorPattern = 'Skipped tests are not allowed'
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

    Invoke-AutomationTestCase -Name 'UnityWrapperAllowsOnlyExactDisposableUnityDrift' -Body {
        Assert-AutomationTest -Condition ($null -ne $script:fixture) -Message 'Smoke fixture setup failed.'
        $integrationScript = Join-Path $repository 'Tools\Automation\New-ReviewIntegration.ps1'
        $cleanupScript = Join-Path $repository 'Tools\Automation\Remove-ReviewIntegration.ps1'
        $unityScript = Join-Path $repository 'Tools\Automation\Invoke-UnityTests.ps1'
        $protectedFixturePaths = @(
            $script:fixture.BasePath,
            $script:fixture.DeveloperPath,
            $script:fixture.ReviewerPath
        )
        $scenarios = @(
            [ordered]@{ Name = 'ArtifactsInsideWorkspaceRejected'; Mode = 'Exact'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $true },
            [ordered]@{ Name = 'Exact'; Mode = 'Exact'; ShouldPass = $true; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'NoFinalNewline'; Mode = 'NoFinalNewline'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'HeaderLikeContent'; Mode = 'HeaderLikeContent'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'RelocatedApprovedField'; Mode = 'RelocatedApprovedField'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'ExtraField'; Mode = 'ExtraField'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'SecondProjectSettingsFile'; Mode = 'SecondProjectSettingsFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'AssetsFile'; Mode = 'AssetsFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false },
            [ordered]@{ Name = 'PackagesFile'; Mode = 'PackagesFile'; ShouldPass = $false; EnableTestHarness = $true; ArtifactsInsideWorkspace = $false }
        )

        foreach ($scenario in $scenarios) {
            $scenarioName = [string]$scenario.Name
            $mode = [string]$scenario.Mode
            $fakeUnityPath = New-DisposableDriftUnityFixture -Root (Join-Path $script:temporaryRoot 'Disposable Drift Unity') -Mode $mode
            $integrationTemp = Join-Path $script:temporaryRoot ("disposable drift integration $scenarioName")
            $integration = Invoke-AutomationChildScript -ScriptPath $integrationScript -WorkingDirectory $repository -Parameters @{
                PullRequestNumber = 23
                RepositoryUrl     = $script:fixture.OriginPath
                TempRoot          = $integrationTemp
                KeepWorkspace     = $true
            }
            Assert-AutomationTest -Condition ($integration.ExitCode -eq 0) -Message "Disposable drift integration setup failed for $mode`: $($integration.Output)"
            $integrationJson = ConvertFrom-LastAutomationJson -Output $integration.Output
            $workspace = [string]$integrationJson.WorkspaceRoot
            try {
                $artifactsPath = if ([bool]$scenario.ArtifactsInsideWorkspace) {
                    Join-Path $workspace 'Artifacts'
                }
                else {
                    Join-Path $script:temporaryRoot ("Unity Artifacts\disposable drift $scenarioName")
                }
                $previousHarness = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', 'Process')
                try {
                    if ([bool]$scenario.EnableTestHarness) {
                        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', '1', 'Process')
                    }
                    else {
                        [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $null, 'Process')
                    }
                    $unity = Invoke-AutomationChildScript -ScriptPath $unityScript -WorkingDirectory $repository -Parameters @{
                        ProjectPath          = [string]$integrationJson.IntegrationPath
                        ArtifactsPath        = $artifactsPath
                        UnityExecutable      = $fakeUnityPath
                        ExpectedUnityVersion = '6000.4.0f1'
                        InternalRequiredPersistentPath = $protectedFixturePaths
                    }
                }
                finally {
                    [Environment]::SetEnvironmentVariable('SASHIMI_BOY_AUTOMATION_TEST_HARNESS', $previousHarness, 'Process')
                }
                $unityJson = ConvertFrom-LastAutomationJson -Output $unity.Output
                if ([bool]$scenario.ShouldPass) {
                    Assert-AutomationTest -Condition ($unity.ExitCode -eq 0 -and [bool]$unityJson.Success) -Message "Exact disposable drift was not accepted: $($unity.Output)"
                    Assert-AutomationTest -Condition ([string]$unityJson.KnownDisposableUnityDrift.Classification -eq 'KnownDisposableUnityDrift' -and [bool]$unityJson.KnownDisposableUnityDrift.Allowed) -Message 'Exact disposable drift classification is missing.'
                    Assert-AutomationTest -Condition (@($unityJson.KnownDisposableUnityDrift.ProtectedWorktrees | Where-Object { $_.IsGitRoot -and $_.Clean -and -not $_.Error }).Count -eq 3) -Message 'Exact drift did not verify all three persistent worktrees as clean.'
                    $diffPath = [string]$unityJson.KnownDisposableUnityDrift.DiffArtifactPath
                    $shaPath = [string]$unityJson.KnownDisposableUnityDrift.DiffSha256ArtifactPath
                    Assert-AutomationTest -Condition (Test-Path -LiteralPath $diffPath -PathType Leaf) -Message 'Known drift diff artifact is missing.'
                    Assert-AutomationTest -Condition (Test-Path -LiteralPath $shaPath -PathType Leaf) -Message 'Known drift SHA-256 artifact is missing.'
                    $actualDiffHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $diffPath).Hash.ToUpperInvariant()
                    Assert-AutomationTest -Condition ($actualDiffHash -eq [string]$unityJson.KnownDisposableUnityDrift.DiffSha256) -Message 'Known drift diff SHA-256 does not match its Summary evidence.'
                    $shaEvidence = [System.IO.File]::ReadAllText($shaPath)
                    Assert-AutomationTest -Condition ($shaEvidence -match ('^' + [regex]::Escape($actualDiffHash) + '  KnownDisposableUnityDrift\.diff\r?\n?$')) -Message 'Known drift SHA-256 artifact has an unexpected contract.'
                }
                else {
                    Assert-AutomationTest -Condition ($unity.ExitCode -ne 0 -and -not [bool]$unityJson.Success) -Message "Rejected drift scenario was accepted for $scenarioName`: $($unity.Output)"
                    Assert-AutomationTest -Condition (-not [bool]$unityJson.KnownDisposableUnityDrift.Allowed) -Message "Rejected drift scenario was classified as allowed for $scenarioName."
                    Assert-AutomationTest -Condition (@($unityJson.Failures | Where-Object { $_.Stage -eq 'WorkspaceMutation' }).Count -eq 1) -Message "Rejected drift scenario did not produce WorkspaceMutation for $scenarioName."
                }
            }
            finally {
                if ($workspace -and (Test-Path -LiteralPath $workspace -PathType Container)) {
                    $cleanup = Invoke-AutomationChildScript -ScriptPath $cleanupScript -WorkingDirectory $repository -Parameters @{
                        WorkspaceRoot = $workspace
                        TempRoot      = $integrationTemp
                    }
                    Assert-AutomationTest -Condition ($cleanup.ExitCode -eq 0) -Message "Disposable drift cleanup failed for $scenarioName`: $($cleanup.Output)"
                    Assert-AutomationTest -Condition (-not (Test-Path -LiteralPath $workspace)) -Message "Disposable drift workspace remains after cleanup for $scenarioName."
                }
            }
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

    $script:suiteCompleted = $true
    $script:suiteSucceeded = (@($script:testResults | Where-Object { -not $_.Passed }).Count -eq 0)
}
finally {
    $script:testRootPreserved = [bool]$KeepTemporaryFiles -and
        $script:ownedTestRootCreated -and
        $script:ownerMarkerWritten -and
        $script:suiteCompleted -and
        $script:suiteSucceeded
    if ($script:ownedTestRootCreated -and $script:temporaryRoot -and -not $script:testRootPreserved) {
        Remove-OwnedTestRoot `
            -Path $script:temporaryRoot `
            -AutomationRoot $automationTempRoot `
            -ExpectedRunId $script:testRunId `
            -MarkerWasWritten $script:ownerMarkerWritten
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
    TemporaryRoot = if ($script:testRootPreserved) { $script:temporaryRoot } else { $null }
}

$summary | ConvertTo-AutomationJson
if ($failed.Count -gt 0) {
    exit 1
}

exit 0
