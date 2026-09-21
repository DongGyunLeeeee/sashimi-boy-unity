# Invoke only from the marked installer harness after staging its real plan.
param([string]$BundleRoot,[string]$HostRoot,[object]$Plan,[Security.Principal.SecurityIdentifier]$UserSid)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$orchestrator = Join-Path $HostRoot 'Invoke-SashimiHostOrchestrator.ps1'
$tokens=$null; $errors=$null
$ast = [Management.Automation.Language.Parser]::ParseFile($orchestrator,[ref]$tokens,[ref]$errors)
Assert-HostTest ($errors.Count -eq 0) 'Cannot inspect runtime declarations with parser errors.'
$assignments = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -ceq '$script:RequiredBundleFiles'
},$false))
Assert-HostTest ($assignments.Count -eq 1) 'Runtime required file declaration is missing or ambiguous.'
. ([scriptblock]::Create($assignments[0].Extent.Text))
foreach ($name in @('Get-OrchestratorFileSha256','Get-OrchestratorTextSha256',
    'Get-OrchestratorBundleIdentityFromManifest','Assert-OrchestratorBundlePayload')) {
    Set-Item -LiteralPath ("Function:\$name") -Value (Get-HostTestFunctionScriptBlock $orchestrator $name)
}
$aclReads = [Collections.Generic.List[string]]::new()
function Assert-OrchestratorProtectedAcl {
    param($Path,$UserSid)
    [void](Get-InstallerHarnessFixtureLocation -Path $Path -Purpose 'Runtime payload fixture ACL read')
    $aclReads.Add($Path)
}

# Consume the actual installer-produced complete manifest and its staged files.
# This positive runtime path catches file-order drift that rejection-only tests miss.
$manifest = Read-SashimiJsonFile (Join-Path $BundleRoot 'HostIntegrity.json')
Assert-HostTest ($manifest.Files.Count -eq 11) 'Expected the complete installed runtime payload.'
Assert-OrchestratorBundlePayload $manifest $BundleRoot $UserSid
Assert-HostTest ($aclReads.Count -eq 11) 'Runtime did not verify every staged payload ACL.'
$manifestJson = $manifest | ConvertTo-Json -Depth 32
$reversed = $manifestJson | ConvertFrom-Json -Depth 32
[array]::Reverse($reversed.Files)
Assert-OrchestratorBundlePayload $reversed $BundleRoot $UserSid

foreach ($kind in @('missing','duplicate','unexpected','case','hash','length','provenance')) {
    $changed = $manifestJson | ConvertFrom-Json -Depth 32
    switch ($kind) {
        'missing' { $changed.Files = @($changed.Files | Select-Object -Skip 1) }
        'duplicate' { $changed.Files[1] = $changed.Files[0] }
        'unexpected' { $changed.Files[0].RelativePath = 'Unknown.ps1' }
        'case' { $changed.Files[0].RelativePath = $changed.Files[0].RelativePath.ToUpperInvariant() }
        'hash' { $changed.Files[0].Sha256 = '0' * 64 }
        'length' { $changed.Files[0].Length++ }
        'provenance' { $changed.SourceConfig.Length++ }
    }
    Assert-HostThrows { Assert-OrchestratorBundlePayload $changed $BundleRoot $UserSid } 'file count|invalid entry|hash|length'
}
$extra = Join-Path $BundleRoot 'unexpected.txt'
try {
    [IO.File]::WriteAllText($extra,'fixture')
    Assert-HostThrows { Assert-OrchestratorBundlePayload $manifest $BundleRoot $UserSid } 'unexpected or missing'
} finally { [IO.File]::Delete($extra) }
Assert-OrchestratorBundlePayload $manifest $BundleRoot $UserSid

$identity = Read-SashimiJsonFile (Join-Path $BundleRoot 'ExecutableIdentity.json')
$codex = $identity.Executables[0]
Assert-HostTest ($identity.SchemaVersion -eq 2 -and $identity.Executables.Count -eq 6) 'Installer lost the six-tool identity contract.'
Assert-HostTest ((Get-SashimiCodexDistributionHash $codex) -ceq [string]$Plan.CodexDistribution.Sha256) 'Installer and process gate disagree on the two-file distribution identity.'
Assert-HostTest ($manifest.CodexDistribution.Length -eq ($codex.Length + $codex.CodeModeHost.Length)) 'Manifest length does not cover both binaries.'
Assert-HostTest ((Split-Path -Leaf (Split-Path -Parent $codex.Path)) -cne $codex.Sha256) 'New distribution would collide with the legacy one-file directory.'
Assert-HostTest (@(Get-ChildItem -LiteralPath $Plan.CodexDistribution.Root -Force).Count -eq 2) 'Companion was not installed beside codex.exe.'

# Removing/changing either image and adding a third file must fail the real verifier.
foreach ($file in @($Plan.CodexDistribution.Files)) {
    $path = Join-Path $Plan.CodexDistribution.Root $file.FileName
    try {
        [IO.File]::Delete($path)
        Assert-HostThrows { Assert-InstallerCodexDistribution $Plan.CodexDistribution $UserSid } 'missing or unexpected'
        [IO.File]::WriteAllBytes($path,[byte[]]$file.Bytes)
        [IO.File]::AppendAllText($path,'changed')
        Assert-HostThrows { Assert-InstallerCodexDistribution $Plan.CodexDistribution $UserSid } 'hash|length'
    } finally { [IO.File]::WriteAllBytes($path,[byte[]]$file.Bytes) }
}
$extra = Join-Path $Plan.CodexDistribution.Root 'unexpected.dll'
try {
    [IO.File]::WriteAllText($extra,'fixture')
    Assert-HostThrows { Assert-InstallerCodexDistribution $Plan.CodexDistribution $UserSid } 'missing or unexpected'
} finally { [IO.File]::Delete($extra) }
Assert-InstallerCodexDistribution $Plan.CodexDistribution $UserSid

# The normal app source uses a version alias. Both snapshots must come from its
# one resolved target; protected installation remains free of junctions.
$alias = Join-Path (Split-Path -Parent $BundleRoot) 'source-version-alias'
$sourcePath = [string](Read-SashimiJsonFile $script:fakeConfigPath).CodexExecutable
try {
    [void](New-Item -ItemType Junction -Path $alias -Target (Split-Path -Parent $sourcePath))
    $pair = Get-InstallerCodexSourceSnapshots (Join-Path $alias (Split-Path -Leaf $sourcePath))
    Assert-HostTest ((Get-InstallerCodexDistributionHash $pair.Executable $pair.CodeModeHost) -ceq $Plan.CodexDistribution.Sha256) 'Versioned source alias changed captured distribution identity.'
    Assert-HostTest ((Split-Path -Parent $pair.Executable.Path) -ceq (Split-Path -Parent $pair.CodeModeHost.Path)) 'Source snapshots came from different resolved directories.'
} finally {
    if (Test-Path -LiteralPath $alias) {
        [void](Get-SashimiMarkedFixtureRoot $alias)
        Assert-HostTest ([IO.Path]::GetDirectoryName($alias) -ceq (Split-Path -Parent $BundleRoot)) 'Source alias escaped fixture root.'
        [IO.Directory]::Delete($alias,$false)
    }
}
