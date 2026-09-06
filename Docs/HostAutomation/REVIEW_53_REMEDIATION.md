# PR #53 Phase A blocker remediation

Reviewed head: `b8dbe0897a9bb701ec62cf9cf16d355ce9b9a6cf`

This document records Phase A of the ReviewFix for Issue #52 / Draft PR #53.
It is intentionally limited to the six independent-review Blockers and the
hardening that those fixes necessarily overlap. It does not declare the pull
request merge-ready and it does not replace the Owner's final verification.

## Finding validation and remediation matrix

| Finding | Validation at reviewed head | Root cause | Changed production functions | Regression | Phase A status |
| --- | --- | --- | --- | --- | --- |
| B1: ambient Codex transport redirection | Confirmed | The Codex allowlist retained ambient endpoint, proxy, CA-bundle, and authentication-location variables; the first remediation also left root-level capability probes able to read user config. | `Get-SashimiCodexEnvironmentPolicy`; `Assert-SashimiCodexWorkspaceConfigurationAbsent`; `Invoke-AdapterProcess`; `Invoke-CodexCapabilityProbe`; Codex execution argument construction | `CodexEnvironmentIsAllowlistedAndCredentialStoreOnly`; `CodexTransportEnvironmentIsHermeticForProbesAndExecution`; `CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch` | **PASS (79/79 full fixture suite)** — the sole no-op capability probe and execution rebuild the same minimal environment, both require no-user/strict config plus disabled command transports, expose no configurable endpoint, and reject project `.codex` state before launch. |
| B2: command validation permits untrusted execution | Confirmed | Free-form command text was audited only after Codex had already executed it, and native tools were trusted by leaf name with incomplete shell-token filtering. | Codex no-command launch policy; `Get-CodexCommandTokens`; `Assert-CodexCommandExecutionAllowed`; `Assert-CodexJsonHasUniquePropertyNames`; `ConvertFrom-CodexJsonLines` | `CodexCommandAuditRejectsWrappersAndRetainsMetadataOnly`; `CodexCommandBoundaryRejectsRealFakePayloadsWithoutSentinels`; `CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch` | **PASS (79/79 full fixture suite)** — command execution is disabled at the Codex capability boundary and every exact or disguised command-bearing event is terminal. The defense-in-depth AST accepts no relative/PATH/alias/function/module/scriptblock/operator/nested-shell surface and validates only exact bound executable identities before rejecting the event. |
| B3: profile paths redacted before audit | Confirmed | `Invoke-SashimiHostProcess` returned redacted output, so the JSONL parser and path audit never saw original child bytes. | `Invoke-SashimiHostProcess`; `Invoke-AdapterProcess`; `Assert-CodexDecodedJsonObjectSafe`; `ConvertFrom-CodexJsonLines`; Codex artifact promotion and outer result sinks | `CodexRawOutputIsAuditedBeforeRedactionAndNeverPromoted`; `CodexForbiddenProfileOutputCannotReachOrchestratorFinalSinks` | **PASS (79/79 full fixture suite)** — bounded original output exists only in memory until validation, including recursive audit of decoded JSON strings that were Unicode-escaped on the wire, while every persisted or returned diagnostic and orchestrator sink is structured and content-free. |
| B4: executable hash not bound to process creation | Confirmed | Only the executable leaf was hashed; ancestors, reparse points, and untrusted ACLs were not checked. The configured Codex path was task-user-writable and traversed junctions. | installer executable projection; `Assert-SashimiProtectedCodexExecutable`; `Open-SashimiExecutableLaunchLease`; executable identity validation | `ExecutableIdentityRejectsPathShadowAndChangedBinaryBeforeLaunch`; `ProtectedCodexProcessGateRejectsWritableReparseChangedAndReplacementRaces` | **PASS (79/79 full fixture suite)** — Codex is copied to a hash-keyed protected Program Files distribution, bound by path/length/SHA-256, and revalidated under a no-write/no-delete lease immediately before launch. Same-administrator replacement remains outside the defended threat boundary. |
| B5: install not pinned to reviewed bundle | Confirmed | DryRun calculated the bytes currently present and live install silently recalculated and trusted a different bundle; the first remediation included a self-hash but lacked a separately supplied pre-execution bootstrap pin. | Owner in-memory pinned-launch contract; `New-InstallerBundlePlan`; entry/bootstrap authorization gates; `Install-InstallerCodexDistribution`; `Install-InstallerBundle`; scheduler boundary; protected-runtime manifest identity | `OwnerPinnedInstallerLeaseRejectsReplacementBeforeExecution`; `ProtectedManifestIdentityIncludesExactInstallerProvenance`; `InstallerDryRunHasExactTaskContractAndNoMutation`; `InstallerRejectsChangedBytesAfterPreviewBeforeAnyPrivilegedBoundary` | **PASS (79/79 full fixture suite plus two-step installer DryRun)** — the Owner launcher hashes the installer outside the child process under a no-write/no-delete lease retained across exact `pwsh -File` execution; live install also requires the retained external SHA-256 and complete BundleId before any Program Files, ACL, or scheduler mutation. BundleId covers bootstrap, source configuration, Codex distribution, canonical projection, runtime, and executable identities. |
| B6: Unity can rewrite Host-owned Git state | Confirmed | The Developer snapshot was incomplete, existed only around Codex, and was not rechecked after Unity or before commit/LFS/push. Network commands used mutable `origin`; Git LFS still accepted repository URL/transfer overrides; Unity had no kill-on-close descendant boundary. Operation pseudorefs and sequencer state were also absent from the first remediation's explicit snapshot. | `Assert-DeveloperGitOperationStateAbsent`; `Get-DeveloperGitControlManifest`; `Get-GitOwnershipSnapshot`; `Assert-GitOwnershipUnchanged`; `Invoke-DeveloperGit`; Developer delivery gates; `Set-SashimiFixedGitProcessEnvironment`; `Assert-SashimiValidationGitOperationStateAbsent`; `Get-SashimiValidationGitControlManifest`; `Get-SashimiUnityGitControlSnapshot`; `Assert-SashimiUnityGitControlUnchanged`; kill-on-close Unity process boundary | `GitLfsRoutingIsPinnedAndRepositoryRedirectsFailClosed`; `UnityKillOnCloseJobPreventsDelayedDescendantMutation`; `NewWorkUnityGitControlDriftOccursBeforeAnyProjectMutation`; `UnityGitControlAndDelayedDescendantDriftSuppressEveryDeliveryMutation`; `ValidationOnlyResumeReusesExactExistingBranchAndNoNewPr` | **PASS (79/79 full fixture suite)** — complete control state, including real merge/sequencer operation files and every non-object/LFS-payload `.git` entry, is checked after every untrusted stage and immediately before mutations. Git and LFS use immutable canonical endpoints and exact refspecs, job membership must reach zero, and drift is terminal with publication suppressed. |

