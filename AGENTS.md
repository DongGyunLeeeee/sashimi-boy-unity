# SASHIMI BOY Agent Operating Rules

## Project

- Repository: `DongGyunLeeeee/sashimi-boy-unity`
- GitHub Project: owner `DongGyunLeeeee`, number `1`
- Unity: `6000.4.0f1`
- Windows repository: `C:\Dev\sashimi-boy-unity`
- Unity executable:
  `C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe`

## Mission

Develop SASHIMI BOY through issue-driven, test-backed, human-supervised delivery.

GitHub Issue and Project state are the persistent source of truth.
Automation memory is optional and must never be a build or workflow gate.

## Source-of-truth order

1. GitHub Issue acceptance criteria and comments
2. This `AGENTS.md`
3. Linked PR review findings
4. Repository architecture and QA documentation
5. Existing production code and serialized Unity data
6. Product sketches and references

Do not silently invent missing product, story, UX, balance, or asset decisions.

## Project fields

The GitHub Project must contain these exact fields and options.

- `Status`
  - Backlog
  - Ready
  - In Progress
  - Review
  - Verification
  - Done
- `Priority`
  - P0
  - P1
  - P2
  - P3
- `Area`
- `Size`

Do not guess misspelled field names. Report a setup blocker instead.

## Human and agent responsibilities

Human owner:

- Backlog → Ready
- Final Unity visual/audio/input/save verification
- PR merge
- Final product and UX decisions

Developer agent:

- Ready/In Progress → implementation
- tests
- Draft PR
- Review

Reviewer agent:

- independent review and retest
- Review → In Progress when Blocker/Major exists
- Review → Verification when automated verification passes

No agent may merge a PR or move an Issue to Done.

## Git rules

- Never work directly on `main`.
- One Issue per branch and PR.
- Use a fresh isolated worktree or temporary clone.
- Never modify the user's base checkout.
- Never use `git reset --hard`, `git clean`, destructive checkout, or force push.
- Never discard or overwrite user changes.
- Merge latest `origin/main` into an existing PR branch when integration is required.
- Preserve Unity `.meta` files and GUIDs.
- Do not commit `Library`, `Temp`, `Logs`, `UserSettings`, generated IDE files, or local automation memory.
- Do not begin a second Issue in the same automation run.

## Unity rules

- Preserve serialized references.
- Do not unnecessarily reserialize whole Scenes or Prefabs.
- Generated assets must be changed through their authoritative generator.
- Do not modify `Art/Source` unless the Issue explicitly requires it.
- Do not modify source FBX, texture, audio, BPM, beatmap timing, or judgement windows without an approved Issue.
- Do not use negative scale as an orientation fix.
- Missing Script, broken reference, duplicate AudioListener, duplicate EventSystem, and new Console errors are merge blockers.
- The same Unity project directory must not be opened by two Editor processes at once.
- Automated tests must run in the isolated worktree/clone, not in the user's base checkout.

## Testing rules

For every implementation:

- clean import/compile
- relevant EditMode tests
- relevant PlayMode tests
- `git diff --check`
- `.meta`/GUID integrity
- Missing Script/reference scan
- Console error scan
- Issue-specific regression tests

Never claim a test passed unless it was actually executed and its result was inspected.

Automated tests do not replace human verification of:

- visual composition
- camera feel
- music sync
- rhythm readability
- interaction feel
- story pacing
- final save/persistence flow when the test intentionally disables disk writes

## State machine

```text
Backlog --human--> Ready

Ready --developer--> In Progress
In Progress --developer--> Review

Review --reviewer, Blocker/Major--> In Progress
Review --reviewer, automated PASS--> Verification

Verification --human PASS + merge--> Done
Verification --human FAIL--> In Progress
```

## Definition of Ready

- acceptance criteria are testable
- scope and exclusions are explicit
- blockers are resolved
- required source assets/data exist
- no conflicting open PR edits the same Scene/Prefab
- human product decisions are complete

## Definition of Review

- focused implementation committed and pushed
- Draft PR exists and links the Issue
- compile and applicable automated tests pass
- PR body contains root cause, changes, tests, and manual verification checklist

## Definition of Verification

- independent reviewer found no Blocker or Major
- latest-main synthetic merge compiles
- required automated tests pass
- exact human verification checklist and evidence requirements are posted

## Definition of Done

- human verification passes
- evidence is attached
- PR is merged by the human owner
- linked Issue is closed
- Project status is Done

## Approval and infrastructure failures

External writes and local commands may be approval-gated.

If an automation cannot obtain approval or its local runner fails:

- do not claim that a command ran
- do not fabricate a test result
- do not change Issue or PR state based on an incomplete run
- report the exact failed command, exit code, and stderr
- provide the exact pending command for the owner
- stop

Failure to update automation memory is never a reason to block development or review.
