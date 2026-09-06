# Host Automation Troubleshooting

Failures are designed to preserve state rather than advance unsafe work. Start
with the structured output and the active run's `State\RunState.json` and
`State\Selection.json`. Use only the exact run and artifact paths reported
there.

Never respond to a failure by force pushing, rebasing, resetting hard, running
`git clean`, bypassing the Codex sandbox, deleting an unverified workspace,
killing every Unity process, merging the PR, or moving the Issue to `Done`.

## Quick diagnosis

| Symptom | Meaning and safe action |
| --- | --- |
| `Selected=false`, mode `None` | Successful empty queue. Confirm the Project statuses and wait for an eligible item; do not manufacture a transition. |
| `AlreadyRunning` | Another process owns the global mutex. `IgnoreNew`/mutex behavior is expected. Inspect the current task instance and active run; do not start a second copy. |
| Task remains Ready while logged off | `InteractiveToken` runs only while `02031` is logged on. Log on as that user; do not add a stored password. |
| Installer reports access denied | Preview again with `-DryRun`, then use the exact in-memory pinned-launcher contract from `OPERATIONS.md` in an elevated protected PowerShell 7 session as the same `02031` identity. Program Files staging and ACL protection require administrative authority; do not change task identity. |
| PowerShell version is rejected | Confirm `C:\Program Files\PowerShell\7\pwsh.exe` is PowerShell Core `7.5.0` or newer. Do not point the task at Windows PowerShell or an alternate mutable executable. |
| Expected BundleId mismatch | A runtime, canonical config, executable, Codex source, or installer-bootstrap byte changed after preview. No staging, ACL, or scheduler mutation is authorized. Inspect the diff, rerun `-DryRun`, record its new exact `BundleId`, then pass that value explicitly as `-ExpectedBundleId` only if it is the reviewed payload. |
| External/expected installer SHA-256 mismatch | The independently leased installer bytes, the reviewed value retained outside the checkout before DryRun, or the installer's self-report differ. The pinned launcher must stop before process creation when the reviewed value differs, and after DryRun when the self-report differs. Do not recalculate and trust the new file. Return to the exact reviewed commit, inspect it, obtain the new hash as independent review evidence, then rerun the leased DryRun and pass that same value explicitly as `-ExpectedInstallerSha256`. |
| Installer path reparse or lease failure | Do not fall back to direct `& ...Install-SashimiHostAutomation.ps1` execution or another file-backed wrapper. A reparse ancestor, existing writer/delete handle, or coordinated replacement prevented the Owner launcher from establishing its no-write/no-delete-sharing lease. Preserve the mismatch evidence, close only a process you positively identify as the unexpected holder, and retry from reviewed source. |
| Bundle manifest, transaction, hash, or ACL fails | Do not edit, re-ACL, rename, or run staged content. Preserve an unexpected marker-owned staging directory for inspection. Review the source files and config, rerun installer `-DryRun`, then install the exact expected content-addressed bundle from an elevated session as `02031`. |
| Trusted PowerShell module provenance fails | Do not alter `PSModulePath`, import a similarly named user module, or bypass signature/hash checks. Run the exact stable PowerShell 7 host and repair the Microsoft-signed Security or ScheduledTasks system component before retrying. |
| Executable identity or absolute-path check fails | Do not substitute a name found through `PATH` or update the identity file by hand. Verify the configured Git, Git LFS, GitHub CLI, Unity, and stable PowerShell files plus the protected hash-keyed Codex Program Files distribution. A Codex reparse ancestor or untrusted write ACE is a hard failure. Review a new DryRun and install only its exact expected bundle so all six paths, lengths, and SHA-256 hashes are rebound together. |
| Project schema error | Verify the exact `Status`, `Priority`, `Area`, and `Size` fields/options. The Host does not create or guess schema. |
| Incomplete pagination or missing cursor | GitHub data is incomplete. Preserve evidence and retry only after the configured cooldown; never select from a partial page set. |
| Korean text is garbled | Confirm the stable PowerShell 7 executable and explicit UTF-8 stdin/stdout/stderr behavior. Preserve the raw sanitized response; do not rewrite Issue content. |
| PR rejected as fork/base/author | This is a security exclusion. Verify live head repository, base `main`, and configured PR-author allowlist. Do not broaden the allowlist merely to unblock a run. |
| Authenticated GitHub actor rejected | Run `gh auth status` for diagnosis and restore authentication as the exact configured Project owner. Do not authorize a different account or weaken the actor check. |
| Stale PR content, SHA, or ref | Someone changed the PR title/body, head SHA, or head ref after pinning. The run must perform no delivery push or status change. Let a later invocation select the newly current state. |
| `origin/main` advanced | The Developer used an exact pinned main commit and stopped before delivery push/status mutation when the live main ref changed. Let a new invocation clone and integrate the new current main; do not reuse or force the old result. |
| Git configuration/control snapshot drifts | Stop the run. Do not restore config, refs, HEAD, index, hooks, alternates, remotes, attributes, staged state, or worktree metadata. Confirm that no commit, LFS push, Git push, comment, or Project transition occurred, preserve bounded evidence, and start a fresh run only after identifying the untrusted Codex/Unity change. |
| Git operation/control state is present | A merge, cherry-pick, revert, rebase, bisect, notes merge, sequencer plan, lock file, unknown `.git` control entry, oversized control file, or reparse was present or appeared after an untrusted stage. Do not run commit, continue/abort the operation, or silently restore Git state. Confirm every external mutation was suppressed and investigate the isolated run as terminal evidence. |
| Codex exits 0 but Host reports failure | Inspect the sanitized Host error, `CodexEvents.jsonl` metadata, `CodexProcessSummary.json`, and validated result if present. The in-memory raw stream may have contained `error`, `turn.failed`, an unsafe/mismatched/unfinished command event, or invalid result. Raw Codex stdout/stderr is intentionally not retained. Exit 0 alone is never PASS. |
| Codex reports a `command_execution` event | Treat it as a terminal launch-policy failure. The Host disables shell and unified execution before starting Codex; it does not rely on retrospective parsing to contain a command. Preserve content-free hashes, verify no sentinel or Host mutation occurred, and do not weaken the no-command flags or OS sandbox. |
| Repository-scoped Codex configuration is rejected | Remove the branch-owned `.codex` tree from the isolated run source and review why it was present. The Host deliberately rejects project providers, endpoints, hooks, plugins, MCP launchers, and exec-policy state before every Codex probe or execution; do not allowlist the tree. |
| Codex capability probe fails | Verify the protected Program Files Codex distribution and that the installed CLI accepts strict no-shell, no-unified-exec, no-user-rules, JSON, ephemeral, sandbox, approval, and Windows unelevated settings. A missing/unknown security setting must fail closed. Never use a dangerous bypass flag. |
| Codex authentication fails but interactive login worked previously | The Host intentionally removes environment API keys, endpoint/base-URL overrides, every proxy casing, CA bundle overrides, `CODEX_HOME`, and other auth-location variables from both probes and execution. Authenticate the protected installed CLI through its current-user credential store. Do not restore those ambient variables; if a stable OS locator is required, the Host must derive it rather than inherit it. |
| Changed-content containment fails | A changed or staged file contained recognizable credential, profile, save, or sensitive inherited-environment material, or used an unsafe path. Inspect the run clone locally without copying the suspect content into comments or artifacts; remove the material and rerun from a fresh Host invocation. Never bypass the pre-stage/final-staged scan. |
| Unity is locked | Confirm whether the recorded run-owned Unity PID is still active. Do not make CIM enumeration mandatory and do not kill an unrelated Editor. Wait for cooldown or cancel the owning run. |
| Unity timeout or descendant remains | Inspect the stage log and run-owned kill-on-close job evidence. The Host must terminate only that stage job, confirm its active-process count is zero, mark validation failed, and retain evidence before any Git state can be trusted. |
| Unity crash | Inspect the Editor log, native exit code, crash artifacts, and XML. A crash is failure even if a partial XML file exists. |
| Missing NUnit XML | The stage failed even with native exit code 0. Check the Unity arguments, artifact directory, and log. Do not mark PASS manually. |
| Native/XML disagreement | Treat as failure. Preserve both forms of evidence and investigate the wrapper or crash; do not choose the more favorable result. |
| Generator nondeterminism | Compare the two recorded outputs/hashes. Fix the authoritative generator or input; do not commit one arbitrary generated result. |
| `git diff --check`, LFS, meta/GUID, or reference failure | Inspect the focused validation report. Do not push until the exact integrity error is fixed and the full required validation is rerun. |
| Protected production scope changed | Stop. Issue #52 does not authorize gameplay, scenes, prefabs, art, audio, save data, packages, or gameplay ProjectSettings changes. Preserve the clone for inspection. |
| Unexpected Unity raw state | Validation cleanup found an unrecognized file, directory, or reparse point. The Host intentionally leaves it outside publishable Artifacts and fails closed. Preserve the run and inspect the exact owned State path locally; do not publish or delete the entry merely to make cleanup pass. |
| Unity artifact boundary violation | A raw log/XML changed, exceeded its 8/16 MiB limit, was not strict UTF-8, or the public Unity tree violated its exact recursive manifest, 4 MiB metadata limit, 25 MiB PNG limit, 128 MiB total quota, stable hash, or no-reparse rule. The Host invalidates the entire public Unity root, atomically quarantines it under State, and removes it without traversing reparse targets. Do not recreate or publish individual files from that failed root; verify the root is absent and rerun from a fresh owned workspace. |
| Cleanup failure | The owned run remains intentionally. Inspect the cleanup error, marker, path boundary, reparse-point scan, and each child PID/start-time identity before any manual removal. |
| Authentication/network error | No state should advance. Re-establish current-user authentication or connectivity, then observe retry cooldown. Never paste tokens into config or artifacts. |

