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
unelevated Windows sandbox, disabled workspace-write network access, and an
explicitly disabled shell/command-execution capability. Source changes may be
reported only through the structured file-change capability; any
`command_execution` event is a terminal policy violation.
Reviewer Codex runs are read-only. The Host, outside the Codex sandbox, owns
the bounded GitHub and Git network operations. The adapter performs one
no-op `exec --help` probe with the same no-user-config, strict-config,
no-rules, no-shell, and no-unified-exec prefix used for execution, and fails
closed if the installed CLI cannot provide the required contract. The flags
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

The installer hashes the eight required runtime scripts, its own bootstrap,
the canonical allowlisted configuration projection, and a generated
`ExecutableIdentity.json`. It also copies the reviewed Codex source binary to a
SHA-256-keyed distribution below Program Files. A non-DryRun installation is
authorized only when its recomputed complete identity equals the explicit
Owner-supplied `ExpectedBundleId` from the immediately preceding DryRun. The
elevated invocation must also supply the independently retained exact
`ExpectedInstallerSha256`. That value is computed by the Owner's trusted
in-memory launcher from an already-open read handle and compared before process
creation to the SHA-256 retained with the independent review; it is not
accepted from the installer's own output as its own authority. The launcher
denies write/delete sharing and keeps that handle open while exact protected
PowerShell starts the installer with `-File` and until it exits. The bootstrap
then checks the same hash at entry and again when capturing the immutable
bundle.
Bundle and Codex-distribution payloads are built in marker-owned sibling
directories, completely verified and ACL-protected, then exposed by atomic
directory rename. The task action points only at the staged entry point,
staged config, and manifest. The executable identity binds the absolute
canonical path, length, and SHA-256 of Git, Git LFS, GitHub CLI, protected
Codex, PowerShell, and Unity. Administrators and SYSTEM receive full control;
the task user receives the complete read-and-execute rights and no write
rights.
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
six exact executable paths against the protected identity. Codex additionally
must be exactly `CodexDistributions\<bound-sha256>\codex.exe`; every path
ancestor is checked for reparse traversal, and the executable plus each
ancestor through the protected `SashimiBoyAutomation` install root is checked
for both a trusted servicing owner and absence of untrusted write-like ACLs.
Common opens a read lease that denies write/delete sharing, hashes that open file, and
keeps the lease through process creation. A PATH shadow, task-user path swap,
or binary replacement therefore fails closed. An actor already holding local
administrator or SYSTEM authority remains outside this same-admin boundary.
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

Before Codex, after each Unity stage, and immediately before every commit, LFS,
or push boundary, the Developer compares complete Git-control state: canonical
git/common/worktree roots, HEAD/ref and upstream, all refs, index bytes and
staged tree, configuration and extensions, hooks, alternates, remotes including
fetch and push URLs, attributes/filter inputs, worktree identity, and every
`.git` control entry except object and LFS payload stores. Merge, cherry-pick,
revert, rebase, bisect, notes-merge, sequencer, lock, unknown-control, and
reparse state are terminal even if present in the first snapshot. Read-only Git
inspection runs with optional locks and automatic maintenance disabled. Each
Unity stage runs in a kill-on-close job and the Host confirms that no descendant
remains before trusting the post-stage snapshot. Drift is terminal and cannot
fall through to commit, push, comment, or Project transition. Network Git/LFS
commands use the immutable configured repository and `/info/lfs` URLs and exact
refspec rather than mutable `origin.pushurl`, `lfs.url`, remote-specific LFS URL,
or `.lfsconfig` state. Repository custom LFS transfer agents are rejected.

Git and GitHub CLI children also receive a credential-clean environment. The
Host removes inherited sensitive names and ambient repository/helper/askpass,
SSH, proxy, editor, pager, and routing controls before restoring only its fixed
safe overrides. Removed secret values are retained only in a bounded in-memory
redaction set so opaque child stdout/stderr cannot enter runner results or
failure artifacts; those values are never serialized.

Codex probes and execution share a stricter hermetic environment. Ambient
endpoint/base-URL, proxy (in every casing), CA bundle/directory, API-key,
configuration-home, credential-file, and `CODEX_HOME`-style variables are
removed. Only fixed OS bootstrap values reconstructed by the Host are supplied;
there is no configurable model endpoint in `Config.json`.

The adapter parses and security-audits bounded original Codex output in memory
before redaction. The unredacted stream is never persisted or returned.
Before every probe or execution launch, it also recursively rejects any
case-variant repository `.codex` tree or reparse point, preventing branch-owned
provider, endpoint, hook, plugin, MCP, or policy configuration from entering
the launch contract.
`CodexEvents.jsonl` is not raw Codex JSONL. Each retained line contains only a
sequence number, syntax-constrained event/item types, and hashes of an item ID
or command where present. `CodexProcessSummary.json` retains the native exit,
timeout/cancellation flags, UTF-8 byte counts, and SHA-256 hashes. Raw Codex
stdout, stderr, agent messages, and command text are never artifacts. Only the
validated, schema-constrained `CodexResult.json` retains model-authored text.
Unity raw logs and NUnit XML are read through stable, no-write-sharing,
strict-UTF-8 gates before sanitization and promotion. `Artifacts\Unity` has a
recursive exact manifest: logs are limited to 8 MiB each, XML to 16 MiB,
structured metadata to 4 MiB, PNG hooks to 25 MiB, and the complete tree to
128 MiB. An unexpected, changing, oversized, non-UTF-8, unregistered, or
reparse entry terminally fails validation; the whole public Unity artifact root
is atomically moved to run-owned State and removed without traversing a reparse
target, so unvalidated bytes do not remain publishable.
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
non-mutating checks first. Authenticate and run the installer DryRun with the
Owner-pinned Step 1 launcher in [OPERATIONS.md](OPERATIONS.md); do not invoke a
mutable installer file directly, even for preview.

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$Config = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation\Config.json'

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 `
  -ConfigPath $Config -EnvironmentSmoke

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 `
  -ConfigPath $Config `
  -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json `
  -DryRun
```

Record the exact `BundleId` and `InstallerBootstrapSha256` printed by the
installer DryRun from the reviewed checkout, but accept the bootstrap hash only
after it equals both the independently reviewed SHA-256 retained before preview
and the open-handle hash reported by the Owner's in-memory launcher. The later
elevated install must pass both retained values unchanged as `-ExpectedBundleId`
and `-ExpectedInstallerSha256`. The launcher reopens the same canonical
installer under a no-write/no-delete-sharing lease, verifies the external hash
before process creation, launches exact protected PowerShell with `-File`, and
retains the lease through exit. Any changed byte aborts before Program Files,
ACL, or scheduler mutation. See the exact two-step contract in
[OPERATIONS.md](OPERATIONS.md). Do not replace that in-memory trust anchor with
another repository script; doing so merely moves the unverified-bootstrap
problem to a new mutable wrapper.

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

- Developer New Work: remains `Ready` through untrusted execution and exact
  Git delivery checks, then `Ready -> In Progress` as its first Project
  mutation and `In Progress -> Review` only after successful PR publication.
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
