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

Additional checkout-role, Unity, integrity, and independent review results
are recorded below after execution. The new protected bundle is NOT_INSTALLED.
The product pilot remains FAILED and the 15-minute task remains Disabled.

