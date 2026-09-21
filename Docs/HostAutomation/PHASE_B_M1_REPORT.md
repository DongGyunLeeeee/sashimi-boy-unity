# Issue 52 / Draft PR 53 — focused M1 continuation

2026-09-07. Developer evidence only; independent review remains required.
Starting and retained branch: `infra/52-windows-host-orchestrator`.
HEAD: `879072b96da77dd187f27d1444e5e86e12881d83`.
SPEC_VERSION: `1.0.2`, blob `6d7de6e6abef13b18021a3591debc53ac00616d4`.
The prepared worktree started with 10 modified and 2 untracked files, nothing
staged. The Owner's explicit source-continuation boundary was followed.

```text
FOCUS: M1
TARGET_STATUS: FIXED_PENDING_INDEPENDENT_REVIEW
TARGET_UNRESOLVED_COUNT: 0
OTHER_MAJORS: M2,M3,M4,M5,M6,M7,M8 not closed by this run
BLOCKER_REGRESSIONS: PASS
FIXTURE_TESTS: 83/83, 0 failed
REAL_CODEX_FUNCTIONAL_SMOKE: NOT_RUN
REAL_UNITY_VALIDATION: NOT_RUN
SAFE_TO_INSTALL: NO
```

## Scope and root cause

M1 only. M2–M8 remain open and are not closed by overlapping verification.
`PHASE_B_LOCAL_REPORT.md` is unchanged historical evidence.

The original implementation accepted extra config properties and staged raw
source bytes. The prepared work already had strict recursive schema checks,
immutable/range checks, decoded string-value scanning, and canonical projection.
This run found that decoded property names were not credential-audited before
they entered schema diagnostics. JSON parse exceptions could also quote input.
The staged bundle verifier used a generic JSON reader rather than the config
importer. Actual canonical file writes and paired malformed/customized config
coverage lacked executable evidence.

Working plan before behavior changes: audit decoded keys and contain parser
diagnostics; exercise both existing self-contained importers with paired
negative inputs; test real canonical distribution/bundle writes with only fake
ACL boundaries; verify runtime agrees with staged values; run all local gates.
The elevated bootstrap remains self-contained and does not dot-source writable
Common code. No new schema or unrelated policy was introduced.

Changes made in this run:

- `Tools/HostAutomation/HostAutomation.Common.ps1`: decoded key credential audit
  and content-free JSON parse failure.
- `Tools/HostAutomation/Install-SashimiHostAutomation.ps1`: matching audit and
  parse handling; staged verifier reuses `Import-InstallerConfig`.
- `Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1`: 15 additional
  rejection inputs in the existing case, non-DryRun invalid installer entry,
  synthetic-credential diagnostic containment, one real canonical staging case.
- `Docs/HostAutomation/SECURITY.md`: config contract and fixture limits.
- `Docs/HostAutomation/REVIEW_53_REMEDIATION.md`: current M1 disposition only.
- This report.

## Current production call paths

| Entry / stage | Config path and enforcement |
| --- | --- |
| Orchestrator, queue, Developer, Reviewer, Unity validator, publisher | Each script calls `Import-SashimiHostConfig` → `Assert-SashimiHostConfigJsonSchema` → decoded key/value scan, exact recursive object/type checks before `ConvertFrom-Json`; importer applies immutable values, ranges, arguments and canonical paths. |
| Codex adapter | `Import-AdapterConfig` → `Import-SashimiHostConfig`; no separate config parser. |
| EnvironmentSmoke and functional smoke | `Import-SashimiHostConfig`; functional smoke forwards the canonical config path to the production adapter. |
| Installer entry | Bootstrap hash gate (non-DryRun) → trusted PowerShell / marker-owned boundary initialization → `Import-InstallerConfig` → `Assert-InstallerConfigJsonSchema`, decoded audit, exact types and value rules → `New-InstallerCanonicalConfigProjection`. No staging or scheduler call precedes import. |
| Immutable bundle capture | `New-InstallerBundlePlan` rechecks captured JSON schema, normalizes paths, projects and compares with the fully validated import. Only equal canonical values proceed. Raw source hash/length still contributes to B5 authorization. Codex path is projected to its content-addressed distribution. |
| Actual staging / readback | `Install-InstallerCodexDistribution`, `Install-InstallerBundle` write captured distribution bytes and allowlisted canonical config; `Assert-InstallerBundle` verifies manifest/hash/length and now calls `Import-InstallerConfig`. Runtime imports that staged file using its normal importer and executable identity. |

The two schema implementations use decoded `JsonElement` property names and
ordinal case-insensitive duplicate detection before PowerShell conversion.
They enforce the same existing config contract; the shared-input fixtures are
their parity gate. Bundle snapshot equality binds its schema-only reread to the
previous full validation; it does not authorize a different value configuration.
Uninstaller does not import a runtime config.

## Targeted evidence matrix

