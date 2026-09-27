# PR #53 Host completion — 2026-09-21

Issue #52 / existing Draft PR #53. The old Desktop Developer v3 and Reviewer v2
schedules were briefly re-enabled without merging the Host fixes. That was an
operational error: they were paused again, with no replacement task registered
and no product Issue state changed. Merely resuming those old schedules cannot
apply this pull request.

Base: `9e101c8702a04d107cbf1dde9270bc34d39b1c2c`; inspected main:
`e4e26386da695c179216137240911fe200b81856`. The starting specification was
`1.0.3`, blob `21e8796a09d4f26935ffc4879147879a153f0193`; this change advances
the specification to `1.0.4`.

## Implementation

| Finding | Change | Evidence / limit |
| --- | --- | --- |
| M3 | Every common Host child uses a suspended kill-on-close job. Run-bound Git/GitHub/PowerShell children also join the PID/start-time ledger. Missing root PIDs and unconfirmed fallback termination preserve ledgers and block cleanup/retention. | Real fake executables exercise blocked stdin, timeout, cancellation, parent exit and descendants. The existing later-generation Unity race test uses the same native job boundary. |
| M6 | Copy the complete pre-generator source state into a separate marker-owned project before either invocation. Check both copies against the full input manifest; compare full source deltas and declared outputs; refuse undeclared changes. Reviewer regeneration must match the committed deliverable. | Executable deterministic and random-if-missing generators. The latter passes the old same-directory repeat but fails independent comparison. Private baseline cleanup requires confirmed termination. |
| M7 | Bound linked-token and Owner-launcher output; audit original non-Codex output before redaction; monitor capture directories during execution; seal recursive artifact sets, byte lengths and hashes through publication and retention. | Fixed byte/count quotas, oversized-file writer termination, extra-file/empty-directory/changed-content rejection and original-secret-output rejection. Monitoring allows growth between 250 ms checks; it is not an OS disk quota. |
| M8 | Read-only Unity Editor scan of every Assets scene/prefab, including unloaded assets, with structured active AudioListener/EventSystem and missing-script/reference inventory. Host requires exact asset coverage and internally consistent counts. | Real inventory: 50 assets, zero errors. Two real EditMode tests cover active duplicates and disabled/inactive contexts; Host negatives cover omission, absent report, duplicate and inconsistent counts. |
| Useful Codex source access | Bundle-pinned stdio MCP tools list/read source and permit Developer-only SHA-checked writes or literal replacements. Reviewer tools are read only. Parent/file handle checks reject path escape, reparse points and hard links. | Actual stdio server calls test reads, edits, stale hashes, metadata GUID preservation, forbidden paths, exact argument types and role separation. Fake model smoke exercises the production adapter. **Real protected model smoke: NOT_RUN before installation.** |

The earlier evidence-based reviewer classification remains: confirmed current
Blocker/Major defects may return Review to In Progress; human checks alone go
to Verification after automated PASS. Infrastructure failures and unverified
concerns leave the item in Review. Findings are handed back together.

Installation also now defaults to `Task/Settings/Enabled=false`, including
updates to an existing task. The previous default enabled the timer immediately
after registration, before Issue #52's functional check and one-Issue pilot.
The Owner explicitly enables the task only after those gates pass.

The first real Project preview exposed an invalid GraphQL fragment,
`ProjectV2RepositoryField`, in both queue and pre-publication queries. It has
been removed; GitHub represents the Repository column with the existing
`ProjectV2Field` fragment. The fake GitHub boundary now rejects unknown fragment
types instead of selecting canned data by operation name alone. Both production
queries were then executed successfully against Project 1 without mutation.
See GitHub's [ProjectV2FieldConfiguration union](https://docs.github.com/en/graphql/reference/projects#projectv2fieldconfiguration).

The Issue-specific pilot interface now implements `-Once -IssueNumber N` and
will not fall through to another eligible item. Production `-DryRun` supports
real read-only queue previews; harness previews still require fake input. The
live preview also exposed PR #53's `Refs #52` body: it did not populate the
Project's linked-PR field. Delivery changes that to the required `Closes #52`
and reads back the actual linkage before claiming queue readiness.

