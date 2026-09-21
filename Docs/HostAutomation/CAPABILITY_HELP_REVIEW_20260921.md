# Issue #52 capability-help independent review

**Examined commit `19bb295b843888caa640060bb34e7698b3ea9794`: Blocker 0, Major 0, Minor 0. The demonstrated capability-help false positive is resolved in these reviewed bytes.**

This is a focused independent review of the seven changed files against Owner-merged main `c4d3fa83374da2ccaaa38d2a755d394598186604`. It supplements the earlier PR #53/#54 review; it does not overwrite those findings, failure records, or reports. No implementation file, protected installation, schedule, Issue, or PR was changed by this reviewer.

## Examined identity and scope

- Commit: `19bb295b843888caa640060bb34e7698b3ea9794`; tree: `f6df21d329bdb2df9699ac87316d2cd5611dbd2b`.
- SPEC_VERSION: `1.0.6`; blob: `af0b7ddbffd5d5a877581ba8ba10deed158da14d`.
- Adapter SHA-256: `534e5b13b27381cb513956da9a3604edbaa7b5f026cf1ccebe6e673398852dd8`; Git blob: `0551f735b1b3058ed15c998eed700b83e072e94b`.
- Installer unchanged: SHA-256 `cff0acc1587d6ac75897c688847a7e672e64f289e3411bad7e4cbc46966dc973`; Git blob `e956466f18368f85cd0015c9477d5abd19011c41`.
- Reviewed in the independent standalone no-LFS clone, fetched from the implementation clone and checked out detached at the exact commit. Working tree is clean; `git diff --check c4d3fa8 HEAD` has no diagnostics.
- Current AGENTS.md, WORKFLOW.md, and REVIEWER.md are byte-identical to the applicable specifications already read in this review chain. The new version, Security/Operations changes, complete adapter delta, fixture helper, raw help specimen and harness changes were inspected.

The only production runtime change is in `Invoke-SashimiCodexExec.ps1`. Game/Unity content, process ownership/ACL/lease machinery, installer implementation, and source MCP are unchanged by this patch.

## Confirmed original failure and resolution

The original production adapter rejected the installed Codex CLI's successful secure exec-help output before model execution because its public documentation contains one literal `~/.codex/config.toml`. The general model-output path recognizer classified that literal as forbidden content.

The independent reviewer read `../adapter-pre-model-reproduction.json`: DataSource=Live, Success=false, Executed=false, `CODEX_ORIGINAL_OUTPUT_FORBIDDEN_CONTENT`, combined output 3,958 UTF-8 bytes, SHA-256 `dcea2abef5e33f41aa334ad3e1c6d2d39723a9649a213645d728dc91d84d2799`. `../real-codex-functional-smoke.json` remains FAIL for that original attempt; it is not relabelled PASS.

The actual untrimmed stdout specimen is 3,957 UTF-8 bytes, including its final LF, SHA-256 `e504bac5a6364566fbe408132dec7993639def9258ece34e8352f51f8d43687c`. The previously saved 3,956-byte presented output is trimmed and has a different hash. The patch uses the untrimmed identity, which the independent actual-boundary execution also accepted.

The correction is suitably narrow:

- Only the internal Host capability caller sets `CapabilityHelpProbe`. The adapter requires exact argument count/order/case, no stdin parameter or stdin requirement, exit 0, no timeout/cancellation, exactly empty stderr, and the reviewed complete stdout hash. Existing executable identity/lease, hermetic environment, repository policy, owned-job and termination checks still precede acceptance.
- `Assert-CodexOriginalProcessOutputSafe` checks raw quotas and NUL first. Actual profile spellings and inherited exact secret values use the original text. Only the fixed documentation literal is substituted for recognizable-path classification; returned text and output bytes are unchanged.
- Model output, decoded JSONL audit, version/other commands, failed probes and unmatched help retain the existing strict audit. The reviewed-help switch is not exposed as an adapter script parameter. New unrecognized text is never automatically learned or normalized.
- No supported bypass or acceptance-criteria violation was identified. A CLI update whose changed help still triggers the protected-path audit requires another explicit specimen review; the operational documentation explains this maintenance limit.

