#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium', DefaultParameterSetName = 'ByNumber')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'ByNumber')]
    [ValidateRange(1, 2147483647)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true, ParameterSetName = 'ByUrl')]
    [ValidateNotNullOrEmpty()]
    [string]$IssueUrl,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Developer', 'Reviewer', 'Owner')]
    [string]$Role,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done')]
    [string]$ToStatus,

    [ValidateNotNullOrEmpty()]
    [string]$Repository = 'DongGyunLeeeee/sashimi-boy-unity',

    [ValidateNotNullOrEmpty()]
    [string]$ProjectOwner = 'DongGyunLeeeee',

    [ValidateRange(1, 2147483647)]
    [int]$ProjectNumber = 1,

    [ValidateNotNullOrEmpty()]
    [string]$GitHubCliPath = 'gh'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Automation common helpers are missing: $commonPath")
    exit 1
}
. $commonPath

$scriptExitCode = 0
$errors = New-Object 'System.Collections.Generic.List[string]'
$resolvedIssueNumber = $null
$resolvedIssueUrl = $null
$projectId = $null
$projectItemId = $null
$fromStatus = $null
$changed = $false
$wouldChange = $false
$cancelled = $false
$message = $null

function Stop-StatusUpdate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [int]$NativeExitCode = 1
    )

    $script:scriptExitCode = ConvertTo-AutomationExitCode -NativeExitCode $NativeExitCode
    throw $Message
}

function Invoke-RequiredGitHubCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $invocation = Invoke-AutomationNativeCommand -FilePath $GitHubCliPath -ArgumentList $ArgumentList
    if (-not $invocation.Succeeded) {
        $detail = $invocation.StdErr
        if (-not $detail) {
            $detail = $invocation.StdOut
        }
        $command = Format-AutomationCommand -FilePath $GitHubCliPath -ArgumentList $ArgumentList
        Stop-StatusUpdate -Message ("{0} failed. Command: {1}; exit code: {2}; stderr: {3}" -f $Operation, $command, $invocation.ExitCode, $detail) -NativeExitCode $invocation.ExitCode
    }
    return $invocation
}

function ConvertFrom-RequiredJson {
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
        Stop-StatusUpdate -Message ("{0} returned invalid JSON: {1}" -f $Operation, $_.Exception.Message)
    }
}

function Get-ProjectItemForIssue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExpectedIssueUrl
    )

    $itemInvocation = Invoke-RequiredGitHubCommand -Operation 'GitHub Project item lookup' -ArgumentList @(
        'project', 'item-list', [string]$ProjectNumber,
        '--owner', $ProjectOwner,
        '--format', 'json',
        '--limit', '1000'
    )
    $itemResponse = ConvertFrom-RequiredJson -Json $itemInvocation.StdOut -Operation 'GitHub Project item lookup'
    $matchingItems = New-Object 'System.Collections.Generic.List[object]'

    $itemsProperty = $itemResponse.PSObject.Properties['items']
    if ($null -eq $itemsProperty) {
        Stop-StatusUpdate -Message ("GitHub Project item response does not contain items. Raw stdout: {0}" -f $itemInvocation.StdOut)
    }
    foreach ($candidate in @($itemsProperty.Value)) {
        $contentProperty = $candidate.PSObject.Properties['content']
        if ($null -eq $contentProperty -or $null -eq $contentProperty.Value) {
            continue
        }
        $urlProperty = $contentProperty.Value.PSObject.Properties['url']
        if ($null -ne $urlProperty -and [string]::Equals([string]$urlProperty.Value, $ExpectedIssueUrl, [System.StringComparison]::OrdinalIgnoreCase)) {
            $matchingItems.Add($candidate)
        }
    }

    if ($matchingItems.Count -ne 1) {
        Stop-StatusUpdate -Message ("Expected exactly one Project item for {0}; found {1}." -f $ExpectedIssueUrl, $matchingItems.Count)
    }
    return $matchingItems[0]
}

