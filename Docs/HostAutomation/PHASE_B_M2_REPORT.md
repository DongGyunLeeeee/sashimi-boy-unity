# Issue 52 / Draft PR 53 — focused M2 continuation

2026-09-07. Developer evidence only; independent review remains required.
Branch: `infra/52-windows-host-orchestrator`.
Checkpoint: `879072b96da77dd187f27d1444e5e86e12881d83`.
SPEC_VERSION: `1.0.2`, blob `6d7de6e6abef13b18021a3591debc53ac00616d4`.

## Starting state and bounded plan

The prepared worktree matched the Host checkpoint: ten modified tracked files,
three untracked files, nothing staged. All earlier partial work is preserved.
The two earlier Phase B reports remain historical evidence.

Original M2: an interrupted in-place write at the final content-addressed name
left a partial destination that poisoned identical retries. The current partial
implementation already uses protected sibling staging and directory rename.
Inspection found hidden preparation-cleanup failures, in-place ACL rewriting
on existing destinations, and missing executable failure/retry coverage.

Plan recorded before implementation: retain the existing stagers and importer;
make existing-bundle reuse read-only; validate transaction ownership before
writes and promotion; report cleanup failures; add a production-path matrix
derived from observed write/ACL/verification/publication boundaries. Separate
filesystem publication from scheduler registration and report uncertain task
state on registration failure. Do not implement or close M3–M8.

Starting M1 regressions: `Configuration|FixtureCompilerBoundary|FixtureSourceAudit`
selected seven cases, all passed, exit 0, one fake executable invocation and
zero observed external/scheduler mutations. The starting named fixture inventory
contained 83 cases. The unfiltered starting suite then passed 83/83, exit 0,
with 2,129 fake executable invocations, 15 simulated executable-boundary
mutations and zero observed external or scheduler-sentinel mutations.

## Transaction and authority map

Installer entry checks the externally supplied bootstrap hash, initializes its
trusted PowerShell and fixture boundaries, then fully imports/canonicalizes the
configuration. `New-InstallerBundlePlan` captures source bytes and source config
provenance, binds the protected Codex distribution and executable identity, and
calculates BundleId and the exact manifest digest. ExpectedBundleId and the
bootstrap hash are compared before privileged root creation or any ACL change.
The ShouldProcess-approved entry prepares the protected install/bundle/Codex
roots, then calls `Install-InstallerCodexDistribution` and
`Install-InstallerBundle`. These prepare a GUID-named sibling workspace with a
transaction/bundle-bound marker and a Payload directory. Payload/config/identity
and manifest writes occur there; integrity and ACL verification precede the
same-filesystem directory rename. Final-name visibility begins at that rename.
Only the complete final bundle may reach the scheduler boundary.

The staging marker is outside immutable Payload; its unique leaf does not enter
BundleId. The final bundle's pinned integrity manifest is its deterministic
identity evidence. Unknown or incomplete destinations must never be repaired
or removed based on a hash-shaped directory name.

## Validation status

```text
FOCUS: M2
TARGET_STATUS: FIXED_PENDING_INDEPENDENT_REVIEW
TARGET_UNRESOLVED_COUNT: 0
M1_REGRESSION: PASS
OTHER_MAJORS: M3,M4,M5,M6,M7,M8 not closed by this run
BLOCKER_REGRESSIONS: PASS
FIXTURE_TESTS: 84/84, 0 failed
REAL_CODEX_FUNCTIONAL_SMOKE: NOT_RUN
REAL_UNITY_VALIDATION: NOT_RUN
SAFE_TO_INSTALL: NO
```

No known unresolved M2 acceptance gap remains. This is developer source and
fixture evidence, not independent approval or installation authorization.
All real Codex, Unity, GitHub, scheduler and system ACL operations are NOT_RUN.

## Targeted source changes

- `Initialize-InstallerProtectedRoots` exposes the existing entrypoint root
  preparation loop to the same real-IO/fake-ACL matrix. Owner hash and BundleId
  gates still precede this helper.
- `Assert-InstallerFinalPath` requires the exact canonical root/identity pair.
  Both existing bundle and Codex distribution reuse now verify bytes and ACLs
  without rewriting the existing destination. Root preparation retains its
  existing protected-root ACL policy.