## Independently executed verification

`capability-19bb295-focused.json`: **5 wrapper cases PASS, 0 FAIL, external mutation count 0**, native exit 0, empty stderr. The cases are script parsing, fixture compiler boundary, exact-help/context regression, native adapter contamination regression, and fixture isolation.

The context group executes an accepted baseline plus 16 rejected variants covering model context, stdin presence, additional/case-changed arguments, nonzero exit, timeout, cancellation, stderr, appended/changed/trimmed bytes, credential/profile paths, and inherited secrets both inside the documentation literal and elsewhere. The seven native fake-Codex cases confirm the actual adapter accepts reviewed help, refuses altered help before the fake model stage, and still rejects identical help bytes emitted from the fake model stage without promoting result/events. These are offline fake-model tests, not real model execution.

`Test-IndependentProtectedHelpBoundary.ps1` additionally extracted the exact production capability-call graph from the reviewed adapter and invoked **only the real secure `--help` command** on the installed protected Codex executable. The adapter entrypoint/model dispatch was not loaded. The test used the installed protected configuration and ordinary production Common functions, with no mocks or bypass of identity/lease/environment/process checks.

`capability-19bb295-protected-help.json`: **Success=true**, protected binary SHA-256 `e86ffd96751ded51f669b520d70ba3139b514eb36313a8eeeedde37baa7b58e3`, required capability flags confirmed, and owned PID ledger empty after termination. ModelExecuted=false, InstallationChanged=false, ScheduleChanged=false. This proves the corrected candidate capability boundary works with the actual installed executable; it does not prove the later model session or the currently installed old adapter has been fixed.

## Implementation-run evidence independently inspected

The reviewer read `../help-fix-codex-regression.json`: **24 PASS, 0 FAIL**, 163 fake executable invocations, external and simulated mutation counts 0. This covers the new cases and existing command, JSONL/result, profile/credential, artifact promotion, environment, repository-config, read-only Reviewer, protected-executable, timeout/cancellation, and fake functional-smoke boundaries. These 24 cases were executed by the implementation agent, not by this reviewer, and overlap the five independent wrapper cases.

Earlier focused failures remain preserved: `../help-fix-focused.json` was 3 PASS/1 FAIL because the fake result's head did not match its pin; `../help-fix-focused-v2.json` was 3 PASS/1 FAIL because the test equated `Executed=true` with a fake model call even though raw-output rejection throws before that assignment. The fixed regression uses the native invocation audit to distinguish the one-call pre-model refusals from the two-call model-output refusal and still requires no accepted execution/artifact promotion. Neither earlier run is represented as PASS.

The implementation agent's fresh Unity validation at this commit is still in progress when this report is written. No new Unity run was performed in the independent no-LFS clone. The patch changes no game or Unity files; pending implementation validation is not an additional code finding.

## Disposition and remaining rollout

The independent zero-Blocker/Major review gate is satisfied for the exact reviewed commit. Complete the remaining required implementation validation, then follow the existing Owner merge and protected bundle preview/update procedure. Do not treat the prior installed bundle's failed functional smoke as resolved merely because the candidate helper passed.

Actual protected Developer source-write model smoke, Reviewer read-only model smoke, the one-Issue #20 pilot, and schedule enablement remain **NOT_RUN** for this correction. Installation/update must retain the disabled task until the prescribed real checks pass. This reviewer ran no model, product pilot or scheduler operation and made no protected-bundle change.

This report and copies of its independent evidence are under `Logs/HostActivation52-20260921/IndependentReview`. The independent clone and original evidence copies remain under `Logs/HostRollout52-20260921/IndependentReview`; those earlier reports are untouched.
