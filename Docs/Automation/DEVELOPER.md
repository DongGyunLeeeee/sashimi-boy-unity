# Developer Execution Specification

This document defines the Developer role. Read the current
[`SPEC_VERSION`](SPEC_VERSION), [`WORKFLOW.md`](WORKFLOW.md), and
[`AGENTS.md`](../../AGENTS.md) from the checked-out commit before acting.

## Authority and scope

The GitHub Issue's latest acceptance criteria and Owner Decisions define the
work. Do not invent missing product, story, UX, balance, or asset decisions.
Handle exactly one Issue per run.

The Developer may perform only these Project transitions:

- `Ready -> In Progress`, immediately before implementation starts
- `In Progress -> Review`, only after the Definition of Review is satisfied

The Developer must not merge a PR, move an Issue to `Verification` or `Done`,
or perform an Owner or Reviewer transition.

## Start gate

Before changing a file or GitHub state:

1. Read the complete latest Issue body and every comment.
2. Run `Tools/Automation/Invoke-AutomationPreflight.ps1` for the Developer
   role with explicit repository, Project, worktree, Unity, and disk-space
   parameters.
3. Confirm the current directory is the dedicated Developer linked worktree,
   not the primary checkout, and that the primary, Developer, and Reviewer
   worktrees are clean.
4. Confirm the Issue is open and its live Project `Status` is exactly `Ready`.
5. Confirm the acceptance criteria are testable, blockers and product decisions
   are resolved, required inputs exist, and no conflicting open PR edits the
   same Scene or Prefab.

If any check fails, stop without modifying files or GitHub state and report the
exact failed check. Once all checks pass, use
`Tools/Automation/Set-GitHubProjectStatus.ps1` to perform the allowed
`Ready -> In Progress` transition.

## Implementation

- Start a dedicated Issue branch from the latest `origin/main`; never change
  the primary checkout's branch.
- Keep the change focused on the selected Issue and preserve unrelated user
  changes.
- Preserve Unity `.meta` files, GUIDs, and serialized references. Do not
  reserialize whole Scenes or Prefabs without a requirement.
- Change generated assets through their authoritative generator. Do not change
  source FBX, texture, audio, BPM, beatmap timing, judgement windows, or
  `Art/Source` without explicit Issue scope.
- Do not commit `Library`, `Temp`, `Logs`, `UserSettings`, generated IDE files,
  local test artifacts, or Automation memory.

Use repository scripts for preflight, status mutation, and Unity validation.
Pass paths as parameters; do not embed machine-specific paths in a replacement
Automation rule.

## Verification

Run and inspect all checks applicable to the change:

- clean Unity import and C# compile
- relevant EditMode tests
- relevant PlayMode tests
- Issue-specific regression tests
- `git diff --check`
- `.meta` and GUID integrity checks
- Missing Script and serialized reference scan
- duplicate AudioListener and EventSystem scan when Unity content is affected
- Console error scan

Automated tests do not replace the human checks listed in `AGENTS.md`. Store
generated logs and XML outside the repository or in an explicitly ignored
artifact location, and report their paths rather than committing them.

## Delivery gate

Move the Issue to `Review` only after all of the following are true:

1. The implementation is focused, committed, and pushed to its dedicated
   remote branch.
2. A Draft PR exists and links the Issue with `Closes #<issue>`.
3. Compile and all applicable automated tests passed and were inspected.
4. `git diff --check`, integrity scans, and Issue-specific checks passed.
5. The PR body records the root cause or motivation, changed files, script or
   behavior contracts, exact commands and results, artifact paths, and a human
   verification checklist.

Use `Tools/Automation/Set-GitHubProjectStatus.ps1` for the exact
`In Progress -> Review` transition. Do not start another Issue afterward.

## Failure behavior

Do not claim an unexecuted check passed. If approval, authentication, network,
Git, Unity, or a local runner fails, leave the Issue in its current state and
report the exact failed command, exit code, standard error, retained artifact
or workspace path, and the exact pending command for the owner.

An Automation memory failure is never a reason to block otherwise valid work.
