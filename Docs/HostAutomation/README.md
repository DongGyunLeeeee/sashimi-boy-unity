# SASHIMI BOY Windows Host Automation

The Windows Host Orchestrator is the unattended execution engine for SASHIMI
BOY development and review. It replaces Codex Desktop scheduled automations
with one Windows Task Scheduler task running as Windows user `02031`.

The orchestrator is intentionally issue-driven and conservative:

- one scheduled invocation selects at most one Issue;
- every selected Issue, pull request, PR title/body digest, head SHA, and head
  ref is pinned;
- live state is checked again before any mutation;
- a stale or ambiguous run stops instead of selecting a second Issue;
- `Verification` and `Done` are never queue inputs;
- no agent merges a pull request, closes an Issue, or moves an Issue to `Done`.

The fixed service scope is:

- repository: `DongGyunLeeeee/sashimi-boy-unity`
- Project owner: `DongGyunLeeeee`
- Project number: `1`
- base branch: `main`
- canonical remote: `https://github.com/DongGyunLeeeee/sashimi-boy-unity.git`
- Unity: `6000.4.0f1`
- PowerShell: Core edition `7.5.0` or newer at
  `C:\Program Files\PowerShell\7\pwsh.exe`

## Responsibility boundary

The Host and Codex have deliberately different authority.

| Windows Host PowerShell | Codex CLI |
| --- | --- |
| Reads the ProjectV2 queue with complete pagination and explicit UTF-8 | Interprets the selected Issue and its current Acceptance Criteria |
| Pins and revalidates the Issue, PR, SHA, and ref | Implements focused production/editor-pipeline and test changes for a Developer run |
| Creates a fresh standalone clone for the selected run | Performs a read-only independent analysis for a Reviewer run |
| Performs fetch, normal merge, branch creation, commit, and normal push | Emits an explicit schema-valid result object |
| Invokes and validates Codex JSONL | Does not run `gh`, push, create or merge PRs, mutate Project state, close Issues, or select more work |
| Runs Unity and repository validation | Does not replace Host validation |
| Publishes evidence and performs only role-authorized transitions | Runs inside the sandbox selected by the Host |
| Owns artifacts, retry/cancellation state, cleanup, and retention | Never supplies shell commands for the Host to execute |

Developer Codex runs use `workspace-write`, approval policy `never`, an
unelevated Windows sandbox, and disabled workspace-write network access.
Reviewer Codex runs are read-only. The Host, outside the Codex sandbox, owns
the bounded GitHub and Git network operations. The adapter probes the installed
CLI and fails closed if it cannot provide the required contract. The flags
`danger-full-access` and `dangerously-bypass-approvals-and-sandbox` are
forbidden.

## Queue order

The Host selects from the GitHub Project in this exact class order:

1. `Review` with one linked, open Draft PR -> Reviewer.
2. `In Progress` with a current `ReviewFix` handoff -> Developer.
3. `In Progress` with a current `DeliveryResume` handoff -> Developer.
4. `Ready` with no linked open PR -> Developer New Work.
5. No eligible item -> successful no-op.

Within one class, order by `Priority` (`P0`, `P1`, `P2`, `P3`), then the
oldest `ProjectV2Item.updatedAt`, then Issue number. Missing pages, unexpected
Project schema, unrecognized priority, ambiguous evidence, or an ineligible
pinned item is an error or exclusion; it is never permission to guess.

Resume modes reuse the exact existing PR branch. They do not create another
Issue, PR, or remote feature branch. A validation-only resume with no tracked
change and no merge commit creates no empty commit and performs no push.

## Runtime layout

The scheduled task runs from a content-addressed installation bundle:

```text
C:\Program Files\SashimiBoyAutomation\Bundles\<bundle-sha256>\
|-- HostIntegrity.json
|-- ExecutableIdentity.json
|-- Config.json
|-- HostAutomation.Common.ps1
|-- Invoke-SashimiHostOrchestrator.ps1
`-- <the remaining required runtime scripts>
```

The default mutable run-data root is:

```text
%LOCALAPPDATA%\SashimiBoyAutomation\Runs\
    `-- <run-id>\
        |-- .sashimi-host-run.json
        |-- cancel.requested                 (only when cancellation is requested)
        |-- Repository\
        |-- Artifacts\
        |   |-- RunResult.json
        |   |-- Codex\
        |   |   |-- CodexEvents.jsonl        (event metadata and hashes only)
        |   |   |-- CodexProcessSummary.json (exit state, sizes, and hashes only)
        |   |   |-- CodexResult.json
        |   |   `-- CodexResult.schema.json
        |   |-- Unity\
        |   |   |-- UnityValidation.Summary.json
        |   |   |-- CompileImport.log
        |   |   |-- EditMode.log
        |   |   |-- EditMode.xml
        |   |   |-- PlayMode.log
        |   |   `-- PlayMode.xml
        |   |-- DraftPullRequest.md          (Developer New Work)
        |   |-- HandoffCompletion.md         (when completing a handoff)
        |   |-- Failure.md                   (Developer failure evidence)
        |   |-- ReviewFinding.md             (Reviewer Blocker/Major)
        |   |-- ReviewFixHandoff.md          (Reviewer Blocker/Major)
        |   |-- OwnerVerificationChecklist.md
        |   `-- ReviewerFailure.md           (Reviewer failure evidence)
        `-- State\
            |-- RunState.json
            |-- Selection.json
            |-- Events.json
            |-- OwnedHostPids.json
            |-- OwnedUnityPids.json
            |-- FinalResult.json
            |-- CodexPrompt.txt              (transient Developer prompt)
            `-- ReviewerCodexPrompt.txt      (transient Reviewer prompt)