- `Assert-InstallerStagingWorkspace` is the shared marker/path/no-reparse
  validation used before writes, promotion and cleanup. Staging verifies parent,
  workspace, marker and payload ACL results. Payload writes use the closed
  runtime filename set. The final bundle's complete no-reparse scan now precedes
  its manifest digest/read.
- Preparation cleanup no longer swallows errors. Only a freshly created empty
  plain directory can be removed before a full marker exists. Partial marker
  writes are preserved with an explicit cleanup failure. Normal cleanup removes
  only the exact validated marker-owned workspace. A final published directory
  is never moved back or removed by cleanup.
- Integrity/ACL verification is repeated at promotion and before registration.
  `Invoke-InstallerBundleRegistration`, called by the actual entrypoint, retains
  a complete published bundle on scheduler failure and reports task state as
  unconfirmed. It adds no invented rollback or OS-wide transaction guarantee.
- Inert production checkpoints expose simulated interruptions at actual
  operations; they have no runtime config/environment callback. Real ACL
  implementations explicitly refuse harness mode unless replaced by the
  marked fixture boundary. The scheduler's existing fixture implementation is
  used directly, including failures before and after its journal write.

## Test methodology and limits

`M2ProductionTransactionFailureMatrixAndRecovery` loads the installer's actual
declarations, leaving its production root preparation, stagers, config importer,
hash/length/closed-tree checks, publication and registration helper intact.
`M2.TransactionFixtures.ps1` substitutes only ACL outcomes and checkpoints.
The successful production trace supplies the fault boundary names and count;
looped payload/ACL operations and repeated verifier calls have distinct names
and occurrence numbers. Each fault must set its reached flag, produce an
asserted exception, leave the expected final state, preserve unrelated data and
prior task state (except the explicitly simulated post-write scheduler case),
and allow an identical plan to succeed after the transient fault is removed.

Additional cases tamper actual on-disk bytes, exercise closed manifest/file
completeness, preserve invalid/foreign destinations, reject wrong/missing
markers and reparse/outside-root cleanup, simulate a concurrent complete
destination winner, and interrupt a real payload write while cleanup also fails.
The latter leaves a uniquely named remainder and then successfully retries the
same approved bundle without deleting the earlier transaction. Persistent
foreign destination negatives deliberately continue to fail closed; their data
is never erased to manufacture a successful retry.

The suite uses actual files/directories only inside its existing marked Windows
temporary root. Fake executable identities remain absolute and fixture-owned;
the existing signed, narrowly validated compiler bootstrap is preserved.
M2 fake scheduler journal writes and fake ACL calls are separate from the suite's
fake executable invocation/mutation counters. Zero external mutations means
zero observations at instrumented boundaries, not a machine-wide attestation.
No real system ACL or Task Scheduler registration is performed.

The injected scheduler journal starts with a prior-task sentinel. Pre-write
failures assert that sentinel is unchanged. The post-append failure asserts a
changed fixture journal and the production helper's unconfirmed-task diagnostic;
it does not claim a live task-definition readback or successful compensation.
On registration failure, `Changed=false` is not evidence of rollback: the Error
explicitly requires Owner readback because the scheduler may have accepted the
request. This run establishes the bounded recovery-failure result and retention
of a complete bundle, not an unchanged live task after an ambiguous RPC failure.

An exception before/after a write simulates interruption, disk or antivirus
failure. It does not establish power-loss/fsync durability, actual installed
ACL/owner inheritance, restart behavior, scheduler RPC behavior or live task
readback. Registration can fail after acceptance; the Owner must inspect task
state before rollout. Actual Codex functional smoke, Unity, GitHub, installation,
independent review, human merge and full Phase B completion remain outside this
run. SAFE_TO_INSTALL remains NO.

## Files and preserved partial work

Changed this run: installer, main fixture suite, new M2 fixture helper,
OPERATIONS, SECURITY, the M2 remediation disposition, and this new report.
All are under Tools/HostAutomation or Docs/HostAutomation.

