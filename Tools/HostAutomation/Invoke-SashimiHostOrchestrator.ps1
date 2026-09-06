#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath,
    [string]$IntegrityManifestPath,
    [string]$QueueFixturePath,
    [string]$CodexFixturePath,
    [string]$UnityFixturePath,
    [string]$PublishFixturePath,
    [string]$MutexName,
    [Parameter(DontShow = $true)][switch]$UnelevatedChild,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MinimumPowerShellVersion = [Version]'7.5.0'
$script:ExecutableIdentityName = 'ExecutableIdentity.json'
$script:ExecutableProperties = @('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','PowerShellExecutable','UnityExecutable')
$script:RequiredBundleFiles = @(
    'Config.json',
    'ExecutableIdentity.json',
    'Get-SashimiProjectQueue.ps1',
    'HostAutomation.Common.ps1',
    'Invoke-SashimiCodexExec.ps1',
    'Invoke-SashimiDeveloperRun.ps1',
    'Invoke-SashimiHostOrchestrator.ps1',
    'Invoke-SashimiReviewerRun.ps1',
    'Invoke-SashimiUnityValidation.ps1',
    'Publish-SashimiRunResult.ps1'
)
$script:commonLoaded = $false
$script:integrityResult = [pscustomobject]@{ Required=$false; Verified=$false; BundleId=''; Manifest=''; ExecutableIdentity=''; ExecutablesVerified=0; Reason='NotChecked' }
$script:privilegeBoundaryResult = [pscustomobject]@{ Required=$false; Verified=$false; CurrentProcessElevated=$null; Relaunched=$false; Reason='NotChecked' }

$lease = $null
$workspace = $null
$selection = $null
$runnerResult = $null
$cleanup = $null
$state = 'Starting'
$success = $false
$exitCode = 0
$errorMessage = ''
$alreadyRunning = $false
$commands = [Collections.Generic.List[object]]::new()
$events = [Collections.Generic.List[object]]::new()
$startedAt = [DateTime]::UtcNow
$cancellationMarkerPath = ''
$ownedHostPidPath = ''
$retentionResults = @()

