# Host Automation Security Model

The Host Orchestrator processes untrusted repository, Issue, pull-request, and
model output. Its core rule is that data may influence a typed Host plan but
must never become arbitrary shell input.

## Fixed trust boundary

Automatic execution is restricted to:

- repository `DongGyunLeeeee/sashimi-boy-unity`;
- Project owner `DongGyunLeeeee`, Project number `1`;
- base branch `main`;
- canonical remote
  `https://github.com/DongGyunLeeeee/sashimi-boy-unity.git`;
- the configured Windows identity `02031`;
- pull-request authors explicitly listed in
  `Security.AuthorizedPrAuthors`.

The Host validates those values instead of accepting repository or Project
identity from an Issue, PR URL, comment, handoff, Codex output, or artifact.
Project fields and options must exactly match the repository contract; the Host
does not create, rename, or guess schema.

The scheduled task executes only a content-addressed bundle below
`C:\Program Files\SashimiBoyAutomation\Bundles`. Its manifest pins the exact
runtime files, staged configuration, lengths, SHA-256 hashes, entry point, and
minimum PowerShell version. The bundle and every file use a non-inheriting ACL
owned by Administrators: Administrators and SYSTEM have full control, while
the `02031` task identity has read and execute only. The runtime verifies the
manifest before it trusts the staged configuration or starts work.

The manifest also covers `ExecutableIdentity.json`. That generated file binds
the absolute canonical path, byte length, and SHA-256 of Git, Git LFS, GitHub
CLI, Codex, PowerShell, and Unity. Bare names, PATH resolution,
missing/non-file targets, and reparse-point executable targets are rejected.
The protected entry point rehashes all six binaries before loading Common or
configuration; Common matches the installed config to those identities and
rehashes the exact bound path immediately before every launch. A PATH shadow or
changed length or hash fails before the process can cross the mutation boundary.

The task's `HighestAvailable` parent is an integrity bootstrap only. Before
loading `HostAutomation.Common.ps1`, parsing `Config.json`, or invoking any
configured executable, it verifies the stable PowerShell installation and the
installed bundle's location, ACL, manifest, lengths, and hashes. An elevated
parent must relaunch the same protected entry point with its linked
non-elevated token. The child must have the same SID, must prove that its token
is not elevated, and repeats the complete integrity verification. Missing or
invalid manifest evidence, a missing linked token, SID mismatch, an elevated
child, relaunch failure, or an invalid child result stops the run. The elevated
parent never runs Git, GitHub CLI, Codex, Unity, or repository content.

Installer and uninstaller initialization does not trust ambient module search
paths. It binds the running process, main module, and `PSHOME` to the stable
PowerShell 7 installation, replaces `PSModulePath` with exact system roots,
imports exact Security and ScheduledTasks manifests, validates their Microsoft
Authenticode signature and code-signing EKU, records hashes for their required
files, and rechecks those hashes before module-qualified ACL or scheduler calls.

## Pull-request admission

Before a PR can cause automatic work, and again before every mutation, the Host
requires all of the following:

- the PR is linked to the selected Issue through live GitHub data;
- it is open and Draft;
- its base repository is the fixed repository;
- its base ref is exactly `main`;
- it is not cross-repository and its head repository is not a fork;
- its author is explicitly authorized;
- the live authenticated GitHub CLI actor is exactly the configured Project
  owner;
- its current title/body content digest exactly matches the pinned digest;
- its live head SHA and head ref exactly match the pinned values;
- its Issue status, handoff, and other queue evidence are still eligible.

A URL is not proof of scope. A repository-name lookalike, fork, unauthorized
PR author or authenticated actor, changed Draft/state/base/title/body/ref/SHA,
missing page, or ambiguous linkage fails closed. A stale run performs no
delivery push, PR creation, or Project transition and does not select another
Issue. Sanitized failure evidence may be commented only after a separate live
pin and actor revalidation authorizes that exact comment mutation.

## Untrusted text stays inert

Issue titles, Issue bodies, comments, review bodies, labels, Codex messages,
and handoff values are data. They are never evaluated, dot-sourced, passed to
`Invoke-Expression`, or concatenated into a shell command.