| M1 requirement | Executed evidence |
| --- | --- |
| Valid example | `ConfigIsFixedAndRetentionDefaultsTo14Days` in full suite; `ConfigContract` in EnvironmentSmoke; Owner-pinned example previews. |
| Permitted customization and Unicode | `ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization`: retention 30, Git timeout 123, cooldown 0, issue validation, Korean/Ω strings and `C:\M1 자료\input.txt`; both production importers return equal canonical values. |
| Recursive unknown / duplicate / escaped keys | `ConfigurationSchemaRejectsUnknownDuplicateSecretAndWrongShapeFields`: unknown root/nested/security field, exact/case/dynamic duplicates, escaped equivalent root key and escaped case-variant timeout key. |
| Wrong JSON types / ranges / immutable values | Same case: object-as-array, null object, number/string and boolean/string mismatch, fractional integer, non-string array member, retention/timeout/retry bounds, immutable author/Unity/network/protection. |
| Recognizable credentials and containment | Same case: author token, secret argument flag, escaped nested token, escaped protected-pattern token, token-shaped property, escaped secret key, malformed secret JSON; runtime error and installer stdout/stderr exclude synthetic credential markers. |
| Real canonical staging | Real distribution and bundle stagers write within marker-owned suite root; fake ACL setters/readers validate that root. Exact staged bytes equal canonical projection plus newline, differ from raw pretty JSON, preserve Unicode/path values, and agree with runtime import and identity. Raw input contributes only hash/length provenance to the plan/manifest. |
| Invalid input before mutation | All 29 rejection inputs run through runtime import and non-DryRun installer entry with a current bootstrap hash and dummy BundleId. Every rejection has the expected config error and leaves both injected install root and scheduler record absent. Thus no rejected config reaches bundle retention or commit-author use. |
| B5 retained | Canonical staging verifies source SHA-256; existing changed-source/bootstrap/pinned-launcher regressions run in full suite. Second current preview matches both authorizations. |

The 29 rejection input names, all executed by the existing named case:

```text
escaped-nested-credential
escaped-credential-protected-pattern
unknown
duplicate-exact
duplicate-case
secret-endpoint
wrong-object-shape
unknown-nested
duplicate-nested-case-variant
duplicate-dynamic-case-variant
opaque-author-field
secret-validation-argument
token-shaped-validation-key
altered-immutable-unity-version
escaped-equivalent-key
escaped-case-key
number-as-string
boolean-as-string
fractional-integer
null-object
wrong-array-element
retention-range
timeout-range
retry-range
immutable-network
immutable-protection
secret-author
escaped-secret-key
malformed-secret-json
```

## Commands and results