function Protect-OrchestratorText {
    param([AllowNull()][object]$Text)
    if (Get-Command Protect-SashimiText -CommandType Function -ErrorAction SilentlyContinue) {
        return Protect-SashimiText -Text $Text
    }
    if ($null -eq $Text) { return '' }
    $value = [string]$Text
    foreach ($pattern in @(
        '(?i)\b(?:Proxy-)?Authorization\s*:\s*Basic\s+[A-Za-z0-9+/=]{8,}',
        '(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+',
        '(?i)\b(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}',
        '(?i)(?:access[_-]?token|refresh[_-]?token|api[_-]?key|token|password|secret|credential)\s*[=:]\s*[^\s,;}]+',
        '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    )) {
        $value = [regex]::Replace($value, $pattern, '[REDACTED_SECRET]')
    }
    $profilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($profilePath)) {
        $value = $value.Replace($profilePath, '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
        $value = $value.Replace($profilePath.Replace('\','\\'), '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
        $value = $value.Replace($profilePath.Replace('\','/'), '[REDACTED_PROFILE]', [StringComparison]::OrdinalIgnoreCase)
    }
    return $value
}

function Protect-OrchestratorDiagnostic {
    param([AllowNull()][object]$Text)

    $raw = if ($null -eq $Text) { '' } else { [string]$Text }
    if ((Get-Command Test-SashimiRecognizableSensitiveText -CommandType Function -ErrorAction SilentlyContinue) -and
        (Get-Command Get-SashimiSensitiveEnvironmentEntries -CommandType Function -ErrorAction SilentlyContinue)) {
        $sensitiveValues = @(Get-SashimiSensitiveEnvironmentEntries | ForEach-Object { [string]$_.Value } | Where-Object { $_.Length -ge 8 -and $_.Length -le 4096 } | Sort-Object -Unique)
        if (Test-SashimiRecognizableSensitiveText -Text $raw -SensitiveValues $sensitiveValues) {
            return 'Host diagnostic contained sensitive content and was suppressed.'
        }
        return Protect-SashimiTextWithExactValues -Text $raw -ExactValues $sensitiveValues
    }
    return Protect-OrchestratorText $raw
}

function ConvertTo-OrchestratorJson {
    param([Parameter(Mandatory = $true)][AllowNull()][object]$InputObject, [switch]$Pretty)
    if (Get-Command ConvertTo-SashimiJson -CommandType Function -ErrorAction SilentlyContinue) {
        return ConvertTo-SashimiJson -InputObject $InputObject -Pretty:$Pretty
    }
    if ($Pretty) { return $InputObject | ConvertTo-Json -Depth 32 }
    return $InputObject | ConvertTo-Json -Depth 32 -Compress
}

function Get-OrchestratorPropertyValue {
    param([AllowNull()][object]$Object,[Parameter(Mandatory = $true)][string]$Name,[AllowNull()][object]$DefaultValue=$null)
    if (Get-Command Get-SashimiPropertyValue -CommandType Function -ErrorAction SilentlyContinue) {
        return Get-SashimiPropertyValue -Object $Object -Name $Name -DefaultValue $DefaultValue
    }
    if ($null -eq $Object) { return $DefaultValue }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $DefaultValue }
    return $property.Value
}

function ConvertTo-OrchestratorSelectionSummary {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    return [ordered]@{
        Selected=[bool](Get-OrchestratorPropertyValue $Value 'Selected' $false)
        DispatchCount=[int](Get-OrchestratorPropertyValue $Value 'DispatchCount' 0)
        Role=[string](Get-OrchestratorPropertyValue $Value 'Role' '')
        Mode=[string](Get-OrchestratorPropertyValue $Value 'Mode' '')
        DataSource=[string](Get-OrchestratorPropertyValue $Value 'DataSource' '')
        ProjectPageCount=[int](Get-OrchestratorPropertyValue $Value 'ProjectPageCount' 0)
        CandidateCount=[int](Get-OrchestratorPropertyValue $Value 'CandidateCount' 0)
        ProjectItemId=[string](Get-OrchestratorPropertyValue $Value 'ProjectItemId' '')
        Status=[string](Get-OrchestratorPropertyValue $Value 'Status' '')
        Priority=[string](Get-OrchestratorPropertyValue $Value 'Priority' '')
        UpdatedAt=[string](Get-OrchestratorPropertyValue $Value 'UpdatedAt' '')
        IssueNumber=[int](Get-OrchestratorPropertyValue $Value 'IssueNumber' 0)
        IssueUpdatedAt=[string](Get-OrchestratorPropertyValue $Value 'IssueUpdatedAt' '')
        IssueBodySha256=[string](Get-OrchestratorPropertyValue $Value 'IssueBodySha256' '')
        PullRequestNumber=Get-OrchestratorPropertyValue $Value 'PullRequestNumber' $null
        PullRequestHeadSha=[string](Get-OrchestratorPropertyValue $Value 'PullRequestHeadSha' '')
        PullRequestHeadRef=[string](Get-OrchestratorPropertyValue $Value 'PullRequestHeadRef' '')
        PullRequestHeadRepository=[string](Get-OrchestratorPropertyValue $Value 'PullRequestHeadRepository' '')
        PullRequestContentSha256=[string](Get-OrchestratorPropertyValue $Value 'PullRequestContentSha256' '')
        HandoffReason=[string](Get-OrchestratorPropertyValue $Value 'HandoffReason' '')
        MutationAttempted=[bool](Get-OrchestratorPropertyValue $Value 'MutationAttempted' $false)
    }
}

function ConvertTo-OrchestratorRunnerSummary {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    $commands=@(Get-OrchestratorPropertyValue $Value 'Commands' @())
    $events=@(Get-OrchestratorPropertyValue $Value 'Events' @())
    $runnerSucceeded=[bool](Get-OrchestratorPropertyValue $Value 'Success' $false)
    return [ordered]@{
        Tool=[string](Get-OrchestratorPropertyValue $Value 'Tool' '')
        Success=$runnerSucceeded
        ExitCode=[int](Get-OrchestratorPropertyValue $Value 'ExitCode' 1)
        DryRun=[bool](Get-OrchestratorPropertyValue $Value 'DryRun' $false)
        IssueNumber=[int](Get-OrchestratorPropertyValue $Value 'IssueNumber' 0)
        PullRequestNumber=[int](Get-OrchestratorPropertyValue $Value 'PullRequestNumber' 0)
        Mode=[string](Get-OrchestratorPropertyValue $Value 'Mode' '')
        Pushed=[bool](Get-OrchestratorPropertyValue $Value 'Pushed' $false)
        CreatedPullRequest=[bool](Get-OrchestratorPropertyValue $Value 'CreatedPullRequest' $false)
        TransitionedToReview=[bool](Get-OrchestratorPropertyValue $Value 'TransitionedToReview' $false)
        FindingCount=[int](Get-OrchestratorPropertyValue $Value 'FindingCount' 0)
        Transition=[string](Get-OrchestratorPropertyValue $Value 'Transition' '')
        ReviewerPushAttempted=[bool](Get-OrchestratorPropertyValue $Value 'ReviewerPushAttempted' $false)
        CommandCount=$commands.Count
        EventCount=$events.Count
        ErrorCode=if ($runnerSucceeded) { '' } else { 'RunnerFailed' }
    }
}

function Get-OrchestratorFullPath {
    param([Parameter(Mandatory = $true)][string]$Path,[switch]$MustExist)
    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not [IO.Path]::IsPathRooted($expanded)) { $expanded = Join-Path (Get-Location).ProviderPath $expanded }
    $full = [IO.Path]::GetFullPath($expanded).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if ($MustExist) { $full = (Get-Item -LiteralPath $full -Force -ErrorAction Stop).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar) }
    return $full
}

function Test-OrchestratorPathEqual {
    param([Parameter(Mandatory = $true)][string]$Left,[Parameter(Mandatory = $true)][string]$Right)
    return [string]::Equals((Get-OrchestratorFullPath $Left),(Get-OrchestratorFullPath $Right),[StringComparison]::OrdinalIgnoreCase)
}

function Get-OrchestratorSidValue {
    param([Parameter(Mandatory = $true)][object]$Identity)
    if ($Identity -is [Security.Principal.SecurityIdentifier]) { return $Identity.Value }
    if ($Identity -is [Security.Principal.IdentityReference]) { return $Identity.Translate([Security.Principal.SecurityIdentifier]).Value }
    return ([Security.Principal.NTAccount]::new([string]$Identity)).Translate([Security.Principal.SecurityIdentifier]).Value
}

function Assert-OrchestratorProtectedAclState {
    param(
        [Parameter(Mandatory = $true)][object]$Acl,
        [Parameter(Mandatory = $true)][bool]$IsContainer,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )

    $administrators='S-1-5-32-544'; $system='S-1-5-18'
    if (-not $acl.AreAccessRulesProtected) { throw "Protected host ACL inherits permissions: $Path" }
    if ((Get-OrchestratorSidValue $acl.Owner) -cne $administrators) { throw "Protected host path is not owned by Administrators: $Path" }
    $allowedSids=@($administrators,$system,$UserSid.Value)
    $rulesBySid=@{}
    $expectedInheritance=if ($IsContainer) {
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    }
    else { [Security.AccessControl.InheritanceFlags]::None }
    $expectedUserRights=[Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize
    $granularWriteMask=[Security.AccessControl.FileSystemRights]::WriteData -bor
        [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($rule in @($acl.Access)) {
        $sid=Get-OrchestratorSidValue $rule.IdentityReference
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $allowedSids -cnotcontains $sid) { throw "Protected host ACL contains an unexpected access rule for '$sid': $Path" }
        if ($rulesBySid.ContainsKey($sid)) { throw "Protected host ACL contains duplicate access rules for '$sid': $Path" }
        if ($rule.InheritanceFlags -ne $expectedInheritance -or $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) {
            throw "Protected host ACL contains incorrect inheritance flags for '$sid': $Path"
        }
        if ($sid -ceq $UserSid.Value) {
            if (($rule.FileSystemRights -band $granularWriteMask) -ne 0) { throw "Task user has write access to protected host path: $Path" }
            if ($rule.FileSystemRights -ne $expectedUserRights) { throw "Task user must have exactly ReadAndExecute plus Synchronize on protected host path: $Path" }
        }
        elseif ($rule.FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl) {
            throw "Protected host ACL does not grant exact FullControl to '$sid': $Path"
        }
        $rulesBySid[$sid]=$rule
    }
    if ($rulesBySid.Count -ne 3 -or @($allowedSids | Where-Object { -not $rulesBySid.ContainsKey($_) }).Count -ne 0) {
        throw "Protected host ACL is missing a required exact access rule: $Path"
    }
}

function Assert-OrchestratorProtectedAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$UserSid
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Protected host path is a reparse point: $Path" }
    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    Assert-OrchestratorProtectedAclState -Acl $acl -IsContainer ([bool]$item.PSIsContainer) -Path $Path -UserSid $UserSid
}

function Get-OrchestratorFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Assert-OrchestratorExecutableIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)
    $identityText=[IO.File]::ReadAllText((Get-OrchestratorFullPath $Path -MustExist),[Text.Encoding]::UTF8)
    try { $identity=$identityText | ConvertFrom-Json -Depth 16 -DateKind String -ErrorAction Stop } catch { throw 'Executable identity is not valid UTF-8 JSON.' }
    if ([int]$identity.SchemaVersion -ne 1) { throw 'Executable identity SchemaVersion must be 1.' }
    $entries=@($identity.Executables)
    if ($entries.Count -ne $script:ExecutableProperties.Count) { throw "Executable identity must contain exactly $($script:ExecutableProperties.Count) bound tools." }
    for ($index=0; $index -lt $script:ExecutableProperties.Count; $index++) {
        $expectedName=$script:ExecutableProperties[$index]; $entry=$entries[$index]
        $executablePath=[string]$entry.Path
        if ([string]$entry.Name -cne $expectedName -or [string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$entry.Length -lt 1 -or
            [string]::IsNullOrWhiteSpace($executablePath) -or -not [IO.Path]::IsPathFullyQualified($executablePath) -or $executablePath -cnotmatch '^[A-Za-z]:\\') {
            throw "Executable identity entry is invalid at index $index."
        }
        $fullPath=[IO.Path]::GetFullPath($executablePath)
        if (-not [string]::Equals($executablePath,$fullPath,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($fullPath) -cne '.exe') {
            throw "$expectedName executable identity path is not canonical."
        }
        $item=Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::Equals($item.FullName,$fullPath,[StringComparison]::OrdinalIgnoreCase) -or
            [int64]$item.Length -ne [int64]$entry.Length -or (Get-OrchestratorFileSha256 $fullPath) -cne [string]$entry.Sha256) {
            throw "$expectedName failed exact executable identity verification."
        }
        if ($expectedName -ceq 'PowerShellExecutable' -and
            -not [string]::Equals($fullPath,'C:\Program Files\PowerShell\7\pwsh.exe',[StringComparison]::OrdinalIgnoreCase)) {
            throw 'PowerShellExecutable identity does not name the stable protected PowerShell path.'
        }
    }
    return $entries.Count
}

function Get-OrchestratorTextSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text)
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Get-OrchestratorJsonObjectMap {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw "$Context must be a JSON object." }
    $map=[Collections.Generic.Dictionary[string,Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($property in $Element.EnumerateObject()) {
        $name=[string]$property.Name
        if (-not $names.Add($name)) { throw "$Context contains a duplicate or case-variant property '$name'." }
        if (-not $map.TryAdd($name,$property.Value.Clone())) { throw "$Context contains duplicate property '$name'." }
    }
    return ,$map
}

function Assert-OrchestratorExactJsonObject {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context,
        [Parameter(Mandatory = $true)][string[]]$PropertyNames
    )

    $map=Get-OrchestratorJsonObjectMap -Element $Element -Context $Context
    if ($map.Count -ne $PropertyNames.Count) { throw "$Context must contain exactly: $([string]::Join(', ', $PropertyNames))." }
    foreach ($propertyName in $PropertyNames) {
        if (-not $map.ContainsKey($propertyName)) { throw "$Context is missing property '$propertyName'." }
    }
    foreach ($actualName in @($map.Keys)) {
        if ($PropertyNames -cnotcontains $actualName) { throw "$Context contains unknown property '$actualName'." }
    }
    return ,$map
}

