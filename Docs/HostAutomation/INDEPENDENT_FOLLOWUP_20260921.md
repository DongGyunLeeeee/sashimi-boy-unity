# Issue #52 follow-up independent review

**Final independent finding count at `8ff1891c62c3943b732f23c38d6c7e6edeb95ac9`: Blocker 0, Major 0, Minor 0. The independent code-review gate is satisfied for these exact reviewed bytes.**

This report supplements and preserves `INDEPENDENT_REVIEW_20260921.md`, which examined merged main `528452fd22a9ac9f722c41f565a7351e8ed292b3` and identified Major M1, M2, and M3. The original 100-case independent regression and the original failure reproductions remain separate evidence.

## First follow-up identity

- Examined local follow-up commit: `9bb168426bb372851310acda7f8f5cd8bee7940b`.
- Tree: `74767b4e9171d26a0d851da1beae5ddbeadf7d97`.
- SPEC_VERSION: `1.0.5`; version blob: `90a27f9cea6e8f02e05a8bbab5d14650e3e932af`.
- Same independent standalone clone, fetched from the implementation clone and checked out detached. No implementation changes were made in the independent clone.
- Installer bytes unchanged: SHA-256 `cff0acc1587d6ac75897c688847a7e672e64f289e3411bad7e4cbc46966dc973`; Git blob `e956466f18368f85cd0015c9477d5abd19011c41`.
- All eight changed files inspected; applicable AGENTS.md, WORKFLOW.md, REVIEWER.md and updated Host documentation reread. `git diff --check 528452f HEAD` and clean clone status confirmed.

## Findings resolution at 9bb1684

**M1 resolved at 9bb1684 by code inspection and independent execution.** `Get-ReviewerDiffContext` pins the actual synthetic commit and reads the exact main-to-synthetic changed-path manifest and patch through the Host Git boundary. External diff/textconv are disabled, renames are explicit deletion/addition, binary changes are explicit, and oversized/sensitive/inconsistent context fails before model invocation. The complete bounded context is now placed in the actual production prompt. Temporary prompt deletion and the read-only source MCP remain in place.

An additional independent fixture used a real local Git repository with modified, deleted, added, binary, and Korean-path files and the exact AST-extracted production helper. All seven checks passed, including exact SHAs, before/after lines, deleted content, full changed-path count, and binary notice. This supplements the implementation's fake-Git fixture without claiming a real Codex model was run. Evidence: `Test-IndependentReviewerRealGit.ps1`, `followup-real-git-diff-context.json`.

**M2 resolved at 9bb1684 by code inspection and independent execution.** The live Issue query requests `author{login}`, conversion preserves the login, and the common queue eligibility function rejects missing/empty/unlisted authors before every role or mode. The fresh publication contract repeats the same check; the contract's author participates in the full snapshot comparison immediately before mutations. Owner authorization to mutate the Project remains a separate check.

**M3 remains open at 9bb1684.** `HostAutomation.Common.ps1` is unchanged from the original reviewed main; the assignment-failure suspended-child leak is still present. No installer or scheduler activation is approved at this intermediate commit.

## Independent follow-up execution

The six selected harness cases completed: **6 PASS, 0 FAIL, external mutation count 0**, native exit 0, empty stderr. They comprise the three Rollout cases, publication pin revalidation, script parsing, and fixture isolation. The native fake Codex input fixture confirms that the actual production transport includes removed and before/after content; absent/oversized/inconsistent evidence never starts the model boundary or changes Project state. Queue tests cover every mode; live-shaped author tests cover Owner, explicitly allowed maintainer, unknown, empty, missing, and null identities; mutation-boundary tests include author drift. Evidence: `followup-9bb1684-regression.json`, `followup-9bb1684-regression.stderr.txt`, and `followup-9bb1684-regression.exit.txt`. GitHub/model/Unity boundaries in these tests are fakes, not live operations.

No Issue/PR write, push, base-checkout modification, protected installation, schedule change, or real-model run was performed by this reviewer. The original rollout checks remain required after all demonstrated findings are fixed and reviewed.

## Final follow-up identity and M3 resolution

- Final examined commit: `8ff1891c62c3943b732f23c38d6c7e6edeb95ac9`.
- Final tree: `5d04ad4a2a8d75777c761ffebf08a325a0b66abd`.
- SPEC_VERSION remains `1.0.5`, blob `90a27f9cea6e8f02e05a8bbab5d14650e3e932af`.
- The installer SHA-256 and blob above remain unchanged. Native Common source SHA-256 is now `dfc1c96945a81a593cbad8b00c1f03d06d3cb28e6bb0cd8bfad55e701928fe72`.
- All four additional changed files inspected. The M1/M2 implementation bytes are unchanged from the independently verified first follow-up. Full previous-main-to-final diff checking is clean, and the independent clone has no working-tree changes.