An artifact-boundary failure can no longer leave an earlier successful
RunResult or FinalResult. The orchestrator quarantines the unvalidated artifact
tree into private State and writes a sealed, content-free failed public result.
The original failure remains in the bounded event record. Executable-policy
rejections before process creation continue to throw without a launch record.
Developer, Reviewer and orchestrator deadlines now include all four Unity
stages (import, component inventory, EditMode and PlayMode), plus both generator
runs and their existing overhead allowance.

## Executed verification

Evidence is local and uncommitted under the parent of this isolated Repository
in `Logs/AutomationCompletion52-20260921`; no original checkout was modified.

- Clean Unity `6000.4.0f1` import/compile: exit 0.
- Real Editor component inventory: exit 0, 50 scene/prefab records, zero errors.
- EditMode: **43/43 PASS**, failed/skipped 0; PlayMode: **8/8 PASS**, failed/skipped 0.
- Metadata/GUID/reference scan: **1,264 meta files**, 11 explicit `.gitkeep`
  placeholders ignored; missing/orphan/invalid metadata, duplicate GUID,
  Missing Script and broken-reference counts all zero.
- Compile/inventory/EditMode/PlayMode logs: no matched compiler or unexpected
  Console error diagnostics.
- Focused Host run under Windows user `02031`: **12/12 PASS**, external changes 0.
- PowerShell parser: **31 scripts, zero errors**. Windows user `02031`
  environment/plan check: **12/12 PASS**, external changes 0; live model/auth
  capability remains explicitly NOT_RUN in that plan-only check.
- Host main partition: **100 distinct cases PASS by latest executed result**.
  The complete run had 98 PASS / 2 fixture failures; the final affected group
  had **34/34 PASS**, including both corrected cases and all affected role,
  orchestrator, source and installer-preview regressions. External changes 0.
  `completion-core-final.json` maps every case to its actual result file.
- Separate full installer transaction matrix: **3/3 wrapper cases PASS**,
  **170 matrix rows PASS**, external changes 0. Combined with the main
  partition, **101 distinct Host cases** have an inspected passing result.
- Live `HostProjectItems` and `HostPublishContract` queries: PASS; Project field
  contract read successfully, zero writes. The pre-delivery #52 preview was
  NoWork with `RequiresExactlyOneLinkedOpenPullRequest`, not a pipeline PASS.
- Re-import/compile after default normalization: exit 0, zero compiler errors,
  ProjectSettings SHA-256 unchanged (`1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902`).
- Actual-machine installer preview: PASS, Changed/Staged false, TaskEnabled
  false. Installer SHA-256:
  `cff0acc1587d6ac75897c688847a7e672e64f289e3411bad7e4cbc46966dc973`;
  preview BundleId:
  `6bae23ac2acdf93bde3ffd5da837ff81f90d82e877bf24dcd9e3df4f963f9e85`.
  These are review inputs, not Owner installation authorization. Re-preview
  after review if any runtime/config/executable identity changes.

Commands used the exact isolated repository, protected Unity/PowerShell
executables, `-batchmode -nographics -buildTarget StandaloneWindows64`, the
inventory `ScanBatch` method, and `-runTests -testPlatform EditMode/PlayMode`.
The final fixture invocations were these three partitions. `Repository` held
the frozen M2 input; `RuntimeCandidate` held the final role/query/error-path
changes, subsequently copied back into `Repository` after M2 completed:

