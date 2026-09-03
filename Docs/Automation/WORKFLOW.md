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

1. the current Issue's latest Owner Decision
2. the current Issue's latest body and Acceptance Criteria
3. repository-wide safety rules in [`AGENTS.md`](../../AGENTS.md)
4. the role-specific execution specification in `Docs/Automation/**` from the
   current commit
5. the latest independent Review finding on the linked PR
6. older Issue/PR comments and older Reviews
7. previous chat summaries and Automation memory

Only the latest Owner Decision and the current Acceptance Criteria are
authoritative comments. Older or non-Owner general comments, previous chat
summaries, and Automation memory yield whenever they conflict with a
higher-priority source. Failure to read or update Automation memory is not a
development or review blocker.

These safety prohibitions are non-relaxable by any Agent, comment, review, chat
summary, or memory:

- Never destroy the user's checkout.
- Never use `git reset --hard`, `git clean`, or force push.
- An Agent must never merge a PR or move an Issue to `Done`.
- One run handles exactly one Issue.
- Never report an unexecuted test as PASS.

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

## Developer selection and handoff

Developer work has three modes. Resuming an existing item is not a new state
transition or an `In Progress` self-transition; it resumes work toward the
existing `In Progress -> Review` transition.

- `ReviewFix`: an open `In Progress` Issue with exactly one linked, live, open
  Draft PR and a current Reviewer Blocker/Major or Owner manual-verification
  FAIL handoff.
- `DeliveryResume`: an open `In Progress` Issue with exactly one linked, live,
  open Draft PR and a current Developer handoff for an approved transient
  required-check failure.
- `NewWork`: an open `Ready` Issue with no linked live open PR that satisfies
  the Definition of Ready.

Run `Tools/Automation/Get-DeveloperWorkItem.ps1` once to select at most one
item. It uses the following queue bands in this exact order:

1. P0/P1 `ReviewFix`
2. P0/P1 `DeliveryResume`
3. P2/P3 `ReviewFix`
4. P2/P3 `DeliveryResume`
5. P0/P1 `NewWork`
6. P2/P3 `NewWork`

Within a band, sort by `ProjectV2Item.updatedAt` in ascending UTC order, then by
Issue number ascending as the final deterministic tie-break. P0 and P1 are one
band, as are P2 and P3; do not add another Priority sort inside a band. The
selector is read-only. `Selected=false` with `Mode=None` is a successful empty
queue, while API, parse, incomplete pagination, or exact Project schema
failures are errors. A production selection is authoritative only when its
output says `DataSource=Live`; fixture input is gated to the owned PowerShell
smoke harness and is visibly reported as `DataSource=Fixture`.

The selected Issue and, for resume modes, PR head are pinned for the run. Read
all of that Issue's body and comments plus the PR and reviews, run preflight,
then re-query eligibility and the PR's live `head.sha` and `head.ref`. If the
pinned item changed or became ineligible, stop; do not fall through to a second
candidate in the same run.

The selector emits compact JSON containing at least:

- `Selected`, `Mode`, `IssueNumber`, `IssueUrl`, `Status`, `Priority`,
  `UpdatedAt`
- `PullRequestNumber`, `PullRequestUrl`, `PullRequestHeadSha`,
  `PullRequestHeadRef`
- `Reason`, `LatestHandoffUrl`, `FindingUrl`, `PendingCommand`
- `ExcludedCandidates[]`

For resume modes it also emits a same-branch refspec and denies creation of a
new Issue, PR, or remote branch. These are execution contracts, not permission
for the selector to mutate Git, GitHub, or Project state.

### Handoff marker

Post this exact machine-readable marker in an Issue or PR comment and read it
back before performing the transition that hands work to Developer:

```md
<!-- sashimi-boy-automation-handoff:v1
mode: ReviewFix|DeliveryResume
issue: <issue-number>
pr: <pull-request-number>
head: <40-char SHA>
sourceRole: Reviewer|Developer|Owner
reason: <stable reason id>
findingUrl: <URL or empty>
pendingCommand: <command or empty>
-->
```

Every field must occur exactly once with the shown spelling and casing. Values
must be single-line and cannot terminate the HTML comment. `issue`, `pr`, and
`head` must match the selected Issue and the freshly queried PR head. The
comment author must be the Owner or a repository member/collaborator; an Owner
handoff additionally requires the repository Owner as author. Marker evidence
must be a new, unedited Issue or PR comment; never edit an existing marker
comment. PR review bodies are not marker locations. When distinct newest
marker comments share GitHub's timestamp granularity, the candidate fails
closed until a later unambiguous marker is posted.

Allowed combinations are:

- Reviewer `ReviewFix`: `review-blocker` or `review-major`, with a non-empty
  absolute HTTPS `findingUrl`
- Owner `ReviewFix`: `owner-verification-fail`
- Developer `DeliveryResume`: `unity-lock`, `unity-process`,
  `protected-worktree-dirty`, `disk-space`, `network`, `authentication`,
  `runner-failure`, or `required-check-transient`

`DeliveryResume` requires a non-empty exact `pendingCommand`. Product decisions,
missing source assets, unresolved external blockers, and unknown reasons are
not transient failures. Never infer a mode, branch, or blocker resolution from
unstructured prose.

