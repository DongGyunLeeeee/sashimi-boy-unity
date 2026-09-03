#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Acquire', 'Renew', 'Release', 'Inspect')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$PullRequestNumber,

    [Parameter(Mandatory = $true)]
    [string]$HeadSha,

    [string]$LeaseId,

    [ValidateRange(5, 1440)]
    [int]$LeaseMinutes = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commonPath = Join-Path -Path $PSScriptRoot -ChildPath 'Automation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Automation common helpers are missing: $commonPath")
    exit 1
}
. $commonPath

function Get-DeveloperLeaseRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$ExpectedPullRequestNumber
    )

    $record = Get-Content -Raw -LiteralPath $Path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $recordHead = [string]$record.HeadSha
    $recordLeaseId = [string]$record.LeaseId
    $parsedLeaseId = [Guid]::Empty
    $expiresAt = [DateTimeOffset]::MinValue
    $acquiredAt = [DateTimeOffset]::MinValue
    if ([int]$record.SchemaVersion -ne 1 -or
        [int]$record.PullRequestNumber -ne $ExpectedPullRequestNumber -or
        $recordHead -notmatch '^[0-9a-fA-F]{40}$' -or
        -not [Guid]::TryParse($recordLeaseId, [ref]$parsedLeaseId) -or
        $parsedLeaseId -eq [Guid]::Empty -or
        -not [DateTimeOffset]::TryParse([string]$record.ExpiresAt, [ref]$expiresAt) -or
        -not [DateTimeOffset]::TryParse([string]$record.AcquiredAt, [ref]$acquiredAt)) {
        throw 'Developer lease file is malformed or does not match its path.'
    }
    return [pscustomobject][ordered]@{
        SchemaVersion    = 1
        PullRequestNumber = [int]$record.PullRequestNumber
        HeadSha          = $recordHead.ToLowerInvariant()
        LeaseId          = $parsedLeaseId.ToString('D')
        AcquiredAt       = $acquiredAt.ToUniversalTime()
        ExpiresAt        = $expiresAt.ToUniversalTime()
        ProcessId        = [int]$record.ProcessId
        MachineName      = [string]$record.MachineName
    }
}

function Assert-DeveloperLeaseIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Record,
        [Parameter(Mandatory = $true)][string]$ExpectedHeadSha,
        [Parameter(Mandatory = $true)][Guid]$ExpectedLeaseId
    )

    if (-not [string]::Equals([string]$Record.HeadSha, $ExpectedHeadSha, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$Record.LeaseId, $ExpectedLeaseId.ToString('D'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Developer lease is owned by another run or PR head.'
    }
}

function Write-NewDeveloperLeaseFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Record
    )

    $json = (ConvertTo-AutomationJson -InputObject $Record) + "`n"
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function New-DeveloperLeaseOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][bool]$Succeeded,
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Path,
        [Parameter(Mandatory = $true)][bool]$Exists,
        [Parameter(Mandatory = $true)][bool]$Active,
        [Parameter(Mandatory = $true)][bool]$Changed,
        [AllowNull()][object]$Record,
        [AllowNull()][string]$ErrorMessage
    )

    $reportedHeadSha = $HeadSha.ToLowerInvariant()
    if ($null -ne $Record -and $null -ne $Record.PSObject.Properties['HeadSha']) {
        $reportedHeadSha = ([string]$Record.HeadSha).ToLowerInvariant()
    }
    return [pscustomobject][ordered]@{
        Succeeded         = $Succeeded
        Action            = $Operation
        WhatIf            = [bool]$WhatIfPreference
        LeasePath         = $Path
        Exists            = $Exists
        Active            = $Active
        Changed           = $Changed
        Cancelled         = ($Succeeded -and @('Acquire', 'Renew', 'Release') -ccontains $Operation -and -not $Changed -and -not [bool]$WhatIfPreference)
        PullRequestNumber = $PullRequestNumber
        HeadSha           = $reportedHeadSha
        LeaseId           = if ($null -ne $Record) { [string]$Record.LeaseId } else { $LeaseId }
        AcquiredAt        = if ($null -ne $Record) { $Record.AcquiredAt.ToString('o') } else { $null }
        ExpiresAt         = if ($null -ne $Record) { $Record.ExpiresAt.ToString('o') } else { $null }
        Error             = $ErrorMessage
    }
}

