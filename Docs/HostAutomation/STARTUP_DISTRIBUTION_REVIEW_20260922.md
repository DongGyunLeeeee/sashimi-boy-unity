# Independent startup distribution review — 2026-09-22

**Latest reviewed head: `b8e7d516e50d19137bf76ac821dc23e7ea763243`. Open findings: Blocker 0 / Major 0 / Minor 0.** The original review below covers `b1ce169098ea258719bd2de739383c82001453a9`; the final addendum records the later test-only change and inspected retry evidence.

The final M2 and aggregate evidence readback was completed on **2026-09-25** and is appended at the end. Earlier statements that those results were pending preserve the status at the time of those earlier reviews; they are superseded by the final evidence addendum.

This is the authorized independent review of the Issue #52 startup follow-up. The result covers the changed code and the selected independent regressions below. It is not a claim that the new protected bundle has been installed, that a real Codex model run now succeeds, or that scheduling may already be enabled.

## Exact source and scope

- Base: `b856e0a697f293ac5ff1c996ebd8bafbe3ffbed3`.
- Runtime change commit: `5f52488c41b6b75408f06ee4434bd696537aba58`, tree `5c0eea19c9662734befee7d2669e9fa8ef8664fc`.
- Final head after two fixture-only corrections: `b1ce169098ea258719bd2de739383c82001453a9`, tree `aa11ac9e1002a1f4ca5c15d6a9f3b5d20bc71e36`.
- Specification: `1.0.7`; SPEC_VERSION blob `238d6e882a08cec91c9bf37bd210147a0c968798`.
- A fresh, standalone, no-LFS clone was created under this independent evidence directory. The four runtime files' raw Git blobs match the committed blobs and the runtime commit. The source clone was not changed by the reviewer. The independent clone is clean.
- Reviewed AGENTS.md, WORKFLOW.md, REVIEWER.md, the Host documentation changes, installer, Common, Orchestrator, adapter, and affected test fixtures. No GitHub state, installed bundle, scheduled task, or game source was changed.

`review-identity.json` records the exact identities. Installer SHA-256 is **`3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733`**; Git blob is **`fc95c602fbe2fe339f266953e4bfc0bfbbd927c9`**. The independent clone's bytes are authoritative; the separate development checkout's temporary CRLF representation was not used to compute this identity.

## Resolution of the startup failures

The earlier installed bundle rejected a valid manifest before loading Common. At the base commit, the Orchestrator sorted manifest entries but compared them with the declaration-order required names. The reviewer independently read the installed entrypoint and manifest: indices 5–8 differed, although all 11 actual payload files matched their manifest hashes and lengths. This was a confirmed Major startup defect, not a security fail-open.

The final `Invoke-SashimiHostOrchestrator.ps1:921` extracts the actual payload check into `Assert-OrchestratorBundlePayload`. It sorts both name lists, checks every expected file, rejects unexpected filesystem entries, verifies provenance, and retains the existing ACL calls. The production integrity entrypoint calls this same helper at line 992 after its location, ACL, schema, and manifest identity checks. The new fixture consumes the actual installer-produced 11-file manifest and staged files, rather than a parallel hand-written manifest. Both normal and reversed order pass; missing, duplicate, unexpected, wrong-case, changed hash, changed length, changed provenance, and extra filesystem entries fail.

The parent separately diagnosed the prior installed model attempt's missing `codex-code-mode-host.exe`. The new installer captures and stages both fixed filenames. The distribution ID covers their names, hashes, and lengths; the manifest records their combined length, while the main executable's individual identity remains distinct. A nested companion identity is required by executable identity schema 2. Six configured tools and the source configuration schema are preserved. The aggregate directory identity leaves the old one-file distribution untouched.

Important implementation boundaries examined:

- `Install-SashimiHostAutomation.ps1:743`: resolve only the source directory alias, then reject further reparse traversal, acquire both source leases, and snapshot both files from the resolved directory.
- Installer lines 773, 787, 1127, and 1163: aggregate identity, generated companion binding, exact two-file validation, per-file staging checks and ACLs, and existing atomic publication/transaction cleanup.
- `HostAutomation.Common.ps1:336,477,576`: preserve and validate nested companion metadata, bind both files to the protected aggregate path, enforce the closed file set and protected ancestry, and rehash both leased handles immediately before launch.
- Common line 2241 forces the owned native job path. That path retains both leases across native execution and releases them at line 2389. The older fallback's earlier release is not reachable through current production dispatch. `Invoke-SashimiCodexExec.ps1:809` also uses the new complete lease cleanup helper.
- `Invoke-SashimiHostOrchestrator.ps1:257,295`: recheck both executable identities and their protected distribution before Common loads, including consistency with manifest distribution provenance.