function Assert-OrchestratorJsonKind {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][Text.Json.JsonValueKind]$Kind,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Element.ValueKind -ne $Kind) { throw "$Context must be JSON $($Kind.ToString().ToLowerInvariant())." }
}

function Assert-OrchestratorJsonInt64 {
    param(
        [Parameter(Mandatory = $true)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-OrchestratorJsonKind -Element $Element -Kind Number -Context $Context
    $integer=[int64]0
    if (-not $Element.TryGetInt64([ref]$integer)) { throw "$Context must be a 64-bit JSON integer." }
}

function Assert-OrchestratorIntegrityManifestJsonSchema {
    param([Parameter(Mandatory = $true)][string]$JsonText)

    $document=$null
    try {
        $options=[Text.Json.JsonDocumentOptions]::new()
        $options.AllowTrailingCommas=$false
        $options.CommentHandling=[Text.Json.JsonCommentHandling]::Disallow
        $options.MaxDepth=16
        $document=[Text.Json.JsonDocument]::Parse($JsonText,$options)
        $root=Assert-OrchestratorExactJsonObject -Element $document.RootElement -Context 'IntegrityManifest' -PropertyNames @(
            'SchemaVersion','BundleId','MinimumPowerShellVersion','EntryPoint','ConfigFile','ExecutableIdentityFile',
            'InstallerBootstrap','SourceConfig','CodexDistribution','Files'
        )
        Assert-OrchestratorJsonInt64 -Element $root['SchemaVersion'] -Context 'IntegrityManifest.SchemaVersion'
        foreach ($name in @('BundleId','MinimumPowerShellVersion','EntryPoint','ConfigFile','ExecutableIdentityFile')) {
            Assert-OrchestratorJsonKind -Element $root[$name] -Kind String -Context "IntegrityManifest.$name"
        }
        foreach ($name in @('InstallerBootstrap','SourceConfig')) {
            $metadata=Assert-OrchestratorExactJsonObject -Element $root[$name] -Context "IntegrityManifest.$name" -PropertyNames @('Sha256','Length')
            Assert-OrchestratorJsonKind -Element $metadata['Sha256'] -Kind String -Context "IntegrityManifest.$name.Sha256"
            Assert-OrchestratorJsonInt64 -Element $metadata['Length'] -Context "IntegrityManifest.$name.Length"
        }
        $codex=Assert-OrchestratorExactJsonObject -Element $root['CodexDistribution'] -Context 'IntegrityManifest.CodexDistribution' -PropertyNames @('Sha256','Length','FileName')
        Assert-OrchestratorJsonKind -Element $codex['Sha256'] -Kind String -Context 'IntegrityManifest.CodexDistribution.Sha256'
        Assert-OrchestratorJsonInt64 -Element $codex['Length'] -Context 'IntegrityManifest.CodexDistribution.Length'
        Assert-OrchestratorJsonKind -Element $codex['FileName'] -Kind String -Context 'IntegrityManifest.CodexDistribution.FileName'
        Assert-OrchestratorJsonKind -Element $root['Files'] -Kind Array -Context 'IntegrityManifest.Files'
        $index=0
        foreach ($element in $root['Files'].EnumerateArray()) {
            $entry=Assert-OrchestratorExactJsonObject -Element $element -Context "IntegrityManifest.Files[$index]" -PropertyNames @('RelativePath','Sha256','Length')
            foreach ($name in @('RelativePath','Sha256')) { Assert-OrchestratorJsonKind -Element $entry[$name] -Kind String -Context "IntegrityManifest.Files[$index].$name" }
            Assert-OrchestratorJsonInt64 -Element $entry['Length'] -Context "IntegrityManifest.Files[$index].Length"
            $index++
        }
    }
    finally { if ($null -ne $document) { $document.Dispose() } }
}

function Get-OrchestratorBundleIdentityFromManifest {
    param(
        [Parameter(Mandatory = $true)][object]$Manifest,
        [Parameter(Mandatory = $true)][object[]]$Entries
    )

    foreach ($metadataName in @('InstallerBootstrap','SourceConfig','CodexDistribution')) {
        $metadata=$Manifest.$metadataName
        if ($null -eq $metadata -or [string]$metadata.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$metadata.Length -lt 1) {
            throw "Integrity manifest has invalid '$metadataName' provenance."
        }
    }
    if ([string]$Manifest.CodexDistribution.FileName -cne 'codex.exe') { throw 'Integrity manifest Codex distribution filename must be codex.exe.' }
    $identityLines=@(
        "installer-bootstrap`0$([string]$Manifest.InstallerBootstrap.Sha256)`0$([int64]$Manifest.InstallerBootstrap.Length)",
        "source-config`0$([string]$Manifest.SourceConfig.Sha256)`0$([int64]$Manifest.SourceConfig.Length)",
        "source-codex`0$([string]$Manifest.CodexDistribution.Sha256)`0$([int64]$Manifest.CodexDistribution.Length)"
    ) + @($Entries | Sort-Object RelativePath | ForEach-Object { "$([string]$_.RelativePath)`0$([string]$_.Sha256)`0$([int64]$_.Length)" })
    return Get-OrchestratorTextSha256 ([string]::Join("`n",$identityLines))
}

function Import-OrchestratorTrustedCoreModules {
    $expectedPowerShell='C:\Program Files\PowerShell\7\pwsh.exe'
    $actualPowerShell=[IO.Path]::GetFullPath([Environment]::ProcessPath)
    if (-not [string]::Equals($actualPowerShell,$expectedPowerShell,[StringComparison]::OrdinalIgnoreCase)) { throw "Production runtime must use '$expectedPowerShell'." }
    $expectedHome=[IO.Path]::GetDirectoryName($expectedPowerShell)
    if (-not [string]::Equals([IO.Path]::GetFullPath($PSHOME),$expectedHome,[StringComparison]::OrdinalIgnoreCase)) { throw 'PowerShell home does not match the stable protected executable directory.' }
    foreach ($moduleName in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')) {
        $manifest=[IO.Path]::Combine($expectedHome,'Modules',$moduleName,"$moduleName.psd1")
        if (-not [IO.File]::Exists($manifest) -or ([IO.File]::GetAttributes($manifest) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Trusted PowerShell module manifest is missing or redirected: $moduleName" }
        Microsoft.PowerShell.Core\Import-Module -Name $manifest -Force -ErrorAction Stop
        $loaded=Microsoft.PowerShell.Core\Get-Module -Name $moduleName | Where-Object { [string]::Equals([IO.Path]::GetFullPath($_.Path),$manifest,[StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($null -eq $loaded) { throw "PowerShell loaded $moduleName from an unexpected location." }
    }
}

function Initialize-OrchestratorTokenNative {
    if ('SashimiBoyAutomation.LinkedTokenProcess' -as [type]) { return }
    Microsoft.PowerShell.Utility\Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SashimiBoyAutomation
{
    public sealed class LinkedTokenProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = "";
        public string StandardError { get; set; } = "";
    }

    public static class LinkedTokenProcess
    {
        private const UInt32 TOKEN_QUERY = 0x0008;
        private const int TokenLinkedToken = 19;
        private const int TokenElevation = 20;
        private const UInt32 STARTF_USESTDHANDLES = 0x00000100;
        private const UInt32 HANDLE_FLAG_INHERIT = 0x00000001;
        private const UInt32 LOGON_WITH_PROFILE = 0x00000001;
        private const UInt32 CREATE_SUSPENDED = 0x00000004;
        private const UInt32 CREATE_NO_WINDOW = 0x08000000;
        private const UInt32 WAIT_OBJECT_0 = 0x00000000;
        private const UInt32 WAIT_TIMEOUT = 0x00000102;
        private const UInt32 WAIT_FAILED = 0xffffffff;
        private const UInt32 RESUME_FAILED = 0xffffffff;
        private const int JobObjectExtendedLimitInformation = 9;
        private const UInt32 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const UInt32 LINKED_CHILD_TIMEOUT_MS = 11u * 60u * 60u * 1000u;
        private const UInt32 TERMINATION_CONFIRM_TIMEOUT_MS = 10000u;
        private const int PIPE_DRAIN_TIMEOUT_MS = 10000;
        private const UInt32 FORCED_TERMINATION_EXIT_CODE = 0x53415348;

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public UInt32 dwX;
            public UInt32 dwY;
            public UInt32 dwXSize;
            public UInt32 dwYSize;
            public UInt32 dwXCountChars;
            public UInt32 dwYCountChars;
            public UInt32 dwFillAttribute;
            public UInt32 dwFlags;
            public UInt16 wShowWindow;
            public UInt16 cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public UInt32 dwProcessId;
            public UInt32 dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public UInt32 LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public UInt32 ActiveProcessLimit;
            public UIntPtr Affinity;
            public UInt32 PriorityClass;
            public UInt32 SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public UInt64 ReadOperationCount;
            public UInt64 WriteOperationCount;
            public UInt64 OtherOperationCount;
            public UInt64 ReadTransferCount;
            public UInt64 WriteTransferCount;
            public UInt64 OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SECURITY_ATTRIBUTES attributes, UInt32 size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr handle, UInt32 mask, UInt32 flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UInt32 WaitForSingleObject(IntPtr handle, UInt32 milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out UInt32 exitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, UInt32 informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UInt32 ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, UInt32 exitCode);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, UInt32 desiredAccess, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, UInt32 informationLength, out UInt32 returnLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr token,
            UInt32 logonFlags,
            string applicationName,
            StringBuilder commandLine,
            UInt32 creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        private static Win32Exception Error(string operation)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed");
        }

        private static void CloseNativeHandle(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        private static void DisposeQuietly(IDisposable resource)
        {
            if (resource == null) return;
            try { resource.Dispose(); }
            catch { }
        }

        private static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw Error("CreateJobObject");
            IntPtr buffer = IntPtr.Zero;
            try
            {
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                UInt32 size = (UInt32)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                buffer = Marshal.AllocHGlobal((int)size);
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, size))
                    throw Error("SetInformationJobObject");
                return job;
            }
            catch
            {
                CloseHandle(job);
                throw;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        private static void RequestProcessTreeTermination(ref IntPtr job, IntPtr process, bool jobAssigned)
        {
            if (jobAssigned && job != IntPtr.Zero)
            {
                // Closing the only non-inheritable job handle is the second,
                // kernel-enforced kill path if explicit termination fails.
                TerminateJobObject(job, FORCED_TERMINATION_EXIT_CODE);
                CloseNativeHandle(ref job);
                return;
            }
            if (process != IntPtr.Zero) TerminateProcess(process, FORCED_TERMINATION_EXIT_CODE);
            CloseNativeHandle(ref job);
        }

        private static IntPtr OpenCurrentToken()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) throw Error("OpenProcessToken");
            return token;
        }

        private static bool IsTokenElevated(IntPtr token)
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                UInt32 returned;
                if (!GetTokenInformation(token, TokenElevation, buffer, sizeof(int), out returned)) throw Error("GetTokenInformation(TokenElevation)");
                return Marshal.ReadInt32(buffer) != 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static IntPtr GetLinkedToken(IntPtr token)
        {
            IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                UInt32 returned;
                if (!GetTokenInformation(token, TokenLinkedToken, buffer, (UInt32)IntPtr.Size, out returned)) throw Error("GetTokenInformation(TokenLinkedToken)");
                return Marshal.ReadIntPtr(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public static bool IsCurrentProcessElevated()
        {
            IntPtr token = IntPtr.Zero;
            try { token = OpenCurrentToken(); return IsTokenElevated(token); }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        public static LinkedTokenProcessResult RunUnelevated(string executable, string commandLine, string workingDirectory)
        {
            IntPtr currentToken = IntPtr.Zero;
            IntPtr linkedToken = IntPtr.Zero;
            IntPtr job = IntPtr.Zero;
            IntPtr stdoutRead = IntPtr.Zero, stdoutWrite = IntPtr.Zero;
            IntPtr stderrRead = IntPtr.Zero, stderrWrite = IntPtr.Zero;
            IntPtr stdinRead = IntPtr.Zero, stdinWrite = IntPtr.Zero;
            PROCESS_INFORMATION process = new PROCESS_INFORMATION();
            bool processCreated = false;
            bool jobAssigned = false;
            bool processTerminated = false;
            bool terminationAttempted = false;
            SafeFileHandle stdoutHandle = null, stderrHandle = null;
            FileStream stdoutStream = null, stderrStream = null;
            StreamReader stdoutReader = null, stderrReader = null;
            try
            {
                currentToken = OpenCurrentToken();
                if (!IsTokenElevated(currentToken)) throw new InvalidOperationException("The relaunch parent token is not elevated.");
                linkedToken = GetLinkedToken(currentToken);
                if (linkedToken == IntPtr.Zero || IsTokenElevated(linkedToken)) throw new InvalidOperationException("The linked token is missing or still elevated.");
                string currentSid = WindowsIdentity.GetCurrent().User.Value;
                using (WindowsIdentity linkedIdentity = new WindowsIdentity(linkedToken))
                {
                    if (!String.Equals(currentSid, linkedIdentity.User.Value, StringComparison.Ordinal)) throw new InvalidOperationException("The linked token belongs to a different Windows account.");
                }

                SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES();
                attributes.nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
                attributes.bInheritHandle = true;
                if (!CreatePipe(out stdoutRead, out stdoutWrite, ref attributes, 0)) throw Error("CreatePipe(stdout)");
                if (!CreatePipe(out stderrRead, out stderrWrite, ref attributes, 0)) throw Error("CreatePipe(stderr)");
                if (!CreatePipe(out stdinRead, out stdinWrite, ref attributes, 0)) throw Error("CreatePipe(stdin)");
                if (!SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0) ||
                    !SetHandleInformation(stderrRead, HANDLE_FLAG_INHERIT, 0) ||
                    !SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0)) throw Error("SetHandleInformation");

                STARTUPINFO startup = new STARTUPINFO();
                startup.cb = Marshal.SizeOf<STARTUPINFO>();
                startup.dwFlags = STARTF_USESTDHANDLES;
                startup.hStdInput = stdinRead;
                startup.hStdOutput = stdoutWrite;
                startup.hStdError = stderrWrite;
                job = CreateKillOnCloseJob();
                if (!CreateProcessWithTokenW(linkedToken, LOGON_WITH_PROFILE, executable, new StringBuilder(commandLine), CREATE_NO_WINDOW | CREATE_SUSPENDED, IntPtr.Zero, workingDirectory, ref startup, out process)) throw Error("CreateProcessWithTokenW");
                processCreated = true;
                if (!AssignProcessToJobObject(job, process.hProcess)) throw Error("AssignProcessToJobObject");
                jobAssigned = true;

                CloseNativeHandle(ref stdoutWrite);
                CloseNativeHandle(ref stderrWrite);
                CloseNativeHandle(ref stdinRead);
                CloseNativeHandle(ref stdinWrite);

                stdoutHandle = new SafeFileHandle(stdoutRead, true); stdoutRead = IntPtr.Zero;
                stderrHandle = new SafeFileHandle(stderrRead, true); stderrRead = IntPtr.Zero;
                stdoutStream = new FileStream(stdoutHandle, FileAccess.Read);
                stderrStream = new FileStream(stderrHandle, FileAccess.Read);
                stdoutReader = new StreamReader(stdoutStream, new UTF8Encoding(false), true);
                stderrReader = new StreamReader(stderrStream, new UTF8Encoding(false), true);
                Task<string> stdoutTask = stdoutReader.ReadToEndAsync();
                Task<string> stderrTask = stderrReader.ReadToEndAsync();

                if (ResumeThread(process.hThread) == RESUME_FAILED) throw Error("ResumeThread");
                CloseNativeHandle(ref process.hThread);

                UInt32 waitResult = WaitForSingleObject(process.hProcess, LINKED_CHILD_TIMEOUT_MS);
                if (waitResult == WAIT_TIMEOUT)
                {
                    terminationAttempted = true;
                    RequestProcessTreeTermination(ref job, process.hProcess, jobAssigned);
                    UInt32 confirmation = WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_TIMEOUT_MS);
                    processTerminated = confirmation == WAIT_OBJECT_0;
                    if (!processTerminated)
                        throw new TimeoutException("The unelevated host child exceeded its fixed deadline; termination could not be confirmed.");
                    throw new TimeoutException("The unelevated host child exceeded its fixed deadline and was terminated.");
                }
                if (waitResult == WAIT_FAILED) throw Error("WaitForSingleObject");
                if (waitResult != WAIT_OBJECT_0) throw new InvalidOperationException("The unelevated host child returned an unexpected wait result.");
                processTerminated = true;

                // End any descendants before draining pipes; descendants cannot
                // retain inherited stdout/stderr handles beyond this boundary.
                CloseNativeHandle(ref job);
                if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, PIPE_DRAIN_TIMEOUT_MS))
                    throw new InvalidOperationException("The unelevated host child output channels did not close after termination.");
                string stdout = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();
                UInt32 nativeExitCode;
                if (!GetExitCodeProcess(process.hProcess, out nativeExitCode)) throw Error("GetExitCodeProcess");
                return new LinkedTokenProcessResult { ExitCode = unchecked((int)nativeExitCode), StandardOutput = stdout, StandardError = stderr };
            }
            finally
            {
                if (processCreated && !processTerminated && !terminationAttempted)
                {
                    terminationAttempted = true;
                    RequestProcessTreeTermination(ref job, process.hProcess, jobAssigned);
                    WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_TIMEOUT_MS);
                }
                CloseNativeHandle(ref job);
                DisposeQuietly(stdoutReader);
                DisposeQuietly(stderrReader);
                DisposeQuietly(stdoutStream);
                DisposeQuietly(stderrStream);
                DisposeQuietly(stdoutHandle);
                DisposeQuietly(stderrHandle);
                CloseNativeHandle(ref process.hThread);
                CloseNativeHandle(ref process.hProcess);
                CloseNativeHandle(ref stdinRead);
                CloseNativeHandle(ref stdinWrite);
                CloseNativeHandle(ref stdoutRead);
                CloseNativeHandle(ref stdoutWrite);
                CloseNativeHandle(ref stderrRead);
                CloseNativeHandle(ref stderrWrite);
                CloseNativeHandle(ref linkedToken);
                CloseNativeHandle(ref currentToken);
            }
        }
    }
}
'@ -ErrorAction Stop
}