## Overlapping review findings

Phase A also closes the portions of the following findings needed by the
Blocker fixes:

- configuration is parsed as strict UTF-8 JSON, duplicate and unknown fields
  are rejected recursively, secret/transport fields are forbidden, and the
  installer stages a canonical allowlisted projection;
- new bundles and Codex distributions are prepared in marker-owned sibling
  staging directories and made visible by an atomic directory rename;
- Codex output capture and promoted Codex artifacts are bounded and use a
  recursive, no-reparse allowlist;
- Unity raw logs/XML use locked bounded strict-UTF-8 reads, and the public Unity
  artifact tree has an exact recursive manifest, stable hash/length checks,
  per-file and total quotas, and atomic State quarantine/removal on violation;
- installer ACL verification requires the complete `ReadAndExecute` mask;
- installer fixtures inject the scheduler implementation behind the same typed
  boundary used in production.

These overlaps do not declare the remaining Major findings closed. Phase B
still owns an independent full-scope review of configuration and transaction
hardening, complete process accounting outside the Unity boundary, the final
freshness barrier for all remote publication operations, Reviewer-only
Unity-default-drift classification, generator reproducibility, non-Codex
evidence quotas, and the duplicate AudioListener/EventSystem scan. The two
Minor production paths necessarily overlapped Phase A: the scheduler DryRun
fixture now intercepts the production scheduler boundary, and ACL verification
requires exact full `ReadAndExecute` rights; both have executable regressions.

## Owner installation contract

Run the preview from the exact reviewed checkout and record the lowercase
64-character `BundleId`. Before preview, supply the installer SHA-256 retained
with the independent review; the Owner launcher compares it to
`ExternalInstallerSha256` computed from its already-open handle.
`InstallerBootstrapSha256` from installer JSON must equal both but is never its
own authorization source.
The exact reviewed in-memory launcher, complete environment reconstruction, and
reparse/lease checks are in [OPERATIONS.md](OPERATIONS.md#install-the-scheduled-task).
Paste that function into a fresh elevated exact protected PowerShell 7
`-NoProfile` session; do not execute it from another mutable wrapper file.

```powershell
$ConfigPath = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation\Config.json'
$InstallerPath = [IO.Path]::GetFullPath(
  '.\Tools\HostAutomation\Install-SashimiHostAutomation.ps1')
$OrchestratorPath = [IO.Path]::GetFullPath(
  '.\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1')
$ReviewedInstallerSha256 = '<exact SHA-256 from independent review evidence>'
$Preview = Invoke-OwnerPinnedInstaller -InstallerPath $InstallerPath `
  -ExpectedInstallerSha256 $ReviewedInstallerSha256 `
  -InstallerArgumentList @(
    '-ConfigPath', $ConfigPath,
    '-OrchestratorPath', $OrchestratorPath,
    '-DryRun')
$Preview.ResultJson
```

After inspecting and recording that result outside the mutable checkout, type
the two exact reviewed values explicitly rather than deriving either from a new
installer invocation:

```powershell
$ExpectedBundleId = '<exact reviewed BundleId from preview>'
$ExpectedInstallerSha256 = $ReviewedInstallerSha256
$Install = Invoke-OwnerPinnedInstaller -InstallerPath $InstallerPath `
  -ExpectedInstallerSha256 $ExpectedInstallerSha256 `
  -InstallerArgumentList @(
    '-ConfigPath', $ConfigPath,
    '-OrchestratorPath', $OrchestratorPath,
    '-ExpectedBundleId', $ExpectedBundleId,
    '-ExpectedInstallerSha256', $ExpectedInstallerSha256)
$Install.ResultJson
```

The launcher opens with `FileShare.Read`, independently hashes that handle,
rechecks the no-reparse path, starts exact protected PowerShell with a separate
`-File` argument array, and keeps its handle open through child exit. It aborts
before starting a changed bootstrap; the installer aborts before staging, ACL,
or Task Scheduler mutation if the payload differs. Same-administrator/SYSTEM
replacement remains outside the defended threat boundary.
