# Empty decoded JSON text follow-up — 2026-09-25

PR #56 installed successfully, but actual Codex execution revealed a content-dependent adapter failure after model work. This correction accepts empty strings in the recursive decoded-output audit accumulator without skipping output inspection.

References #52; follow-up to #56. This is a manual infrastructure correction. Issue #52 remains Owner-closed / Done. No product Issue state change, PR merge, or schedule activation is part of this change.

## Source identity and observed failure

- Owner-merged base: `9fbf6d804c6a728d626d3f9b7f6b63216271014b` (PR #56, merged 2026-09-25 06:38:53 UTC).
- Reviewed implementation: `f604ca965a97cb9f4e5004c716c8d05ee211f58f`; tree `1c2b59aaf9583dce5c164f9184d121909d4f54f9`.
- SPEC_VERSION `1.0.8`; blob `b0f3d96f877256ed9ae03858ecc5185a989b1d1b`.
- Evidence directory below: `%LOCALAPPDATA%\SashimiBoyGame\Issue39\Logs\HostActivation56-20260925`. Generated logs, JSON receipts, local diagnostic drivers, and model artifacts remain outside Git.

PR #56's reviewed bundle `731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd` was installed with verified hashes and ACLs. All 15 disabled-task readback checks passed, including the resolved account SID. The initial literal account-name comparison failed because Task Scheduler returned a short name; that receipt is retained, and the later SID comparison confirmed the intended user. EnvironmentOnly passed 12/12; it is a DryRun plan, not live model/auth verification.

The installed orchestrator's explicit-manifest DryRun succeeded with six verified executable identities and a live queue response. It returned NoWork for #20, whose existing item lacks a current eligible handoff. This is not a completed #20 pilot; no handoff or product state was changed.

The exact installed smoke command was:

```powershell
$Source = Join-Path $env:LOCALAPPDATA 'SashimiBoyGame\Issue39\Logs\HostRollout52-20260921\SourceRepository'
$Installed = 'C:\Program Files\SashimiBoyAutomation\Bundles\731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -NonInteractive `
  -File (Join-Path $Source 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') `
  -ConfigPath (Join-Path $Installed 'Config.json') -RunRealCodex -TimeoutSeconds 180
```

Both attempts exited **1**, with **empty stderr**. The first reached DeveloperEdit=true before Reviewer failed; the second stopped during Developer. Both recorded Cleaned=true. The original `real-codex-functional-smoke.json` and `real-codex-functional-smoke-retest.json` remain FAIL.

A diagnostic invocation of the unchanged installed adapter captured exception type, ID, and stack without raw model output:

- `System.Management.Automation.ParameterBindingValidationException`
- `ParameterArgumentValidationErrorEmptyStringNotAllowed,Add-CodexDecodedJsonAuditText`
- recursive call at adapter line 481, reached from decoded JSON auditing at line 504 and JSONL parsing at line 1086.

The requested edit had already occurred. The model had run, but the Host rejected its event stream. An independent standalone Reviewer success does not contradict this content-dependent failure. Original evidence is in `DeveloperExceptionDiagnostic/exception-metadata.json` and `post-state.json`.

## Focused correction

`Add-CodexDecodedJsonAuditText` appends decoded strings to a mandatory `List[string] TextValues`. Once an empty value is appended, the next recursive invocation revalidates that list. `AllowEmptyCollection` alone does not permit an empty string element, so PowerShell rejected the call before the remaining event could be inspected.

The runtime change adds `[AllowEmptyString()]` only to that accumulator. Recursion, key/value collection, traversal limits, raw/decoded output controls, command restrictions, schema checks, process isolation, and artifact promotion rules are unchanged.

The smoke fixture now provides a small immutable `AGENTS.md`, since the production prompt instructs both roles to read it. Its hash is checked after each role along with Git metadata. The native fake emits a realistic MCP result with empty text followed by another field. The positive fixture also includes empty strings in nested objects and arrays. Existing unknown-command-wrapper and Unicode-escaped forbidden-profile fixtures place an empty field first and still require the expected security rejection.

## Validation

| Check | Result | Evidence |
| --- | --- | --- |
| Original regression before runtime fix | 0 PASS / 3 FAIL, exit 1, empty stderr; retained unchanged | `empty-text-before-fix.json` |
| Focused functionality/output/security regressions | 24 PASS / 0 FAIL, exit 0, empty stderr, zero external mutations, owned temporary root removed | `empty-text-security-regressions.json` |
| Independent fresh-clone source review and focused tests | 0 Blocker / 0 Major / 0 Minor; 5 PASS / 0 FAIL, exit 0, empty stderr | [Independent review](EMPTY_TEXT_REVIEW_20260925.md) |
| Actual Codex with candidate source adapter | Developer edit and read-only Reviewer PASS; native 0 each, 11 / 9 events; immutable files, closed workspace, empty owned process ledger confirmed | `CandidateAdapterDiagnostic/summary.json` |
| Fresh Unity import/compile, EditMode, PlayMode | PASS, 43/43 EditMode and 8/8 PlayMode, failed/skipped/inconclusive 0, native exits 0, empty stderr | `unity-candidate.json`, `UnityValidation/*.xml` |
| Component/reference, meta/GUID, Console scans | PASS, 50 inventoried assets, 1264 meta files, zero defects/diagnostics, owned process termination confirmed | `unity-integrity.json` |
| Canonical bytes and externally pinned installer DryRun | PASS, no staging or installation, exactly one changed payload file | `candidate-installer-preview.json`, `candidate-installer-identity.json` |

The pre-fix run contains two reproduced adapter failures plus a fixture-isolation audit failure because that narrow failing run never recorded a fake tool invocation. Later runs include the compiler boundary case and verify their own audit. The original third failure is not hidden or relabeled.

Unity ran in a fresh standalone clone at `f604ca9`, after all 38 LFS files were SHA-256 verified and materialized. The actual NUnit XML was inspected. The clone was clean after tests and inventory; ProjectSettings SHA-256 remained `1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902`. The live main head was still the candidate's parent `9fbf6d8`, so the candidate already contains that main. `git diff --check` passed.

Executed validation commands (the local diagnostic drivers remain in the retained evidence directory):

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$Evidence = Join-Path $env:LOCALAPPDATA 'SashimiBoyGame\Issue39\Logs\HostActivation56-20260925'
$Rollout = Join-Path $env:LOCALAPPDATA 'SashimiBoyGame\Issue39\Logs\HostRollout52-20260921'
& $Pwsh -NoProfile -NonInteractive -File (Join-Path $Source 'Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1') `
  -RepositoryRoot $Source -TestNamePattern 'Codex|FunctionalSmoke|SourceServer|PowerShell7Parser|FixtureCompilerBoundary'
& $Pwsh -NoProfile -NonInteractive -File (Join-Path $Evidence 'TestCandidateAdapter.ps1') `
  -SourceRepository $Source -EvidenceRoot $Evidence
& $Pwsh -NoProfile -NonInteractive -File (Join-Path $Evidence 'UnityRepository\Tools\Automation\Invoke-UnityTests.ps1') `
  -ProjectPath (Join-Path $Evidence 'UnityRepository') -ArtifactsPath (Join-Path $Evidence 'UnityValidation') `
  -UnityExecutable 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe' `
  -ExpectedUnityVersion '6000.4.0f1' -GitExecutable 'C:\Program Files\Git\cmd\git.exe'
& $Pwsh -NoProfile -NonInteractive -File (Join-Path $Rollout 'VerifyFinalIntegrity.ps1') `
  -ProjectPath (Join-Path $Evidence 'UnityRepository') -EvidenceRoot (Join-Path $Evidence 'UnityValidation')
```

The actual model diagnostic used the candidate source adapter with the already protected Codex executable pair. It retained its small exercise for investigation; its empty process ledger is not a claim of workspace removal. This successful candidate result is **not** a PASS of the newly installed bundle. The original installed smoke remains FAIL. Installer/M2 and full prior-suite evidence from PR #56 were not rerun for this adapter-only change and are not reported as new execution.

## Reviewed candidate installation identity

| Identity | Value |
| --- | --- |
| Installer SHA-256, unchanged | `3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733` |
| Candidate BundleId | `d315326a64346cb1d5d68178e0d10942e0851c08d5d169ff6f99d0b68e4d8617` |
| Candidate manifest SHA-256 | `d3365ccaf36f0b6e6ac7941b69919269553595d78e8de3331197ec5a98a666dd` |
| Candidate adapter SHA-256 | `52ae2fb2bb57a1313f062664244bf368654d7c659045a7519b211c3c6b228538` |
| Fixed Codex distribution SHA-256, unchanged | `965aa84ab38d77aab72bdb43d10b865ba70a300941db440d2584dd5e7e8bf6f2` |
| Source config SHA-256, unchanged | `316873e02986d5219d61e126c1a983c12dfd63d4b06423524299c3822957d09e` |

Each runtime file used for the preview matched its committed raw Git blob. Only `Invoke-SashimiCodexExec.ps1` differs from the PR #56 payload. Success/DryRun/SourceHashesVerified are true; Changed/Staged/TaskEnabled are false. The candidate has not been installed.

## Pending Owner and activation steps

Under [AGENTS.md](../../AGENTS.md), **an agent must never merge a PR or move an Issue to Done**. Issue #52's Owner sequence remains independent review → Owner merge → protected installation → separate #20 pilot → schedule enablement.

After Owner merge, verify the merged runtime matches this candidate, repeat the externally pinned preview, and install only the reviewed identities through [OPERATIONS.md](OPERATIONS.md). Read back the disabled task and new protected bundle, then run:

```powershell
$Bundle = 'C:\Program Files\SashimiBoyAutomation\Bundles\d315326a64346cb1d5d68178e0d10942e0851c08d5d169ff6f99d0b68e4d8617'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -NonInteractive `
  -File (Join-Path $Source 'Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1') `
  -ConfigPath (Join-Path $Bundle 'Config.json') -RunRealCodex -TimeoutSeconds 180
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -NonInteractive `
  -File (Join-Path $Bundle 'Invoke-SashimiHostOrchestrator.ps1') `
  -ConfigPath (Join-Path $Bundle 'Config.json') `
  -IntegrityManifestPath (Join-Path $Bundle 'HostIntegrity.json') `
  -Once -IssueNumber 20 -DryRun
```

Require the installed Developer edit, read-only Reviewer result, confirmed cleanup, and verified integrity. Resolve #20's live handoff eligibility through the workflow; never fabricate an Owner failure or force a Ready transition. The separate #20 / PR #47 Developer → Review → Reviewer → Verification pilot must complete before enabling the 15-minute schedule.

The Windows task is Disabled and all five legacy Desktop automations remain PAUSED. Owner merge, new protected installation/smoke, #20 pilot, and schedule enablement remain pending. Game visual/audio/input/save checks have not been performed or claimed by this infrastructure correction.
