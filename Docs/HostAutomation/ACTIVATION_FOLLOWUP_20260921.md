# Issue #52 installed capability-help follow-up

Owner-merged PR #54 (`c4d3fa83374da2ccaaa38d2a755d394598186604`) installed successfully on 2026-09-21. The first protected functional smoke then failed before model execution because the Codex CLI's public help contains the symbolic path `~/.codex/config.toml`. The scheduled task remains disabled. This follow-up fixes that false positive without relaxing the audit of model output.

References #52; follow-up to #54. Issue #52 is already Closed / Done by the Owner. This manual infrastructure correction does not reopen it, change a product Issue, or claim the complete rollout has passed.

## Reviewed change

- Runtime/test commit: `19bb295b843888caa640060bb34e7698b3ea9794`, based on unchanged live main `c4d3fa83374da2ccaaa38d2a755d394598186604`.
- Specification: `1.0.6`, blob `af0b7ddbffd5d5a877581ba8ba10deed158da14d`.
- Only production runtime change: `Tools/HostAutomation/Invoke-SashimiCodexExec.ps1`, SHA-256 `534e5b13b27381cb513956da9a3604edbaa7b5f026cf1ccebe6e673398852dd8`.
- The internal capability caller must use the exact secure-help arguments, omit stdin entirely, and receive exit 0, no timeout/cancellation, empty stderr, and the reviewed complete stdout hash. Existing executable identity, protected-path lease, environment, repository, owned-job and termination checks remain in force.
- Original quotas, NUL, actual profile paths and inherited secret values are checked before the one documented literal is substituted for recognizable-path classification. Original output is returned unchanged. The same bytes emitted by a model still fail.
- The raw Codex 0.153.2 help specimen is 3,957 UTF-8 bytes, including its final LF: SHA-256 `e504bac5a6364566fbe408132dec7993639def9258ece34e8352f51f8d43687c`. Trimmed, changed or appended output is not accepted by this exception. A CLI update needs explicit review; hashes are never learned from failures.
- Fixture additions exercise the exact context and actual native adapter. Game assets, Unity settings, the installer, and all other production runtime files are unchanged.

## Installation and original failure

The actual reviewed install reported `Success=true`, `Changed=true`, `Staged=true`, `AclVerified=true`, `HashesVerified=true`, and `TaskEnabled=false`. Task XML was read back: user `02031`, InteractiveToken, HighestAvailable, 15-minute repetition, IgnoreNew, StartWhenAvailable, WakeToRun, and the exact protected bundle arguments.

Installed bundle: `c6f7777a8547213b572710361ce454abee89f40679a93bb9075aa0c9f117daec`.

The exact failed functional command was:

```powershell
$SourceRepository = 'C:\Users\02031\AppData\Local\SashimiBoyGame\Issue39\Logs\HostRollout52-20260921\SourceRepository'
$InstalledBundle = 'C:\Program Files\SashimiBoyAutomation\Bundles\c6f7777a8547213b572710361ce454abee89f40679a93bb9075aa0c9f117daec'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File (Join-Path $SourceRepository 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') -ConfigPath (Join-Path $InstalledBundle 'Config.json') -RunRealCodex -TimeoutSeconds 60
```

Native exit **1**, stderr **empty**. Result: `RealCodexFunctionalSmoke=FAIL`, `DeveloperEdit=false`, `ReviewerReadOnly=false`, `Cleaned=true`; error `Production adapter failed for Developer (exit 1).` A separate unchanged-adapter reproduction returned `Executed=false` and `CODEX_ORIGINAL_OUTPUT_FORBIDDEN_CONTENT`. Its combined output identity was 3,958 bytes, SHA-256 `dcea2abef5e33f41aa334ad3e1c6d2d39723a9649a213645d728dc91d84d2799`. The failure was in capability help, before model dispatch. That failed attempt remains FAIL.

## Completed validation

| Check | Actual result | Evidence below the local activation directory |
| --- | --- | --- |
| Relevant Host/Codex regressions | 24 PASS / 0 FAIL; exit 0, empty stderr; 163 fake executable invocations; external mutations 0 | `help-fix-codex-regression.json` |
| Independent review of exact runtime commit | Blocker 0 / Major 0 / Minor 0 | `IndependentReview/CAPABILITY_HELP_REVIEW_20260921.md` |
| Independently rerun focused cases | 5 PASS / 0 FAIL; exit 0, empty stderr | `IndependentReview/capability-19bb295-focused.json` |
| Actual protected Codex capability helper | PASS with reviewed candidate call graph and installed executable; model execution false; no install/schedule changes; owned PID ledger empty | `IndependentReview/capability-19bb295-protected-help.json` |
| Fresh standalone Unity import / compile | PASS; native exit 0 | `UnityValidation/Summary.json`, `CompileImport.log` |
| Unity EditMode | 43 / 43 PASS; 0 skipped; native exit 0 | `UnityValidation/EditMode.xml` |
| Unity PlayMode | 8 / 8 PASS; 0 skipped; native exit 0 | `UnityValidation/PlayMode.xml` |
| Component inventory / serialized references | 50 assets; no errors, Missing Scripts or broken references | `UnityValidation/IntegritySummary.json`, `ComponentInventory.log` |
| Meta / GUID integrity | 1,264 meta files; missing, orphan, invalid and duplicate GUID counts all 0 | `UnityValidation/MetaGuidIntegrity.json` |
| Console error scan | No unexpected diagnostics across compile, inventory, EditMode and PlayMode | `UnityValidation/IntegritySummary.json` |
| Git and Unity settings | `git diff --check` PASS; fresh clone clean after all stages; ProjectSettings SHA-256 unchanged | `unity-post-validation.json` |
| Corrected installer preview | PASS; DryRun true, Changed/Staged false, TaskEnabled false | `corrected-installer-preview.json` |
| Final scheduler readback | Existing installed task disabled; all five old Desktop schedules PAUSED | `task-final-disabled-readback.json`, `desktop-automation-status.json` |