No new runtime bypass, premature release in the active execution path, or compatibility defect was demonstrated in this focused review. Real companion launch and tool discovery remain an explicit rollout test.

## Independent execution and preserved failure

The first selected run used exact runtime/test commit `5f52488`: **5 PASS / 1 FAIL**, native exit 1, empty stderr, no external or simulated mutations. Evidence is `startup-focused.json`, `startup-focused.exit.txt`, and `startup-focused.stderr.txt`.

The failed case was `ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization`. Its separately invoked fixture attempted to read the parent's `$script:fakeConfigPath`, which is not in that script's scope. The reviewer reported this concrete fixture failure. The original failed evidence remains unchanged. It is not retrospectively recorded as PASS.

Commit `8e6dd0670568dc231e55cb799370de22333e53fc` corrected junction cleanup to validate the marked parent, require a reparse leaf, and delete the junction nonrecursively. The reviewer read this correction but did not run a separate suite at that intermediate head. Commit `b1ce169` then passed the source executable path explicitly into the separate fixture. Both changes are test-only; the four production runtime blobs remain identical to `5f52488`.

The affected case and its parser/compiler/audit checks were independently rerun at **`b1ce169`: 4 PASS / 0 FAIL**, native exit 0, empty stderr, no external or simulated mutations, and owned temporary directory removed. Evidence is `startup-fixture-retest.json`, `startup-fixture-retest.exit.txt`, and `startup-fixture-retest.stderr.txt`.

| Selected case | Latest execution | Result |
| --- | --- | --- |
| PowerShell7ParserAcceptsEveryScript | b1ce169 | PASS |
| FixtureCompilerBoundaryAcceptsOnlyTheOwnedBootstrapPlan | b1ce169 | PASS |
| ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization | b1ce169 | PASS |
| ExecutableIdentityRejectsPathShadowAndChangedBinaryBeforeLaunch | 5f52488; unchanged relevant code/test | PASS |
| ProtectedCodexProcessGateRejectsWritableReparseChangedAndReplacementRaces | 5f52488; unchanged relevant code/test | PASS |
| FixtureIsolationHasNoLiveIssueMutation | b1ce169 | PASS |

This is **6 distinct selected cases with their latest executed result PASS**, not a claim that one six-case suite passed at the final head. The protected process gate case exercised companion ACL rejection, missing/changed companion rejection, extra-file rejection, write/replacement denial on both leased files, and partial-acquisition cleanup. The revised partial-failure fixture observes main-file write denial before injecting the companion sharing conflict, so a preflight failure cannot satisfy that check.

Both independent harness results report `ExternalMutationCount=0`, `SimulatedMutationCount=0`, `SchedulerMutationSentinelCount=0`, and `TemporaryRoot=null`. Each included the compiler boundary test and a nonempty fake-invocation audit. `git diff --check` against the base passed, and source status remained clean. No real Codex model or Unity process was launched by this reviewer.

The long M2 matrix and full Host suite were not duplicated. The matrix source derives fault points from the actual successful installer trace, which includes the new companion write/check/ACL operations. Their complete execution results were still pending when this report was written.

## Existing evidence independently inspected

The root agent's completed Unity evidence was read independently, not executed by this reviewer. The Unity clone's HEAD is `5f52488`; later commits change only Host test fixtures. `unity-candidate.json` reports compile/import native exit 0, EditMode native exit 0, PlayMode native exit 0, empty diagnostic findings, and clean post-test Git status. The reviewer also directly parsed the NUnit XML: EditMode 43/43 and PlayMode 8/8, with failed/skipped/inconclusive counts all zero. Compile and test logs exist. This readback is retained in `unity-evidence-readback.json`.

The reviewer additionally read the root agent's `unity-integrity.json`: success, inventory native exit 0, 50 inventoried assets, 1264 meta files, zero inventory/meta/GUID/Missing Script/reference defects, empty diagnostics, assigned owned job, and confirmed process termination. These checks were executed by the root agent; they were not independently rerun here.