```powershell
$Evidence = Join-Path $env:LOCALAPPDATA 'SashimiBoyGame\Issue39\Logs\AutomationCompletion52-20260921'
$Final = Join-Path $Evidence 'RuntimeCandidate'
$Frozen = Join-Path $Evidence 'Repository'
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  (Join-Path $Final 'Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1') `
  -RepositoryRoot $Final -KeepTemporaryFiles `
  -TestNamePattern '^(?!M2ProductionTransactionFailureMatrixAndRecovery$)'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  (Join-Path $Final 'Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1') `
  -RepositoryRoot $Final -KeepTemporaryFiles `
  -TestNamePattern 'GitAndGitHubEnvironment|Completion|Reviewer|Orchestrator|ValidationOnlyResume|FunctionalSmoke|FixtureCompilerBoundary|InstallerDryRun|OwnerPinnedInstallerLease|ProtectedManifest'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  (Join-Path $Frozen 'Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1') `
  -RepositoryRoot $Frozen -KeepTemporaryFiles `
  -TestNamePattern 'M2ProductionTransactionFailureMatrixAndRecovery|FixtureCompilerBoundary'
```

The first focused attempt under the Desktop sandbox account had 8 passes and
3 failures: the source server could not lease the user's ancestor directories,
one new fixture used the fake-tool object instead of its executable path, and
the selected group omitted the fake Git/GitHub audit prerequisite. The fixture
was corrected, the prerequisite included, and the run repeated as the intended
Windows Host user. The later 12/12 result is the actual corrected run; no
failed attempt is counted as PASS.

An initial full fixture invocation was interrupted without claiming a result
when the premature scheduler-enable default was found. The corrected complete
suite is run in two isolated partitions: all tests except the long M2 matrix,
and that full matrix plus its executable-isolation prerequisite.

The first completed main partition returned 92 PASS / 5 FAIL. Two were
pre-launch identity exceptions that the new native wrapper converted into a
result instead of preserving the established refusal contract; that was fixed
before the final partition. The other failures exposed the strengthened
original-output/quarantine contract and fixture input assumptions. The next
complete main partition returned 98 PASS / 2 FAIL: a shared dummy askpass/token
value also appeared in non-secret probe fields, and the new invalid-GraphQL
negative omitted its fake audit-log environment, stopping before schema
validation. Unique credential sentinels and the required fake log input fixed
those fixtures. The affected role/orchestrator/source/installer-preview group
is re-executed after these corrections and the four-stage timeout adjustment;
final results are reported above. Earlier failed runs remain in local evidence.

The long M2 partition holds the installer and runtime source bytes fixed in
`Repository` throughout. Final role/query/error-path changes are tested in the
separate `RuntimeCandidate` copy and copied back only after that matrix ends.
The installer, its runtime filename set and M2 transaction code are unchanged
between those snapshots; final installer-preview/provenance regressions also
exercise the final payload bytes. No fixture matrix is represented as a live
privileged installation.

The Owner explicitly approved committing exactly six Unity default-setting
normalizations in the September 21 task: "기본값 정규화를 PR에 포함 (권장)".
This narrow Issue #52 decision takes precedence over DEVELOPER.md's default
prohibition on committing Unity-default serialization. It authorizes only
`targetPixelDensity=30`, the four zero-valued `buildNumber` entries, iOS/tvOS
minimum `15.0`, VisionOS `1.0`, and macOS `12.0`. The same normalized settings
passed the Unity suites above and the unchanged-hash re-import. Keeping the
old blank values would make each fresh Developer clone rewrite a protected
file and fail delivery. The role's protection is not broadened.

The 38 LFS FBX payloads were
materialized from verified local objects and normalized to their identical
pointer hashes; none has an asset-content change.

## Remaining rollout

This report is implementation/test evidence, **not independent approval**.
No scheduled task, protected runtime installation, live Codex model dispatch,
GitHub product-state pilot, PR merge or Done transition was performed.

1. Independent review of this exact PR head, zero Blocker/Major.
2. Owner merge, as required by AGENTS.md and Issue #52.
3. Keep all old Desktop schedules paused. Preview and install the reviewed,
   externally pinned protected bundle with its task disabled.
4. Run the installed environment check and opt-in real Developer/Reviewer
   functional smoke. Verify actual MCP source edit and read-only review.
5. Run #20 / PR #47 as a separate one-Issue pilot through Verification.
6. Enable the new 15-minute Host schedule only after the pilot passes.

The protected live smoke must not be made to pass by disabling executable,
sandbox, command, artifact or credential boundaries. If it fails, keep the
task disabled and return the exact failure to this infra Issue.