Earlier Common decoded-key/parser-diagnostic/canonicalization changes,
compiler-bootstrap exception, fixture-owned executable guard, unique immutable
fixture configs, injected scheduler boundary, AST source audit, process ledger
and job wiring, functional smoke and its script are preserved. The M1 and
earlier Phase B reports are not rewritten. M1 remains pending independent
review; no M3–M8 or B/m disposition is changed by this run. No stage, commit,
push, branch switch, checkout/worktree creation, remote mutation, or pipeline
operation is authorized or performed.


## Executed commands and results

All commands ran from BootstrapRepo in stable PowerShell 7.6.5. Fixture filters
use regex `-match`; the isolation audit always executes. No filter was supplied
to either full run. All executed checks returned exit 0; no unexpected harness,
runner, approval or isolation failure occurred in this run.

**Starting M1: 7/7; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'Configuration|FixtureCompilerBoundary|FixtureSourceAudit'
```

**Starting unfiltered baseline: 83/83; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1
```

**Intermediate focused transaction: 4/4; 146 matrix rows; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'M2Production|ConfigurationCanonical|FixtureCompilerBoundary'
```

**Expanded focused transaction / M1: 8/8; 162 matrix rows; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'M2Production|Configuration|FixtureCompilerBoundary|FixtureSourceAudit'
```

**Installer / pin / Minor regressions: 10/10; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'Installer|OwnerPinned|ProtectedAcl|ProtectedManifest|Uninstaller|FixtureCompilerBoundary'
```

**Final unfiltered suite: 84/84; 163 matrix rows; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1
```

**EnvironmentSmoke: 12/12; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -EnvironmentSmoke
```

**Empty fixture queue: NoWork; dispatch 0; MutationAttempted=false; exit 0.**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json -DryRun
```

The two focused matrix runs are intermediate evidence. They loaded production
declarations before the final pre-manifest no-reparse scan and destination
junction case were added. The final full run started after the last production
and fixture source edit; only documentation changed during that run. Its 163-row
matrix below is the authoritative evidence for the final working-tree source.
The earlier reports and counts are not rewritten as though they tested this code.

Full-suite inventory: **84 current source declarations, 84 executed names**.
Missing names: **none**. Unexpected names: **none**. Missing starting-baseline
cases: **none (all 83 retained)**. The one added named case is
`M2ProductionTransactionFailureMatrixAndRecovery`; its scenario rows are
reported separately and are not added to the suite test count.

The expanded focused run executed these eight names, all PASS:

```text
FixtureCompilerBoundaryAcceptsOnlyTheOwnedBootstrapPlan
FixtureSourceAuditDistinguishesRefusalFromLaunch
CustomizedFixtureConfigurationsAreDistinctImmutableSnapshots
ConfigurationSchemaRejectsUnknownDuplicateSecretAndWrongShapeFields
ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization
M2ProductionTransactionFailureMatrixAndRecovery
CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch
FixtureIsolationHasNoLiveIssueMutation
```

M1's full production import/canonical staging regressions pass in the final
suite, including 29 paired invalid inputs before writes, decoded credential
keys, parser-diagnostic containment, Unicode customization and runtime parity.
The exact B/m regression name mapping was checked against the final returned
results; every name below exists and passed:

| Finding | Final executed cases |
| --- | --- |
| B1 | `CodexEnvironmentIsAllowlistedAndCredentialStoreOnly`; `CodexTransportEnvironmentIsHermeticForProbesAndExecution`; `CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch` |
| B2 | `CodexCommandAuditRejectsWrappersAndRetainsMetadataOnly`; `CodexCommandBoundaryRejectsRealFakePayloadsWithoutSentinels` |
| B3 | `CodexRawOutputIsAuditedBeforeRedactionAndNeverPromoted`; `CodexForbiddenProfileOutputCannotReachOrchestratorFinalSinks` |
| B4 | `ExecutableIdentityRejectsPathShadowAndChangedBinaryBeforeLaunch`; `ProtectedCodexProcessGateRejectsWritableReparseChangedAndReplacementRaces` |
| B5 | `ProtectedManifestIdentityIncludesExactInstallerProvenance`; `InstallerRejectsChangedBytesAfterPreviewBeforeAnyPrivilegedBoundary`; `InstallerRejectsBootstrapReplacementAfterPreviewBeforeAnyPrivilegedBoundary`; `OwnerPinnedInstallerLeaseRejectsHostileBootstrapAndPathSwap`; `OwnerPinnedInstallerLeaseRejectsReplacementBeforeExecution` |
| B6 | `GitLfsRoutingIsPinnedAndRepositoryRedirectsFailClosed`; `UnityKillOnCloseJobPreventsDelayedDescendantMutation`; `NewWorkUnityGitControlDriftOccursBeforeAnyProjectMutation`; `UnityGitControlAndDelayedDescendantDriftSuppressEveryDeliveryMutation`; `ValidationOnlyResumeReusesExactExistingBranchAndNoNewPr` |
| m1 | `InstallerDryRunHasExactTaskContractAndNoMutation`; `UninstallerDryRunPreservesArtifactsAndSchedulerState` |
| m2 | `ProtectedAclAcceptsExactReadExecuteAndRejectsEveryWriteRight` |

