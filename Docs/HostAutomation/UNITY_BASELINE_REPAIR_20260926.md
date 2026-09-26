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

Real Unity path reproduction, full main Unity checks, independent final review
and installer preview are pending. This source is not installed. A source-level
test does not establish that the actual Issue #20 pilot has passed.

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