In particular, a handoff `pendingCommand` is historical evidence only. The
Host may display and preserve it, but it never executes it. A DeliveryResume
uses a Host-owned validation operation selected from typed configuration and
the runtime mode. Natural-language text cannot add a command, executable,
argument, path, Project transition, test bypass, or cleanup target.

All native invocations use an executable plus an argument array. Logs store a
quoted representation for evidence; the logged representation is not replayed.
Paths are canonicalized and passed as literal paths.

The Codex adapter accepts exactly one inline Host parameter or strict UTF-8
prompt file, then sends the preamble and prompt to the Codex process over
standard input. The runners use transient `State\CodexPrompt.txt` or
`State\ReviewerCodexPrompt.txt` with `-PromptPath` and attempt to remove the
file as soon as the adapter returns. Prompt text is not a Codex command-line
argument, and a prompt file is not a publishable artifact.

## Git and GitHub authority

The Host may perform only the Git/GitHub operations required by the selected
role:

- fresh standalone clone and explicit fetch;
- local branch creation;
- normal merge of latest `origin/main` for Developer resume;
- local synthetic merge for Reviewer;
- focused commit and normal push for Developer only;
- Draft PR creation only for Developer New Work;
- evidence comments and exact allowed Project transitions.

Configuration import rejects any `Repository`, `ProjectOwner`, `ProjectNumber`,
`DefaultBranch`, `RemoteUrl`, `RunRoot`, or global mutex value outside the fixed
production contract. Developer clones also compare `origin` with the exact
configured canonical URL before delivery. The mandatory protected-path set
and minimum artifact-exclusion set cannot be removed from configuration.

Every Developer and Reviewer lifecycle Git process uses `GIT_CONFIG_NOSYSTEM`,
an empty global config, and a fixed command-scope configuration beginning with
the pre-clone audit and `clone`. It disables hooks, fsmonitor, external
diff/editor/signing/proxy helpers, ambient credential helpers, and non-HTTPS
transports, then pins the exact GitHub CLI credential helper and Git LFS
clean/smudge/process executable. Each new clone receives only the configured
author name/email. User-global and repository-controlled executable helpers are
therefore not authority at a Host Git boundary.

Before every Git or GitHub CLI launch, the shared process boundary removes all
inherited credential-, token-, secret-, key-, password-, askpass-, and
authorization-shaped environment variables in addition to ambient repository,
config/helper, SSH, proxy, editor, pager, and GitHub routing controls. Only the
fixed non-secret Host overrides are applied afterward. A bounded set of removed
secret values is held in memory only for exact-value redaction of child stdout
and stderr; it is never placed in a result, command record, failure artifact, or
other serialized evidence. Git and GitHub authentication therefore continues
through the current user's configured credential stores rather than inherited
token variables.

These operations are forbidden:

- `git reset --hard`;
- `git clean`;
- rebase;
- force or force-with-lease push;
- remote branch deletion;
- pushing a Reviewer integration or modifying the PR branch during review;
- merging a PR through GitHub;
- closing an Issue;
- moving any Issue to `Done`;
- selecting `Verification` or `Done` as work;
- creating a new Issue, PR, or remote feature branch in ReviewFix or
  DeliveryResume mode.

The Reviewer may transition `Review -> Verification` only after all automated
checks pass with no Blocker or Major. This is not a PR merge or a `Done`
transition. The human Owner alone performs final verification, merge, Issue
closure, and `Done`.

## Pinning and time-of-check protection

The selected Issue and PR identity are recorded in the per-run state before
work begins. The Host re-queries the authenticated actor, live eligibility, PR
state, title/body content digest, `head.sha`, and `head.ref`:

- after selection and before the workspace is trusted;
- before a Developer push;
- before a PR creation or evidence comment;
- before every Project transition.

For resume modes, the only permitted push destination is the originally
pinned, freshly revalidated existing head ref:

```text
git push origin HEAD:<existing-pr-head-ref>
```

Any mismatch stops the run. It is never resolved by force pushing, rebasing,
changing the destination, or selecting a replacement Issue.

Developer delivery separately pins the exact fetched `origin/main` commit used
for New Work branch creation or a resume merge. Before delivery mutations it
queries `refs/heads/main` directly; if the live main SHA advances, the run may
publish independently revalidated sanitized failure evidence but performs no
delivery push or Project transition.

## Codex containment and result validation

The adapter probes the installed Codex CLI instead of assuming flags. A
Developer run targets non-interactive `codex exec` with:

