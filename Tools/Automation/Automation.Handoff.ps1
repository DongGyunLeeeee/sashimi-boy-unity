#requires -Version 5.1

Set-StrictMode -Version Latest

function Assert-AutomationMarkerSingleLine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Value -match '[\r\n\x00-\x08\x0B\x0C\x0E-\x1F\x7F]' -or $Value.Contains('-->')) {
        throw "$Name must be a safe single-line HTML-comment value."
    }
}

function Assert-AutomationHttpUrlOrEmpty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $parsedUri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsedUri) -or
        @('http', 'https') -notcontains $parsedUri.Scheme) {
        throw "$Name must be an absolute HTTP(S) URL or empty."
    }
}

function Assert-AutomationHandoffContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('ReviewFix', 'DeliveryResume')]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$IssueNumber,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$PullRequestNumber,

        [Parameter(Mandatory = $true)]
        [string]$HeadSha,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Reviewer', 'Developer', 'Owner')]
        [string]$SourceRole,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$FindingUrl,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$PendingCommand
    )

    if (@('ReviewFix', 'DeliveryResume') -cnotcontains $Mode) {
        throw 'mode must use canonical casing: ReviewFix or DeliveryResume.'
    }
    if (@('Reviewer', 'Developer', 'Owner') -cnotcontains $SourceRole) {
        throw 'sourceRole must use canonical casing: Reviewer, Developer, or Owner.'
    }
    if ($HeadSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'head must be exactly one 40-character hexadecimal commit SHA.'
    }
    if ($Reason -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        throw 'reason must be a stable lower-kebab-case identifier.'
    }

    Assert-AutomationMarkerSingleLine -Value $Reason -Name 'reason'
    Assert-AutomationMarkerSingleLine -Value $FindingUrl -Name 'findingUrl'
    Assert-AutomationMarkerSingleLine -Value $PendingCommand -Name 'pendingCommand'
    Assert-AutomationHttpUrlOrEmpty -Value $FindingUrl -Name 'findingUrl'

    if ($Mode -eq 'ReviewFix') {
        if ($SourceRole -eq 'Reviewer') {
            if (@('review-blocker', 'review-major') -cnotcontains $Reason) {
                throw 'Reviewer ReviewFix handoff reason must be review-blocker or review-major.'
            }
            if ([string]::IsNullOrWhiteSpace($FindingUrl)) {
                throw 'Reviewer ReviewFix handoff findingUrl must not be empty.'
            }
            $reviewFindingUri = $null
            if (-not [Uri]::TryCreate($FindingUrl, [UriKind]::Absolute, [ref]$reviewFindingUri) -or
                $reviewFindingUri.Scheme -cne 'https') {
                throw 'Reviewer ReviewFix handoff findingUrl must use absolute HTTPS.'
            }
        }
        elseif ($SourceRole -eq 'Owner') {
            if ($Reason -cne 'owner-verification-fail') {
                throw 'Owner ReviewFix handoff reason must be owner-verification-fail.'
            }
        }
        else {
            throw 'ReviewFix handoff sourceRole must be Reviewer or Owner.'
        }
    }
    else {
        if ($SourceRole -cne 'Developer') {
            throw 'DeliveryResume handoff sourceRole must be Developer.'
        }
        $transientReasons = @(
            'unity-lock',
            'unity-process',
            'protected-worktree-dirty',
            'disk-space',
            'network',
            'authentication',
            'runner-failure',
            'required-check-transient'
        )
        if ($transientReasons -cnotcontains $Reason) {
            throw "DeliveryResume handoff reason is not an approved transient reason: $Reason"
        }
        if ([string]::IsNullOrWhiteSpace($PendingCommand)) {
            throw 'DeliveryResume handoff pendingCommand must not be empty.'
        }
    }
}

function ConvertFrom-AutomationMarkerFields {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FieldText,

        [Parameter(Mandatory = $true)]
        [string[]]$RequiredKeys,

        [Parameter(Mandatory = $true)]
        [string]$MarkerName
    )

    $values = @{}
    foreach ($line in @($FieldText -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        $fieldMatch = [regex]::Match($line, '^([A-Za-z][A-Za-z0-9]*):[ \t]*(.*)$')
        if (-not $fieldMatch.Success) {
            throw "$MarkerName contains an invalid field line: $line"
        }
        $key = $fieldMatch.Groups[1].Value
        if ($RequiredKeys -cnotcontains $key) {
            throw "$MarkerName contains an unknown or incorrectly cased field: $key"
        }
        if ($values.ContainsKey($key)) {
            throw "$MarkerName contains a duplicate field: $key"
        }
        $values[$key] = $fieldMatch.Groups[2].Value.TrimEnd()
    }

    foreach ($requiredKey in $RequiredKeys) {
        if (-not $values.ContainsKey($requiredKey)) {
            throw "$MarkerName is missing required field: $requiredKey"
        }
    }
    if ($values.Count -ne $RequiredKeys.Count) {
        throw "$MarkerName contains an unexpected field count."
    }

    return $values
}