## Inspect a run safely

Use read-only commands against the exact reported path:

```powershell
$RunPath = 'C:\exact\reported\Runs\<run-id>'

Get-Content -LiteralPath (Join-Path $RunPath 'State\RunState.json') -Raw
Get-Content -LiteralPath (Join-Path $RunPath 'State\Selection.json') -Raw
Get-Content -LiteralPath (Join-Path $RunPath 'State\Events.json') -Raw
Get-Content -LiteralPath (Join-Path $RunPath 'State\OwnedHostPids.json') -Raw
Get-Content -LiteralPath (Join-Path $RunPath 'State\OwnedUnityPids.json') -Raw
Get-Content -LiteralPath (Join-Path $RunPath 'State\FinalResult.json') -Raw
Get-Content -LiteralPath `
  (Join-Path $RunPath 'Artifacts\Codex\CodexProcessSummary.json') -Raw
Get-Content -LiteralPath `
  (Join-Path $RunPath 'Artifacts\Codex\CodexEvents.jsonl') -Raw
Get-Content -LiteralPath `
  (Join-Path $RunPath 'Artifacts\Codex\CodexResult.json') -Raw
Get-Content -LiteralPath `
  (Join-Path $RunPath 'Artifacts\Unity\UnityValidation.Summary.json') -Raw
Get-ChildItem -LiteralPath (Join-Path $RunPath 'Artifacts') -Recurse -File |
  Select-Object FullName, Length, LastWriteTimeUtc
```