Commands ran from BootstrapRepo with stable PowerShell 7.6.5. Fixture filtering
uses regex `-match`; `FixtureIsolationHasNoLiveIssueMutation` always executes.
No filter was applied to the full suite.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'Configuration|FixtureCompilerBoundary|FixtureSourceAudit'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -EnvironmentSmoke
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json -DryRun
```

Focused final: **7/7, 0 failed, exit 0**. Exact selected cases:

```text
FixtureCompilerBoundaryAcceptsOnlyTheOwnedBootstrapPlan
FixtureSourceAuditDistinguishesRefusalFromLaunch
CustomizedFixtureConfigurationsAreDistinctImmutableSnapshots
ConfigurationSchemaRejectsUnknownDuplicateSecretAndWrongShapeFields
ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization
CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch
FixtureIsolationHasNoLiveIssueMutation
```

Unfiltered full suite: **83/83, 0 failed, exit 0**. Compared the actual returned
case names with the current source declarations: 83 expected, 83 executed,
zero missing, unexpected, or failed cases. Compared names against the checkpoint
commit: all 79 retained; the three prepared partial-work cases (functional smoke,
compiler boundary and source audit) remain, plus the one new M1 staging case.
Thus all 82 starting worktree cases remain; no test was removed or renamed.

Full audit: **2,129 fake executable invocations**, **15 simulated mutations**,
**0 external fake mutations**, **0 scheduler mutation sentinels**, **0 observed
external mutations**. Focused audit: 1 fake invocation, 0 simulated/external
mutations. The canonical staging case additionally performs real suite-owned
temporary file writes and fake ACL calls; these are not counted as remote fake
mutations. They are distinct from the compiler's signed bootstrap execution.

The following named cases returned PASS in that full run; this is a recheck of
the B/m fixture evidence, not a new independent review:

| Finding | Exact full-run regressions |
| --- | --- |
| B1 | `CodexEnvironmentIsAllowlistedAndCredentialStoreOnly`; `CodexTransportEnvironmentIsHermeticForProbesAndExecution`; `CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch` |
| B2 | `CodexCommandAuditRejectsWrappersAndRetainsMetadataOnly`; `CodexCommandBoundaryRejectsRealFakePayloadsWithoutSentinels` |
| B3 | `CodexRawOutputIsAuditedBeforeRedactionAndNeverPromoted`; `CodexForbiddenProfileOutputCannotReachOrchestratorFinalSinks` |
| B4 | `ExecutableIdentityRejectsPathShadowAndChangedBinaryBeforeLaunch`; `ProtectedCodexProcessGateRejectsWritableReparseChangedAndReplacementRaces` |
| B5 | `ProtectedManifestIdentityIncludesExactInstallerProvenance`; `InstallerRejectsChangedBytesAfterPreviewBeforeAnyPrivilegedBoundary`; `InstallerRejectsBootstrapReplacementAfterPreviewBeforeAnyPrivilegedBoundary`; `OwnerPinnedInstallerLeaseRejectsHostileBootstrapAndPathSwap`; `OwnerPinnedInstallerLeaseRejectsReplacementBeforeExecution` |
| B6 | `GitLfsRoutingIsPinnedAndRepositoryRedirectsFailClosed`; `UnityKillOnCloseJobPreventsDelayedDescendantMutation`; `NewWorkUnityGitControlDriftOccursBeforeAnyProjectMutation`; `UnityGitControlAndDelayedDescendantDriftSuppressEveryDeliveryMutation`; `ValidationOnlyResumeReusesExactExistingBranchAndNoNewPr` |
| m1 | `InstallerDryRunHasExactTaskContractAndNoMutation`; `UninstallerDryRunPreservesArtifactsAndSchedulerState` |
| m2 | `ProtectedAclAcceptsExactReadExecuteAndRejectsEveryWriteRight` |

First focused invocation: 6/7, exit 1, named schema case failed with
`Runtime failed to reject escaped-secret-key.` The key-audit fix was then
applied and the identical command passed. No isolation failure occurred.
An earlier shell edit attempt returned exit 1 with `ParserError: Unexpected
token 'C:\Program' in expression or statement` due to nested here-string
quoting. It performed no edits; edits were subsequently applied with patches.
An exploratory `rg` Windows wildcard argument returned exit 1 / OS error 123;
the corrected directory plus `-g '*.ps1'` search succeeded. Neither was an
external executable guard refusal or compiler/runner setup failure.

EnvironmentSmoke: **12/12, exit 0**. Empty queue: **exit 0, NoWork, dispatch 0,
MutationAttempted=false**. Parser, executed inside exact stable pwsh with
`Parser::ParseFile` over `Get-ChildItem Tools/HostAutomation -Recurse -Filter
*.ps1`: **13 scripts, zero errors**.

Current documented `Invoke-OwnerPinnedInstaller` was extracted from its exact
PowerShell fenced function in OPERATIONS.md and executed in memory. Preview:

```powershell
$installerPath=[IO.Path]::GetFullPath('.\Tools\HostAutomation\Install-SashimiHostAutomation.ps1')
$previewHash=(Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$arguments=@('-ConfigPath',[IO.Path]::GetFullPath('.\Tools\HostAutomation\Config.example.json'),'-OrchestratorPath',[IO.Path]::GetFullPath('.\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1'),'-DryRun')
$preview=Invoke-OwnerPinnedInstaller -InstallerPath $installerPath -ExpectedInstallerSha256 $previewHash -InstallerArgumentList $arguments
$pinned=Invoke-OwnerPinnedInstaller -InstallerPath $installerPath -ExpectedInstallerSha256 $previewHash -InstallerArgumentList ($arguments + @('-ExpectedBundleId',$preview.BundleId,'-ExpectedInstallerSha256',$previewHash))
```

Both exit 0, Success/DryRun true, Staged/AclVerified/Changed false. Second
BundleAuthorizationMatched and InstallerAuthorizationMatched true.
Program Files install root remains absent. These are preview evidence, not
independently retained authorization to install.

```text
Installer SHA-256: 31fdd94760cd03f49e9a5807134a83165e6bcce41119b6da3c4b618d447306bd
BundleId: ed03012b24a4a039f8244c7c5b1f6444cd2423e5dc473269eeeed17daf635849
Manifest SHA-256: c6b058844a48b8ef9eecb0e8d5f1bc1635834ec04ee68674cbb531df3a627bd9
```

## Limits and preserved work

Recognizable credential detection is not universal opaque-secret detection.
Live ACL/ownership/inheritance, scheduler, installation, real Codex, real Unity,
GitHub, independent review and full Phase B rollout remain outside this run.
The marker-owned file writes and fake ACL calls prove canonical staging only;
they do not close M2 recovery or certify actual permissions. Fixture counters
are observations at instrumented boundaries, not an OS-wide security attestation.

All pre-existing compiler-bootstrap, fixture-owned executable/unique-config,
injected scheduler, AST audit, process-ledger/job and functional-smoke work is
preserved. No Assets, Packages, ProjectSettings, product checkout, credential,
branch, commit, index or remote mutation was authorized or performed.

Final local Git checks: `git diff --check` exit 0; changed/untracked path audit
13 paths, zero outside `Tools/HostAutomation/**` and `Docs/HostAutomation/**`;
`git diff --cached --name-only` empty. Branch/HEAD match the start. No staging,
commit, push, fetch or branch switch. Current status:

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
?? Tools/HostAutomation/Test-SashimiCodexFunctionalSmoke.ps1
```

No known M1 acceptance gap remains in the original recognizable-credential
threat model. Separate bootstrap/runtime implementations remain a maintenance
consideration: changes must retain the shared-input parity regressions.
Independent review, full Phase B completion, human merge and installation
authorization are not implied by this result.