Final fixture audit: **2,129 fake executable invocations; 15 simulated
executable-boundary mutations; 0 external fake mutations; 0 scheduler mutation
sentinels; 0 observed external mutations**. Each focused M2 run recorded one
fake executable invocation and zero external mutations. M2's real marked-temp
writes, fake ACL calls and fake scheduler journal appends are additional local
fixture operations, not real external/scheduler mutations or those executable
counter totals. The fixture root was cleaned by its existing marker/no-reparse
suite cleanup after all assertions; no test root retention was requested.

Parser and source-name inventory command, executed with **14 scripts and zero
parser errors**:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -Command { $ErrorActionPreference='Stop'; $files=@(Get-ChildItem Tools/HostAutomation -Recurse -Filter *.ps1); $parseFailures=@(); foreach($file in $files){$tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors); foreach($errorItem in $errors){$parseFailures += [pscustomobject]@{File=$file.Name; Error=$errorItem.Message}}}; $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath('Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1'),[ref]$tokens,[ref]$errors); $names=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Invoke-HostTestCase'},$true) | ForEach-Object { [string]$_.CommandElements[1].Value }); [Console]::Out.WriteLine(([ordered]@{Scripts=$files.Count; Errors=$parseFailures; CaseNames=$names} | ConvertTo-Json -Depth 8 -Compress)); if($parseFailures.Count){exit 1} }
```

The exact current `Invoke-OwnerPinnedInstaller` function was extracted from
OPERATIONS.md and executed in memory, with `-DryRun` retained in **both** calls.
The supplied hash was computed solely for these local previews, not taken as
independent review or future installation authorization. Exact command:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -Command { $ErrorActionPreference='Stop'; $doc=[IO.File]::ReadAllText([IO.Path]::GetFullPath('Docs/HostAutomation/OPERATIONS.md')); $launcherMatches=[regex]::Matches($doc,'(?ms)^function Invoke-OwnerPinnedInstaller \{.*?^\}'); if($launcherMatches.Count -ne 1){throw 'Expected exactly one documented Owner launcher.'}; . ([scriptblock]::Create($launcherMatches[0].Value)); $installerPath=[IO.Path]::GetFullPath('Tools/HostAutomation/Install-SashimiHostAutomation.ps1'); $previewHash=(Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant(); $installRoot=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'SashimiBoyAutomation'; $before=Test-Path -LiteralPath $installRoot; $arguments=@('-ConfigPath',[IO.Path]::GetFullPath('Tools/HostAutomation/Config.example.json'),'-OrchestratorPath',[IO.Path]::GetFullPath('Tools/HostAutomation/Invoke-SashimiHostOrchestrator.ps1'),'-DryRun'); $preview=Invoke-OwnerPinnedInstaller -InstallerPath $installerPath -ExpectedInstallerSha256 $previewHash -InstallerArgumentList $arguments; $pinned=Invoke-OwnerPinnedInstaller -InstallerPath $installerPath -ExpectedInstallerSha256 $previewHash -InstallerArgumentList ($arguments + @('-ExpectedBundleId',$preview.BundleId,'-ExpectedInstallerSha256',$previewHash)); $first=$preview.ResultJson | ConvertFrom-Json -Depth 64; $second=$pinned.ResultJson | ConvertFrom-Json -Depth 64; $after=Test-Path -LiteralPath $installRoot; foreach($result in @($first,$second)){if(-not $result.Success -or -not $result.DryRun -or $result.Staged -or $result.Changed -or $result.AclVerified){throw 'Preview reached an unexpected result.'}}; if(-not $second.BundleAuthorizationMatched -or -not $second.InstallerAuthorizationMatched -or $before -ne $after){throw 'Pinned preview or install-root invariance failed.'}; [Console]::Out.WriteLine(([ordered]@{InstallerSha256=$previewHash; BundleId=$second.BundleId; ManifestSha256=$second.ManifestSha256; Previews=2; BothDryRun=$true; Staged=$false; AclVerified=$false; Changed=$false; BundleAuthorizationMatched=$second.BundleAuthorizationMatched; InstallerAuthorizationMatched=$second.InstallerAuthorizationMatched; InstallRootExistedBefore=$before; InstallRootExistsAfter=$after} | ConvertTo-Json -Compress)) }
```