function New-AutomationHandoffMarker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('ReviewFix', 'DeliveryResume')]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$IssueNumber,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$PullRequestNumber,

        [Parameter(Mandatory = $true)]
        [string]$HeadSha,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Reviewer', 'Developer', 'Owner')]
        [string]$SourceRole,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [AllowEmptyString()]
        [string]$FindingUrl = '',

        [AllowEmptyString()]
        [string]$PendingCommand = ''
    )

    $contract = @{
        Mode              = $Mode
        IssueNumber       = $IssueNumber
        PullRequestNumber = $PullRequestNumber
        HeadSha           = $HeadSha
        SourceRole        = $SourceRole
        Reason            = $Reason
        FindingUrl        = $FindingUrl
        PendingCommand    = $PendingCommand
    }
    Assert-AutomationHandoffContract @contract
    $lines = @(
        '<!-- sashimi-boy-automation-handoff:v1',
        "mode: $Mode",
        "issue: $IssueNumber",
        "pr: $PullRequestNumber",
        ('head: ' + $HeadSha.ToLowerInvariant()),
        "sourceRole: $SourceRole",
        "reason: $Reason",
        "findingUrl: $FindingUrl",
        "pendingCommand: $PendingCommand",
        '-->'
    )
    return [string]::Join("`n", $lines)
}

function Get-AutomationHandoffMarkers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $requiredKeys = @('mode', 'issue', 'pr', 'head', 'sourceRole', 'reason', 'findingUrl', 'pendingCommand')
    $pattern = '(?ms)\A\s*<!--[ \t]*sashimi-boy-automation-handoff:v1[ \t]*\r?\n(?<fields>.*?)\r?\n[ \t]*-->\s*\z'
    $results = New-Object 'System.Collections.Generic.List[object]'
    foreach ($markerMatch in [regex]::Matches($Content, $pattern)) {
        $values = ConvertFrom-AutomationMarkerFields `
            -FieldText $markerMatch.Groups['fields'].Value `
            -RequiredKeys $requiredKeys `
            -MarkerName 'automation handoff marker'

        $issueNumber = 0
        $pullRequestNumber = 0
        if (-not [int]::TryParse([string]$values['issue'], [ref]$issueNumber) -or $issueNumber -lt 1) {
            throw 'automation handoff marker issue must be a positive integer.'
        }
        if (-not [int]::TryParse([string]$values['pr'], [ref]$pullRequestNumber) -or $pullRequestNumber -lt 1) {
            throw 'automation handoff marker pr must be a positive integer.'
        }

        $contract = @{
            Mode              = [string]$values['mode']
            IssueNumber       = $issueNumber
            PullRequestNumber = $pullRequestNumber
            HeadSha           = [string]$values['head']
            SourceRole        = [string]$values['sourceRole']
            Reason            = [string]$values['reason']
            FindingUrl        = [string]$values['findingUrl']
            PendingCommand    = [string]$values['pendingCommand']
        }
        Assert-AutomationHandoffContract @contract
        $results.Add([pscustomobject][ordered]@{
                SchemaVersion     = 1
                Mode              = $contract.Mode
                IssueNumber       = $contract.IssueNumber
                PullRequestNumber = $contract.PullRequestNumber
                HeadSha           = $contract.HeadSha.ToLowerInvariant()
                SourceRole        = $contract.SourceRole
                Reason            = $contract.Reason
                FindingUrl        = $contract.FindingUrl
                PendingCommand    = $contract.PendingCommand
                MarkerIndex       = [int]$markerMatch.Index
            })
    }

    return $results.ToArray()
}

function New-AutomationOwnerQueueDecisionMarker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$IssueNumber,

        [Parameter(Mandatory = $true)]
        [ValidateSet('block', 'unblock')]
        [string]$Queue,

        [Parameter(Mandatory = $true)]
        [ValidateSet('product-decision', 'source-asset-missing', 'external-blocker', 'resolved')]
        [string]$Reason
    )

    if (@('block', 'unblock') -cnotcontains $Queue) {
        throw 'queue must use canonical lowercase: block or unblock.'
    }
    if (@('product-decision', 'source-asset-missing', 'external-blocker', 'resolved') -cnotcontains $Reason) {
        throw 'Owner queue decision reason is not a supported stable reason.'
    }
    if (($Queue -ceq 'block' -and $Reason -ceq 'resolved') -or
        ($Queue -ceq 'unblock' -and $Reason -cne 'resolved')) {
        throw 'Owner queue decision reason does not match queue state.'
    }

    return [string]::Join("`n", @(
            '<!-- sashimi-boy-automation-owner-decision:v1',
            "issue: $IssueNumber",
            "queue: $Queue",
            "reason: $Reason",
            '-->'
        ))
}

