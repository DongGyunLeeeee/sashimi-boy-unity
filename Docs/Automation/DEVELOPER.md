# Developer Execution Specification

This document defines the Developer role. Read the current
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

The authoritative sources above define the work. Do not invent missing product,
story, UX, balance, or asset decisions. Handle exactly one Issue per run.

The Developer may perform only these Project transitions:

- `Ready -> In Progress`, immediately before implementation starts
- `In Progress -> Review`, only after the Definition of Review is satisfied

The Developer must not merge a PR, move an Issue to `Verification` or `Done`,
or perform an Owner or Reviewer transition.

## Start gate

Before changing a file or GitHub state:

1. Run `Tools/Automation/Get-DeveloperWorkItem.ps1` once and pin its single
   selected Issue, mode, and (for resume modes) PR head/ref for this run. An
   empty queue is a successful no-op. Stop unless the production result says
   `DataSource=Live`.
2. Read the complete latest selected Issue body and every comment. For a resume
   mode, also read the linked PR, all comments and reviews, the current finding,
   handoff, and completion evidence.
3. Run `Tools/Automation/Invoke-AutomationPreflight.ps1` for the Developer
   role with explicit repository, Project, worktree, Unity, and disk-space
   parameters.
4. Confirm the current directory is the dedicated Developer linked worktree,
   not the primary checkout, and that the primary, Developer, and Reviewer
   worktrees are clean.
5. Re-run live eligibility checks for the pinned Issue only and re-query an
   existing PR's state, Draft flag, same-repository head, `head.sha`, and
   `head.ref`. If it changed, stop instead of selecting another Issue.
6. For a resume mode, generate an unguessable lease ID and atomically acquire
   the exact PR/head lease with `Tools/Automation/Use-DeveloperLease.ps1`.
   Retain that ID, renew before expiry, and release it in `finally`. If
   acquisition fails, stop without selecting another Issue.
7. Apply the selected mode gate below. Confirm the acceptance criteria are
   testable, blockers and product decisions are resolved, required inputs
   exist, and no conflicting open PR edits the same Scene or Prefab.

If any check fails, stop without modifying files or GitHub state and report the
exact failed check. For `NewWork` only, once all checks pass, use
`Tools/Automation/Set-GitHubProjectStatus.ps1` to perform the allowed
`Ready -> In Progress` transition. `ReviewFix` and `DeliveryResume` are already
`In Progress`; do not perform a start-of-run status mutation.

### Mode gates

- `ReviewFix`: the Issue is open and exactly `In Progress`; exactly one linked
  PR is live, open, Draft, same-repository, and based on `main`; the latest
  unresolved handoff matches its live head and is a Reviewer Blocker/Major or
  Owner manual-verification FAIL.
- `DeliveryResume`: the same Issue/PR requirements hold; the latest unresolved
  handoff is from Developer, uses an allowed transient reason, and preserves a
  non-empty exact pending command. Product decisions, missing assets, and
  unresolved external blockers are not eligible.
- `NewWork`: the Issue is open and exactly `Ready`, has no linked live open PR,
  and satisfies the Definition of Ready.

An ambiguous legacy `In Progress` item without a current valid handoff is not
eligible. A current `blocked` label without the repository-defined latest Owner
unblock decision, a later completion/PASS, a stale PR head, or an active lease,
Unity process, lock, or protected-worktree conflict also excludes the item.

## Implementation

- For `NewWork`, start a dedicated Issue branch from the latest `origin/main`;
  never change the primary checkout's branch.
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

### Existing PR resume contract

For `ReviewFix` and `DeliveryResume`:

1. Re-query the one linked PR's live `head.sha` and `head.ref`.
2. Fetch that exact existing remote head and create a unique local-only agent
   branch at the queried SHA. Do not create a new Issue, PR, or remote feature
   branch.
3. Fetch and normally merge the latest `origin/main`. Do not rebase or force
   push.
4. In `ReviewFix`, address the current finding and run the full regression
   suite. In `DeliveryResume`, first rerun the exact pending command in a fresh
   workspace, make only necessary fixes, then complete the Delivery gate.
5. Immediately before delivery, re-query the PR head/ref. Stop on a concurrent
   change. Immediately before every remote mutation, including this push and
   the final Project transition, successfully renew the same PR/head/lease ID;
   a missing, expired, replaced, or mismatched lease requires an immediate
   stop. The only allowed branch update is a normal push to the existing PR
   branch:

   ```text
   git push origin HEAD:<existing-pr-head-ref>
   ```

If a validation-only resume needs no tracked change and creates no merge
commit, do not create an empty commit and do not push. Update the existing Draft
PR evidence only.

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

`Invoke-UnityTests.ps1` uses `StandaloneWindows64` for compile, EditMode, and
PlayMode. Compile diagnostics remain strict; native exit plus strict NUnit XML
is authoritative for test stages, so a passing `LogAssert.Expect` negative-path
test is not failed a second time by its expected error text. Skipped tests and
all unexpected failures remain blocking. Do not broaden or bypass the wrapper's
narrow `KnownDisposableUnityDrift` policy, restore drift to hide it, or commit
Unity-default serialization changes.

Automated tests do not replace the human checks listed in `AGENTS.md`. Store
generated logs and XML outside the repository or in an explicitly ignored
artifact location, and report their paths rather than committing them.

## Delivery gate

Move the Issue to `Review` only after all of the following are true:

1. Required changes, if any, are focused, committed, and pushed to the
   dedicated branch. A validation-only resume may have no new commit or push.
2. A Draft PR exists and links the Issue with `Closes #<issue>`. Resume modes
   update only the existing Draft PR.
3. Compile and all applicable automated tests passed and were inspected.
4. `git diff --check`, integrity scans, and Issue-specific checks passed.
5. The PR body records the root cause or motivation, changed files, script or
   behavior contracts, exact commands and results, artifact paths, and a human
   verification checklist.

Use `Tools/Automation/Set-GitHubProjectStatus.ps1` for the exact
`In Progress -> Review` transition. For `ReviewFix` or `DeliveryResume` only,
after that transition succeeds, post and read back the matching handoff
completion marker so same-head validation-only work cannot be selected again.
`NewWork` has no handoff to complete. Release any resume lease in `finally` and
do not start another Issue afterward.

## Failure behavior

Do not claim an unexecuted check passed. If approval, authentication, network,
Git, Unity, or a local runner fails, leave the Issue in its current state and
report the exact failed command, exit code, standard error, retained artifact
or workspace path, and the exact pending command for the owner.

An Automation memory failure is never a reason to block otherwise valid work.

When a transient required check leaves an Issue `In Progress`, use
`Tools/Automation/New-AutomationHandoff.ps1` to format a `DeliveryResume`
marker for the current live PR head, including the stable transient reason and
exact pending command. A `DeliveryResume` marker is safe only after every
required tracked change and commit is already present at that exact live remote
PR head. A push/authentication/network failure that leaves unpushed local state
must retain and report that workspace; do not advertise it as remotely
resumable. Post a safe marker as a new, unedited Issue or PR comment and read it
back. Do not use a transient marker for a product decision, missing source
asset, or unresolved external blocker. If an unchanged `ReviewFix` handoff
remains unresolved, preserve it rather than replacing it with a weaker marker.
If marker posting or read-back fails, report that exact failure and do not
claim the item is safely resumable.