function Test-OrchestratorTokenElevated {
    Initialize-OrchestratorTokenNative
    return [SashimiBoyAutomation.LinkedTokenProcess]::IsCurrentProcessElevated()
}

function ConvertTo-OrchestratorNativeArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    if ($Value.IndexOfAny([char[]]@([char]0,'"',"`r","`n")) -ge 0 -or $Value.EndsWith('\',[StringComparison]::Ordinal)) { throw 'An orchestrator relaunch argument contains an unsafe character or trailing separator.' }
    return '"' + $Value + '"'
}

function Invoke-OrchestratorUnelevated {
    param()
    $executable='C:\Program Files\PowerShell\7\pwsh.exe'
    $arguments=[Collections.Generic.List[string]]::new()
    foreach ($value in @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$PSCommandPath,'-ConfigPath',$ConfigPath,'-IntegrityManifestPath',$IntegrityManifestPath,'-UnelevatedChild')) { $arguments.Add((ConvertTo-OrchestratorNativeArgument ([string]$value))) }
    if (-not [string]::IsNullOrWhiteSpace($MutexName)) { $arguments.Add((ConvertTo-OrchestratorNativeArgument '-MutexName')); $arguments.Add((ConvertTo-OrchestratorNativeArgument $MutexName)) }
    $commandLine=(ConvertTo-OrchestratorNativeArgument $executable)+' '+[string]::Join(' ',$arguments)
    Initialize-OrchestratorTokenNative
    # The protected identity is rehashed at the last external-launch boundary;
    # the unelevated child independently repeats full bundle verification.
    [void](Assert-OrchestratorExecutableIdentity -Path (Join-Path $PSScriptRoot $script:ExecutableIdentityName))
    return [SashimiBoyAutomation.LinkedTokenProcess]::RunUnelevated($executable,$commandLine,$PSScriptRoot)
}