The root's `corrected-startup-installer-preview.json` was also inspected. It records successful DryRun, `Changed=false`, `Staged=false`, `TaskEnabled=false`, six bound tools, and verified source hashes. Its bootstrap SHA-256 matches the independently reviewed installer. It identifies bundle `731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd`, manifest SHA-256 `062eb039641cc7effc07e7e16860c144e133007297a1094176e60678c8d38436`, and distribution `965aa84ab38d77aab72bdb43d10b865ba70a300941db440d2584dd5e7e8bf6f2`. This is preview evidence, not an installed-runtime success claim.

The older installed-bundle startup failure and model failure evidence are preserved in the parent `HostActivation55-20260921` directory. They have not been relabelled as successful new-bundle runs.

## Remaining rollout gates

There is no open Blocker or Major from this independent code and selected-regression review. The work may advance to the remaining required validation and Owner review steps. Before automatic development resumes, retain and inspect the complete Host/M2 results and required integrity evidence, obtain the applicable Owner merge/install authorization, and install only the matching reviewed bundle with the task disabled.

Then verify the actual installed entrypoint with an explicit integrity manifest reaches a verified bundle and queue phase, execute the real protected Codex functional smoke including code-mode tool discovery, and perform the authorized Issue #20 pilot if its live queue contract permits selection. Enable scheduling only after the required rollout evidence passes. Installation, new-bundle real model smoke, Issue #20 execution, and scheduling changes were **NOT_RUN by this independent reviewer**.

## Final addendum — test deadline and manual pilot example

The reviewer inspected exact commit `b8e7d516e50d19137bf76ac821dc23e7ea763243`, tree `44db11271ad0702d4b29ec92e833b3801791367e`. Its only change from `b1ce169` is in `Test-SashimiHostAutomation.ps1:2810`: the `PullRequestContentDriftAtSameHeadCausesNoPushOrStatusMutation` fixture's outer deadline increases from 60 to 120 seconds, and a new assertion requires `!TimedOut && TerminationConfirmed` before inspecting the expected nonzero exit and rejection JSON. The production runtime files remain byte-identical to runtime commit `5f52488`; their reviewed hashes and installer identity above are unchanged. The added assertion prevents a timeout from masquerading as the intended policy rejection. `git diff --check` passed for this tiny change.

The earlier root-agent results remain failures and are retained:

- `startup-remainder.json`: 94 PASS / 1 FAIL. The content-drift case failed after 60.251 seconds because no result JSON was captured. The run reports zero external mutations; 27 simulated mutations belong to its fake fixture workload.
- `startup-content-drift-retest.json`: 2 PASS / 1 FAIL. The same case again produced no JSON after 60.370 seconds with the original 60-second deadline. External and simulated mutations are zero.

The root agent then executed `startup-content-drift-final.json` at the corrected head. The reviewer independently read this JSON and its exit/stderr evidence: **3 PASS / 0 FAIL**, native exit 0, empty stderr, and the content-drift case completed successfully in **63.794 seconds**. The compiler boundary and fixture isolation cases also passed. The audit records 200 fake calls, zero simulated or external mutations, zero scheduler mutation sentinels, and removed owned temporary state. The reviewer did not duplicate this execution.

Selecting each case's latest result within the original remainder set gives **95 distinct remainder cases with latest result PASS**. This is not a claim that the original remainder run passed or that a complete suite ran successfully in one attempt. The full M2 matrix remains pending at addendum time. `addendum-summary.json` preserves this separate aggregate and identifies the root agent as the executor.

The uncommitted `Docs/HostAutomation/OPERATIONS.md` change was also inspected: the installed Issue #20 DryRun example now supplies `-IntegrityManifestPath (Join-Path $Bundle 'HostIntegrity.json')`, matching the required installed-runtime contract and the bundle used for its config. This documentation-only delta was not represented as part of commit `b8e7d51`; its examined raw Git blob is `bff0ef5c420f04835b201cfe569f62eb8d8d7f21` and SHA-256 is `ec90e691fd292fa1fd69823e4e57d96fba2b9d38efaa4eeea3592f823795a6ae`.

No new Blocker, Major, or Minor finding is supported by these small changes. The remaining installation, actual protected model smoke, pilot, and scheduling gates are unchanged. No installed process, bundle, scheduler, or GitHub state was changed by the reviewer.

## Final evidence addendum — M2 and coverage readback, 2026-09-25

The reviewer independently read the completed JSON artifacts and reconstructed the aggregate without rerunning any tests or changing source, installed processes, scheduling, or GitHub state. The result is recorded in `final-coverage-readback.json`. This adds evidence to the existing review of runtime commit `5f52488` and final test commit `b8e7d51`; it does not introduce another code change.

