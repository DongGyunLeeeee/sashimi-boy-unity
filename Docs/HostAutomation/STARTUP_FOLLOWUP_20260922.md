# Issue #52: complete installed startup distribution

Owner-merged PR #55 (`b856e0a697f293ac5ff1c996ebd8bafbe3ffbed3`) installed successfully, with the scheduled task disabled. Its first real Developer smoke reached the model but returned `Blocked`: `codex-code-mode-host.exe` was absent from the protected distribution. A separate protected orchestrator DryRun rejected the valid runtime manifest before queue selection because only the manifest's file list was sorted.

References #52; follow-up to #55. This is a manual infrastructure correction. Issue #52 remains Owner-closed / Done; no product Issue transition, PR merge, or schedule activation is part of this change.

## Fix

- Runtime/test commit: `5f52488c41b6b75408f06ee4434bd696537aba58`; fixture-only corrections: `8e6dd0670568dc231e55cb799370de22333e53fc`, `b1ce169098ea258719bd2de739383c82001453a9`, `b8e7d516e50d19137bf76ac821dc23e7ea763243`.
- Specification version: `1.0.7`. Game code, assets, and Unity settings are unchanged.
- The runtime sorts both required and manifest file names before comparing their exact closed sets. The actual installer-generated 11-file payload now has a positive runtime-verifier regression as well as missing, duplicate, unknown, case, hash, length, and provenance failures.
- The installer resolves the app's versioned source-directory alias once and captures both `codex.exe` and `codex-code-mode-host.exe` while both are held against writes/deletion. It stages and verifies both files before atomic publication. A missing/changed companion changes or rejects the proposed bundle before registration.
- Generated executable identity schema 2 binds the companion filename, length, and hash. Its distribution ID covers both executable identities. The source configuration schema and six configured tool entries are unchanged. Existing one-file installations remain intact.
- Both runtime entry point and Common require exactly the two reviewed files, their hashes, canonical protected locations, no-reparse ancestry, and protected ACLs. The real process boundary holds both read leases through owned-job termination and releases both on failure. Tests include denied companion writes/replacement and a sharing failure observed after the main lease is held.

The known Codex 0.153.2 source pair is:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `codex.exe` | 295447856 | `e86ffd96751ded51f669b520d70ba3139b514eb36313a8eeeedde37baa7b58e3` |
| `codex-code-mode-host.exe` | 72472880 | `ee37f218b8052cf7c2c99fcc5414da20c07262eaab14c81870c6ed8cd522d3f0` |

Their distribution ID is `965aa84ab38d77aab72bdb43d10b865ba70a300941db440d2584dd5e7e8bf6f2`; combined length is 367920736. This ID is distinct from either file's hash. The precise grammar is in [OPERATIONS.md](OPERATIONS.md).

## Original installed failures

The successfully installed #55 bundle is `89e04a131a80bb7bce850bfa5063d2c944b0216cd4327882a8d2697d71dd2058`. Actual installation reported staged, hashes verified, ACL verified, task disabled. Scheduler readback matched all 15 checked task fields. The following commands then failed; their evidence is retained as FAIL:

```powershell
$SourceRepository = 'C:\Users\02031\AppData\Local\SashimiBoyGame\Issue39\Logs\HostRollout52-20260921\SourceRepository'
$InstalledBundle = 'C:\Program Files\SashimiBoyAutomation\Bundles\89e04a131a80bb7bce850bfa5063d2c944b0216cd4327882a8d2697d71dd2058'
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $SourceRepository 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') -ConfigPath (Join-Path $InstalledBundle 'Config.json') -RunRealCodex -TimeoutSeconds 60
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $InstalledBundle 'Invoke-SashimiHostOrchestrator.ps1') -ConfigPath (Join-Path $InstalledBundle 'Config.json') -IntegrityManifestPath (Join-Path $InstalledBundle 'HostIntegrity.json') -Once -IssueNumber 20 -DryRun
```

Both exited **1**, with **empty stderr**. Smoke: `DeveloperEdit=false`, `ReviewerReadOnly=false`, `Cleaned=true`; error `Production adapter failed for Developer (exit 1).` The diagnostic adapter run had `Executed=true`, six events, outcome `Blocked`, and an OS error 2 identifying the missing code-mode host. Its owned-process ledger is empty. This was not a capability-help rejection. The orchestrator result was `Integrity.Verified=false`, `Reason=VerificationFailed`, `Selection=null`, with no commands dispatched. An independent read confirmed that all 11 installed file hashes/lengths matched and that unsorted expected names caused the rejection.

Local evidence remains under `%LOCALAPPDATA%\SashimiBoyGame\Issue39\Logs\HostActivation55-20260921`. Raw model output, logs, config, and temporary repositories are not committed.

## Validation and rollout