Do not recursively inspect or package the whole user profile. Before sharing
an artifact, check it for tokens, authorization headers, credentials, save
data, and unrelated profile paths.

The owned-process files are multi-entry ledgers. Match both `Id` and
`StartTimeUtc`; a PID by itself can have been reused. A retained entry means
the Host did not confirm process-tree termination and is not permission for a
broad `Stop-Process` command.

## Task Scheduler checks

Inspect the registered task without changing it:

```powershell
$Task = Get-ScheduledTask -TaskName 'SASHIMI BOY Host Orchestrator'
$Task | Select-Object TaskName, State
$Task.Principal | Format-List UserId, LogonType, RunLevel
$Task.Settings | Format-List StartWhenAvailable, WakeToRun, MultipleInstances
Export-ScheduledTask -TaskName 'SASHIMI BOY Host Orchestrator'
```

Expected values are user `02031`, `InteractiveToken`, `HighestAvailable`,
`StartWhenAvailable=True`, `WakeToRun=True`, `IgnoreNew`, a 15-minute repeat,
and action executable `C:\Program Files\PowerShell\7\pwsh.exe`.

The task action must point to one staged orchestrator, staged config, and
`HostIntegrity.json` below the same content-addressed Program Files bundle. If
any path is missing or fails integrity, do not edit the task or staged files by
hand. Review the source runtime files and source config, rerun the installer
first with `-DryRun`, then let the Owner install a new protected bundle.

## Request cancellation

Identify the exact active run from state or task output, then create its
cooperative cancellation marker:

```powershell
$RunPath = 'C:\exact\reported\Runs\<run-id>'
New-Item -ItemType File -LiteralPath `
  (Join-Path $RunPath 'cancel.requested') -Force
