# Unity generator baseline repair

## Observed failure

PR #59 was installed as reviewed bundle `e15ca8ebcd18f82be430dca7a7958871ebad919314a225ff4c93cdb6d5c80596`.
All 16 live task checks and the real installed Developer/Reviewer functional
smoke passed. The task remained Disabled.

The separate Issue #20 delivery pilot
`20260926T073858Z-b04d4e0a2e7d40aea5af01a9e7658ebc` restored all 44 LFS objects
from local cache and completed its real Codex assessment without source edits.
Unity import/compile and GeneratorRun1 passed. GeneratorRun2 exited 1 with nine
managed exceptions in the nested independent baseline. EditMode and PlayMode
were not executed. The product PR head and In Progress state did not change.

The original baseline added 58 characters to the primary project's path.
Observed failed Mono reads had absolute paths of 266, 267, 279, 280 and 298
characters. The corresponding primary files were readable; the same files at
the proposed layout have paths of 207, 208, 220, 221 and 239 characters.
After failure, partial cleanup removed some baseline files; their later
absence is not evidence that the baseline input copy was incomplete.

Cleanup also encountered a copied read-only Git pack index. The source and
copy had identical bytes and ReadOnly/Archive attributes; File.Copy preserved
ReadOnly, while the strict deletion helper used File.Delete.

## Change

- Keep the fixed production RunRoot and use its run-owned `State/g/r` for the
  independent project. It is one character shorter than the primary project.
- Refuse an existing parent. Bind cleanup to the exact parent/project shape,
  a random ownership nonce and confirmed child-process termination.
- Clear only ReadOnly on each newly created independent file copy before
  Unity starts. Source bytes/attributes, ACLs and the shared deletion helper
  remain unchanged.
- Preserve the complete source comparison, separate generation runs, declared
  output/delta checks and strict whitespace/Console/reference requirements.

## Verification and rollout

Focused parent fixtures: six PASS, zero external mutations, temporary roots
cleaned. Coverage includes input independence, readonly pack copies and source
attribute preservation, refusal of existing destinations, wrong marker/path,
unconfirmed termination, and junction-target preservation. The first test
selection's isolation audit was not exercised and failed; its receipt remains
separate. Adding the existing fake executable boundary case exercised that
audit and produced the six-case passing result.

Independent review of runtime commit `a827dab89e4bfa949956954a3090f119b13cc0b3`
found zero Blocker, Major or Minor findings. Seven independently executed
focused Host cases passed, including the parser/compiler/environment boundaries,
input/delta checks, short baseline cleanup, stale ownership and junction target
preservation. Native exit was zero, stderr was empty, no external mutation
occurred, and the fixture temporary root was cleaned.

An independent minimal Unity 6000.4.0f1 project reproduced the file I/O failure:
all five old-length paths existed but Mono OpenRead threw
DirectoryNotFoundException (native exit 1). All five corresponding short paths
were readable and matched their expected SHA-256 (native exit 0). This is a
positive/negative path probe, not a successful product generator run.

The isolated main-based candidate passed clean import/compile, EditMode 43/43,
and PlayMode 8/8 with native exit zero, no skipped tests, empty diagnostics and
no tracked workspace changes. Actual NUnit XML was inspected. Production
integrity scans passed: 1,264 meta files, zero missing/orphan/invalid meta,
duplicate GUIDs, Missing Scripts or missing references; all 38 main LFS assets
matched their required bytes. `git diff --check` passed.

Read-only installer preview passed for bundle
`8e979f9fae67d7e2a11f6fbc903e8ba4ba703e9b83b4dd0b863a1a1164428552`,
manifest `811b51488da4847b3d13a3371f9b011b650fb461e14cc0297f60aa921e3a3fc2`.
Only the UnityValidation payload differs from the installed bundle; config,
installer and Codex distribution identities are unchanged. Preview did not
stage files, modify the task or enable scheduling. This source remains
NOT_INSTALLED and the original Issue #20 pilot remains FAILED.

Local receipts are retained under `Logs/HostUnityPathRepair52-20260926` outside
this checkout: `UnityVerification`, `integrity-scans.json`,
`installer-preview.json`, and `IndependentRegression`. The complete old pilot
diagnosis is retained separately; failed receipts were not overwritten.

## Separate Issue #20 finding

The successful first generator also added one trailing space to seven empty
YAML fields in each of the three SalmonAssembly preview `.png.meta` files.
The whitespace gate correctly rejected these 21 lines. The fix belongs to the
authoritative Issue #20 preview generator on PR #47, after its final import,
with exact GUID and non-whitespace content preservation. This infrastructure
change neither edits that generator nor exempts generated metadata from checks.

After Owner merge, install and read back the new bundle while Disabled, run
the real installed functional smoke, and execute separate #20 Developer and
Reviewer runs through Verification. Address the recorded generator finding in
that Developer run. Enable scheduling only after all required gates pass.
