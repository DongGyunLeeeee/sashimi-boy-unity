# Source access and long-path repair — 2026-09-26

## Observed production failure

Merged PR #58 installed as bundle
5be6e12feea162d40635808c7fdd3e3faf06aafacb46fb3ef5b947b0c47a15e3.
Its real functional smoke passed both Developer editing and Reviewer read-only
access. The separate Issue #20 Developer pilot stopped before Unity at run
20260925T203801Z-cfb7cf55aad1423982806b5e081f5ee6.

All 44 LFS objects were restored locally (44 hits, zero misses/downloads).
Codex itself exited 0 with empty stderr and no native timeout/cancellation,
but returned outcome Blocked: required document/listing requests could not
complete. The adapter and outer runner exited 1 with
CODEX_RESULT_OUTCOME_NOT_SUCCEEDED. Unity and Reviewer were NOT_RUN; no delivery
push or Project transition occurred. Unsealed failure artifacts were retained
privately under State/.unpublished-artifacts-* rather than published.

## Causes and correction

- list_files previously enumerated the entire repository for every prefix,
  including duplicate full ancestor checks per text file. It now prunes only
  subtrees unable to match the literal prefix, validates lexical names, and
  holds verified directory handles while descending. Read/write controls are
  unchanged; returned paths remain case-insensitive-prefix matched, sorted,
  paginated, and fresh on later requests.
- The isolated Git environment intentionally disables global configuration,
  including the user's core.longpaths=true. In the pilot clone, 533 tracked
  files were absent; all their absolute paths exceeded 259 characters.
  The fixed Host configuration now explicitly enables core.longpaths.
- Git for Windows can report Filename too long yet return exit 0 from switch.
  Developer now rejects any missing tracked path before LFS/model processing.

No product code, assets, ProjectSettings, acceptance criteria, approval gates,
protected installed bundle, or scheduling state is modified by this repair.

## Executed evidence

- Same five source requests against the real repository: baseline exceeded
  30 seconds with only the first response; correction completed all five in
  1.842 seconds with empty stderr. This diagnostic deadline is separate from
  the production Codex process, which did not time out.
- Native offline Git, 349-character asset path: baseline switch returned 0
  but omitted the file and reported Filename too long; corrected switch
  returned 0, materialized the exact hash, and left a clean checkout.
- Focused source/Git fixture run: 4 PASS, 0 FAIL, zero external mutations.
  Coverage includes unchanged read/write restrictions, GUID protection,
  hard-link/junction rejection, literal prefix pages, unrelated-tree pruning,
  and a fresh list after writing a new source file.

## Final validation and rollout

- Runtime/test commit: 4684706e3fe36b8f5b73f310803b1ebdfd85450c.
- Additional checkout/role regressions: 5 PASS, 0 FAIL. This includes the new
  partial-checkout refusal, full validation-only resume, canonical LFS routing,
  parser, and fixture isolation. Together the two parent fixture groups have
  eight distinct passing cases and zero external mutations.
- Independent review: 0 Blocker / 0 Major / 0 Minor. Seven distinct independent
  cases pass. An initial sandbox-token run failed two source requests and is
  retained as FAIL; the identical source passed under the actual user token.
- Unity 6000.4.0f1: fresh import/compile PASS, EditMode 43/43 and PlayMode 8/8,
  zero skips. Native exits are 0; diagnostics, failures, protected changes and
  final workspace status are empty.
- Integrity: 1,264 meta files, zero missing/orphan/invalid meta, duplicate GUID,
  Missing Script or missing reference; 38 LFS working assets match their OIDs.
- Installer DryRun: PASS, no staging/task change. Config and Codex distribution
  are unchanged. Only the three intended runtime files differ from PR #58.
  Candidate bundle: e15ca8ebcd18f82be430dca7a7958871ebad919314a225ff4c93cdb6d5c80596.
  Manifest: 11768a649f3ccfd43c16a341c48fac0063efdc66347dc3d01ab82fd57b997a9e.
  Installer: 3bb3b82b2210ee5558d8995b8a35d361f57d760b3490d073c6e6ac629e19c733.

The new protected bundle is NOT_INSTALLED. Actual model execution and a new
product pilot for this correction are NOT_RUN. The PR #58 product pilot remains
FAILED, Issue #20 remains In Progress, and the 15-minute task remains Disabled.
After Owner merge, install/read back the exact reviewed bundle while disabled,
run the installed real functional smoke, then separate Issue #20 Developer and
Reviewer runs through Verification. Only then enable the reviewed schedule.
