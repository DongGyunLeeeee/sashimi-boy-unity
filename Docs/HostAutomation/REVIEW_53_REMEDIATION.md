# PR #53 remediation — Phase A and partial Phase B

## Review-loop repair — 2026-09-17 (current)

The prior local M1/M2 fixes and partial M3 work have been recovered into an
isolated clone without changing the original prepared checkout. This run adds
evidence-based Reviewer classification, all-finding handoffs, final publication
revalidation after authentication, exact owned-Host Unity-default drift
classification, and the `.gitkeep` missing-meta correction. See
[the repair report](REVIEW_REPAIR_20260917.md) for executed results and limits.

M4's final common write boundary is implemented and regression-tested for late
changes. GitHub does not expose an atomic conditional operation covering the
whole comment/Project snapshot; no transactional remote lock is claimed.
M5's Host counterpart is implemented with ownership, exact bytes, and clean
protected-checkout gates. Both remain **pending independent review**.

**SAFE_TO_INSTALL: NO.** M3 process accounting coverage, M6 independent-baseline
generator reproducibility, M7 complete non-Codex capture/retention boundaries,
M8 complete scene/prefab component inventory, and useful live Codex file access
are still open. No claim below closes these gaps. All sections dated September
7 or describing Phase A are historical checkpoints, including their older
M4/M5 statuses and test totals. No Project transition, merge, or schedule
activation is authorized by fixture results alone.

## Focused M2 continuation — 2026-09-07

**M2: FIXED_PENDING_INDEPENDENT_REVIEW**, no known unresolved M2 acceptance
gap. The final unfiltered suite passed **84/84** with **163 M2 matrix rows**,
including **141 operation boundaries derived from the production trace**.
M1 and B1–B6/m1–m2 regressions passed. Parser: 14 scripts / 0 errors;
EnvironmentSmoke: 12/12; empty queue: dispatch 0; two pinned previews: DryRun
only. Exact operations, cleanup/final/retry results, commands, scope and live-only
limits are in [PHASE_B_M2_REPORT.md](PHASE_B_M2_REPORT.md).
M1 remains pending independent review. M3–M8 are not closed by this run.
Filesystem evidence is real marked-temp IO; ACL/scheduler outcomes are injected
fakes. **SAFE_TO_INSTALL: NO.** No commit, push or workflow state change.

## Focused M1 continuation — 2026-09-07

**M1: FIXED_PENDING_INDEPENDENT_REVIEW**, no known M1 acceptance gap after
83/83 full fixtures, 7/7 focused cases, 13-script parser, 12/12 EnvironmentSmoke,
empty-queue DryRun, two current Owner-pinned installer previews, and scope/diff
checks. Exact commands, 29 rejection inputs, canonical staging evidence and
limits are in [PHASE_B_M1_REPORT.md](PHASE_B_M1_REPORT.md).
At the M1 checkpoint, M2–M8 remained open; no B/m disposition or historical
result is replaced.
**SAFE_TO_INSTALL: NO.** The checkpoint narrative below is historical except
for the explicitly updated M1 and M2 rows.

## Phase B local checkpoint — 2026-09-07

Host checkpoint: `879072b96da77dd187f27d1444e5e86e12881d83`, branch
`infra/52-windows-host-orchestrator`. Phase B is **PARTIAL**, with **8 Major
findings still open**. The Phase A table below is historical evidence, not an
independent approval of these local edits. **SAFE_TO_INSTALL: NO.**

Three files were already modified when this resume started: Common, installer,
and the fixture suite. Those changes were preserved. The continued changes add
decoded configuration-value auditing, Codex job/ledger wiring, the uninstaller
harness guard, and an opt-in functional smoke. They do not close the full review.

