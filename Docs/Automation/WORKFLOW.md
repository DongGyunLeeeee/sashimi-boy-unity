# SASHIMI BOY Automation Workflow

This directory is the version-controlled execution specification for Codex
Developer and Reviewer automation. The current version is the single line in
[`SPEC_VERSION`](SPEC_VERSION). Do not copy that value into another file.

At the start of every run, report both values below:

- the contents of `Docs/Automation/SPEC_VERSION`
- `git rev-parse HEAD:Docs/Automation/SPEC_VERSION`, the Git blob SHA for the
  version file used by the run

## Source of truth

Apply instructions in this order:

1. the target GitHub Issue's latest Owner Decision and acceptance criteria
2. [`AGENTS.md`](../../AGENTS.md)
3. the current specification in `Docs/Automation/`
4. the linked PR's latest review findings
5. older PR comments and older reviews
6. Automation memory and previous chat summaries

Ignore an older comment, memory entry, or chat summary when it conflicts with a
higher-priority source. Failure to read or update Automation memory is not a
development or review blocker.

## Separation of responsibilities

```text
Automation UI
-> schedules a run and selects its Project

Repository documentation
-> defines roles, policy, and the state machine

PowerShell tools
-> deterministically execute Git, GitHub Project, and Unity operations

GitHub Project
-> stores the live work queue and current state
```

The Automation UI rule is only a bootstrap. It must not duplicate this
specification.

## Canonical state machine

```text
Backlog --Owner--> Ready

Ready --Developer--> In Progress
In Progress --Developer--> Review

Review --Reviewer, Blocker/Major--> In Progress
Review --Reviewer, automated PASS--> Verification

Verification --Owner FAIL--> In Progress
Verification --Owner PASS + Merge--> Done
```

These transitions are exhaustive:

- A Developer may perform only `Ready -> In Progress` and
  `In Progress -> Review`.
- A Reviewer may perform only `Review -> In Progress` and
  `Review -> Verification`.
- Owner transitions are reserved for the human owner. An agent must never
  merge a PR or move an Issue to `Done`.
- Pending Manual Verification does not block `Review -> Verification` after
  automated verification passes.
- A Minor finding does not block `Review -> Verification` unless the finding
  is explicitly designated as a merge gate.
- One run handles exactly one Issue. Do not start a second Issue in the same
  run.

## Project contract

GitHub Project `DongGyunLeeeee` number `1` must already contain these exact
fields:

- `Status`: `Backlog`, `Ready`, `In Progress`, `Review`, `Verification`, `Done`
- `Priority`: `P0`, `P1`, `P2`, `P3`
- `Area`
- `Size`

Tools must resolve existing fields and options by their exact names. They must
not create, rename, or guess a field, option, status, or label.

## Safety and evidence

- Work only in the dedicated linked worktree selected for the role. The user's
  primary checkout and the other role's worktree are read-only.
- Never work directly on `main`, force push, delete a remote branch, merge a
  PR, or use destructive checkout, `git reset --hard`, or `git clean`.
- Never use `git read-tree -u <tree>` for review integration. Use a fresh
  temporary clone and a normal `git merge --no-ff --no-edit`.
- A temporary workspace may be removed only through the repository cleanup
  helper after its canonical temp-root boundary, ownership marker, run ID, and
  complete no-reparse-point tree have been validated.
- Do not change production code, serialized Unity content, or source assets
  unless the selected Issue explicitly requires it.
- Never report a command or test as passed unless it ran and its output was
  inspected.
- If a required command, approval, or runner fails, do not advance state.
  Report the exact command, exit code, standard error, and pending owner action.

## Repository tools

Use the parameterized scripts in `Tools/Automation/` for operations they cover;
do not reconstruct equivalent ad hoc Git, Project, or Unity commands in an
Automation rule.

- `Invoke-AutomationPreflight.ps1` validates the role, worktrees, repository,
  Project schema, Unity, Git LFS, disk space, and Unity lock/process state. It
  reports a structured result and exits non-zero on any failed check.
- `New-ReviewIntegration.ps1` resolves the exact PR head and latest main head,
  creates a fresh temporary clone, performs a normal synthetic merge, and
  returns the integration path and SHAs. It never pushes.
- `Remove-ReviewIntegration.ps1` validates and removes only a preserved,
  marker-owned review integration below `%TEMP%\SashimiBoyAutomation`. Use
  `-WhatIf` first; it refuses a missing/mismatched marker or any reparse point.
- `Invoke-UnityTests.ps1` runs clean import/compile, all EditMode tests, and all
  PlayMode tests, writes logs and XML only under the requested artifact path,
  summarizes counts, and propagates failure.
- `Set-GitHubProjectStatus.ps1` validates an existing Project item and an
  allowed role transition before editing the existing `Status` field. It
  supports `-WhatIf` and never creates schema.

Every mutation-capable command must support `-WhatIf` or `-DryRun` as
appropriate. Every temporary resource must be cleaned in `finally`, except
when a documented investigation option explicitly preserves it.

## Role specifications

- Developer: [`DEVELOPER.md`](DEVELOPER.md)
- Reviewer: [`REVIEWER.md`](REVIEWER.md)
- Automation UI bootstrap text: [`BOOTSTRAP.md`](BOOTSTRAP.md)