```

`Repository` is a new standalone clone for that selected run. It is not a
linked worktree and it is never the user's normal checkout. The ownership
marker and run ID bind cleanup to this exact directory. `cancel.requested` is
created only when cancellation is requested; its contents are ignored.
The two prompt files are UTF-8 transport files owned by their respective
runners and are removed after the adapter returns; if one remains after an
abnormal failure, treat it as sensitive run state rather than a publishable
artifact.

The installer hashes the eight required runtime scripts, the reviewed source
configuration, and a generated `ExecutableIdentity.json`, stages all ten files
together in the bundle, writes `HostIntegrity.json`, applies a protected ACL,
then makes the task action point only at that staged entry point, staged config,
and manifest. The executable identity binds the absolute canonical path,
length, and SHA-256 of Git, Git LFS, GitHub CLI, Codex, PowerShell, and Unity
into the content-addressed bundle. Administrators and SYSTEM receive full
control; the task user receives read and execute only.
The source checkout and source config are installation inputs, not the mutable
runtime authority. Updating either requires a new installer run and a new
content-addressed bundle; never edit the installed copy in place.

`HighestAvailable` may initially give the protected entry point an elevated
token. That parent verifies the fixed PowerShell installation and the complete
bundle manifest, ACL, lengths, and hashes before loading the common library,
parsing configuration, or starting a configured tool. If elevated, it launches
the same protected entry point through the current account's linked
non-elevated token. The child proves the same SID, proves it is not elevated,
and repeats the full integrity check. A missing linked token, SID mismatch,
still-elevated child, missing manifest, or invalid child result fails closed;
the elevated parent never invokes Git, GitHub CLI, Codex, or Unity.
Before loading Common or configuration, both parent and child also rehash all
six exact executable paths against the protected identity. Common repeats the
matching path/length/hash check immediately before each bound tool launch, so a
PATH shadow or binary replacement cannot silently select a different process.
Installer and uninstaller startup also bind the running process and `PSHOME` to
the stable PowerShell 7 installation, replace `PSModulePath` with the exact
system module roots, verify Microsoft Authenticode/code-signing provenance and
unchanged hashes for the required Security and ScheduledTasks module files, and
invoke their commands by module-qualified name.

Every Developer and Reviewer lifecycle Git invocation uses an isolated system
and global configuration stack plus fixed command-scope overrides that disable
hooks, external diffs, fsmonitor, editors, signing, ambient credential/proxy
helpers, and non-HTTPS transports. The Host pins the exact GitHub CLI credential
helper and Git LFS process executable. The standalone clone then receives only
the configured `GitAuthorName` and `GitAuthorEmail`; it does not inherit author
identity or executable helpers from user-global Git configuration.

Git and GitHub CLI children also receive a credential-clean environment. The
Host removes inherited sensitive names and ambient repository/helper/askpass,
SSH, proxy, editor, pager, and routing controls before restoring only its fixed
safe overrides. Removed secret values are retained only in a bounded in-memory
redaction set so opaque child stdout/stderr cannot enter runner results or
failure artifacts; those values are never serialized.

`CodexEvents.jsonl` is not raw Codex JSONL. Each retained line contains only a
sequence number, syntax-constrained event/item types, and hashes of an item ID
or command where present. `CodexProcessSummary.json` retains the native exit,
timeout/cancellation flags, UTF-8 byte counts, and SHA-256 hashes. Raw Codex
stdout, stderr, agent messages, and command text are never artifacts. Only the
validated, schema-constrained `CodexResult.json` retains model-authored text.
The two owned-process ledgers can contain multiple `{ Id, StartTimeUtc }`
records; they are identity records rather than permission to kill a PID by
number alone.

`RunResult.json` and structured orchestrator output contain dispatch and runner
metadata only. They do not copy Issue/PR prose, conversations, handoff
`pendingCommand`, Codex findings, or raw linked-child output. The selected
content stays in access-controlled per-run state for the bounded runner and is
not promoted into the top-level retained result.

Run paths reported by structured output are authoritative if an implementation
version adds further files. Do not move a run directory while it is active.
The default artifact retention is 14 days. A cleanup failure retains the run
and its evidence for inspection instead of bypassing path or ownership guards.

Artifacts must never contain GitHub tokens, Codex credentials, save data, or a
copy of unrelated user-profile content.

## Components

The host implementation lives under `Tools/HostAutomation`:

- `Config.example.json` documents non-secret configuration.
- `HostAutomation.Common.ps1` contains shared validation, process, state, and
  safety helpers.
- `Get-SashimiProjectQueue.ps1` performs the read-only paginated selection.
- `Invoke-SashimiCodexExec.ps1` probes and invokes the Codex CLI and validates
  its JSONL and explicit result.
- `Invoke-SashimiDeveloperRun.ps1` implements New Work, ReviewFix, and
  DeliveryResume delivery.
- `Invoke-SashimiReviewerRun.ps1` implements independent synthetic-merge
  review without modifying or pushing the PR branch.
- `Invoke-SashimiUnityValidation.ps1` runs Unity and repository validation.
- `Publish-SashimiRunResult.ps1` publishes sanitized evidence and authorized
  status transitions.
- `Invoke-SashimiHostOrchestrator.ps1` owns selection and the run lifecycle.
- `Install-SashimiHostAutomation.ps1` stages and verifies the protected
  runtime bundle, then registers the scheduled host task.
- `Uninstall-SashimiHostAutomation.ps1` removes only that task and preserves
  installed bundles and run evidence.
- `Test-SashimiHostAutomation.ps1` and `Tests/` contain the offline fixture
  suite.

## Safe first review

From the repository root, review the example configuration and execute only
non-mutating checks first:

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$Config = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation\Config.json'

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 `
  -ConfigPath $Config -EnvironmentSmoke

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Install-SashimiHostAutomation.ps1 `
  -ConfigPath $Config -DryRun

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 `
  -ConfigPath $Config `
  -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json `
  -DryRun