| Check | Executed result | Local evidence |
| --- | --- | --- |
| Independent code review | Blocker 0 / Major 0 / Minor 0, including the `b8e7d51` test-only addendum | `IndependentReview/STARTUP_DISTRIBUTION_REVIEW_20260922.md` |
| Independent focused retest | 4 PASS, exit 0, empty stderr; 6 distinct latest selected cases PASS across the two unchanged-runtime heads | `IndependentReview/startup-fixture-retest.json`, `startup-focused.json` |
| Complete Host coverage | 107 distinct cases with latest applicable executed result PASS; original failures retained | `startup-coverage.json` |
| Installer failure/recovery matrix | 176 scenarios reached; matrix PASS; 0 external mutations | `startup-focused-2.json` |
| Host remainder partition | 95 distinct cases PASS by latest executed result: 94 original passes plus the corrected deadline retest | `startup-remainder.json`, `startup-content-drift-final.json` |
| Fresh Unity compile/import | PASS, native exit 0 | `UnityValidation/Summary.json` |
| EditMode / PlayMode | 43/43 and 8/8 PASS; no skipped or inconclusive tests | `UnityValidation/EditMode.xml`, `PlayMode.xml` |
| Inventory / serialized references | 50 assets; no Missing Scripts or broken references | `unity-integrity.json` |
| Meta / GUID and Console | 1264 meta files; missing/orphan/invalid/duplicate counts 0; no unexpected Console diagnostics | `UnityValidation/MetaGuidIntegrity.json`, `unity-integrity.json` |
| Unity checkout / process cleanup | Clean; settings hash unchanged; owned PID ledger empty | `unity-post-validation.json` |
| Externally pinned installer preview | PASS, DryRun true, Changed/Staged/TaskEnabled false; final source reproduces the independently inspected bundle | `corrected-startup-installer-preview.json`, `final-source-installer-preview.json` |

Unity ran in a new standalone clone at `5f52488`, after SHA-256 verification of all 38 cached LFS objects. The three fixture corrections listed above change only Host tests. The independent reviewer directly read the Unity result XML and integrity evidence without claiming an independent Unity rerun. ProjectSettings SHA-256 remains `1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902`.

The initial independent run was 5 PASS / 1 FAIL because the separately invoked fixture could not access the parent script's variable. That original failure is retained; the explicit path parameter and safe junction cleanup were retested successfully. Root test-authoring failures are likewise preserved rather than relabelled.

The 95-case Host remainder run returned 94 PASS / 1 FAIL (native exit 1, empty stderr). Its PR-content drift case exhausted the 60-second fixture deadline, and a separate unchanged retry did the same. At `b8e7d51`, only that test's wrapper deadline was raised to 120 seconds and an explicit no-timeout/confirmed-termination assertion was added. The case then completed in 63.794 seconds with the expected policy rejection and no push or state mutation. That case plus compiler/audit checks passed 3/3, exit 0, empty stderr. Production deadlines and policy were unchanged. All original failures remain preserved. The first partition recorded 27 permitted fake mutations and 0 external mutations; the final retest recorded 0 of either.

Reviewed installer bootstrap SHA-256: `3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733`. The external pinned launcher from [OPERATIONS.md](OPERATIONS.md) held that exact installer open while running the preview. Candidate BundleId: `731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd`; manifest SHA-256: `062eb039641cc7effc07e7e16860c144e133007297a1094176e60678c8d38436`. This candidate is previewed, not installed.

On 2026-09-25, a fresh external-hash-pinned preview reproduced the same bundle, manifest, and Codex distribution identities, with six bound tools and verified source hashes. It again reported `Changed=false`, `Staged=false`, and `TaskEnabled=false`; evidence is `final-source-installer-preview-20260925.json`. Live main remained `b856e0a`, and the installed task still read back as Disabled.

The full focused/M2 run completed at the original runtime/test revision with **12 PASS / 1 FAIL**, native exit 1, empty stderr, and removed owned temporary state. Its only failure is the same separately invoked fixture variable-scope error already fixed and independently retested at `b1ce169`. The production installer matrix itself passed all 176 reached scenarios, including the new companion write and ACL checkpoints, in 4,786,213 milliseconds. Every matrix row records zero external mutations. The original failed suite remains failed in its retained evidence.

`startup-coverage.json` records each source result's SHA-256 and selects the latest applicable executed result per case, following code/test revision order rather than the long matrix's completion time. It contains **107 distinct cases, 107 PASS, 0 FAIL**, and 176 matrix scenarios. This is coverage across the documented runs and retests, not a claim that a single full-suite invocation passed. See the [independent review](STARTUP_DISTRIBUTION_REVIEW_20260922.md) for its separate execution and evidence readback scope.

The installed real-model smoke for this correction, separate #20 pilot, and schedule enablement remain **NOT_RUN** until the Owner merges this follow-up and the reviewed bundle is installed/read back.

After Owner merge, repeat the externally pinned preview and install only the matching reviewed BundleId using [OPERATIONS.md](OPERATIONS.md). Read back the disabled task before these pending commands:

```powershell
$UpdatedBundle = 'C:\Program Files\SashimiBoyAutomation\Bundles\731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $SourceRepository 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') -ConfigPath (Join-Path $UpdatedBundle 'Config.json') -RunRealCodex -TimeoutSeconds 180
& $Pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $UpdatedBundle 'Invoke-SashimiHostOrchestrator.ps1') -ConfigPath (Join-Path $UpdatedBundle 'Config.json') -IntegrityManifestPath (Join-Path $UpdatedBundle 'HostIntegrity.json') -Once -IssueNumber 20 -DryRun
```

Require the real Developer edit, read-only Reviewer result, confirmed cleanup, and verified installed integrity before the separate #20 pilot. Resolve its live handoff eligibility through the workflow; never fabricate an Owner verification failure to force selection. Only successful required rollout evidence permits enabling the schedule.

Under [AGENTS.md](../../AGENTS.md), agents may not merge a PR or move an Issue to Done. The Issue #52 Owner sequence remains independent review → Owner merge → protected installation → separate #20 pilot → schedule enablement. The installed task and all five old Desktop automations remain disabled/paused. A passing fixture or help probe does not replace the required real Developer edit and read-only Reviewer smoke.
