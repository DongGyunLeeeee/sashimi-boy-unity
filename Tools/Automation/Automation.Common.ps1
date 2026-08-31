#requires -Version 5.1

Set-StrictMode -Version Latest

function ConvertTo-AutomationJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [AllowNull()]
        [object]$InputObject
    )

    process {
        $InputObject | ConvertTo-Json -Depth 16 -Compress
    }
}

function ConvertTo-AutomationPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Path,

        [switch]$AllowMissing
    )

    $candidate = $Path
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path -Path (Get-Location).ProviderPath -ChildPath $candidate
    }

    if (Test-Path -LiteralPath $candidate) {
        $candidate = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).ProviderPath
    }
    elseif (-not $AllowMissing) {
        throw "Path does not exist: $Path"
    }

    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        $fullPath = $fullPath.TrimEnd([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar))
    }

    return $fullPath
}

function ConvertTo-AutomationLexicalPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    $candidate = $Path
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path -Path (Get-Location).ProviderPath -ChildPath $candidate
    }

    # Deliberately do not call Resolve-Path here. Cleanup guards must inspect
    # the caller's lexical ancestor chain so a junction cannot disappear from
    # validation by resolving to its target first.
    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        $fullPath = $fullPath.TrimEnd([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar))
    }

    return $fullPath
}

function Test-AutomationPathEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,

        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    $normalizedLeft = ConvertTo-AutomationPath -Path $Left -AllowMissing
    $normalizedRight = ConvertTo-AutomationPath -Path $Right -AllowMissing
    return [string]::Equals($normalizedLeft, $normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-AutomationPathWithin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $normalizedPath = ConvertTo-AutomationPath -Path $Path -AllowMissing
    $normalizedRoot = ConvertTo-AutomationPath -Path $Root -AllowMissing
    if (Test-AutomationPathEqual -Left $normalizedPath -Right $normalizedRoot) {
        return $true
    }

    $rootWithSeparator = $normalizedRoot + [System.IO.Path]::DirectorySeparatorChar
    return $normalizedPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-AutomationNativeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FilePath,

        [Parameter()]
        [AllowEmptyCollection()]
        [string[]]$ArgumentList = @(),

        [Parameter()]
        [string]$WorkingDirectory
    )

    $standardErrorPath = [System.IO.Path]::GetTempFileName()
    $pushedLocation = $false
    $exitCode = 127
    $standardOutput = ''
    $standardError = ''

    try {
        if ($WorkingDirectory) {
            $resolvedWorkingDirectory = ConvertTo-AutomationPath -Path $WorkingDirectory
            Push-Location -LiteralPath $resolvedWorkingDirectory
            $pushedLocation = $true
        }

        try {
            $previousErrorActionPreference = $ErrorActionPreference
            $previousWhatIfPreference = $WhatIfPreference
            $ErrorActionPreference = 'Continue'
            $WhatIfPreference = $false
            try {
                $outputLines = @(& $FilePath @ArgumentList 2> $standardErrorPath)
                $exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
                $WhatIfPreference = $previousWhatIfPreference
            }
            if ($null -eq $exitCode) {
                $exitCode = 0
            }

            if ($outputLines.Count -gt 0) {
                $standardOutput = [string]::Join([Environment]::NewLine, [string[]]$outputLines)
            }
        }
        catch {
            $exitCode = 127
            $standardError = $_.Exception.Message
        }

        if (Test-Path -LiteralPath $standardErrorPath -PathType Leaf) {
            $capturedError = [System.IO.File]::ReadAllText($standardErrorPath)
            if ($capturedError) {
                if ($standardError) {
                    $standardError += [Environment]::NewLine
                }
                $standardError += $capturedError.TrimEnd()
            }
        }
    }
    finally {
        if ($pushedLocation) {
            Pop-Location
        }

        if (Test-Path -LiteralPath $standardErrorPath -PathType Leaf) {
            [System.IO.File]::Delete($standardErrorPath)
        }
    }

    return [pscustomobject][ordered]@{
        FilePath  = $FilePath
        Arguments = @($ArgumentList)
        ExitCode  = [int]$exitCode
        StdOut    = $standardOutput
        StdErr    = $standardError
        Succeeded = ($exitCode -eq 0)
    }
}

function Format-AutomationCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter()]
        [string[]]$ArgumentList = @()
    )

    $parts = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @($FilePath) + @($ArgumentList)) {
        $text = [string]$value
        if ($text.Length -eq 0 -or $text -match "[\s']") {
            $parts.Add("'" + $text.Replace("'", "''") + "'")
        }
        else {
            $parts.Add($text)
        }
    }
    return [string]::Join(' ', $parts.ToArray())
}

function Get-AutomationSpecVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryPath
    )

    $repository = ConvertTo-AutomationPath -Path $RepositoryPath
    $versionPath = Join-Path -Path $repository -ChildPath 'Docs\Automation\SPEC_VERSION'
    if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        throw "SPEC_VERSION does not exist: $versionPath"
    }

    $lines = @([System.IO.File]::ReadAllLines($versionPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1) {
        throw 'SPEC_VERSION must contain exactly one non-empty line.'
    }

    $version = $lines[0].Trim()
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'SPEC_VERSION must not be empty.'
    }

    return $version
}

function Assert-AutomationPathHasNoReparsePoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $current = ConvertTo-AutomationLexicalPath -Path $Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not allowed in an automation path: $($item.FullName)"
            }
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $current, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = $parent
    }
}

function Assert-AutomationTreeHasNoReparsePoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $normalizedRoot = ConvertTo-AutomationLexicalPath -Path $Root
    Assert-AutomationPathHasNoReparsePoint -Path $normalizedRoot

    if (-not (Test-Path -LiteralPath $normalizedRoot -PathType Container)) {
        throw "Automation tree root does not exist: $normalizedRoot"
    }

    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($normalizedRoot)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing recursive automation cleanup because a reparse point exists: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                $pending.Push($entry.FullName)
            }
        }
    }
}

function Get-AutomationOwnedWorkspace {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$WorkspaceRoot,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AllowedRoot,

        [string]$ExpectedRunId
    )

    $normalizedWorkspaceRoot = ConvertTo-AutomationLexicalPath -Path $WorkspaceRoot
    $normalizedAllowedRoot = ConvertTo-AutomationLexicalPath -Path $AllowedRoot

    Assert-AutomationPathHasNoReparsePoint -Path $normalizedAllowedRoot
    Assert-AutomationPathHasNoReparsePoint -Path $normalizedWorkspaceRoot

    if (-not (Test-Path -LiteralPath $normalizedWorkspaceRoot -PathType Container)) {
        throw "Owned automation workspace does not exist: $normalizedWorkspaceRoot"
    }
    if (-not (Test-AutomationPathWithin -Path $normalizedWorkspaceRoot -Root $normalizedAllowedRoot) -or
        (Test-AutomationPathEqual -Left $normalizedWorkspaceRoot -Right $normalizedAllowedRoot)) {
        throw "Refusing cleanup outside the automation temp root: $normalizedWorkspaceRoot"
    }

    $leafName = Split-Path -Leaf $normalizedWorkspaceRoot
    if ($leafName -notmatch '^ReviewIntegration-\d{8}T\d{6}Z-([0-9a-fA-F]{32})$') {
        throw "Refusing cleanup because the workspace name is not an owned review-integration name: $leafName"
    }
    $pathRunId = $Matches[1].ToLowerInvariant()

    $markerPath = Join-Path -Path $normalizedWorkspaceRoot -ChildPath '.sashimi-boy-automation-owned.json'
    Assert-AutomationTreeHasNoReparsePoint -Root $normalizedWorkspaceRoot
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Refusing cleanup because the ownership marker is missing: $markerPath"
    }

    try {
        $marker = Get-Content -Raw -LiteralPath $markerPath -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Refusing cleanup because the ownership marker is invalid JSON: $($_.Exception.Message)"
    }

    if ([int]$marker.SchemaVersion -ne 1) {
        throw "Refusing cleanup because the ownership marker schema is unsupported."
    }
    $markerRunId = [string]$marker.RunId
    if ($markerRunId -notmatch '^[0-9a-fA-F]{32}$' -or
        -not [string]::Equals($markerRunId, $pathRunId, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup because the ownership marker RunId does not match the workspace name."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId) -and
        -not [string]::Equals($markerRunId, $ExpectedRunId, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup because the ownership marker does not match this run."
    }
    if (-not (Test-AutomationPathEqual -Left ([string]$marker.WorkspaceRoot) -Right $normalizedWorkspaceRoot)) {
        throw "Refusing cleanup because the ownership marker workspace path does not match."
    }
    if ([System.IO.Path]::GetFileName([string]$marker.Script) -ne 'New-ReviewIntegration.ps1') {
        throw "Refusing cleanup because the ownership marker script is unexpected."
    }

    return [pscustomobject][ordered]@{
        WorkspaceRoot = $normalizedWorkspaceRoot
        AllowedRoot   = $normalizedAllowedRoot
        MarkerPath    = $markerPath
        RunId         = $markerRunId.ToLowerInvariant()
        Marker        = $marker
    }
}

function Remove-AutomationOwnedWorkspace {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$WorkspaceRoot,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AllowedRoot,

        [string]$ExpectedRunId
    )

    $ownedWorkspace = Get-AutomationOwnedWorkspace `
        -WorkspaceRoot $WorkspaceRoot `
        -AllowedRoot $AllowedRoot `
        -ExpectedRunId $ExpectedRunId

    # Revalidate immediately before the sole production recursive delete. If a
    # junction or symbolic link appeared after marker validation, retain the
    # workspace and require manual inspection instead of following it.
    Assert-AutomationTreeHasNoReparsePoint -Root $ownedWorkspace.WorkspaceRoot
    Remove-Item -LiteralPath $ownedWorkspace.WorkspaceRoot -Recurse -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $ownedWorkspace.WorkspaceRoot) {
        throw "Automation workspace still exists after cleanup: $($ownedWorkspace.WorkspaceRoot)"
    }

    return $ownedWorkspace.Marker
}

function ConvertTo-AutomationExitCode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$NativeExitCode
    )

    if ($NativeExitCode -ge 1 -and $NativeExitCode -le 255) {
        return $NativeExitCode
    }

    return 1
}