The 24 cases are a focused relevant regression run, not a new full-suite claim. They include the accepted help baseline plus 16 rejected contexts and seven native fake-Codex cases. The independent five cases overlap that set. The real help-only call does not certify a real Developer or Reviewer model session.

Earlier test-authoring failures remain preserved in `help-fix-focused.json` and `help-fix-focused-v2.json`: a mismatched fake head, then an incorrect test expectation about the outer `Executed` flag after raw output rejection. The corrected fixture uses native invocation audit counts to distinguish pre-model rejection from rejected model output; both final groups passed. No earlier failed run is relabelled.

The independent report was written while implementation Unity validation was pending; the completed Unity results above were inspected afterward. Its original report is reproduced in [CAPABILITY_HELP_REVIEW_20260921.md](CAPABILITY_HELP_REVIEW_20260921.md).

The Unity validation clone was detached at `19bb295b843888caa640060bb34e7698b3ea9794`. All 38 LFS objects were SHA-256 checked before checkout. No user checkout or existing linked worktree was used. The inventory job exited and its owned PID ledger is empty. ProjectSettings SHA-256 remains `1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902`.

Commands to reproduce the same focused case selection and the executed Unity stages:

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$ActivationRoot = 'C:\Users\02031\AppData\Local\SashimiBoyGame\Issue39\Logs\HostActivation52-20260921'
$UnityRepository = Join-Path $ActivationRoot 'UnityRepository'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $SourceRepository 'Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1') -RepositoryRoot $SourceRepository -TestNamePattern 'Codex|FunctionalSmoke|PowerShell7Parser|FixtureCompilerBoundary|FixtureIsolation'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $UnityRepository 'Tools\Automation\Invoke-UnityTests.ps1') -ProjectPath $UnityRepository -ArtifactsPath (Join-Path $ActivationRoot 'UnityValidation') -GitExecutable 'C:\Program Files\Git\cmd\git.exe'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File 'C:\Users\02031\AppData\Local\SashimiBoyGame\Issue39\Logs\HostRollout52-20260921\VerifyFinalIntegrity.ps1' -ProjectPath $UnityRepository -EvidenceRoot (Join-Path $ActivationRoot 'UnityValidation')
```

`VerifyFinalIntegrity.ps1` is a local evidence driver, not a new shipped runtime. It extracts the exact production integrity/log functions, runs the actual Unity component inventory through the existing owned-process boundary, and retains JSON/log evidence.

## Corrected bundle preview and pending rollout

The unchanged externally pinned installer bootstrap SHA-256 is `cff0acc1587d6ac75897c688847a7e672e64f289e3411bad7e4cbc46966dc973`. The corrected preview used the reviewed source configuration and protected Codex distribution SHA-256 `e86ffd96751ded51f669b520d70ba3139b514eb36313a8eeeedde37baa7b58e3`.

- Candidate BundleId: `89e04a131a80bb7bce850bfa5063d2c944b0216cd4327882a8d2697d71dd2058`.
- Candidate manifest SHA-256: `7de854b9aab3f2b92d23d0f3a47221de1a67a93955224d15c2c4f1e07c86f156`.
- This candidate has only been previewed. It has **not** replaced the installed bundle.

Remaining Owner rollout checklist:

1. Review and merge this follow-up. Agents may not merge under `AGENTS.md`; Issue #52 retains Owner merge before updated installation.
2. Verify merged runtime identity and repeat the externally pinned preview from [OPERATIONS.md](OPERATIONS.md). Install the same reviewed candidate through that launcher with the matching expected BundleId and bootstrap hash. Keep the task disabled and read back its definition.
3. Run the exact pending protected functional smoke below. Require both real Developer edit and read-only Reviewer results plus confirmed cleanup. Do not bypass the installed-bundle boundary or replace a failed result with the help-only result.
4. Resolve any existing handoff eligibility using the documented workflow, then run the separate #20 / PR #47 one-Issue pilot through Developer, Review, Reviewer and Verification. Do not fabricate an Owner failure or handoff to force selection.
5. Enable the 15-minute task only after the prescribed real functional smoke and pilot pass. Old Desktop schedules stay paused.

Pending command, **only after Owner merge and installation/readback of this exact candidate**:

```powershell
$UpdatedBundle = 'C:\Program Files\SashimiBoyAutomation\Bundles\89e04a131a80bb7bce850bfa5063d2c944b0216cd4327882a8d2697d71dd2058'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File (Join-Path $SourceRepository 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') -ConfigPath (Join-Path $UpdatedBundle 'Config.json') -RunRealCodex -TimeoutSeconds 60
```

Real model smoke for the correction, the #20 pilot, and schedule activation are **NOT_RUN**. No product Issue/Project transition, PR merge, or Done transition was performed during this follow-up.

Local evidence root: `%LOCALAPPDATA%\SashimiBoyGame\Issue39\Logs\HostActivation52-20260921`. Raw outputs, runtime configuration, logs and temporary repositories stay local and are not committed.