function Assert-OrchestratorRuntimeIntegrity {
    param([Parameter(Mandatory = $true)][string]$ConfigurationPath,[string]$ManifestPath)

    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion -lt $script:MinimumPowerShellVersion) {
        throw "Host orchestrator requires PowerShell Core $script:MinimumPowerShellVersion or newer."
    }
    Import-OrchestratorTrustedCoreModules
    $harness=[string]::Equals($env:SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS,'1',[StringComparison]::Ordinal)
    $protectedInvocation=-not [string]::IsNullOrWhiteSpace($ManifestPath)
    $required=(-not $DryRun -and (-not $harness -or $protectedInvocation))
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        if ($required) { throw 'Production orchestrator runs require an integrity manifest from the protected installed bundle.' }
        return [pscustomobject]@{ Required=$required; Verified=$false; BundleId=''; Manifest=''; ExecutableIdentity=''; ExecutablesVerified=0; Reason='DryRunOrHarnessSourceTree' }
    }

    $programFiles=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $installRoot=Join-Path $programFiles 'SashimiBoyAutomation'
    $bundlesRoot=Join-Path $installRoot 'Bundles'
    $bundleRoot=Get-OrchestratorFullPath $PSScriptRoot -MustExist
    $expectedManifest=Join-Path $bundleRoot 'HostIntegrity.json'
    $expectedConfig=Join-Path $bundleRoot 'Config.json'
    $expectedExecutableIdentity=Join-Path $bundleRoot $script:ExecutableIdentityName
    if (-not (Test-OrchestratorPathEqual (Split-Path -Parent $bundleRoot) $bundlesRoot)) { throw 'The runtime bundle is outside the protected Program Files Bundles directory.' }
    $bundleId=Split-Path -Leaf $bundleRoot
    if ($bundleId -cnotmatch '^[0-9a-f]{64}$') { throw 'The runtime bundle directory is not a canonical SHA-256 identifier.' }
    if (-not (Test-OrchestratorPathEqual $ManifestPath $expectedManifest)) { throw 'IntegrityManifestPath does not identify this runtime bundle manifest.' }
    if (-not (Test-OrchestratorPathEqual $ConfigurationPath $expectedConfig)) { throw 'ConfigPath does not identify this runtime bundle configuration.' }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if ((($identity.Name -split '\\')[-1]) -cne '02031') { throw "Production runtime must execute as Windows user '02031'." }
    $userSid=$identity.User
    foreach ($protectedPath in @($installRoot,$bundlesRoot,$bundleRoot,$expectedManifest,$expectedExecutableIdentity)) { Assert-OrchestratorProtectedAcl $protectedPath $userSid }

    try {
        $manifestBytes=[IO.File]::ReadAllBytes((Get-OrchestratorFullPath $expectedManifest -MustExist))
        $manifestText=[Text.UTF8Encoding]::new($false,$true).GetString($manifestBytes)
        Assert-OrchestratorIntegrityManifestJsonSchema -JsonText $manifestText
        $manifest=$manifestText | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop
    }
    catch { throw "Integrity manifest is not valid exact-schema UTF-8 JSON: $($_.Exception.Message)" }
    if ([int]$manifest.SchemaVersion -ne 1 -or [string]$manifest.BundleId -cne $bundleId -or
        [string]$manifest.MinimumPowerShellVersion -cne $script:MinimumPowerShellVersion.ToString() -or
        [string]$manifest.EntryPoint -cne 'Invoke-SashimiHostOrchestrator.ps1' -or [string]$manifest.ConfigFile -cne 'Config.json' -or
        [string]$manifest.ExecutableIdentityFile -cne $script:ExecutableIdentityName) {
        throw 'Integrity manifest identity does not match this installed runtime bundle.'
    }
    $entries=@($manifest.Files | Sort-Object RelativePath)
    if ($entries.Count -ne $script:RequiredBundleFiles.Count) { throw 'Integrity manifest has the wrong file count.' }
    for ($index=0; $index -lt $script:RequiredBundleFiles.Count; $index++) {
        $entry=$entries[$index]; $requiredName=$script:RequiredBundleFiles[$index]
        if ([string]$entry.RelativePath -cne $requiredName -or [string]$entry.Sha256 -cnotmatch '^[0-9a-f]{64}$' -or [int64]$entry.Length -lt 0) { throw "Integrity manifest contains an invalid entry at index $index." }
        $filePath=Join-Path $bundleRoot $requiredName
        $item=Get-Item -LiteralPath $filePath -Force -ErrorAction Stop
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [int64]$item.Length -ne [int64]$entry.Length -or (Get-OrchestratorFileSha256 $filePath) -cne [string]$entry.Sha256) { throw "Installed runtime file failed its exact hash/length check: $requiredName" }
        Assert-OrchestratorProtectedAcl $filePath $userSid
    }
    $actualItems=@(Get-ChildItem -LiteralPath $bundleRoot -Force -ErrorAction Stop)
    $expectedNames=@($script:RequiredBundleFiles)+@('HostIntegrity.json')
    if (@($actualItems | Where-Object { $_.PSIsContainer }).Count -ne 0 -or @($actualItems | Where-Object { -not $_.PSIsContainer }).Count -ne $expectedNames.Count -or @($actualItems | Where-Object { -not $_.PSIsContainer -and $expectedNames -cnotcontains $_.Name }).Count -ne 0) { throw 'Installed runtime bundle contains an unexpected or missing filesystem entry.' }
    if ((Get-OrchestratorBundleIdentityFromManifest -Manifest $manifest -Entries $entries) -cne $bundleId) {
        throw 'Installed runtime bundle identity hash does not match its manifest provenance and file set.'
    }
    $verifiedExecutableCount=Assert-OrchestratorExecutableIdentity -Path $expectedExecutableIdentity
    return [pscustomobject]@{
        Required=$required; Verified=$true; BundleId=$bundleId; Manifest='HostIntegrity.json'
        ExecutableIdentity=$script:ExecutableIdentityName; ExecutablesVerified=$verifiedExecutableCount; Reason='VerifiedProtectedBundleAndExecutables'
    }
}