```

The marker is a boolean signal. Its contents are ignored and must never be a
shell command. Wait for the Host to enter `finally`, match each recorded PID
and start time, terminate only that owned process tree, confirm exit, and update
the ledger and state. If identity or termination cannot be confirmed, preserve
the run. If the same blocking condition persists, do not bypass the mutex.

## Stale-run response

A stale Issue/PR/head or advanced-main result is a safe stop, not a repair
request. Verify in the event trail that all of these are zero or absent:

- Developer push;
- new PR or Issue;
- Project status mutation;
- fallback selection.

A sanitized failure-evidence comment can exist only when the publisher recorded
a separate successful live pin and authenticated-actor revalidation for that
comment. Its presence is not a delivery or status mutation.

Do not reuse the stale clone for a later run. A new invocation must make a new
selection and fresh standalone clone.

## Developer failure and resume

New Work remains `Ready` through Codex, Unity, commit, LFS, push, and the final
Git-control/main-pin checks. A failure in those stages therefore leaves it
`Ready` with no Project mutation. `Ready -> In Progress` is the first Project
mutation after those boundaries; a later PR/publication failure may then leave
it `In Progress`. ReviewFix and DeliveryResume already begin and remain
`In Progress`. The Host publishes only sanitized evidence that is safe for the
current pin, and publishes nothing after Git-control drift.

A transient DeliveryResume handoff can refer to a `pendingCommand`, but that
field is evidence only. The resumed Host chooses its own typed validation
operation. If a resume has no tracked change and no merge commit, success is a
validation-only resume: no empty commit and no push are expected, and the
existing Draft PR is reused.

Never create a replacement PR or branch to work around an existing-resume
failure.

## Reviewer failure

The Reviewer works in a fresh clone and never pushes the PR branch. A merge
conflict, Codex failure, Unity failure, stale head, or cleanup error prevents a
Project transition.

For a confirmed Blocker or Major, the Host must first publish the focused
finding and current ReviewFix handoff for the exact live head, then may perform
`Review -> In Progress`. With no Blocker/Major, it may perform
`Review -> Verification` only after every required automated check passes and
the Owner checklist is published.

If evidence publication or status mutation fails partway through, preserve the
exact partial result and do not process another Issue.

## Cleanup incidents

Automatic deletion is intentionally narrow. A retained directory may indicate:

- missing, invalid, or mismatched `.sashimi-host-run.json` ownership marker;
- path outside the configured run root;
- run ID/path mismatch;
- junction, symbolic link, or other reparse point;
- an unexpected Unity raw validation-state entry quarantined outside Artifacts;
- a run-owned process still using the clone;
- access denied, antivirus, or filesystem failure.

Do not rename files to make the guard pass. Do not use `git clean`, recursive
deletion against a computed/unverified path, or a broad cleanup command. First
record the exact error and verify the absolute path is the intended
`Runs\<run-id>` child. Preserve the artifacts when ownership cannot be proven.

The normal retention window is 14 days, but a cleanup-failed run should remain
available until the incident is understood. Uninstall with
`-PreserveArtifacts` when removing the scheduled task during an investigation.

## Offline fixture verification

To distinguish a product defect from Host infrastructure, rerun the
self-contained suite from the repository root:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' `
  -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1
```

The suite must use only fixtures under `Tools/HostAutomation/Tests` or its own
marker-owned temporary directory. It must inject fake GitHub, Git, Codex,
Unity, and scheduler boundaries, and its final invocation audit must prove no
live mutation of #20, #26, or #30. Fixture paths are accepted only with
`-DryRun` or while the suite-owned
`SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS=1` gate is active. Do not set that
gate for an ordinary non-DryRun invocation.

Fixture-suite and environment-smoke results are local evidence only; they do
not establish that live GitHub, Git, Codex, Unity, or Task Scheduler execution
passed.

## Evidence to report

For an unresolved incident, provide the Owner:

- run ID, Issue/PR number, mode, and pinned SHA/ref;
- phase and stable failure category;
- exact sanitized command representation;
- native exit code and sanitized stderr when retained by that Host boundary;
- relevant Codex metadata/process-summary/result, Unity XML, and log paths;
- cleanup state and retained workspace path;
- retry count and next eligible time;
- exact pending Owner action.

Do not report an unexecuted test as PASS. Do not include authentication tokens,
Codex credentials, save data, or unrelated user-profile content. Never request
or report raw Codex stdout/stderr; the adapter intentionally stores only
content-free event/process metadata and the validated result.