The original `startup-focused-2.json` remains **12 PASS / 1 FAIL**, native exit **1**, with empty stderr. Its sole failure is the already documented initial `ConfigurationCanonicalStagingMatchesRuntimeWithUnicodeCustomization` fixture scope error. That failure is still present in the source artifact. It is superseded for that case only by the independently executed `b1ce169` retest; the original run is not relabelled as successful.

The M2 case in that original artifact is independently confirmed **PASS** with a duration of **4,786,213 ms**. Its matrix contains **176 distinct scenario rows**, with no duplicate case identifiers. Every row records `Reached=true` and `ExternalMutations=0`; the owned temporary root is null after cleanup. The six additional companion-specific rows cover Write, Written, ACL.Apply, and three ACL.Verify boundaries. All six record the intended fault, reached boundary, and successful identical-plan retry. The overall matrix includes the existing collision, reparse, incomplete marker, cleanup-refusal, atomic publication, and read-only reuse scenarios. These are fixture fault-injection results, not claims that the corresponding failures happened naturally in the installed host.

The aggregate's five declared source files were rehashed directly and checked in this explicit applicability order:

| Order | Source | SHA-256 |
| --- | --- | --- |
| 1 | `startup-focused-2.json` | `ec1ac3386ae52095b8fcb0e2897efc75b82e2c6e76ba0f48c63cc6939699df57` |
| 2 | `startup-remainder.json` | `352717591190de9be0a18fe3f71372c2456522bb0fef672d154e46c2ed9bf471` |
| 3 | `IndependentReview/startup-focused.json` | `165206bddf9095d1b5b6228b65bf03c71b6127ac05935342e1f06d401105661d` |
| 4 | `IndependentReview/startup-fixture-retest.json` | `1d7c5402154ace8202e132c0b6ee4e727a55fa13a3935b0027b238184e950732` |
| 5 | `startup-content-drift-final.json` | `ce913b2874184dee3ce5847f3ccaf1c01972d0175ff578aedb69bfb6e0f69da8` |

All five hashes and their recorded original pass/fail/success metadata match. The sources contain 121 executed case records. Applying the declared order produces 14 duplicate-case replacement events and **107 distinct cases**, each with its latest applicable executed result PASS. Source-file completion time is not used to let an earlier-code run replace a corrected later-code result. For every aggregate case, the reviewer compared name, result, source, revision, executor, duration, and error with the independently reconstructed record: **zero field mismatches**. The aggregate has no duplicate or extra case names, and its totals and runtime/test commit identities match.

`startup-coverage.json` therefore correctly records **107 latest applicable PASS / 0 FAIL** and **176 installer scenarios**. Its SHA-256 is `cb42d6d907095649a8763318cc1aa6b33ecb2dcdff9512584439d69c91006286`. The saved JSON contains concrete numeric and Boolean values; the reported null display from projecting an ordered dictionary was a presentation issue, not missing result data. This remains an aggregate of multiple preserved runs, not one wholly passing full-suite execution.

Every source's external mutation counters, external fake-mutation counters, and scheduler mutation sentinels are zero. The remainder source's 27 simulated mutations are explicitly confined to its fake fixture workload and are not described as external writes. The full focused/M2 source and both corrective retests preserve their own original outcomes and audits.

The full Host coverage and M2 evidence are now independently read back and no longer pending. **Open findings remain Blocker 0 / Major 0 / Minor 0.** The installation and actual protected Codex smoke, eligible Issue #20 pilot, and final scheduling gates still require their separate rollout evidence; this readback does not claim that those operations occurred.

The reviewer also inspected the root agent's fresh `final-source-installer-preview-20260925.json` (SHA-256 `d832ed33a3b7fb79060268a9b9b69f6bc8e947a4d533970341103e9fdea3a5f3`). It reports successful DryRun, exit 0, six bound tools, verified source hashes, and `Changed=false`, `Staged=false`, `TaskEnabled=false`. Its bundle `731ad5644c207dba42948b0c5e11003ce1602ba79d180a7fd35bd4c84aef81cd`, manifest `062eb039641cc7effc07e7e16860c144e133007297a1094176e60678c8d38436`, installer bootstrap `3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733`, and Codex distribution `965aa84ab38d77aab72bdb43d10b865ba70a300941db440d2584dd5e7e8bf6f2` exactly match the previously reviewed identities. This separate readback is saved in `final-preview-readback-20260925.json`; the reviewer did not execute the installer.