- `--ephemeral`;
- `--json`;
- `--color never`;
- `-C <fresh-clone>`;
- sandbox `workspace-write`;
- approval policy `never`;
- `windows.sandbox=unelevated`;
- workspace-write network access disabled;
- user configuration ignored under strict configuration;
- a Host-generated output schema.

Reviewer analysis is read-only. Codex is never given Host GitHub mutation
authority. The adapter fails if the installed CLI cannot enforce the required
sandbox/approval contract.

Every Codex `command_execution` start and completion is audited before the
turn can succeed. The audit permits only explicitly recognized read-only
inspection commands. It rejects Git mutation and unknown Git subcommands,
write-capable Git LFS operations, `gh` and direct GitHub API access, Task
Scheduler operations, unapproved interpreters and script-file wrappers,
network clients, encoded or dynamically evaluated PowerShell, redirection,
PowerShell providers, home aliases, rooted/UNC or parent-traversal paths,
direct `.git`/`.codex` and credential/profile paths, Git options that can
execute external helpers, ripgrep preprocessors, opaque grouping, and
unrecognized pipeline segments.
Production edits must arrive through the workspace file-change capability;
Git, GitHub, Unity, compilation, tests, and publication remain Host work.
This event audit is defense in depth. The unelevated, network-disabled OS
sandbox remains the primary Codex containment boundary.

Process exit code is necessary but insufficient. The Host parses every JSONL
record in memory, rejects malformed JSON, `error`, `turn.failed`, approval
requests, command mismatches, and unfinished command events, and requires one
explicit schema-valid result. Host validation must also pass. A final message
claiming success cannot override either JSONL or Host evidence.

`danger-full-access` and
`dangerously-bypass-approvals-and-sandbox` are prohibited in configuration,
arguments, prompts, results, and retry paths.

Unity and editor C# are a separate residual trust boundary. The Host starts
them only after canonical repository, PR-author, ref/SHA, protected-scope, and
workflow gates, and only under the same user's non-elevated token. They do not
run inside the Codex OS sandbox or another mandatory OS isolation boundary.
Accordingly, this design does not claim to contain arbitrary hostile Unity or
editor code. A threat model that requires that containment should run the Host
under a dedicated least-privilege Windows account and add VM/container or
equivalent OS isolation before enabling unattended Unity execution.

## Process and filesystem containment

Task Scheduler `IgnoreNew` and one global named mutex prevent overlapping Host
runs. Every run has an unguessable ID, an ownership marker named
`.sashimi-host-run.json`, structured state, and a dedicated clone at:

```text
%LOCALAPPDATA%\SashimiBoyAutomation\Runs\<run-id>\Repository
```

The Host records all children that it creates in multi-entry ledgers. Each
identity contains both a PID and its UTC process start time to detect PID reuse.
On timeout, cancellation, or final cleanup, the Host rechecks that complete
identity, requests process-tree termination, waits for confirmed exit, and
removes the record only after confirmation. An identity mismatch or
unconfirmed termination preserves the ledger, workspace, and evidence.
`Get-Process Unity` is a secondary diagnostic, not an authority to kill
arbitrary Unity processes, and CIM process enumeration is not a mandatory gate.

Recursive cleanup requires all of these to agree immediately before deletion:

- the exact configured run-root boundary;
- a child directory rather than the run root itself;
- the expected ownership marker and run ID;
- the expected canonical lexical path;
- a complete tree containing no reparse point;
- no active run-owned process using the workspace.

If any check or deletion fails, preserve the workspace and evidence. Never
weaken the guard to make cleanup succeed.

Unity raw validation state uses a closed allowlist for Host-owned temporary
entries. An unexpected file, directory, or reparse point is treated as possible
sensitive evidence: validation and cleanup fail, the entry remains outside the
publishable `Artifacts` tree, and diagnostics omit its path and contents.

## Credentials and artifacts

