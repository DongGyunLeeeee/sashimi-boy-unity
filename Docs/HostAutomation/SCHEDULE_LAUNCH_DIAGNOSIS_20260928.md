# Scheduled entry diagnosis — 2026-09-28

This records the Issue #52 launch failure on the rollout host after PR #65
(commit `9f0484c4c8c4b0bb114c36ed4b024ef393b12237`). It is not an
installation or schedule-enable receipt for the proposed replacement.

## Failure and repair

The installed `HighestAvailable` task failed before creating a run directory.
Its protected entry point passed bundle and six-executable identity checks,
then `CreateProcessWithTokenW` returned Win32 error 5. An elevated native
diagnostic found that the same-account, non-elevated linked token had
`TokenType=2` (impersonation), not a primary token. Removing redirected pipes
or changing the working directory did not resolve error 5.
`DuplicateTokenEx` to obtain a primary token and `CreateProcessAsUser`
both failed with error 1346.

[CreateProcessWithTokenW requires a primary token](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw).
These results explain why this host's elevated bootstrap failed while manual
standard-user Developer/Reviewer pilots passed.

The proposed task uses `InteractiveToken` with `LeastPrivilege` for the same
Owner. Windows supplies the standard token directly. The protected entry point
continues to verify bundle/executable identity and now rejects elevated tokens
before Common/configuration/tool execution. The unsupported linked-token
launcher is removed. Installer elevation, protected ACLs, child-process jobs,
cancellation, timeouts, and Issue/PR state gates remain in place.

## Actual Task Scheduler experiment

At 06:28:37–06:28:50 UTC, a unique temporary task with no trigger ran once
through Windows Task Scheduler, then was removed. It used the same Owner,
`LogonType=3` (interactive), `RunLevel=0` (least privilege), and trusted
PowerShell 7. Its in-memory action exercised the proposed native token guard,
then invoked the **unchanged installed PR #65 bundle** with
`-Once -IssueNumber 20`.

The live queue had already confirmed Issue #20 was in `Verification`.
The scheduled invocation reported:

- Task Scheduler result and orchestrator exit code: `0`.
- Actual Owner SID matched; `CurrentProcessElevated=false`, `Relaunched=false`.
- `Integrity.Verified=true`; all six executable identities verified.
- `State=NoWork`, `DispatchCount=0`, `MutationAttempted=false`; stderr empty.
- Run ID: `20260928T062841Z-bb1acd41728a43ecb83b4ca3df306e1f`.
- Temporary task removed; production task remained disabled and unchanged.

This proves the actual scheduled standard-user launch path with the existing
protected runtime. It does **not** claim the replacement bundle has been
installed or that new Developer/Reviewer work ran during this experiment.

Content hashes of the local evidence (SHA-256):

| Evidence | SHA-256 |
| --- | --- |
| Native isolation v2 result | `09be049f4870b5d429a0784b313afefdec70ede9900d4cc99bae809f4f65f12e` |
| Scheduled task proof | `ea3a70ef270524fe3ab0b3d78cd8f84937328a47b9b1d457178966e2d01a3e74` |
| Child / installed orchestrator result | `674d41755db952d18abea3353b89460957026b2d2d55d92b3c091cbe0407a4c4` |
| Production task definition before and after | `5d81754c0ed671bf470255f60d7eaa6a25841ec6516a3c195f63ca506591a464` |

The first isolation attempt used incorrect native buffer lengths and did not
reach process creation. It is not counted as evidence of token-launch behavior;
the corrected v2 result above is the diagnostic authority.

## Remaining Owner gate

Issue #52 explicitly required highest privileges. Changing that requirement to
least privilege needs an explicit Owner decision before installation. Review
and Owner merge precede installing a newly pinned bundle. Observe an actual
invocation of the replacement task before declaring recurring execution healthy.

The completed Issue #20 Developer → Review → Reviewer → Verification pilot
remains valid historical evidence. It is not a new test result from this change,
and its Project status must not be reset merely to repeat the startup check.
