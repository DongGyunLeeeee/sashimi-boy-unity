# Issue #52 / Draft PR #53 — review-loop repair

Developer evidence, 2026-09-17. Independent review is still required.
Base main: `e4e26386da695c179216137240911fe200b81856`.
Starting PR head: `879072b96da77dd187f27d1444e5e86e12881d83`.
Repository specification: `1.0.3`.

## Problem and resulting behavior

The old Host used a severity label as the return-to-development gate and
published only the first blocking finding. It also rejected the already
approved Unity default serialization and required metadata for `.gitkeep`.
Those conditions could prevent a valid game from reaching human verification.
Historical PRs also contained real defects: this change does not retroactively
invalidate independently demonstrated generator or visual regressions.

Reviewer schema 2 now distinguishes Defect, HumanCheck, Infrastructure, and
Unverified. A blocking defect must cite the current requirement, exact location,
expected/actual behavior, observed reproduction or deterministic code path, and
the required correction. All supported blocking findings share one handoff.
Human-only checks join the Owner checklist. Infrastructure failures and missing
required evidence preserve Review without a repair handoff or a PASS claim.
Optional polish is not a new acceptance criterion. Host named checks are gates
for both return-to-development and Verification publication.

Every publication rechecks the authenticated actor and then the pinned Issue,
PR identity/content/conversation, exact linked PR, Project contract/status, and
validated main SHA at the common write boundary. Developer completion comments
use the status returned by its own successful transition. A concurrent remote
change suppresses subsequent mutation. This is not an atomic GitHub transaction.

Unity drift handling is restricted to the owned Reviewer clone, exact run and
marker, three clean protected checkouts, the same six approved field changes,
and unchanged other bytes/Git controls. The complete diff and file digest are
retained. There is no Developer exception and no reset to hide changes.
Authoritative generators retain graphics in batch mode for `Camera.Render`.
The metadata scan exempts only leaf `.gitkeep` placeholders after reparse checks.

The Issue's documented `-EnvironmentOnly` switch is accepted as an alias. Its
Codex plan check is explicitly labeled DryRun; it is not a live capability,
authentication, or model-execution result.

## Executed evidence

Local evidence is retained in the repair task's `Logs/AutomationRepair52-20260917`
directory, outside the isolated delivery clone and outside Git. Tests use
synthetic Issue numbers and marked fake executables, never product Issues.

- `review-evidence-tests.json`: 7/7 passed, external mutations 0.
- `review-drift-tests.json`: 6/6 passed, external mutations 0; positive owned
  drift and 13 rejected near misses.
- `reviewer-transitions-tests.json`: 4/4 passed, external mutations 0. The
  production Reviewer runner completed five fake-boundary scenarios: confirmed
  defects -> In Progress; HumanCheck -> Verification; Infrastructure, Unverified,
  and Unity timeout -> no transition. Both confirmed defects were retained.
- `source-scanner-tests.json`: 5/5 passed, external mutations 0. Real fixture
  assets without metadata still fail; `.gitkeep` does not.
- `full-host-tests.json`: the installer transaction regression passed all 163
  rows. This initial run was **not** an overall PASS (81 passed, 7 failed):
  installer identity checks correctly rejected the sandbox user, and a fixture
  executable compiled before the final main-pin change did not understand the
  new query. The final suite is run from stable source as the real task user.
- `final-host-tests.json`: **90/90 passed**, exit 0, external mutations 0,
  under Windows user `02031`. This rerun excludes only the already passed M2
  transaction test; the installer and common-library implementation tested by
  that 163-row matrix is unchanged. The suite includes the new late-publication,
  real-runner decision, default-drift, placeholder, and render-argument cases.
  The final normal-resume tests allow 120 seconds for the added final pin
  queries; an earlier 60-second fixture deadline was exceeded and is not counted
  as a PASS.
- Environment plan: 12/12 passed as Windows user `02031`; no model dispatch,
  Task Scheduler registration, or external mutation.
- Final `git diff --check`: passed. Delivery changes are limited to
  `Docs/Automation`, `Docs/HostAutomation`, and `Tools/HostAutomation`.

Commands for the final fixture run and the separately executed transaction
case (the latter was included in the initial unfiltered run):

```powershell
.\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 `
  -TestNamePattern '^(?!M2ProductionTransactionFailureMatrixAndRecovery$).*'
.\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 `
  -TestNamePattern 'M2ProductionTransactionFailureMatrixAndRecovery|FixtureCompilerBoundary'
```

The second command is the focused reproduction command, not a claim that an
additional transaction run was executed. The executed initial command had no
`TestNamePattern`. Installer SHA-256:
`3780e4a6450d5a26b317f6bed7db12dffe1cc72f4d00b2105a881f0c97da470b`.
Common-library SHA-256:
`38c261893d22d0db91887cb51c38710de6929b396f9871a73e1607a959f9722b`.

Actual Unity source validation used a new marked disposable synthetic merge of
the starting PR head and the pinned main above. There are no game source changes
in this repair, so this verifies the same game tree, not a deployed Host cycle.
`source-unity-result.json`: clean import/compile exit 0, EditMode **41/41**,
PlayMode **8/8**, no failed/skipped/inconclusive tests, no Console diagnostics.
Synthetic merge: `9563dfe9acb00dec0e48feb2508a570e2686adec`.

`source-integrity-before-fix.json` captured 11 false missing-meta reports, all
for `.gitkeep`. The same actual imported project then passed the revised scan:
1,261 metadata files, 11 explicitly recorded placeholders, zero missing/orphan/
invalid metadata, duplicate GUIDs, Missing Script, or missing references.
`source-integrity-evidence.json` also confirms that the revised pure byte
classifier accepts the actual Unity default serialization. This does not
substitute for the owned Host pipeline checks.

The first direct Host validation attempt stopped **before Unity or Git launch**:

```text
Wrapper: pwsh -NoLogo -NoProfile -NonInteractive -File RunUnityValidation.ps1
Script exit: 1
Failure: UnhandledValidationFailure / PreflightOrUnhandled
Error: Protected executable identity is required before a live Git process may start.
First blocked native command (not executed):
"C:\Program Files\Git\cmd\git.exe" -C C:\Dev\sashimi-boy-unity status --porcelain=v1 --untracked-files=all
```

That attempt used the uninstalled example configuration. The executable guard
was kept. The separate supported legacy integration/Unity wrapper supplied the
actual source test evidence above; it is not a bypassed Host PASS.

## Remaining installation gates

**Not ready to install.** The old five Desktop schedules remain paused. No
scheduled task is registered or activated by this repair. Product Issue/PR
states, the user's game checkout, and the prepared BootstrapRepo are unchanged.

The current remediation matrix retains the incomplete M3, M6, M7, and M8 work.
The no-shell Codex adapter also still needs a proven useful scoped source-read/
edit interface and a live functional run. CLI help support alone is not that
proof. A whole live Codex/Unity/GitHub scheduled cycle was not run.

After the remaining implementation and independent review, the Issue #52 Owner
rollout requires Owner merge, a pinned installer preview/install, no-work
DryRun, and the separate #20 supervised pilot. The exact reviewed installer
launcher is in [OPERATIONS.md](OPERATIONS.md#install-the-scheduled-task).
The pending first environment command, from the reviewed checkout as `02031`, is:

```powershell
.\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 -EnvironmentOnly
```

Use the pinned installed Config.json for subsequent Host validation. Do not
rerun the uninstalled example as a production validation claim, and do not
resume the old parallel Desktop schedules.