```

The test command uses generated local fixtures and fake process boundaries. It
must not query or mutate live Issues or PRs, including #20, #26, and #30. A
fixture-only parameter is accepted only with `-DryRun` or when the owned test
harness sets `SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS=1`; fixture results are
reported as fixture data. The environment smoke checks local configuration,
tool discovery, and the Codex adapter's DryRun capability plan. It does not run
Unity or access live GitHub.

These commands are offline/no-mutation checks. Success does not claim a live
Project query, clone/fetch, Codex execution, Unity import/test run, push,
publication, Project transition, or scheduled-task registration was validated.
See [OPERATIONS.md](OPERATIONS.md) for configuration, installation, lifecycle,
cancellation, and removal procedures.

## State authority

The Host enforces the repository state machine; it does not expand it:

- Developer New Work: `Ready -> In Progress`, then `In Progress -> Review`
  only after successful delivery.
- Developer resume: remains `In Progress` while work runs, then
  `In Progress -> Review` after successful delivery.
- Reviewer with Blocker/Major: publish the finding and current ReviewFix
  handoff, then `Review -> In Progress`.
- Reviewer with no Blocker/Major and all automated checks passing: publish the
  Owner checklist, then `Review -> Verification`.
- Any required failure: do not advance status and do not push unsafe output.

Final visual, audio, input, feel, pacing, and save-flow verification remains a
human responsibility. The human owner also merges the PR, closes the Issue,
and performs the final transition to `Done`.

## Current operational limits

- `InteractiveToken` requires user `02031` to remain logged on; the task stores
  no password and cannot run while that user is logged off.
- Installer and update runs must stage and ACL-protect files under Program
  Files. Run them from an elevated PowerShell 7 session as the same `02031`
  identity after reviewing `-DryRun`.
- Installation is content-addressed and does not prune older bundles. The
  uninstaller removes only the scheduled task and preserves installed bundles,
  configuration copies, run state, and artifacts for deliberate Owner review.
- Retry cooldown is bounded within one task invocation, not persisted across
  later 15-minute invocations. Disable the task while investigating a repeating
  infrastructure failure.
- Issue-specific generator or preview hooks run only when an approved typed
  mapping exists in configuration. Natural-language Issue or handoff text
  cannot supply a command.
- Bootstrap validation exercises the privilege boundary through parser, static
  ordering, source-tree fail-closed, fixture, and DryRun checks only. It does
  not register the task or execute a real elevated-parent/linked-token relaunch;
  that remains an Owner-observed installation check after review.