| Finding | Phase B disposition | Precise source / regressions | Remaining verification or implementation |
| --- | --- | --- | --- |
| M1 | Confirmed; fixed pending independent review | Original extra-field/raw-staging weakness was partly fixed in the prepared work. This run closes decoded-key diagnostic leakage and generic staged-config readback: `Assert-SashimiConfigDecodedValues`, `Assert-InstallerConfigDecodedValues`, content-free schema parse failures, `Assert-InstallerBundle` → `Import-InstallerConfig`. `ConfigurationSchemaRejectsUnknownDuplicateSecretAndWrongShapeFields` executes 29 paired runtime/non-DryRun installer negatives; `ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization` executes real projection/distribution/bundle writes with fake ACL boundaries and verifies runtime parity. | No known M1 acceptance gap. Focused 7/7; unfiltered full 83/83; parser 13/0 errors; smoke 12/12; queue dispatch 0; two pinned previews PASS; diff/scope PASS. Exact commands and B/m named regressions: `PHASE_B_M1_REPORT.md`. Recognizable detection cannot detect every opaque secret. Live ACL/scheduler/install and independent review remain unverified; M2–M8 not closed. |
| M2 | Confirmed; fixed pending independent review | `Initialize-InstallerProtectedRoots`, `Assert-InstallerFinalPath`, `Assert-InstallerStagingWorkspace`, existing distribution/bundle stagers, and `Invoke-InstallerBundleRegistration` enforce protected ownership, complete verification, read-only reuse, same-filesystem publication and final verification before the existing scheduler boundary. `M2ProductionTransactionFailureMatrixAndRecovery`: 163 rows, including 141 observed operation boundaries; real writes, fake ACL/scheduler outcomes, interruption/cleanup failure and identical-plan retry. | No known M2 acceptance gap. Full suite 84/84; M1 and B/m regressions PASS; parser 14/0; smoke 12/12; queue/pinned previews PASS. Publication and registration are separate: registration errors retain a complete bundle and explicitly report unconfirmed task state requiring Owner readback; rollback is not claimed. Live installation/ACL/restart/task behavior and independent review remain Owner gates. See `PHASE_B_M2_REPORT.md`. |
| M3 | Confirmed; partial fix, open | `Invoke-AdapterProcess` now passes a ledger and requires the suspended kill-on-close job for probes and execution. Developer/Reviewer forward `State/OwnedHostPids.json`. Native `KillOnCloseProcess.Run` registers PID/creation time before resume and removes the entry only after confirmed termination. Existing transport/raw-output/job regressions: focused group 5/5 passed. | Full process-accounting audit and blocking-Codex timeout/cancellation/later-descendant matrix remain required. Unity's native job path now also honors an explicitly supplied ledger. Exceptional/unconfirmed termination keeps entries for investigation. |
| M4 | Confirmed; open | `Invoke-PublishGh`, `Assert-PublishAuthenticatedActor`, `Assert-LivePin`, `Get-ProjectContract` inspected. Actor lookup still occurs in the mutation wrapper after earlier content/status reads. | Comprehensive final snapshot including main, repository, exact linkage/content/conversation/status; conditional mutation where supported; late-state-change fake matrix. Read-then-write APIs are not atomic. No freshness fix is claimed. |
| M5 | Confirmed; open | `Invoke-SashimiUnityValidation.ps1` still sets `KnownUnityDefaultDrift.Allowed=false`. Current `WORKFLOW.md` / `REVIEWER.md` inspected. | Canonical classification requires a marker/run-bound integration under `%TEMP%/SashimiBoyAutomation` and three clean protected worktrees. Current Host Reviewer clones under configured LocalAppData RunRoot/Repository with Host ownership. That workspace cannot be relabeled as satisfying the canonical temporary-root/marker contract. Runtime integration relocation and the exact positive/near-miss matrix remain required; no policy or drift allowlist was changed. |
| M6 | Confirmed; open | GeneratorRun1/GeneratorRun2 block and `Get-SashimiDeterminismSnapshot` inspected. Both runs still use the same modified project. | Independent byte-identical pre-generator baselines, complete input/output/delta manifests, out-of-scope detection and committed-deliverable comparison; executable random-if-missing and deterministic fixture generators. Existing snapshot fixtures are not reproducibility proof. |
| M7 | Confirmed; open | Native bounded capture and Codex audit ordering preserved. Elevated orchestrator launcher and documented Owner launcher still contain `ReadToEndAsync`; Unity artifact checks do not establish every per-run capture/retention boundary. | Full capture-time per-file/process/run quotas, original-byte audit, termination/cleanup-failure containment and recursive closed manifests through publication/retention. Shell audit cannot undo an executed command. |
| M8 | Confirmed; open | Current Unity log/reference checks and repository scene-context tests inspected. | No Editor component-inventory helper was implemented in this checkpoint. Unloaded scene/prefab coverage, active-context classification, structured manifest and fake regressions remain missing. REAL_UNITY_VALIDATION: NOT_RUN. |

The functional harness is `Test-SashimiCodexFunctionalSmoke.ps1`. Plan launches
only the production adapter in DryRun. Fixture execution uses an absolute,
marker-owned compiled fake through the same production flags and output schema.
It checks the exact `Example.txt` edit, Reviewer response and unchanged file/Git
content; fake Codex checks its PID/creation-time ledger entry before both help
and execution. Every retained fixture result remains labeled NOT_RUN for the
real model. The workspace is marker-checked and scanned without following
reparse points before cleanup; outstanding ledger entries refuse cleanup.

The smoke supplies complete tiny source/diff content in the prompt. Scheduled
Developer/Reviewer prompts currently supply Issue/PR evidence and source paths,
not a scoped source-content/read interface. The fake's advertised CLI help is
not proof of installed Codex capabilities. Therefore useful scheduled operation
remains unverified even if this fake smoke passes. Real Codex was not launched,
official documentation was not fetched, and no unrestricted shell was enabled.

Configuration detection is limited to the documented recognizable token,
Bearer, private-key and credential-URL shapes, now including decoded JSON
strings. It cannot detect every arbitrary opaque secret. Author values remain
fixed to the existing immutable owner identity.

Fixture isolation retains compiler signature/path/argument checks, exact fake
executable paths under marked roots, unique configs (including default configs),
the launch-lease refusal of installed tools in harness mode, and the injected
installer scheduler boundary. Uninstaller harness execution now refuses live
scheduler access before even looking up a task. Fake invocation counts describe
inert fake processes, not real GitHub or scheduler operations.

Final local verification: **82/82 full fixtures, 0 failed**; 2,129 fake
invocations, 15 simulated mutations, zero external/scheduler mutations.
EnvironmentSmoke: 12/12; parser: 13 scripts, zero errors; empty-queue DryRun:
dispatch 0. The corrected first-full-run failures and exact commands/results
are recorded in `PHASE_B_LOCAL_REPORT.md`. B1–B6 and
m1–m2 must be judged using those executed regressions, not this historical Phase
A PASS table. No commit, push, external state change, real Unity, real Codex,
installation, elevation, or real ACL mutation is authorized by this checkpoint.

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
