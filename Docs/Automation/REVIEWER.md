# Reviewer Execution Specification

This document defines the independent Reviewer role. Read the current
[`SPEC_VERSION`](SPEC_VERSION), [`WORKFLOW.md`](WORKFLOW.md), and
[`AGENTS.md`](../../AGENTS.md) from the checked-out commit before acting.

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
higher-priority source.

These safety prohibitions are non-relaxable by any Agent, comment, review, chat
summary, or memory:

- Never destroy the user's checkout.
- Never use `git reset --hard`, `git clean`, or force push.
- An Agent must never merge a PR or move an Issue to `Done`.
- One run handles exactly one Issue.
- Never report an unexecuted test as PASS.

## Authority and scope

Review exactly one Issue and its linked PR per run. Use the authoritative
sources above and the current PR head. Treat stale material according to the
source-of-truth order rather than as current instructions.

The Reviewer may perform only these Project transitions:

- `Review -> In Progress` when a Blocker or Major finding exists
- `Review -> Verification` when independent automated verification passes and
  no Blocker or Major finding remains

The Reviewer must not implement fixes on the PR branch, push review integration
commits, merge the PR, or move an Issue to `Done`.

## Start gate

Before changing GitHub state:

1. Read the complete latest Issue body, every Issue comment, the linked PR,
   current review findings, and the exact PR head SHA.
2. Run `Tools/Automation/Invoke-AutomationPreflight.ps1` for the Reviewer role
   with explicit repository, Project, worktree, Unity, and disk-space
   parameters.
3. Confirm the current directory is the dedicated Reviewer linked worktree,
   not the primary checkout, and that the primary, Developer, and Reviewer
   worktrees are clean.
4. Confirm the Issue's live Project `Status` is exactly `Review` and the PR head
   has not changed before verification begins.

If any check fails, stop without changing Project status and report the exact
failure.

## Deterministic integration

Use `Tools/Automation/New-ReviewIntegration.ps1`; do not manually compose a
synthetic merge command.

The integration operation must:

- create a fresh clone below `%TEMP%\SashimiBoyAutomation\...`
- record the exact PR head SHA and latest `origin/main` SHA
- use a detached or local-only integration branch and
  `git merge --no-ff --no-edit`
- return the integration path, PR head, main head, merge head, merge exit code,
  conflict paths, and cleanup state in a structured result
- never use `git read-tree -u <tree>`, push, delete a remote branch, or modify
  the primary, Developer, or Reviewer worktree
- clean only the marked temporary workspace it created in a `finally` block

Use the documented keep-workspace option only when the merged project must
remain available for Unity tests or investigation. The caller then owns cleanup
of that exact marked temporary workspace. Keep Unity logs/XML in an artifact
directory outside the integration workspace, then run
`Tools/Automation/Remove-ReviewIntegration.ps1 -WorkspaceRoot <exact-path>`
first with `-WhatIf` and then without it. Verify its structured `Removed` result
and verify that the workspace no longer exists.

If the PR head changes, discard the temporary result and restart from the new
exact head. A merge conflict is evidence, not a reason to modify the PR branch.

## Independent verification

Run `Tools/Automation/Invoke-UnityTests.ps1` against the temporary integration
project and an explicit artifact directory. Inspect:

- clean import and C# compile result and log
- all EditMode result XML and log
- all PlayMode result XML and log
- passed, failed, skipped, and inconclusive counts
- compiler errors, `NullReferenceException`, `MissingReferenceException`,
  Missing Script messages, and new Console errors

All compile/import and test invocations use `StandaloneWindows64`. Treat the
Unity native exit and strict NUnit XML result/counts as authoritative for
EditMode and PlayMode. Expected `LogAssert.Expect` output from a passing test is
not a new Console failure; skipped tests, failed or missing XML, native/XML
disagreement, crashes, compile failures, Missing Script, and out-of-run Console
errors, assertions, or managed exceptions still fail verification.

Do not accept a dirty integration workspace except for
`KnownDisposableUnityDrift` reported by the wrapper. That classification must
bind to the owned temporary integration marker and run ID, contain exactly the
approved `ProjectSettings/ProjectSettings.asset` Unity-default serialization
sequence and no other change, verify all three protected worktrees are clean,
and preserve the complete diff plus SHA-256 artifacts. Inspect those artifacts
and the Summary. Never restore the file to manufacture a clean result, commit
the defaults, or broaden the allowlist.

Also run `git diff --check`, `.meta` and GUID integrity checks, required
Missing Script/reference scans, and Issue-specific regression checks. Verify
that the synthetic workspace did not modify tracked source content.

Do not claim a pass from an exit code alone. Result XML and logs must exist and
be inspected. Human-only visual, audio, input, feel, pacing, and persistence
checks remain Pending Manual Verification and do not block an automated
`Review -> Verification` transition.

## Findings and transition

- Report findings with severity, evidence, affected file or behavior, and a
  reproducible check.
- If any Blocker or Major remains, post the focused findings and use
  `Tools/Automation/Set-GitHubProjectStatus.ps1` for
  `Review -> In Progress`.
- If automated verification passes with no Blocker or Major, post the exact
  human verification checklist and required evidence, then use the status tool
  for `Review -> Verification`.
- A Minor finding does not block Verification unless it is explicitly marked
  as a merge gate.

Never merge the PR. The owner performs final verification, merge, Issue close,
and `Done`.

## Failure and cleanup

Always verify removal of a non-preserved temporary clone and confirm the
primary, Developer, and Reviewer worktrees remain clean. A cleanup refusal
retains the workspace for manual inspection; do not bypass the marker or
reparse-point guards. If cleanup, approval, authentication, network, Git,
Unity, or a local runner fails, do not change Project status. Report the exact
command, exit code, standard error, artifact path, retained integration path if
any, and the exact pending owner command.