A handoff is current only when it is the newest handoff event, targets the one
linked open Draft PR in this exact repository, and its head still equals the
live PR head. The linked URL, returned PR URL, number, base repository, Draft
state, base ref, `head.sha`, and `head.ref` are revalidated; a URL cannot
override repository scope. A later PR head resolves the old handoff. A trusted
native `APPROVED` review resolves it only when the review targets the same live
head, occurs after the handoff, and is the latest decisive trusted review for
that head. A later `CHANGES_REQUESTED` or `DISMISSED` review supersedes an
earlier approval; an equal-time approval ambiguity fails closed. A successful
same-head validation-only resume uses this companion marker so the old handoff
cannot be selected again:

```md
<!-- sashimi-boy-automation-handoff-completion:v1
issue: <issue-number>
pr: <pull-request-number>
head: <40-char SHA>
sourceRole: Developer
handoffUrl: <exact handoff comment URL>
-->
```

Any later unedited completion that exactly identifies the handoff URL, Issue,
PR, and head resolves it; a later unrelated completion cannot reactivate the
resolved handoff. A still later valid handoff supersedes an older completion.
Post the completion only for `ReviewFix` or `DeliveryResume`, after a successful
`In Progress -> Review` transition.

An active `blocked` label excludes a candidate unless the latest authoritative
Owner queue decision explicitly says `unblock` with this marker:

```md
<!-- sashimi-boy-automation-owner-decision:v1
issue: <issue-number>
queue: block|unblock
reason: product-decision|source-asset-missing|external-blocker|resolved
-->
```

The author must be the repository Owner, the Issue number must match, and the
comment must be new and unedited. A malformed, edited, or equal-time ambiguous
latest Owner decision fails closed. Non-Owner prose does not unblock an item. A
`block` decision requires one of the three blocker reasons; `unblock` requires
`resolved`. The selector does not guess undeclared blocker labels. A current
machine-readable product, asset, or external blocker still excludes the item
even when a stale `blocked` label is unblocked.

An active same-PR Developer lease also excludes a resume candidate. Read-only
lease files, when present, live at
`%TEMP%\SashimiBoyAutomation\DeveloperLeases\pr-<number>.json` and contain
`SchemaVersion`, `PullRequestNumber`, `HeadSha`, an unguessable `LeaseId`,
`AcquiredAt`, and `ExpiresAt`. Use `Use-DeveloperLease.ps1` to atomically
acquire the lease after pin revalidation, renew it before expiry, and release
the same `LeaseId` in `finally`. A malformed lease fails that candidate closed.
Preflight remains authoritative for live Unity processes, Unity locks, and
protected-worktree cleanliness.

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
  summarizes counts, and propagates failure. All three invocations use
  `-buildTarget StandaloneWindows64`; skipped tests are never a strict PASS.
  Compile/import log diagnostics remain strict. For EditMode and PlayMode, the
  native exit and strict NUnit XML result/counts are authoritative, so an
  expected `LogAssert.Expect` error in a passing test is not counted again as a
  new Console failure. A failed/missing XML result, native/XML disagreement,
  crash, compile failure, Missing Script, or out-of-run Console error,
  assertion, or managed exception is
  still a failure.
- `KnownDisposableUnityDrift` is non-blocking only for an owned, marker-bound
  review integration below `%TEMP%\SashimiBoyAutomation`, exactly one modified
  `ProjectSettings/ProjectSettings.asset`, the Owner-approved Unity-default
  serialization sequence, an empty mode/type diff summary, and three distinct
  clean protected worktrees. The full diff and SHA-256 evidence must be stored
  with the Summary. Any extra field or file, `Assets/**`, `Packages/**`, other
  `ProjectSettings/**`, deletion, reordering, or meaningful build/gameplay
  setting change remains `WorkspaceMutation`. Never restore the file to hide
  the drift or commit the serialized defaults.
- `Set-GitHubProjectStatus.ps1` validates an existing Project item and an
  allowed role transition before editing the existing `Status` field. It
  supports `-WhatIf` and never creates schema.
- `Get-DeveloperWorkItem.ps1` validates exact Project schema, reads current
  Issue/PR heads and handoff evidence, and deterministically returns at most one
  Developer item without mutating GitHub, Project state, or Git.
- `Use-DeveloperLease.ps1` uses a named per-PR mutex and an unguessable lease
  identity to atomically acquire, renew, inspect, and release the canonical
  same-PR Developer lease. Mutations support `-WhatIf`; release requires the
  exact PR, head, and lease identity.
- `Automation.Handoff.ps1`, `New-AutomationHandoff.ps1`,
  `New-AutomationHandoffCompletion.ps1`, and
  `New-AutomationOwnerQueueDecision.ps1` validate, parse, and format the exact
  handoff, Owner queue-decision, and completion markers. The formatter scripts
  write marker text only; posting and read-back verification remain explicit
  role steps.

Every mutation-capable command must support `-WhatIf` or `-DryRun` as
appropriate. Every temporary resource must be cleaned in `finally`, except
when a documented investigation option explicitly preserves it.

## Role specifications

- Developer: [`DEVELOPER.md`](DEVELOPER.md)
- Reviewer: [`REVIEWER.md`](REVIEWER.md)
- Automation UI bootstrap text: [`BOOTSTRAP.md`](BOOTSTRAP.md)