function Invoke-OrchestratorScript {
    param([string]$Stage, [string]$ScriptPath, [string[]]$Arguments, [int]$TimeoutSeconds = 600)
    $fullArgs = @('-NoLogo','-NoProfile','-NonInteractive','-File',$ScriptPath) + @($Arguments)
    $script:commands.Add([pscustomobject]@{ Stage=$Stage; FilePath=[string]$script:orchestratorConfig.PowerShellExecutable; Arguments=@($fullArgs | ForEach-Object { Protect-OrchestratorText $_ }) })
    $result = Invoke-SashimiHostProcess -FilePath ([string]$script:orchestratorConfig.PowerShellExecutable) -ArgumentList $fullArgs -WorkingDirectory $PSScriptRoot -TimeoutSeconds $TimeoutSeconds -OwnedProcessRecordPath $script:ownedHostPidPath -CancellationMarkerPath $script:cancellationMarkerPath
    $jsonLines = @($result.StdOut -split '\r?\n' | Where-Object { $_ -match '^\s*\{' })
    if ($jsonLines.Count -eq 0) { throw "$Stage returned no result JSON; exit=$($result.ExitCode); stderr=$($result.StdErr)" }
    try { $json = $jsonLines[-1] | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw "$Stage returned invalid JSON: $($_.Exception.Message)" }
    if (-not $result.Succeeded -or -not [bool](Get-SashimiPropertyValue $json 'Success' $false)) {
        throw "$Stage failed; exit=$($result.ExitCode); error=$([string](Get-SashimiPropertyValue $json 'Error' $result.StdErr))"
    }
    return $json
}

function Set-OrchestratorState {
    param([string]$Name, [string]$Detail = '')
    $script:state = $Name
    $event = [ordered]@{ State=$Name; Detail=(Protect-OrchestratorText $Detail); AtUtc=[DateTime]::UtcNow.ToString('o') }
    $script:events.Add([pscustomobject]$event)
    if ($null -ne $script:workspace -and -not $DryRun) {
        $record = [ordered]@{
            SchemaVersion=1; RunId=$script:workspace.RunId; State=$Name; Detail=(Protect-OrchestratorDiagnostic $Detail)
            UpdatedAtUtc=[DateTime]::UtcNow.ToString('o'); OwnerPid=$PID
            IssueNumber=if ($null -eq $script:selection) { 0 } else { [int](Get-SashimiPropertyValue $script:selection 'IssueNumber' 0) }
            Role=if ($null -eq $script:selection) { 'None' } else { [string](Get-SashimiPropertyValue $script:selection 'Role' 'None') }
        }
        Write-SashimiRunState -StatePath (Join-Path $script:workspace.StatePath 'RunState.json') -State $record
        Write-SashimiUtf8File -Path (Join-Path $script:workspace.StatePath 'Events.json') -Content (ConvertTo-SashimiJson $script:events.ToArray() -Pretty)
    }
}

function Stop-OrchestratorOwnedProcesses {
    if ($null -eq $script:workspace -or $DryRun) { return [pscustomobject]@{ Success=$true; Stopped=@(); Remaining=@() } }
    $stopped = New-Object 'System.Collections.Generic.List[int]'
    $remaining = New-Object 'System.Collections.Generic.List[int]'
    foreach ($recordPath in @((Join-Path $script:workspace.StatePath 'OwnedHostPids.json'),(Join-Path $script:workspace.StatePath 'OwnedUnityPids.json'))) {
        if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) { continue }
        try {
            $record = Read-SashimiJsonFile $recordPath
            foreach ($ownedProcess in @((Get-SashimiPropertyValue $record 'Processes' @()))) {
                $ownedPid = [int](Get-SashimiPropertyValue $ownedProcess 'Id' 0)
                $ownedStart = [string](Get-SashimiPropertyValue $ownedProcess 'StartTimeUtc' '')
                if ($ownedPid -lt 1 -or [string]::IsNullOrWhiteSpace($ownedStart)) { $remaining.Add(-1); continue }
                $process = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
                if ($null -eq $process) {
                    Update-SashimiOwnedProcessLedger -Path $recordPath -Action Remove -ProcessId $ownedPid -StartTimeUtc $ownedStart
                    continue
                }
                if ($process.StartTime.ToUniversalTime().ToString('o') -cne $ownedStart) { $remaining.Add($ownedPid); continue }
                try {
                    if (Stop-SashimiOwnedProcessTree -Process $process) {
                        Update-SashimiOwnedProcessLedger -Path $recordPath -Action Remove -ProcessId $ownedPid -StartTimeUtc $ownedStart
                        $stopped.Add($ownedPid)
                    }
                    else { $remaining.Add($ownedPid) }
                }
                catch { $remaining.Add($ownedPid) }
            }
        }
        catch { $remaining.Add(-1) }
    }
    return [pscustomobject]@{ Success=($remaining.Count -eq 0); Stopped=$stopped.ToArray(); Remaining=$remaining.ToArray() }
}