**M3 resolved by independent code inspection and three direct failure injections.** Common now records PID and kernel start time before the assignment operation. If the process has not joined the job, cleanup terminates it through the already owned native process handle and waits within the confirmation bound. A confirmed exit removes that exact ledger identity; an unconfirmed exit retains it. Successfully assigned jobs keep the original job cleanup path.

`Test-IndependentNativeCleanupFollowup.ps1` independently extracts the final checked-in native function and changes only named Win32 return values (or provides the deliberately failing ledger callback). It creates real harmless `whoami.exe` children without running a model:

| Injected boundary | Independently observed final behavior |
| --- | --- |
| AssignProcessToJobObject returns false | Production removes the suspended child; exact PID/start-time add then remove; no child left after Run. |
| Ledger add callback throws | Production removes the not-yet-assigned child; exact add/remove identity; no child left after Run. |
| Assignment and TerminateProcess both return false | Suspended child remains with its exact PID/start time in the ledger; no false removal. Fixture verifies the identity, terminates only its own child, and confirms cleanup. |

All three direct checks returned Success=true, CleanupConfirmed=true, no test error, and external mutation count 0. These are simulated API failures with real child creation/cleanup, not claims that the OS naturally failed those API calls. Evidence: `followup-native-8ff1891-AssignmentFailure.json`, `followup-native-8ff1891-LedgerCallbackFailure.json`, and `followup-native-8ff1891-AssignmentAndTerminationFailure.json`.

## AudioClock test adjustment

Reviewed `Assets/_SashimiBoy/Tests/PlayMode/AudioClock/AudioClockPlayModeTests.cs:36`. The only functional test change increases this transport fixture's scheduling lead from 0.02 to 0.25 seconds, consistent with the existing scheduled-stop fixture. All transport, pause/resume/replay and song-time assertions remain. Game AudioClock code, BPM, judgement windows and serialized content are unchanged. The previous assertion could read a positive song time after one 21.333 ms DSP advance despite no game-update transition; increasing the test's scheduling margin is consistent with the test's intended pre-deadline assertion. No blocking issue is identified in this limited test adjustment.

The implementation agent reported a prior actual Unity run with compile and EditMode 43 passing but PlayMode 7 passing/1 failing at the old 20 ms assertion. That failure is **not** re-labelled PASS and its evidence is preserved. The fresh final-commit Unity run was pending when this review was first written; its completed evidence has now been independently read as described below. This no-LFS independent clone did not run Unity.

## Final actual Unity and installer-preview evidence independently inspected

The implementation agent executed the following checks. The independent reviewer subsequently read their JSON, both actual NUnit XML files, integrity/native results, and the four final log endings; independently checked the tested clone's Git identity and clean status; and compared its settings file hash with the reviewed clone. These are independently **inspected implementation-run results**, not new Unity executions by this reviewer.

- `UnityFinalRepository` is exactly `8ff1891c62c3943b732f23c38d6c7e6edeb95ac9`, tree `5d04ad4a2a8d75777c761ffebf08a325a0b66abd`, with clean Git status.
- `unity-final-fresh.json`: actual Unity `6000.4.0f1`, `StandaloneWindows64`, `DryRun=false`, compile/import exit 0, EditMode native exit 0, and PlayMode native exit 0. Actual `UnityFinalFresh/EditMode.xml` reports **43/43 PASS**; `PlayMode.xml` reports **8/8 PASS**. Both XML roots have zero failed, skipped, and inconclusive tests. Wrapper diagnostics, protected changes, and post-run Git changes are empty.
- `unity-final-integrity-complete.json` and `UnityFinalFresh/IntegritySummary.json`: component inventory native exit 0, **50 assets, 0 errors**; **1264 metadata files**, with zero missing/orphan/invalid metadata, duplicate GUIDs, Missing Scripts or references. The final four-log diagnostic aggregate is empty. `InventoryNativeResult.json` confirms successful owned-job termination and no remaining descendants.
- `ProjectSettings/ProjectSettings.asset` SHA-256 is `1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902` in both the tested clone and independently reviewed clone. No normalization drift remains.
- `followup-installer-preview.json`: Success=true, DryRun=true, Changed=false, Staged=false, TaskEnabled=false; BundleId `c6f7777a8547213b572710361ce454abee89f40679a93bb9075aa0c9f117daec`, manifest SHA-256 `5e783f999561570f1b1e3833b3bee06928cfd87679703a775a9b144656bff2f3`. This is a non-mutating preview, not an installation PASS.