try {
    $issueTarget = if ($PSCmdlet.ParameterSetName -eq 'ByUrl') { $IssueUrl } else { [string]$IssueNumber }
    $issueInvocation = Invoke-RequiredGitHubCommand -Operation 'GitHub Issue lookup' -ArgumentList @(
        'issue', 'view', $issueTarget,
        '--repo', $Repository,
        '--json', 'id,number,url'
    )
    $issueResponse = ConvertFrom-RequiredJson -Json $issueInvocation.StdOut -Operation 'GitHub Issue lookup'
    $resolvedIssueNumber = [int]$issueResponse.number
    $resolvedIssueUrl = [string]$issueResponse.url

    $expectedIssuePrefix = 'https://github.com/{0}/issues/' -f $Repository
    if (-not $resolvedIssueUrl.StartsWith($expectedIssuePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-StatusUpdate -Message ("Issue does not belong to the configured repository {0}: {1}" -f $Repository, $resolvedIssueUrl)
    }
    if ($PSCmdlet.ParameterSetName -eq 'ByUrl' -and
        -not [string]::Equals($resolvedIssueUrl.TrimEnd('/'), $IssueUrl.TrimEnd('/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-StatusUpdate -Message ("Resolved Issue URL does not match the requested URL. Requested: {0}; resolved: {1}" -f $IssueUrl, $resolvedIssueUrl)
    }

    $projectInvocation = Invoke-RequiredGitHubCommand -Operation 'GitHub Project lookup' -ArgumentList @(
        'project', 'view', [string]$ProjectNumber,
        '--owner', $ProjectOwner,
        '--format', 'json'
    )
    $projectResponse = ConvertFrom-RequiredJson -Json $projectInvocation.StdOut -Operation 'GitHub Project lookup'
    $projectIdProperty = $projectResponse.PSObject.Properties['id']
    $projectId = if ($null -eq $projectIdProperty) { '' } else { [string]$projectIdProperty.Value }
    if ([string]::IsNullOrWhiteSpace($projectId)) {
        Stop-StatusUpdate -Message 'GitHub Project lookup did not return a Project node ID.'
    }

    $fieldInvocation = Invoke-RequiredGitHubCommand -Operation 'GitHub Project field lookup' -ArgumentList @(
        'project', 'field-list', [string]$ProjectNumber,
        '--owner', $ProjectOwner,
        '--format', 'json'
    )
    $fieldResponse = ConvertFrom-RequiredJson -Json $fieldInvocation.StdOut -Operation 'GitHub Project field lookup'
    $fieldsProperty = $fieldResponse.PSObject.Properties['fields']
    if ($null -eq $fieldsProperty) {
        Stop-StatusUpdate -Message ("GitHub Project field response does not contain fields. Raw stdout: {0}" -f $fieldInvocation.StdOut)
    }
    $statusFields = @($fieldsProperty.Value | Where-Object { [string]$_.name -ceq 'Status' })
    if ($statusFields.Count -ne 1) {
        Stop-StatusUpdate -Message ("Expected exactly one case-sensitive Status field; found {0}." -f $statusFields.Count)
    }
    $statusField = $statusFields[0]
    if ([string]$statusField.type -cne 'ProjectV2SingleSelectField') {
        Stop-StatusUpdate -Message ("Status field is not a single-select field: {0}" -f $statusField.type)
    }

    $expectedStatusNames = @('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done')
    $actualStatusNames = @($statusField.options | ForEach-Object { [string]$_.name })
    if ($actualStatusNames.Count -ne $expectedStatusNames.Count) {
        Stop-StatusUpdate -Message 'Status options do not match the repository workflow contract.'
    }
    foreach ($expectedStatusName in $expectedStatusNames) {
        if (-not ($actualStatusNames -ccontains $expectedStatusName)) {
            Stop-StatusUpdate -Message ("Required Status option is missing: {0}" -f $expectedStatusName)
        }
    }

    $targetOptions = @($statusField.options | Where-Object { [string]$_.name -ceq $ToStatus })
    if ($targetOptions.Count -ne 1) {
        Stop-StatusUpdate -Message ("Expected exactly one existing Status option named '{0}'; found {1}." -f $ToStatus, $targetOptions.Count)
    }
    $targetOptionId = [string]$targetOptions[0].id
    if ([string]::IsNullOrWhiteSpace($targetOptionId)) {
        Stop-StatusUpdate -Message "Status option '$ToStatus' does not have an option ID."
    }

    $projectItem = Get-ProjectItemForIssue -ExpectedIssueUrl $resolvedIssueUrl
    $projectItemId = [string]$projectItem.id
    $statusProperty = $projectItem.PSObject.Properties['status']
    if ($null -eq $statusProperty -or [string]::IsNullOrWhiteSpace([string]$statusProperty.Value)) {
        Stop-StatusUpdate -Message "Project item $projectItemId does not have a Status value."
    }
    $fromStatus = [string]$statusProperty.Value
    if (-not ($expectedStatusNames -ccontains $fromStatus)) {
        Stop-StatusUpdate -Message ("Current Status is not an exact workflow option: {0}" -f $fromStatus)
    }

    $allowedTransitions = @{
        Developer = @{
            'Ready'       = @('In Progress')
            'In Progress' = @('Review')
        }
        Reviewer  = @{
            'Review' = @('In Progress', 'Verification')
        }
        Owner     = @{
            'Verification' = @('In Progress', 'Done')
        }
    }
    $allowedDestinations = @{
        Developer = @('In Progress', 'Review')
        Reviewer  = @('In Progress', 'Verification')
        Owner     = @('In Progress', 'Done')
    }

    if ([string]::Equals($fromStatus, $ToStatus, [System.StringComparison]::Ordinal)) {
        if (-not ($allowedDestinations[$Role] -ccontains $ToStatus)) {
            Stop-StatusUpdate -Message ("Role {0} may not target Status '{1}'." -f $Role, $ToStatus)
        }
        $message = "Issue is already in Status '$ToStatus'; no change was made."
    }
    else {
        $roleTransitions = $allowedTransitions[$Role]
        $sourceTargets = if ($roleTransitions.ContainsKey($fromStatus)) { @($roleTransitions[$fromStatus]) } else { @() }
        if (-not ($sourceTargets -ccontains $ToStatus)) {
            Stop-StatusUpdate -Message ("Transition is not allowed for {0}: {1} -> {2}" -f $Role, $fromStatus, $ToStatus)
        }

        $wouldChange = $true
        $action = "Set Project #$ProjectNumber Status from '$fromStatus' to '$ToStatus'"
        $shouldApply = $false
        if (-not [bool]$WhatIfPreference) {
            $shouldApply = $PSCmdlet.ShouldProcess($resolvedIssueUrl, $action)
        }

        if ([bool]$WhatIfPreference) {
            $message = "WhatIf: $action"
        }
        elseif (-not $shouldApply) {
            $cancelled = $true
            $message = "Cancelled: $action"
        }
        else {
            $editInvocation = Invoke-RequiredGitHubCommand -Operation 'GitHub Project Status update' -ArgumentList @(
                'project', 'item-edit',
                '--id', $projectItemId,
                '--project-id', $projectId,
                '--field-id', [string]$statusField.id,
                '--single-select-option-id', $targetOptionId,
                '--format', 'json'
            )

            $verifiedItem = Get-ProjectItemForIssue -ExpectedIssueUrl $resolvedIssueUrl
            $verifiedStatusProperty = $verifiedItem.PSObject.Properties['status']
            $verifiedStatus = if ($null -eq $verifiedStatusProperty) { '' } else { [string]$verifiedStatusProperty.Value }
            if ($verifiedStatus -cne $ToStatus) {
                Stop-StatusUpdate -Message ("Status update could not be verified. Expected '{0}'; received '{1}'." -f $ToStatus, $verifiedStatus)
            }

            $changed = $true
            $message = "Status changed from '$fromStatus' to '$ToStatus'."
        }
    }
}
catch {
    if ($scriptExitCode -eq 0) {
        $scriptExitCode = 1
    }
    $errors.Add($_.Exception.Message)
}

$succeeded = $errors.Count -eq 0
$result = [pscustomobject][ordered]@{
    schemaVersion = 1
    script        = 'Set-GitHubProjectStatus'
    succeeded     = $succeeded
    exitCode      = $(if ($succeeded) { 0 } else { $scriptExitCode })
    whatIf        = [bool]$WhatIfPreference
    changed       = $changed
    wouldChange   = $wouldChange
    cancelled     = $cancelled
    role          = $Role
    issueNumber   = $resolvedIssueNumber
    issueUrl      = $resolvedIssueUrl
    projectOwner  = $ProjectOwner
    projectNumber = $ProjectNumber
    projectId     = $projectId
    projectItemId = $projectItemId
    fromStatus    = $fromStatus
    toStatus      = $ToStatus
    message       = $message
    errors        = $errors.ToArray()
}

[Console]::Out.WriteLine((ConvertTo-AutomationJson -InputObject $result))
if (-not $succeeded) {
    exit $scriptExitCode
}
exit 0