try {
    $script:integrityResult = [pscustomobject]@{
        Required=(-not $DryRun -and (-not [string]::Equals($env:SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS,'1',[StringComparison]::Ordinal) -or -not [string]::IsNullOrWhiteSpace($IntegrityManifestPath)))
        Verified=$false; BundleId=''; Manifest=''; ExecutableIdentity=''; ExecutablesVerified=0; Reason='VerificationFailed'
    }
    $script:integrityResult = Assert-OrchestratorRuntimeIntegrity -ConfigurationPath $ConfigPath -ManifestPath $IntegrityManifestPath
    $currentProcessElevated=Test-OrchestratorTokenElevated
    if ($script:integrityResult.Verified -and -not $DryRun) {
        $script:privilegeBoundaryResult=[pscustomobject]@{ Required=$true; Verified=(-not $currentProcessElevated); CurrentProcessElevated=$currentProcessElevated; Relaunched=[bool]$UnelevatedChild; Reason=if ($currentProcessElevated) { 'ElevatedParentMustRelaunch' } else { 'UnelevatedTokenVerified' } }
        $fixtureArguments=@($QueueFixturePath,$CodexFixturePath,$UnityFixturePath,$PublishFixturePath)
        if (@($fixtureArguments | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) { throw 'Protected production bundle runs cannot use fixture adapters.' }
        if (-not [string]::IsNullOrWhiteSpace($MutexName) -and $MutexName -cne 'Global\SashimiBoyHostOrchestrator') { throw 'Protected production bundle runs cannot override the global mutex.' }
        if ($currentProcessElevated) {
            if ($UnelevatedChild) { throw 'The linked-token child is still elevated; refusing to load host runtime code.' }
            $child=Invoke-OrchestratorUnelevated
            # Never relay arbitrary child stdout/stderr. Parse the single
            # structured result first and require proof that the protected
            # same-SID child actually crossed the privilege boundary.
            $childJsonLines=@($child.StandardOutput -split '\r?\n' | Where-Object { $_ -match '^\s*\{' })
            if ($childJsonLines.Count -eq 0) { throw 'The unelevated host child returned no result JSON object.' }
            try { $childResult=$childJsonLines[-1] | ConvertFrom-Json -Depth 64 -DateKind String -ErrorAction Stop } catch { throw 'The unelevated host child returned invalid result JSON.' }
            $childPrivilege=Get-OrchestratorPropertyValue $childResult 'PrivilegeBoundary' $null
            $childIntegrity=Get-OrchestratorPropertyValue $childResult 'Integrity' $null
            if ([int](Get-OrchestratorPropertyValue $childResult 'SchemaVersion' 0) -ne 1 -or
                [string](Get-OrchestratorPropertyValue $childResult 'Tool' '') -cne 'Invoke-SashimiHostOrchestrator' -or
                -not [bool](Get-OrchestratorPropertyValue $childIntegrity 'Verified' $false) -or
                [int](Get-OrchestratorPropertyValue $childIntegrity 'ExecutablesVerified' 0) -ne $script:ExecutableProperties.Count -or
                [bool](Get-OrchestratorPropertyValue $childPrivilege 'CurrentProcessElevated' $true) -or
                -not [bool](Get-OrchestratorPropertyValue $childPrivilege 'Verified' $false) -or
                -not [bool](Get-OrchestratorPropertyValue $childPrivilege 'Required' $false) -or
                -not [bool](Get-OrchestratorPropertyValue $childPrivilege 'Relaunched' $false)) {
                throw 'The unelevated host child did not prove the required integrity and privilege boundary.'
            }
            if ([int]$child.ExitCode -eq 0 -and -not [bool](Get-OrchestratorPropertyValue $childResult 'Success' $false)) { throw 'The unelevated host child exit/result states disagree.' }
            $validatedChildJson=ConvertTo-OrchestratorJson $childResult
            $protectedChildJson=Protect-OrchestratorText $validatedChildJson
            [Console]::Out.WriteLine($protectedChildJson)
            exit ([int]$child.ExitCode)
        }
    }
    else {
        if ($UnelevatedChild) { throw 'UnelevatedChild is valid only for an integrity-verified installed runtime.' }
        if ($currentProcessElevated) { throw 'Dry-run and test-harness source-tree execution is forbidden from an elevated host process.' }
        $script:privilegeBoundaryResult=[pscustomobject]@{ Required=$false; Verified=(-not $currentProcessElevated); CurrentProcessElevated=$currentProcessElevated; Relaunched=$false; Reason='DryRunOrHarnessProbeOnly' }
    }
    . (Join-Path $PSScriptRoot 'HostAutomation.Common.ps1')
    $script:commonLoaded = $true
    $ConfigPath = ConvertTo-SashimiPath -Path $ConfigPath
    if ($QueueFixturePath) { $QueueFixturePath = ConvertTo-SashimiPath -Path $QueueFixturePath }
    if ($CodexFixturePath) { $CodexFixturePath = ConvertTo-SashimiPath -Path $CodexFixturePath }
    if ($UnityFixturePath) { $UnityFixturePath = ConvertTo-SashimiPath -Path $UnityFixturePath }
    if ($PublishFixturePath) { $PublishFixturePath = ConvertTo-SashimiPath -Path $PublishFixturePath }
    $script:orchestratorConfig = Import-SashimiHostConfig -ConfigPath $ConfigPath
    if ([string]::IsNullOrWhiteSpace($MutexName)) { $MutexName = [string]$script:orchestratorConfig.MutexName }
    if ([string]$MutexName -cne [string]$script:orchestratorConfig.MutexName -and -not (Test-SashimiHarnessMode)) { throw 'Production runs must use the exact configured global mutex name.' }
    foreach ($fixture in @($QueueFixturePath,$CodexFixturePath,$UnityFixturePath,$PublishFixturePath)) {
        if ($fixture) { [void](Assert-SashimiFixtureAllowed -FixturePath $fixture -DryRun:$DryRun) }
    }

    $lease = Enter-SashimiHostMutex -Name $MutexName -TimeoutMilliseconds 0
    if (-not $lease.Acquired) {
        $alreadyRunning = $true; $state = 'AlreadyRunning'; $success = $true
    }
    else {
        if ($DryRun) {
            if (-not $QueueFixturePath) { throw 'Orchestrator -DryRun requires -QueueFixturePath; live queue access is forbidden.' }
            $queueArgs = @('-ConfigPath',$ConfigPath,'-FixturePath',$QueueFixturePath,'-DryRun')
            $selection = Invoke-OrchestratorScript -Stage 'Read fixture ProjectV2 queue' -ScriptPath (Join-Path $PSScriptRoot 'Get-SashimiProjectQueue.ps1') -Arguments $queueArgs -TimeoutSeconds ([int]$script:orchestratorConfig.Timeouts.GitHubSeconds)
            if ([int]$selection.DispatchCount -gt 1) { throw 'Queue attempted to dispatch more than one Issue.' }
            if ([bool]$selection.Selected) {
                $runnerName = if ([string]$selection.Role -ceq 'Reviewer') { 'Invoke-SashimiReviewerRun.ps1' } else { 'Invoke-SashimiDeveloperRun.ps1' }
                $commands.Add([pscustomobject]@{
                    Stage='Dispatch exactly one dry-run role'; FilePath=[string]$script:orchestratorConfig.PowerShellExecutable
                    Arguments=@('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot $runnerName),'-ConfigPath',$ConfigPath,'-SelectionPath','<in-memory-selection>','-RunPath','<fresh-run>','-DryRun')
                })
                $state = 'DryRunPlanned'
            }
            else { $state = 'NoWork' }
            $success = $true
        }
        else {
            $retentionResults = @(Invoke-SashimiRetention -RunRoot ([string]$script:orchestratorConfig.RunRoot) -RetentionDays ([int]$script:orchestratorConfig.ArtifactRetentionDays))
            $workspace = New-SashimiRunWorkspace -RunRoot ([string]$script:orchestratorConfig.RunRoot)
            $script:cancellationMarkerPath = Join-Path $workspace.RunPath 'cancel.requested'
            $script:ownedHostPidPath = Join-Path $workspace.StatePath 'OwnedHostPids.json'
            foreach ($pidFile in @($script:ownedHostPidPath,(Join-Path $workspace.StatePath 'OwnedUnityPids.json'))) {
                Write-SashimiUtf8File -Path $pidFile -Content '{"SchemaVersion":1,"ProcessIds":[],"Processes":[]}'
            }
            foreach ($retentionFailure in @($retentionResults | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Error) })) {
                $events.Add([pscustomobject]@{ State='RetentionWarning'; Detail='Retention cleanup failed; evidence was preserved.'; AtUtc=[DateTime]::UtcNow.ToString('o') })
            }
            Set-OrchestratorState 'QueueLookup'
            $queueOutput = Join-Path $workspace.StatePath 'Selection.json'
            $queueArgs = @('-ConfigPath',$ConfigPath,'-OutputPath',$queueOutput,'-CancellationMarkerPath',$script:cancellationMarkerPath)
            $selection = Invoke-SashimiWithRetry -MaximumAttempts ([int]$script:orchestratorConfig.Retry.MaximumAttempts) -CooldownSeconds ([int]$script:orchestratorConfig.Retry.CooldownSeconds) -Operation {
                Invoke-OrchestratorScript -Stage 'Read live ProjectV2 queue' -ScriptPath (Join-Path $PSScriptRoot 'Get-SashimiProjectQueue.ps1') -Arguments $queueArgs -TimeoutSeconds ([int]$script:orchestratorConfig.Timeouts.GitHubSeconds)
            } -ShouldRetry { param($value,$record) return ($null -ne $record) } -CancellationMarkerPath $script:cancellationMarkerPath
            if ([int]$selection.DispatchCount -gt 1) { throw 'Queue attempted to dispatch more than one Issue.' }
            if (-not [bool]$selection.Selected) {
                Set-OrchestratorState 'NoWork'; $success = $true
            }
            else {
                if (Test-SashimiCancellation $workspace.RunPath) { throw 'Run cancellation was requested before dispatch.' }
                Set-OrchestratorState 'Dispatched' "$($selection.Role)/$($selection.Mode) Issue #$($selection.IssueNumber)"
                $runnerName = if ([string]$selection.Role -ceq 'Reviewer') { 'Invoke-SashimiReviewerRun.ps1' } elseif ([string]$selection.Role -ceq 'Developer') { 'Invoke-SashimiDeveloperRun.ps1' } else { throw 'Queue returned an unsupported role.' }
                $runnerArgs = @('-ConfigPath',$ConfigPath,'-SelectionPath',$queueOutput,'-RunPath',$workspace.RunPath)
                if ($CodexFixturePath) { $runnerArgs += @('-CodexFixturePath',$CodexFixturePath) }
                if ($UnityFixturePath) { $runnerArgs += @('-UnityFixturePath',$UnityFixturePath) }
                if ($PublishFixturePath) { $runnerArgs += @('-PublishFixturePath',$PublishFixturePath) }
                $runnerTimeout = [int]$script:orchestratorConfig.Timeouts.CodexSeconds + (3 * [int]$script:orchestratorConfig.Timeouts.UnityStageSeconds) + (2 * [int]$script:orchestratorConfig.Timeouts.GeneratorSeconds) + 1800
                $runnerResult = Invoke-OrchestratorScript -Stage "Execute $($selection.Role) run" -ScriptPath (Join-Path $PSScriptRoot $runnerName) -Arguments $runnerArgs -TimeoutSeconds $runnerTimeout
                Set-OrchestratorState 'ValidatedAndPublished'
                Set-OrchestratorState 'Succeeded'; $success = $true
            }
        }
    }
}
catch {
    $exitCode = 1; $success = $false; $errorMessage = Protect-OrchestratorText $_.Exception.Message
    if ($script:commonLoaded) {
        try { Set-OrchestratorState 'Failed' $errorMessage } catch { $errorMessage += '; state persistence also failed.' }
    }
}
finally {
    if ($null -ne $workspace -and -not $DryRun) {
        $processCleanup = Stop-OrchestratorOwnedProcesses
        if (-not $processCleanup.Success) {
            $success = $false; $exitCode = 1; $errorMessage = ((Protect-OrchestratorText $errorMessage) + '; run-owned process cleanup was not confirmed; evidence preserved.').TrimStart(';',' ')
            try { Set-OrchestratorState 'Failed' $errorMessage } catch { }
        }
        if ($success) {
            $cleanup = Remove-SashimiRunRepository -RunPath $workspace.RunPath -RunRoot ([string]$script:orchestratorConfig.RunRoot)
            if (-not $cleanup.Success) {
                $success = $false; $exitCode = 1; $errorMessage = 'Repository cleanup failed; evidence was preserved.'
                try { Set-OrchestratorState 'Failed' $errorMessage } catch { }
            }
        }
        else {
            $cleanup = [pscustomobject]@{ Success=$false; Preserved=$true; Error='Run failed or cancellation/owned-process cleanup was not confirmed.' }
        }
    }
}