Both previews succeeded; the second matched both pins. Staged, AclVerified and
Changed were false. The Program Files install root was absent before and after.

```text
Installer SHA-256: 3780e4a6450d5a26b317f6bed7db12dffe1cc72f4d00b2105a881f0c97da470b
BundleId: daf542f37b89abaad833e3bf3edd7938e4be869c16cdd4969d49b3a82019dd25
Manifest SHA-256: d7d1ef03f4eca09aed11a2ce29bd4df74652b62a79fa06b32787737406b08d06
```

## Final production-path fault matrix

**163 distinct rows: 141 faulted operation boundaries derived from the successful
production trace, plus 22 scenario rows (including clean success/reuse).** Every
selected fault was reached and its expected failure was asserted. No row is an
unexplained harness failure. The same plan object and destination are reused on
each transient retry; a fresh transaction leaf does not change BundleId.

`@` below separates the exact checkpoint name from its normalized path; `#n`
is that checkpoint/path's occurrence number in the successful trace. `<install>`
and `<transaction>` replace unique fixture paths; 64-digit bundle identities
are retained as emitted. Final state means the **bundle final path at failure**,
not an installed machine state. Retained Codex distributions must also pass
read-only exact verification on retry. Before scheduler append, every generated
fault row asserts the prior-task sentinel and unrelated file are unchanged;
post-append failure asserts the unconfirmed-state diagnostic. The earlier valid
fixture bundle's manifest is checked per row and its full content is checked at
the end. External `0` refers to the instrumented fake/guard audit described above.