$exitCode = 0
$mutex = $null
$mutexHeld = $false
$leasePath = ''
try {
    if (@('Acquire', 'Renew', 'Release', 'Inspect') -cnotcontains $Action) {
        throw 'Action must use canonical casing: Acquire, Renew, Release, or Inspect.'
    }
    if ($HeadSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'HeadSha must be exactly one 40-character hexadecimal commit SHA.'
    }
    $HeadSha = $HeadSha.ToLowerInvariant()

    $parsedLeaseId = [Guid]::Empty
    if ($Action -cne 'Inspect') {
        if (-not [Guid]::TryParse($LeaseId, [ref]$parsedLeaseId) -or $parsedLeaseId -eq [Guid]::Empty) {
            throw 'Acquire, Renew, and Release require a non-empty GUID LeaseId.'
        }
        $LeaseId = $parsedLeaseId.ToString('D')
    }

    $leaseDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath 'SashimiBoyAutomation\DeveloperLeases'
    Assert-AutomationPathHasNoReparsePoint -Path $leaseDirectory
    $leaseDirectory = ConvertTo-AutomationLexicalPath -Path $leaseDirectory
    $leasePath = Join-Path -Path $leaseDirectory -ChildPath ("pr-{0}.json" -f $PullRequestNumber)
    Assert-AutomationPathHasNoReparsePoint -Path $leasePath

    if ($Action -ceq 'Inspect') {
        if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf)) {
            New-DeveloperLeaseOutput -Succeeded $true -Operation $Action -Path $leasePath -Exists $false -Active $false -Changed $false -Record $null -ErrorMessage $null | ConvertTo-AutomationJson
            exit 0
        }
        $record = Get-DeveloperLeaseRecord -Path $leasePath -ExpectedPullRequestNumber $PullRequestNumber
        $active = $record.ExpiresAt -gt [DateTimeOffset]::UtcNow
        New-DeveloperLeaseOutput -Succeeded $true -Operation $Action -Path $leasePath -Exists $true -Active $active -Changed $false -Record $record -ErrorMessage $null | ConvertTo-AutomationJson
        exit 0
    }

    $mutexName = 'Global\SashimiBoyAutomation-DeveloperLease-' + $PullRequestNumber
    $mutex = New-Object System.Threading.Mutex($false, $mutexName)
    try {
        $mutexHeld = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    }
    catch [System.Threading.AbandonedMutexException] {
        $mutexHeld = $true
    }
    if (-not $mutexHeld) {
        throw "Timed out acquiring the per-PR lease mutex: $mutexName"
    }

    $existing = $null
    if (Test-Path -LiteralPath $leasePath -PathType Leaf) {
        Assert-AutomationPathHasNoReparsePoint -Path $leasePath
        $existing = Get-DeveloperLeaseRecord -Path $leasePath -ExpectedPullRequestNumber $PullRequestNumber
    }

    if ($Action -ceq 'Acquire') {
        if ($null -ne $existing -and $existing.ExpiresAt -gt [DateTimeOffset]::UtcNow) {
            throw "Developer lease is already active for PR #$PullRequestNumber until $($existing.ExpiresAt.ToString('o'))."
        }
        $now = [DateTimeOffset]::UtcNow
        $newRecord = [pscustomobject][ordered]@{
            SchemaVersion     = 1
            PullRequestNumber = $PullRequestNumber
            HeadSha           = $HeadSha
            LeaseId           = $LeaseId
            AcquiredAt        = $now.ToString('o')
            ExpiresAt         = $now.AddMinutes($LeaseMinutes).ToString('o')
            ProcessId         = $PID
            MachineName       = [Environment]::MachineName
        }
        $mutationApproved = $PSCmdlet.ShouldProcess($leasePath, "Acquire Developer lease for PR #$PullRequestNumber")
        if ($mutationApproved) {
            if (-not (Test-Path -LiteralPath $leaseDirectory -PathType Container)) {
                New-Item -ItemType Directory -Path $leaseDirectory -Force -ErrorAction Stop | Out-Null
            }
            Assert-AutomationPathHasNoReparsePoint -Path $leaseDirectory
            if ($null -ne $existing) {
                [System.IO.File]::Delete($leasePath)
            }
            Write-NewDeveloperLeaseFile -Path $leasePath -Record $newRecord
        }
        $outputRecord = if ($mutationApproved) {
            [pscustomobject][ordered]@{
                LeaseId = $LeaseId
                HeadSha = $HeadSha
                AcquiredAt = $now
                ExpiresAt = $now.AddMinutes($LeaseMinutes)
            }
        }
        else {
            $existing
        }
        $existsAfterAcquire = $mutationApproved -or $null -ne $existing
        $activeAfterAcquire = $mutationApproved -or ($null -ne $existing -and $existing.ExpiresAt -gt [DateTimeOffset]::UtcNow)
        New-DeveloperLeaseOutput -Succeeded $true -Operation $Action -Path $leasePath -Exists $existsAfterAcquire -Active $activeAfterAcquire -Changed $mutationApproved -Record $outputRecord -ErrorMessage $null | ConvertTo-AutomationJson
    }
    elseif ($Action -ceq 'Renew') {
        if ($null -eq $existing) {
            throw "Developer lease does not exist for PR #$PullRequestNumber."
        }
        Assert-DeveloperLeaseIdentity -Record $existing -ExpectedHeadSha $HeadSha -ExpectedLeaseId $parsedLeaseId
        if ($existing.ExpiresAt -le [DateTimeOffset]::UtcNow) {
            throw 'Expired Developer lease cannot be renewed; acquire a new LeaseId.'
        }
        $renewedRecord = [pscustomobject][ordered]@{
            SchemaVersion     = 1
            PullRequestNumber = $PullRequestNumber
            HeadSha           = $HeadSha
            LeaseId           = $LeaseId
            AcquiredAt        = $existing.AcquiredAt.ToString('o')
            ExpiresAt         = [DateTimeOffset]::UtcNow.AddMinutes($LeaseMinutes).ToString('o')
            ProcessId         = $PID
            MachineName       = [Environment]::MachineName
        }
        $temporaryLeasePath = Join-Path -Path $leaseDirectory -ChildPath ("pr-{0}.{1}.tmp" -f $PullRequestNumber, $LeaseId)
        $backupLeasePath = Join-Path -Path $leaseDirectory -ChildPath ("pr-{0}.{1}.backup" -f $PullRequestNumber, $LeaseId)
        Assert-AutomationPathHasNoReparsePoint -Path $temporaryLeasePath
        Assert-AutomationPathHasNoReparsePoint -Path $backupLeasePath
        $mutationApproved = $PSCmdlet.ShouldProcess($leasePath, "Renew Developer lease for PR #$PullRequestNumber")
        if ($mutationApproved) {
            try {
                if ((Test-Path -LiteralPath $temporaryLeasePath) -or (Test-Path -LiteralPath $backupLeasePath)) {
                    throw 'Developer lease renewal scratch path already exists.'
                }
                Write-NewDeveloperLeaseFile -Path $temporaryLeasePath -Record $renewedRecord
                [System.IO.File]::Replace($temporaryLeasePath, $leasePath, $backupLeasePath)
            }
            finally {
                if (Test-Path -LiteralPath $temporaryLeasePath -PathType Leaf) {
                    [System.IO.File]::Delete($temporaryLeasePath)
                }
                if (Test-Path -LiteralPath $backupLeasePath -PathType Leaf) {
                    [System.IO.File]::Delete($backupLeasePath)
                }
            }
        }
        $outputRecord = if ($mutationApproved) {
            [pscustomobject][ordered]@{
                LeaseId = $LeaseId
                HeadSha = $HeadSha
                AcquiredAt = $existing.AcquiredAt
                ExpiresAt = [DateTimeOffset]::Parse($renewedRecord.ExpiresAt).ToUniversalTime()
            }
        }
        else {
            $existing
        }
        $activeAfterRenew = $mutationApproved -or $existing.ExpiresAt -gt [DateTimeOffset]::UtcNow
        New-DeveloperLeaseOutput -Succeeded $true -Operation $Action -Path $leasePath -Exists $true -Active $activeAfterRenew -Changed $mutationApproved -Record $outputRecord -ErrorMessage $null | ConvertTo-AutomationJson
    }
    elseif ($Action -ceq 'Release') {
        if ($null -eq $existing) {
            throw "Developer lease does not exist for PR #$PullRequestNumber."
        }
        Assert-DeveloperLeaseIdentity -Record $existing -ExpectedHeadSha $HeadSha -ExpectedLeaseId $parsedLeaseId
        $mutationApproved = $PSCmdlet.ShouldProcess($leasePath, "Release Developer lease for PR #$PullRequestNumber")
        if ($mutationApproved) {
            [System.IO.File]::Delete($leasePath)
        }
        New-DeveloperLeaseOutput -Succeeded $true -Operation $Action -Path $leasePath -Exists (-not $mutationApproved) -Active (-not $mutationApproved -and $existing.ExpiresAt -gt [DateTimeOffset]::UtcNow) -Changed $mutationApproved -Record $existing -ErrorMessage $null | ConvertTo-AutomationJson
    }
}
catch {
    $exitCode = 1
    New-DeveloperLeaseOutput -Succeeded $false -Operation $Action -Path $leasePath -Exists ($leasePath -and (Test-Path -LiteralPath $leasePath -PathType Leaf)) -Active $false -Changed $false -Record $null -ErrorMessage $_.Exception.Message | ConvertTo-AutomationJson
}
finally {
    if ($mutexHeld -and $null -ne $mutex) {
        [void]$mutex.ReleaseMutex()
    }
    if ($null -ne $mutex) {
        $mutex.Dispose()
    }
}

exit $exitCode