function Get-AutomationOwnerQueueDecisionMarkers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $requiredKeys = @('issue', 'queue', 'reason')
    $pattern = '(?ms)\A\s*<!--[ \t]*sashimi-boy-automation-owner-decision:v1[ \t]*\r?\n(?<fields>.*?)\r?\n[ \t]*-->\s*\z'
    $results = New-Object 'System.Collections.Generic.List[object]'
    foreach ($markerMatch in [regex]::Matches($Content, $pattern)) {
        $values = ConvertFrom-AutomationMarkerFields `
            -FieldText $markerMatch.Groups['fields'].Value `
            -RequiredKeys $requiredKeys `
            -MarkerName 'automation Owner queue decision marker'
        $issueNumber = 0
        if (-not [int]::TryParse([string]$values['issue'], [ref]$issueNumber) -or $issueNumber -lt 1) {
            throw 'automation Owner queue decision issue must be a positive integer.'
        }
        $queue = [string]$values['queue']
        if (@('block', 'unblock') -cnotcontains $queue) {
            throw 'automation Owner queue decision queue must be block or unblock.'
        }
        $reason = [string]$values['reason']
        if (@('product-decision', 'source-asset-missing', 'external-blocker', 'resolved') -cnotcontains $reason -or
            ($queue -ceq 'block' -and $reason -ceq 'resolved') -or
            ($queue -ceq 'unblock' -and $reason -cne 'resolved')) {
            throw 'automation Owner queue decision reason does not match its queue state.'
        }
        $results.Add([pscustomobject][ordered]@{
                SchemaVersion = 1
                IssueNumber   = $issueNumber
                Queue         = $queue
                Reason        = $reason
                MarkerIndex   = [int]$markerMatch.Index
            })
    }

    return $results.ToArray()
}

function New-AutomationHandoffCompletionMarker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$IssueNumber,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 2147483647)]
        [int]$PullRequestNumber,

        [Parameter(Mandatory = $true)]
        [string]$HeadSha,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Developer')]
        [string]$SourceRole,

        [Parameter(Mandatory = $true)]
        [string]$HandoffUrl
    )

    if ($SourceRole -cne 'Developer') {
        throw 'handoff completion sourceRole must use canonical casing: Developer.'
    }
    if ($HeadSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'handoff completion head must be exactly one 40-character hexadecimal commit SHA.'
    }
    Assert-AutomationMarkerSingleLine -Value $HandoffUrl -Name 'handoffUrl'
    Assert-AutomationHttpUrlOrEmpty -Value $HandoffUrl -Name 'handoffUrl'
    if ([string]::IsNullOrWhiteSpace($HandoffUrl)) {
        throw 'handoffUrl must not be empty.'
    }

    return [string]::Join("`n", @(
            '<!-- sashimi-boy-automation-handoff-completion:v1',
            "issue: $IssueNumber",
            "pr: $PullRequestNumber",
            ('head: ' + $HeadSha.ToLowerInvariant()),
            "sourceRole: $SourceRole",
            "handoffUrl: $HandoffUrl",
            '-->'
        ))
}

function Get-AutomationHandoffCompletionMarkers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $requiredKeys = @('issue', 'pr', 'head', 'sourceRole', 'handoffUrl')
    $pattern = '(?ms)\A\s*<!--[ \t]*sashimi-boy-automation-handoff-completion:v1[ \t]*\r?\n(?<fields>.*?)\r?\n[ \t]*-->\s*\z'
    $results = New-Object 'System.Collections.Generic.List[object]'
    foreach ($markerMatch in [regex]::Matches($Content, $pattern)) {
        $values = ConvertFrom-AutomationMarkerFields `
            -FieldText $markerMatch.Groups['fields'].Value `
            -RequiredKeys $requiredKeys `
            -MarkerName 'automation handoff completion marker'
        $issueNumber = 0
        $pullRequestNumber = 0
        if (-not [int]::TryParse([string]$values['issue'], [ref]$issueNumber) -or $issueNumber -lt 1) {
            throw 'automation handoff completion issue must be a positive integer.'
        }
        if (-not [int]::TryParse([string]$values['pr'], [ref]$pullRequestNumber) -or $pullRequestNumber -lt 1) {
            throw 'automation handoff completion pr must be a positive integer.'
        }
        $headSha = [string]$values['head']
        $sourceRole = [string]$values['sourceRole']
        $handoffUrl = [string]$values['handoffUrl']
        if ($headSha -notmatch '^[0-9a-fA-F]{40}$') {
            throw 'automation handoff completion head must be a 40-character hexadecimal commit SHA.'
        }
        if ($sourceRole -cne 'Developer') {
            throw 'automation handoff completion sourceRole must be Developer.'
        }
        Assert-AutomationMarkerSingleLine -Value $handoffUrl -Name 'handoffUrl'
        Assert-AutomationHttpUrlOrEmpty -Value $handoffUrl -Name 'handoffUrl'
        if ([string]::IsNullOrWhiteSpace($handoffUrl)) {
            throw 'automation handoff completion handoffUrl must not be empty.'
        }
        $results.Add([pscustomobject][ordered]@{
                SchemaVersion     = 1
                IssueNumber       = $issueNumber
                PullRequestNumber = $pullRequestNumber
                HeadSha           = $headSha.ToLowerInvariant()
                SourceRole        = $sourceRole
                HandoffUrl        = $handoffUrl
                MarkerIndex       = [int]$markerMatch.Index
            })
    }

    return $results.ToArray()
}