$cleanupSummary = if ($null -eq $cleanup) { $null } else {
    [ordered]@{
        Success=[bool](Get-OrchestratorPropertyValue $cleanup 'Success' $false)
        Removed=[bool](Get-OrchestratorPropertyValue $cleanup 'Removed' $false)
        Preserved=[bool](Get-OrchestratorPropertyValue $cleanup 'Preserved' $false)
        Path='Repository'
        ErrorCode=if ([bool](Get-OrchestratorPropertyValue $cleanup 'Success' $false)) { '' } else { 'RepositoryCleanupNotCompleted' }
    }
}
$retentionSummary = [ordered]@{
    Examined=@($retentionResults).Count
    Removed=@($retentionResults | Where-Object { [bool](Get-OrchestratorPropertyValue $_ 'Removed' $false) }).Count
    Preserved=@($retentionResults | Where-Object { [bool](Get-OrchestratorPropertyValue $_ 'Preserved' $false) }).Count
    Failures=@($retentionResults | Where-Object { -not [string]::IsNullOrWhiteSpace([string](Get-OrchestratorPropertyValue $_ 'Error' '')) }).Count
}
$output = [ordered]@{
    SchemaVersion=1; Tool='Invoke-SashimiHostOrchestrator'; Success=$success; ExitCode=$exitCode; DryRun=[bool]$DryRun
    State=$state; AlreadyRunning=$alreadyRunning; ExactlyOneIssue=($null -eq $selection -or [int](Get-OrchestratorPropertyValue $selection 'DispatchCount' 0) -le 1)
    Integrity=$script:integrityResult; PrivilegeBoundary=$script:privilegeBoundaryResult
    RunId=if ($null -eq $workspace) { '' } else { $workspace.RunId }; RunPath=if ($null -eq $workspace) { '' } else { Protect-OrchestratorText $workspace.RunPath }
    Selection=(ConvertTo-OrchestratorSelectionSummary $selection); RunnerResult=(ConvertTo-OrchestratorRunnerSummary $runnerResult)
    Cleanup=$cleanupSummary; Retention=$retentionSummary; Commands=$commands.ToArray(); Events=$events.ToArray()
    StartedAtUtc=$startedAt.ToString('o'); FinishedAtUtc=[DateTime]::UtcNow.ToString('o')
    Error=if ([string]::IsNullOrWhiteSpace($errorMessage)) { '' } else { 'HostOrchestratorFailed' }
}
$safeOutput = if ($script:commonLoaded) { Protect-SashimiData -Value $output } else { $output }
if ($null -ne $workspace -and -not $DryRun) {
    try {
        Write-SashimiUtf8File -Path (Join-Path $workspace.StatePath 'FinalResult.json') -Content (ConvertTo-OrchestratorJson $safeOutput -Pretty)
        Write-SashimiUtf8File -Path (Join-Path $workspace.ArtifactsPath 'RunResult.json') -Content (ConvertTo-OrchestratorJson $safeOutput -Pretty)
    }
    catch {
        $safeOutput.Success = $false; $safeOutput.ExitCode = 1; $safeOutput.Error = Protect-OrchestratorDiagnostic ("Final result persistence failed: $($_.Exception.Message)")
        $exitCode = 1
    }
}
if ($null -ne $lease) { Exit-SashimiHostMutex -Lease $lease }
[Console]::Out.WriteLine((ConvertTo-OrchestratorJson $safeOutput))
exit $exitCode