| Boundary / case | Observed failure | Cleanup outcome | Final path | Retry result | External |
| --- | --- | --- | --- | --- | --- |
| `CleanSuccessAndReadOnlyReuse` | none | removed | complete | reuse | 0 |
| `Root.Create @ <install>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Root.Created @ <install>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Root.Create @ <install>\Bundles#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Root.Created @ <install>\Bundles#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Root.Create @ <install>\CodexDistributions#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Root.Created @ <install>\CodexDistributions#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Workspace.Create @ <install>\CodexDistributions\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Workspace.Created @ <install>\CodexDistributions\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Marker.Write @ <install>\CodexDistributions\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Marker.Written @ <install>\CodexDistributions\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Payload.Create @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Payload.Created @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Codex.Write @ <install>\CodexDistributions\<transaction>\Payload\codex.exe#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Codex.Written @ <install>\CodexDistributions\<transaction>\Payload\codex.exe#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions\<transaction>\Payload\codex.exe#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\CodexDistributions\<transaction>\Payload#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Codex.Verify @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\Payload\codex.exe#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\Payload#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Codex.Promote @ <install>\CodexDistributions\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\Payload\codex.exe#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\<transaction>\Payload#3` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Codex.Published @ <install>\CodexDistributions\25d1a382103be7b32df2a1b5eac4354c1a713791929624715f5f869c3816fa39#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\25d1a382103be7b32df2a1b5eac4354c1a713791929624715f5f869c3816fa39\codex.exe#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\CodexDistributions\25d1a382103be7b32df2a1b5eac4354c1a713791929624715f5f869c3816fa39#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Cleanup.Delete @ <install>\CodexDistributions\<transaction>#1` | Simulated exception; selected checkpoint reached | preserved; failure reported | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Workspace.Create @ <install>\Bundles\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Workspace.Created @ <install>\Bundles\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Marker.Write @ <install>\Bundles\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Marker.Written @ <install>\Bundles\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\.sashimi-installer-staging.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Payload.Create @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Payload.Created @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Config.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Config.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\ExecutableIdentity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\ExecutableIdentity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Get-SashimiProjectQueue.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Get-SashimiProjectQueue.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\HostAutomation.Common.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\HostAutomation.Common.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiCodexExec.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiCodexExec.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiDeveloperRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiDeveloperRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiHostOrchestrator.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiHostOrchestrator.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiReviewerRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiReviewerRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiUnityValidation.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiUnityValidation.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write @ <install>\Bundles\<transaction>\Payload\Publish-SashimiRunResult.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Write.After @ <install>\Bundles\<transaction>\Payload\Publish-SashimiRunResult.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Manifest.Write @ <install>\Bundles\<transaction>\Payload\HostIntegrity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Manifest.Written @ <install>\Bundles\<transaction>\Payload\HostIntegrity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Config.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\ExecutableIdentity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Get-SashimiProjectQueue.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\HostAutomation.Common.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiCodexExec.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiDeveloperRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiHostOrchestrator.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiReviewerRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiUnityValidation.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\Publish-SashimiRunResult.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload\HostIntegrity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Apply @ <install>\Bundles\<transaction>\Payload#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Verify @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Config.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\ExecutableIdentity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Get-SashimiProjectQueue.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\HostAutomation.Common.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiCodexExec.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiDeveloperRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiHostOrchestrator.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiReviewerRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiUnityValidation.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Publish-SashimiRunResult.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\HostIntegrity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Promote @ <install>\Bundles\<transaction>\Payload#1` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Config.json#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\ExecutableIdentity.json#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Get-SashimiProjectQueue.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\HostAutomation.Common.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiCodexExec.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiDeveloperRun.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiHostOrchestrator.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiReviewerRun.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Invoke-SashimiUnityValidation.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\Publish-SashimiRunResult.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload\HostIntegrity.json#2` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\<transaction>\Payload#3` | Simulated exception; selected checkpoint reached | removed/absent | absent | PASS identical plan | 0 |
| `Bundle.Published @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Config.json#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\ExecutableIdentity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Get-SashimiProjectQueue.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\HostAutomation.Common.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiCodexExec.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiDeveloperRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiHostOrchestrator.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiReviewerRun.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiUnityValidation.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Publish-SashimiRunResult.ps1#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\HostIntegrity.json#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `Cleanup.Delete @ <install>\Bundles\<transaction>#1` | Simulated exception; selected checkpoint reached | preserved; failure reported | complete | PASS identical plan | 0 |
| `Registration.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df#1` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Config.json#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\ExecutableIdentity.json#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Get-SashimiProjectQueue.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\HostAutomation.Common.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiCodexExec.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiDeveloperRun.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiHostOrchestrator.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiReviewerRun.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Invoke-SashimiUnityValidation.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\Publish-SashimiRunResult.ps1#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df\HostIntegrity.json#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `ACL.Verify @ <install>\Bundles\3674d9c157540679b8de51bcf47a660b6d7fcf1b5647992a50d291b9ed26c8df#2` | Simulated exception; selected checkpoint reached | removed/absent | complete | PASS identical plan | 0 |
| `Scheduler.Write @ <install>-scheduler.jsonl#1` | Simulated scheduler exception; complete bundle retained; task state unconfirmed | removed/absent | complete | PASS identical plan | 0 |
| `Scheduler.Written @ <install>-scheduler.jsonl#1` | Simulated scheduler exception; complete bundle retained; task state unconfirmed | removed/absent | complete | PASS identical plan | 0 |
| `manifest-missing` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `manifest-bytes` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `payload-missing` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `payload-length` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `payload-hash` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `extra-file` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `config-invalid` | real verifier rejection | removed | absent | PASS identical plan | 0 |
| `InterruptedPayloadAndCleanupFailure` | simulated write + cleanup | owned workspace preserved; later exact cleanup | absent | PASS identical plan | 0 |
| `Destination-foreign` | real verifier rejection | foreign destination untouched | unchanged invalid | fail closed (persistent invalid input) | 0 |
| `Destination-partial` | real verifier rejection | foreign destination untouched | unchanged invalid | fail closed (persistent invalid input) | 0 |
| `Destination-altered` | real verifier rejection | foreign destination untouched | unchanged invalid | fail closed (persistent invalid input) | 0 |
| `Destination-wrong-path` | real verifier rejection | foreign destination untouched | unchanged invalid | fail closed (persistent invalid input) | 0 |
| `Cleanup-wrong-marker` | real ownership rejection | refused; fixture condition restored; exact cleanup | absent | PASS | 0 |
| `Cleanup-missing-marker` | real ownership rejection | refused; fixture condition restored; exact cleanup | absent | PASS | 0 |
| `Cleanup-outside-root` | real ownership rejection | refused; fixture condition restored; exact cleanup | absent | PASS | 0 |
| `Cleanup-reparse` | real ownership rejection | refused; fixture condition restored; exact cleanup | absent | PASS | 0 |
| `DestinationReparse` | real reparse rejection | target untouched; fixture junction removed without recursion | unchanged junction | PASS after fixture condition removed | 0 |
| `CompleteDestinationCollision` | simulated concurrent winner; real collision rejection | owned stage removed; winner retained | complete winner | PASS read-only reuse | 0 |
| `partial-marker-write` | simulated interruption/marker mutation; real cleanup refusal | preserved; failure reported | absent | PASS identical plan | 0 |
| `empty-cleanup-failure` | simulated interruption/marker mutation; real cleanup refusal | preserved; failure reported | absent | PASS identical plan | 0 |
| `marker-changed-before-write` | simulated interruption/marker mutation; real cleanup refusal | preserved; failure reported | absent | PASS identical plan | 0 |