`Config.json` contains no secret. Authentication is obtained through the
current user's approved GitHub CLI, Git credential, and Codex credential
stores. Task registration uses `InteractiveToken` and stores no password.
Before starting Codex, the Host replaces the inherited process environment
with a narrow operational allowlist and removes token-, password-, secret-,
credential-, and key-shaped variables. In particular, `OPENAI_API_KEY`,
`CODEX_API_KEY`, GitHub tokens, askpass helpers, SSH agent variables, cloud
credentials, and authenticated proxy values are not inherited. Codex runtime
authentication must therefore use its current-user credential store;
environment-only API-key authentication is intentionally unsupported. Profile
locator variables needed to locate that store remain available, but command
auditing forbids Codex from reading profile or credential content.
Environment stripping, path scanning, and redaction are defense in depth; they
do not turn same-user Unity/editor execution into an OS sandbox.

Immediately before each Host staging boundary and again after final staging,
the Developer runner scans the exact NUL-delimited changed paths and both ends
of staged rename/copy records. It refuses unsafe/private paths, reparse points,
recognizable credential material, current profile/save paths, and exact values
from sensitive inherited Host environment variables. A failure occurs before
commit or push and reports no matched value.

Before publishing or retaining artifacts, sanitize command output and paths.
Artifacts must not include:

- GitHub tokens or authorization headers;
- Git/Codex credentials, cookies, or credential-store contents;
- environment dumps;
- save data;
- arbitrary files from the user profile;
- unrelated repository content;
- raw secret-bearing command lines.

Prefer allowlisted evidence files over denylist-only redaction: structured run
state, sanitized command summaries, Unity logs and result XML, validation
summaries, focused diffs, screenshots/previews requested by validation, and
cleanup diagnostics. Treat artifacts as sensitive even after sanitization.
The structured Unity summary is
`Artifacts\Unity\UnityValidation.Summary.json`. Under `Artifacts\Codex`,
`CodexEvents.jsonl` retains only event sequencing/types and hashes of item IDs
or command text; `CodexProcessSummary.json` retains only exit state, UTF-8 byte
counts, and SHA-256 hashes. Raw Codex stdout, stderr, command text, and agent
messages are never written as artifacts. Only the validated, constrained
`CodexResult.json` may retain model-authored text alongside its schema. Failure
diagnostics use stable Host codes, bounded numeric/boolean metadata, UTF-8 byte
counts, and SHA-256 hashes only; probe stderr, event IDs, command tokens, and
rejected result values are never copied into an error or failure artifact. The
live capability version line is syntax- and length-validated before retention.
Top-level `RunResult.json` and orchestrator output retain only allowlisted
selection/runner metadata; they omit Issue/PR bodies and titles, conversations,
findings, `pendingCommand`, and raw linked-child streams.

## Scheduler security

The installed task contract is fixed:

- task name `SASHIMI BOY Host Orchestrator`;
- current user `02031`;
- `InteractiveToken`, run only while the user is logged on;
- highest available privileges;
- stable PowerShell 7 executable;
- no stored password;
- repeat every 15 minutes, start when available, wake to run;
- multiple-instance policy `IgnoreNew`.

Always inspect installer `-DryRun` output before registration. Never replace
the action with an encoded Issue/comment/model-supplied command. The task points
at the staged entry point, staged configuration, and integrity manifest inside
one verified bundle, never the editable source checkout or source config. A
code or configuration update requires another reviewed installer run; do not
grant the task identity write access or edit an installed bundle in place.
The bootstrap suite validates this privilege boundary with parser/static order,
source-tree fail-closed, fixture, and DryRun checks. It deliberately does not
register the task or execute a real elevated-parent/linked-token relaunch;
that behavior requires Owner observation during the later reviewed install.

## Fixture and DryRun isolation

Fixture execution is allowed only with `-DryRun` or while the owned test suite
sets `SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS=1`, and is visibly labeled
`Fixture`. A normal non-DryRun invocation rejects fixture paths and dependency
overrides. The self-contained suite uses fake GraphQL, Git, Codex, Unity, and
Task Scheduler boundaries with mutation sentinels.

`-DryRun` emits structured plan output. It performs no live clone, push,
PR/Issue comment, PR creation, Project mutation, task registration, or task
removal. Tests use synthetic Issue numbers and assert that live #20, #26, and
#30 never appear in a mutation invocation. DryRun, fixture-suite, and
environment-smoke success is not a claim that live GitHub, Git, Codex, Unity,
or Task Scheduler execution was validated.

Report a suspected security failure as described in
[TROUBLESHOOTING.md](TROUBLESHOOTING.md), preserve the owned run directory, and
do not retry until the cause is understood.