The evidence paths above are relative to `Logs/HostRollout52-20260921`; the inspection record is `IndependentReview/followup-final-unity-evidence-inspection.json`. The final Host run and targeted retry have since completed and were independently inspected in the addendum below. No additional runtime tests or scope expansion were performed by this reviewer during these evidence updates.

## Final Host aggregate and one-line harness timeout addendum

The implementation agent's full selected run in `followup-final-regression.json` completed with **103 PASS and 1 FAIL**, external mutation count 0. Its failure is preserved: `StaleHeadCausesNoPushStatusOrCommentMutation` ran for 61.797 seconds and produced no JSON output under its 60-second helper limit. This report does not turn that full-run result into a passing run.

The independent reviewer inspected the sole subsequent working-tree code delta against `8ff1891`: `Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1:2770` changes only this end-to-end fixture caller's `-TimeoutSeconds 60` to `120`, within the helper's existing allowed range. Its failure/no-push/no-status/no-comment assertions and fake mutation audit remain unchanged. No production runtime or Unity code changed. This bounded test timeout adjustment introduces no supported finding. The reviewed caller file has SHA-256 `eb8afb53ae3960d3e4bb180dd1356ddc59f51920ab8b906cd3e8cbabe3b9e7b5` and Git blob `d3ca9511b871590f18828115fcbf7798364e4832`; this addendum identifies its bytes separately from the fixed production commit.

The actual retry, `followup-stale-retest.json`, reports **3 PASS, 0 FAIL**: fixture compiler boundary, the stale-head end-to-end case, and fixture isolation. The stale-head case completed in **44.697 seconds**. External and simulated mutation counts are both 0; stderr is empty. The reviewer read this evidence but did not execute this retry.

Aggregating by case name and using the most recent executed result yields **104 distinct cases with latest result PASS, 0 latest failures**. The two repeated supporting cases are not counted as additional distinct tests. This is a full-run-plus-targeted-retry aggregate, **not a single 104-PASS suite execution**. The aggregation, original failure, retry metadata, and caller hash are preserved in `IndependentReview/followup-final-host-aggregate-inspection.json`. No new full suite or runtime test was run by the independent reviewer.

## Final process regression and installation disposition

The first selected process group returned six actual case PASS and one fixture audit FAIL: `FixtureIsolationHasNoLiveIssueMutation` required a fake executable audit invocation that the chosen process-only subset did not generate. It did not report an external mutation. That first result is preserved at `followup-8ff1891-process-regression.json` and is not represented as a passing suite.

The same group plus the live-shaped fake Project pagination case completed: **8 PASS, 0 FAIL, external mutation count 0**, native exit 0 and empty stderr. The group contains script parsing, Project pagination, additive ledger/stdin timeout, all three new pre-assignment fault cases, Unity descendant termination, Codex timeout/cancellation/descendants, unknown-ledger preservation, and the isolation audit. Evidence: `followup-8ff1891-process-and-audit-regression.json`, `followup-8ff1891-process-and-audit-regression.stderr.txt`, and `followup-8ff1891-process-and-audit-regression.exit.txt`.

**No supported Blocker/Major/Minor finding remains at the final reviewed commit.** M1/M2 have independent six-case transport/trust coverage at their unchanged implementation bytes, the real-Git helper adds seven source-context checks, M3 has three separately authored failure injections and final process regression, and the baseline 100-case independent run covers the otherwise unchanged implementation. This is not a claim that 100+6+8 represent distinct tests; overlapping cases are deliberately reported per execution.

The independent-review prerequisite no longer blocks installing the fixed bundle. The final actual Unity/integrity PASS evidence and 104 distinct latest-executed Host PASS aggregate have now been independently inspected. Installation may proceed after the exact reviewed source/bundle preview is verified under the existing rollout procedure. The old merged `528452f` bundle still contains the original three findings and must not be installed as though this result applied to it. Any required merge remains the human Owner's action.

Installation and schedule enablement are separate. Protected installation should first leave the task disabled, followed by actual ACL/task/integrity and linked-token checks, real protected Developer source-write and Reviewer read-only model smoke, and the separate single-Issue pilot. The current CLI help/capability and fake-boundary evidence cannot substitute for those live checks. This reviewer has not installed, run a real model, executed the product pilot, or enabled a schedule; all those rollout checks remain NOT_RUN here.