## Final local Git state

`git diff --check` passed. All 15 modified/untracked paths are within the
approved HostAutomation directories; no Assets, Packages or ProjectSettings
changes, and no staged changes. Branch and HEAD remain the starting values.
A comparison with the saved starting diff confirmed Common, Codex adapter,
Developer, Reviewer and uninstaller partial-work diffs are unchanged by this
run. Earlier main-fixture work was preserved; this run only adds its M2 entry,
matrix result collection and separate helper. The historical untracked M1 and
Phase B reports and functional-smoke script remain in place.

Changed **this run**:

- `Tools/HostAutomation/Install-SashimiHostAutomation.ps1`
- `Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1`
- `Tools/HostAutomation/Tests/M2.TransactionFixtures.ps1` (new)
- `Docs/HostAutomation/OPERATIONS.md`
- `Docs/HostAutomation/SECURITY.md`
- `Docs/HostAutomation/REVIEW_53_REMEDIATION.md` (M2 and factual overlap notes only)
- `Docs/HostAutomation/PHASE_B_M2_REPORT.md` (new)

Current status (10 modified tracked files, 5 untracked, nothing staged):

```text
 M Docs/HostAutomation/OPERATIONS.md
 M Docs/HostAutomation/REVIEW_53_REMEDIATION.md
 M Docs/HostAutomation/SECURITY.md
 M Tools/HostAutomation/HostAutomation.Common.ps1
 M Tools/HostAutomation/Install-SashimiHostAutomation.ps1
 M Tools/HostAutomation/Invoke-SashimiCodexExec.ps1
 M Tools/HostAutomation/Invoke-SashimiDeveloperRun.ps1
 M Tools/HostAutomation/Invoke-SashimiReviewerRun.ps1
 M Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1
 M Tools/HostAutomation/Uninstall-SashimiHostAutomation.ps1
?? Docs/HostAutomation/PHASE_B_LOCAL_REPORT.md
?? Docs/HostAutomation/PHASE_B_M1_REPORT.md
?? Docs/HostAutomation/PHASE_B_M2_REPORT.md
?? Tools/HostAutomation/Test-SashimiCodexFunctionalSmoke.ps1
?? Tools/HostAutomation/Tests/M2.TransactionFixtures.ps1
```

No stage, commit, push, branch change, PR/Issue/Project update, installation,
real system ACL change, real scheduler registration, real Codex or real Unity
execution was performed. SAFE_TO_INSTALL: NO.
