# Independent review: Codex decoded empty text

**Final source verdict: 0 Blocker / 0 Major / 0 Minor at `f604ca965a97cb9f4e5004c716c8d05ee211f58f`.** The demonstrated empty-string accumulator defect is resolved by this candidate. This is approval of the examined source change, not a claim that the installed protected bundle or scheduled automation has passed real-model activation.

## Examined identity and scope

- Base: `9fbf6d804c6a728d626d3f9b7f6b63216271014b` (Owner-merged PR #56).
- Candidate: `f604ca965a97cb9f4e5004c716c8d05ee211f58f`.
- Candidate tree: `1c2b59aaf9583dce5c164f9184d121909d4f54f9`.
- SPEC_VERSION: `1.0.8`; blob `b0f3d96f877256ed9ae03858ecc5185a989b1d1b`.
- Fresh standalone local clone, no shared hardlinks and LFS smudging disabled: `IndependentEmptyTextReview/Repository`. The exact candidate was checked out detached. The clone was clean before and after validation.
- Reviewed all four changed files: version marker, production Codex adapter, functional smoke, and Host fixture suite. Current AGENTS/WORKFLOW/REVIEWER instructions were read and their unchanged content against the base was verified.
- No base-checkout/source edits, model launches, protected installation changes, scheduler changes, GitHub writes, PR merge, Issue transition, or Unity execution were performed by this reviewer.

## Resolved finding

**M1 — normal decoded JSONL text could reject a completed model operation.** In the base adapter, `Tools/HostAutomation/Invoke-SashimiCodexExec.ps1:445` declared mandatory `List[string] TextValues` with `AllowEmptyCollection` but without `AllowEmptyString`. A decoded empty string was correctly appended at line 456, but a subsequent recursive invocation passed the now-populated list through parameter validation again. That validation rejected an empty string element before the remaining event could be inspected.

The original protected-run exception evidence identifies `ParameterBindingValidationException`, `ParameterArgumentValidationErrorEmptyStringNotAllowed,Add-CodexDecodedJsonAuditText`, and the recursive call at line 481, reached from the decoded JSON audit at line 504 and JSONL parsing at line 1086. The parent also observed the expected edit had already occurred. This explains why an otherwise completed source edit could be reported as `CODEX_ADAPTER_INTERNAL_FAILURE` with `Executed=true`, `EventCount=0`, and no promoted result. A standalone Reviewer success does not contradict this content-dependent failure.

The candidate adds only `[AllowEmptyString()]` to the accumulator parameter. It does not filter out empty values, skip recursion, skip decoded-content auditing, change traversal limits, relax command recognition, alter schema validation, or change raw-output controls. Original failure evidence is retained; it is not relabeled PASS.

## Smoke and regression review

- `Test-SashimiCodexFunctionalSmoke.ps1:51–69` now supplies a minimal Host-owned `AGENTS.md` before either role starts. It contains the local exercise rules and states that no additional instruction files exist in this fixture. This removes the contradictory instruction to read a nonexistent required file.
- The instruction hash is recorded before both roles. Lines 131–134 verify it, along with Git metadata, after each executed role. Existing no-reparse, directory, filename, and per-file size checks remain. The maximum of five files at lines 122–125 accounts for Example.txt, AGENTS.md, two Git metadata files, and the fake-only result fixture.
- The compiled fake Codex now emits a realistic MCP result containing an empty text value followed by another field. Thus the native fake smoke reaches the same recursive accumulator pattern missed by the previous two-event fixture.
- `CodexCleanJsonlAndExplicitResultSucceed` includes empty object values and nested array values before subsequent siblings, along with null/empty-array/false/zero values.
- Existing negative fixtures were strengthened: an empty string precedes an unknown command wrapper and a Unicode-escaped forbidden profile path. Their expected rejection codes still require the intended downstream security checks, rather than accepting an incidental internal binding failure.
- No additional demonstrated defect was found in the changed code.

## Independently executed validation

Executed from the exact independent clone with PowerShell 7.6.6:

```text
pwsh.exe -NoProfile -NonInteractive -File Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1
  -RepositoryRoot <independent exact-commit clone>
  -TestNamePattern 'FunctionalSmokePlanAndFake|CodexCleanJsonl|FixtureCompilerBoundary|PowerShell7Parser'
```

Result: **5 PASS / 0 FAIL**, native exit 0, stderr 0 bytes. Start `2026-09-25T07:08:32.1499491Z`; end `2026-09-25T07:08:47.5573807Z`.

| Independently executed case | Result |
| --- | --- |
| FunctionalSmokePlanAndFakeUseTheProductionAdapter | PASS |
| PowerShell7ParserAcceptsEveryScript | PASS |
| FixtureCompilerBoundaryAcceptsOnlyTheOwnedBootstrapPlan | PASS |
| CodexCleanJsonlAndExplicitResultSucceed | PASS |
| FixtureIsolationHasNoLiveIssueMutation | PASS |

The mutation audit exercised its fake executable boundary: FakeInvocationCount 1, SimulatedMutationCount 0, ExternalMutationCount 0. TemporaryRoot is null following harness cleanup. Fake Codex execution is not an actual model run. `git diff --check <base> <candidate>` passed, and the clone remained clean.

Original output is in `focused-regression.json`; process metadata is in `focused-regression.execution.json`; stderr is retained in `focused-regression.stderr.txt`. Commit/file identities are in `examined-commit.json`. The initial metadata shell command misquoted `HEAD^{tree}`; a corrected read-only invocation obtained and verified the tree above. This was not a product/test failure.

## Existing evidence independently read, not executed here

All paths below are relative to `HostActivation56-20260925` and remain unchanged.

- `DeveloperExceptionDiagnostic/exception-metadata.json`: original protected adapter exception metadata described above; no raw model stream was required for the diagnosis.
- `empty-text-before-fix.json`: parent pre-fix regression output, 0 PASS / 3 FAIL, native exit 1, stderr 0. Two failures demonstrate the new functional and clean-JSONL fixtures rejecting the old behavior. The third is the fixture isolation assertion because no fake tool audit invocation was recorded in that narrow failing run. Preserve all three; the JSON itself does not contain a commit identifier.
- `empty-text-security-regressions.json`: parent regression output, **24 PASS / 0 FAIL**, native exit 0, stderr 0, FakeInvocationCount 163, SimulatedMutationCount 0, ExternalMutationCount 0, TemporaryRoot null. Read all case records, including the strengthened command-wrapper and decoded forbidden-profile output cases. This is corroborating parent execution, not 24 tests newly run by this reviewer.
- `real-codex-functional-smoke.json` and `real-codex-functional-smoke-retest.json`: original installed protected smoke failures remain failures. A separate successful Reviewer diagnostic does not convert either full smoke to PASS.

The parent is separately testing the candidate adapter with actual Codex. Any such result must be labeled candidate-source diagnostic and kept distinct from the production installed-bundle smoke. No new real-model result was relied on for this source verdict.

## Hashes and remaining activation gate

- Installer SHA-256 (unchanged): `3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733`.
- Installer Git blob: `fc95c602fbe2fe339f266953e4bfc0bfbbd927c9`.
- Candidate adapter SHA-256: `52ae2fb2bb57a1313f062664244bf368654d7c659045a7519b211c3c6b228538`.
- Candidate smoke SHA-256: `f2f176e728ece32c5591ce40e8656d5f7593eb74d64fc7d36b85dd673fb98822`.
- Independent focused result SHA-256: `87ece446d3f3607eacb00577d657217aa279dc420b1b0b62933b90cad8e140f1`.

The source is suitable to proceed through the remaining repository and Owner gates. The installed bundle still predates this fix. Owner merge, exact reviewed bundle installation while disabled, installed Developer-and-Reviewer real functional smoke, and the approved Issue 20 pilot remain separate requirements before enabling the schedule. The reviewer did not perform or claim those checks, a Unity run, or a full/M2 suite rerun for this focused change.

## Evidence addendum — candidate preview and actual-model diagnostic, 2026-09-25

**Source verdict remains 0 Blocker / 0 Major / 0 Minor at `f604ca965a97cb9f4e5004c716c8d05ee211f58f`.** This addendum only reads completed parent evidence and checks its consistency; it adds no test/model execution or installation by the independent reviewer. The source head, SPEC blob, and adapter/installer bytes remain the identities recorded above. The earlier text describes the evidence available before this addendum and remains preserved.

### Installer preview readback

Read `candidate-installer-preview.json`, `candidate-installer-identity.json`, and the external `PreviewCandidateInstaller.ps1` launcher. The launcher holds a read-only installer handle, compares its hash to the independently retained `3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733`, and retains that handle through child exit. This external pin is separate from the optional installer arguments, which are empty in the DryRun result.

Independently read the existing protected bundle's `HostIntegrity.json`, verified its previously reviewed SHA-256 `062eb039641cc7effc07e7e16860c144e133007297a1094176e60678c8d38436`, and compared all 11 entries with the candidate preview. Membership is identical. Only `Invoke-SashimiCodexExec.ps1` changes, from 80,616 to 80,636 bytes, matching the single added parameter attribute. All nine runtime script lengths and SHA-256 values match the exact independent candidate clone. Generated configuration and executable identity entries are unchanged.

Using the retained installer/source-configuration/distribution identities and lengths plus the candidate file list, independently recomputed both identities:

- BundleId: `d315326a64346cb1d5d68178e0d10942e0851c08d5d169ff6f99d0b68e4d8617`.
- Manifest SHA-256: `d3365ccaf36f0b6e6ac7941b69919269553595d78e8de3331197ec5a98a666dd`.
- Codex distribution identity remains `965aa84ab38d77aab72bdb43d10b865ba70a300941db440d2584dd5e7e8bf6f2`.

Both computed values exactly match the two parent JSON files. Preview reports Success=true, DryRun=true, SourceHashesVerified=true, and Changed/Staged/TaskEnabled=false. This establishes the candidate plan; DryRun does not establish newly installed ACL/hash verification or actual activation.

### Actual-model candidate diagnostic readback

Read `TestCandidateAdapter.ps1`, `CandidateAdapterDiagnostic/summary.json`, each role's adapter result and native exit, promoted result, process summary, and all metadata event records. The helper uses the candidate source adapter and source MCP server with the existing protected Codex distribution and installed configuration. It gives the model an isolated four-file exercise, explicitly asks it to read AGENTS.md, and uses a 180-second adapter timeout. It is not the installed-bundle functional smoke harness.

| Parent actual-model role | Adapter exit | Codex exit | Events | Completed MCP calls | stderr bytes |
| --- | --- | --- | --- | --- | --- |
| Developer | 0 | 0 | 11 | 3 | 0 |
| Reviewer | 0 | 0 | 9 | 2 | 0 |

Both adapters report Live/Executed=true, Success=true, no error, and a promoted Succeeded result identical to `adapter.Result`. Both process summaries show TimedOut=false and Cancelled=false. Both event sequences end in turn.completed; neither contains command-execution metadata. Developer reports exactly Example.txt changed. Reviewer reports changedFiles=[] and `SMOKE_REVIEW_VALUE_2`.

Independently inspected the retained workspace: exactly Example.txt, AGENTS.md, .git/HEAD, and .git/config; no reparse entries. Example.txt is exactly `value=2` followed by LF. Instructions match the reviewed candidate smoke text; Git metadata matches the helper's initial bytes. Both Processes and ProcessIds in the retained owned-process ledger are empty. The helper checks the immutable-file hashes after each role; the final retained bytes corroborate those checks. These observations agree with DeveloperEdit, ReviewerReadOnly, ImmutableFilesUnchanged, ClosedWorkspace, and OwnedProcessLedgerEmpty=true in the parent summary.

Machine-readable independent readback is `candidate-evidence-readback.json`; it also records the original pre-addendum report SHA-256 `100455dbe4f9b68cb6e79a8e3843921c7a63691db9b7fc71b3da2b568a3afb92`. New evidence hashes are retained separately in `candidate-evidence-sha256.json`, leaving the earlier evidence hash record intact.

The candidate-source actual-model diagnostic succeeded. The new protected bundle has not been installed or smoke-tested by this reviewer, and the prior installed-bundle FAIL results remain FAIL. Candidate installation, installed functional smoke, approved Issue 20 pilot, and schedule activation retain their separate gates. The subsequently supplied parent Unity evidence is evaluated below; no Unity rerun occurred.

### Parent Unity evidence readback

Read `unity-candidate.json`, `unity-integrity.json`, `unity-clone-materialization.json`, native exit/stderr files, the original EditMode/PlayMode XML, inventory/meta/native-process JSON, and relevant log records. This is independent evidence inspection of the parent's completed run, not Unity execution by this reviewer.

- Clean import/compile: native exit 0 and Succeeded=true. Unity is `6000.4.0f1`, target `StandaloneWindows64`.
- XML: EditMode **43/43 PASS** and PlayMode **8/8 PASS**; all failed, skipped, and inconclusive counts are zero. Counted the individual XML test cases independently.
- Parsed the original `SASHIMI_COMPONENT_INVENTORY` record in ComponentInventory.log: 50 assets, passed=true, errors=[], zero missing scripts/references and no asset with duplicate active AudioListener/EventSystem.
- Meta evidence: 1,264 meta files, all missing/orphan/invalid meta, duplicate GUID, missing-script and missing-reference counts zero. The 11 explicit repository `.gitkeep` placeholders are separately identified in the report.
- Both outer runs exited 0 with empty stderr. Inventory process evidence confirms job assignment and termination; its owned-process ledger is empty. The validation wrappers report Diagnostics=[] and GitStatusAfter=[]. Literal licensing/log messages and expected error-producing tests are not represented as an assertion that the raw logs contain no occurrences of the word "error".
- Independently reread the Unity clone head as `f604ca965a97cb9f4e5004c716c8d05ee211f58f` and verified its working tree is clean. Current ProjectSettings SHA-256 is `1613f4e7d2a0d1dd791836e4791a12ce1805bade48756541ee9ce802d56fe902`, matching the parent's retained value.
- The materialization receipt records 38 LFS assets with their SHA-256 and sizes and the same candidate commit. The reviewer read that receipt and did not rerun materialization or rehash all 38 payloads.

Structured readback is `unity-evidence-readback.json`. These results corroborate the parent's candidate validation without changing the remaining installed-bundle activation gates or the exact source verdict above.
