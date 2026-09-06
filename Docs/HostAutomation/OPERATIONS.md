# Host Automation Operations

This runbook covers configuration, installation, routine operation,
cancellation, retention, and removal of the SASHIMI BOY Windows Host
Orchestrator.

## Prerequisites

- Windows user `02031` is the interactive task identity.
- PowerShell Core `7.5.0` or newer is installed at
  `C:\Program Files\PowerShell\7\pwsh.exe`. The installer probes that exact
  executable and fails closed on an older version.
- Unity `6000.4.0f1` is installed at the configured path.
- Git, Git LFS, GitHub CLI, and Codex CLI are available to the same Windows
  user that runs the task.
- GitHub CLI and Codex authentication have already been established for that
  user. Do not place credentials in `Config.json`.
- The reviewed source checkout contains the complete `Tools/HostAutomation`
  runtime file set. It is an installation input; the scheduled task does not
  execute from that checkout.
- A reviewed, non-secret source `Config.json` exists for installation or
  update. The installer stages an immutable copy with the runtime scripts.
- No second Unity Editor process opens the same per-run `Repository` directory.

Task Scheduler uses `InteractiveToken`, so unattended here means no Codex
Desktop automation is required; Windows user `02031` must still be logged on.
No password is stored with the task.

## Configure

Use a local configuration outside the repository so it cannot be committed:

```powershell
$RepositoryRoot = (Get-Location).ProviderPath
$AutomationRoot = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation'
$ConfigPath = Join-Path $AutomationRoot 'Config.json'

New-Item -ItemType Directory -Path $AutomationRoot -Force | Out-Null
Copy-Item -LiteralPath `
  (Join-Path $RepositoryRoot 'Tools\HostAutomation\Config.example.json') `
  -Destination $ConfigPath
notepad.exe $ConfigPath
```

This is the Owner-edited source config. Installation copies it into the
content-addressed protected bundle. A later edit has no effect on the task
until the Owner reviews installer `-DryRun` again and installs a new bundle.

Review every value. The configuration contract includes:

- `SchemaVersion`: `1`;
- `Repository`: `DongGyunLeeeee/sashimi-boy-unity`;
- `RemoteUrl`:
  `https://github.com/DongGyunLeeeee/sashimi-boy-unity.git` exactly;
- `ProjectOwner`: `DongGyunLeeeee`;
- `ProjectNumber`: `1`;
- `DefaultBranch`: `main`;
- `RunRoot`: normally
  `%LOCALAPPDATA%\SashimiBoyAutomation\Runs`;
- `ArtifactRetentionDays`: `14` by default;
- absolute canonical, local `.exe` paths for PowerShell, Unity, Git, Git LFS,
  GitHub CLI, and the reviewed Codex installation source exactly as shown in
  `Config.example.json`; bare names and PATH lookup are rejected. The installer
  copies Codex to a SHA-keyed protected Program Files distribution and the
  installed configuration references only that copy;
- `GitAuthorName` and `GitAuthorEmail`, the fixed identity written only to each
  new run clone for Host-owned commits and synthetic merges;
- `Security.AuthorizedPrAuthors`, the explicit PR-author allowlist;
- all immutable `Security.ProtectedPathPatterns` and required
  `Security.ArtifactExclusionPatterns` from `Config.example.json`;
- Codex and Unity stage timeouts;
- retry limit and retry cooldown;
- approved issue-specific validation mappings.

Environment-variable expansion in path values is performed by the Host only
where the schema allows it. Do not add tokens, passwords, Codex credentials,
save data, arbitrary command strings, endpoint/proxy/CA/auth-location fields,
or user-profile files to the config. Configuration JSON is an exact recursive
schema: duplicate keys (including case variants), unknown keys, wrong shapes,
and secret-bearing fields are rejected. The installed copy is a canonical
allowlisted projection, never the raw source bytes.

## Validate before installation

Run the parser, offline fixture suite, environment smoke, and orchestrator
preview from the repository root. Run the installer preview only through the
Owner-pinned Step 1 launcher in
[Install the scheduled task](#install-the-scheduled-task); even a DryRun
bootstrap must be authenticated before PowerShell begins executing it.

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$ConfigPath = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation\Config.json'

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 `
  -ConfigPath $ConfigPath -EnvironmentSmoke

& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 `
  -ConfigPath $ConfigPath `
  -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json `
  -DryRun
```

Inspect the structured result of every command. `-DryRun` must not register a
task, clone, push, comment, create a PR, or change Project state. The offline
suite owns its temporary fixtures, substitutes fake GitHub/Git/Codex/Unity and
scheduler boundaries, and never touches live #20, #26, or #30. The
`-EnvironmentSmoke` invocation checks configuration, executable discovery,
the Task Scheduler command's availability, and a Codex adapter DryRun; it does
not invoke Unity, query GitHub, or change Task Scheduler.

Fixture inputs are allowed only with `-DryRun` or when the owned fixture suite
sets `SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS=1`. Do not set that environment
variable for ordinary runtime execution.

These are no-mutation checks, not evidence that a live Project query, clone,
Codex turn, Unity test, GitHub mutation, or scheduled task has run successfully.

## Install the scheduled task

First review the installer DryRun output. It must describe exactly:

- task name `SASHIMI BOY Host Orchestrator`;
- user `02031` with `InteractiveToken`;
- highest available run level;
- a 15-minute repetition interval;
- start when available and wake to run;
- multiple-instance policy `IgnoreNew`;
- executable `C:\Program Files\PowerShell\7\pwsh.exe`;
- PowerShell Core version `7.5.0` or newer;
- a SHA-256 content-addressed bundle below
  `C:\Program Files\SashimiBoyAutomation\Bundles`;
- a SHA-256-keyed Codex distribution below
  `C:\Program Files\SashimiBoyAutomation\CodexDistributions`;
- a deterministic lowercase 64-character `BundleId` covering the runtime,
  canonical config, executable identities, Codex source identity, and installer
  bootstrap identity;
- a `HostIntegrity.json` manifest covering the exact runtime scripts, staged
  `Config.json`, and generated `ExecutableIdentity.json`, including file
  lengths and SHA-256 hashes;
- six executable identities binding each configured tool's canonical path,
  length, and SHA-256;
- a non-inheriting ACL owned by Administrators, with Administrators and SYSTEM
  full control and the `02031` task identity read and execute only;
- `-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass`, the staged
  orchestrator path, staged config path, and integrity-manifest path;
- no stored password.

The installer rejects missing, relative, non-file, or reparse-point executable
targets, hashes all six tools, validates the source files without following a
reparse point, computes the complete bundle identity, and renders the Task
Scheduler XML. It prepares bundle and Codex payloads in marker-owned sibling
staging directories, verifies their closed file sets and ACLs, and makes them
visible only by atomic rename. The protected entry point rechecks every
executable before it loads Common or configuration; Common rechecks a bound
executable immediately before each launch. Codex additionally requires a
no-reparse, non-user-writable Program Files ancestry and is held under a
no-write/no-delete-sharing lease through process creation.
Installer and uninstaller startup additionally require the stable PowerShell
process and matching `PSHOME`, replace `PSModulePath` with the exact PowerShell
and Windows system module roots, verify Microsoft Authenticode/code-signing
provenance and unchanged hashes for required Security and ScheduledTasks module
files, and resolve the ACL/scheduler commands by module-qualified name.
`-DryRun` reports the bundle ID, bootstrap identity, hashes, ACL plan, detected PowerShell version,
and task XML without creating a directory, changing an ACL, or registering a
task. Its result therefore keeps `Staged`, `AclVerified`, and `HashesVerified`
false even though source hashes were computed. Outside `-DryRun`, it stages
and re-verifies the bundle, then registers or replaces the task. By default the
source orchestrator is the script beside the installer; use
`-OrchestratorPath` only for another reviewed source folder that contains the
complete sibling runtime set.

Do not edit files below `C:\Program Files\SashimiBoyAutomation\Bundles`.
To update code or configuration, review and run the installer again; changed
content creates a new bundle ID, after which the installer replaces the task
definition. Older bundles are retained for deliberate Owner inspection.

Before preview, obtain the exact installer SHA-256 recorded with the reviewed
PR/main commit and keep it outside the mutable checkout. After the preview
matches that contract, the Owner records its exact `BundleId` and supplies both
values unchanged to the install. The installer's self-reported hash is a
cross-check only; a script cannot establish its own trust before PowerShell has
already read and begun executing it.

Run both steps from a newly opened, elevated, exact
`C:\Program Files\PowerShell\7\pwsh.exe -NoLogo -NoProfile` session as `02031`.
Paste the following reviewed function directly into that session. Do not save
it as another repository script: a file-backed launcher would only move the
unverified-bootstrap problem to that new mutable wrapper.

```powershell
function Invoke-OwnerPinnedInstaller {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string[]]$InstallerArgumentList,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedInstallerSha256
  )

  $TrustedPwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
  if (-not [string]::Equals(
      [IO.Path]::GetFullPath([Environment]::ProcessPath),
      $TrustedPwsh,
      [StringComparison]::OrdinalIgnoreCase)) {
    throw "Run the Owner launcher only inside exact protected PowerShell: $TrustedPwsh"
  }

  $Installer = [IO.Path]::GetFullPath($InstallerPath)
  if (-not [IO.File]::Exists($Installer)) {
    throw 'The reviewed installer path is not an existing file.'
  }

  $Lease = $null
  $Process = $null
  $Document = $null
  try {
    # FileShare.Read permits the child to read -File, but denies every writer
    # and delete/rename handle. Keep this external lease through child exit.
    $Lease = [IO.FileStream]::new(
      $Installer,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::Read)

    $Cursor = $Installer
    while (-not [string]::IsNullOrWhiteSpace($Cursor)) {
      if (([IO.File]::GetAttributes($Cursor) -band
          [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The installer path or an ancestor is a reparse point.'
      }
      $Parent = [IO.Path]::GetDirectoryName($Cursor)
      if ([string]::IsNullOrWhiteSpace($Parent) -or
          [string]::Equals($Parent, $Cursor, [StringComparison]::OrdinalIgnoreCase)) {
        break
      }
      $Cursor = $Parent
    }

    $Lease.Position = 0
    $ExternalSha256 = [Convert]::ToHexString(
      [Security.Cryptography.SHA256]::HashData($Lease)).ToLowerInvariant()
    if ($ExpectedInstallerSha256 -cne $ExternalSha256) {
      throw 'The installer differs from the independently retained Owner hash.'
    }

    # Recheck the lexical path after acquiring the lease and immediately before
    # process creation. The open leaf prevents a non-admin rename/replacement.
    $Cursor = $Installer
    while (-not [string]::IsNullOrWhiteSpace($Cursor)) {
      if (([IO.File]::GetAttributes($Cursor) -band
          [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The installer path changed to a reparse traversal.'
      }
      $Parent = [IO.Path]::GetDirectoryName($Cursor)
      if ([string]::IsNullOrWhiteSpace($Parent) -or
          [string]::Equals($Parent, $Cursor, [StringComparison]::OrdinalIgnoreCase)) {
        break
      }
      $Cursor = $Parent
    }

    $Start = [Diagnostics.ProcessStartInfo]::new()
    $Start.FileName = $TrustedPwsh
    $Start.UseShellExecute = $false
    $Start.CreateNoWindow = $true
    $Start.RedirectStandardOutput = $true
    $Start.RedirectStandardError = $true
    $Start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $Start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $Start.Environment.Clear()

    $Windows = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    $System = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $LocalAppData = [Environment]::GetFolderPath(
      [Environment+SpecialFolder]::LocalApplicationData)
    $SafeEnvironment = [ordered]@{
      SystemRoot = $Windows
      WINDIR = $Windows
      ProgramData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonApplicationData)
      ProgramFiles = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)
      'ProgramFiles(x86)' = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
      CommonProgramFiles = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonProgramFiles)
      'CommonProgramFiles(x86)' = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonProgramFilesX86)
      USERPROFILE = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::UserProfile)
      LOCALAPPDATA = $LocalAppData
      APPDATA = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ApplicationData)
      TEMP = [IO.Path]::Combine($LocalAppData, 'Temp')
      TMP = [IO.Path]::Combine($LocalAppData, 'Temp')
      COMSPEC = [IO.Path]::Combine($System, 'cmd.exe')
      PATH = [string]::Join([IO.Path]::PathSeparator, @(
        [IO.Path]::GetDirectoryName($TrustedPwsh), $System, $Windows))
      PSModulePath = [string]::Join([IO.Path]::PathSeparator, @(
        [IO.Path]::Combine([IO.Path]::GetDirectoryName($TrustedPwsh), 'Modules'),
        [IO.Path]::Combine($Windows, 'System32', 'WindowsPowerShell', 'v1.0', 'Modules')))
    }
    foreach ($Name in $SafeEnvironment.Keys) {
      if (-not [string]::IsNullOrWhiteSpace([string]$SafeEnvironment[$Name])) {
        $Start.Environment[[string]$Name] = [string]$SafeEnvironment[$Name]
      }
    }

    foreach ($Argument in @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $Installer) +
        $InstallerArgumentList) {
      [void]$Start.ArgumentList.Add([string]$Argument)
    }

    $Process = [Diagnostics.Process]::new()
    $Process.StartInfo = $Start
    if (-not $Process.Start()) { throw 'Protected PowerShell did not start.' }
    $StdoutTask = $Process.StandardOutput.ReadToEndAsync()
    $StderrTask = $Process.StandardError.ReadToEndAsync()
    $Process.WaitForExit()
    $Stdout = $StdoutTask.GetAwaiter().GetResult()
    $Stderr = $StderrTask.GetAwaiter().GetResult()
    if ($Process.ExitCode -ne 0) {
      throw "Pinned installer exited $($Process.ExitCode): $Stderr"
    }

    $Document = [Text.Json.JsonDocument]::Parse($Stdout)
    $Root = $Document.RootElement
    $Success = $Root.GetProperty('Success').GetBoolean()
    $ReportedExitCode = $Root.GetProperty('ExitCode').GetInt32()
    $BundleId = $Root.GetProperty('BundleId').GetString()
    $ReportedInstallerSha256 =
      $Root.GetProperty('InstallerBootstrapSha256').GetString()
    if (-not $Success -or $ReportedExitCode -ne 0) {
      throw 'Installer JSON did not report Success=true and ExitCode=0.'
    }
    if ($ReportedInstallerSha256 -cne $ExternalSha256) {
      throw 'Installer self-report differs from the external open-handle hash.'
    }
    if ($BundleId -cnotmatch '^[0-9a-f]{64}$') {
      throw 'Installer JSON did not contain an exact BundleId.'
    }

    [pscustomobject][ordered]@{
      Success = $true
      BundleId = $BundleId
      ExternalInstallerSha256 = $ExternalSha256
      InstallerBootstrapSha256 = $ReportedInstallerSha256
      ResultJson = $Root.GetRawText()
    }
  }
  finally {
    if ($null -ne $Document) { $Document.Dispose() }
    if ($null -ne $Process) { $Process.Dispose() }
    if ($null -ne $Lease) { $Lease.Dispose() }
  }
}
```

Step 1 — from the exact commit whose PR/main diff was reviewed, acquire the
external lease and run the real installer DryRun through `pwsh -File`:

```powershell
$InstallerPath = [IO.Path]::GetFullPath(
  '.\Tools\HostAutomation\Install-SashimiHostAutomation.ps1')
$ConfigPath = Join-Path $env:LOCALAPPDATA 'SashimiBoyAutomation\Config.json'
$OrchestratorPath = [IO.Path]::GetFullPath(
  '.\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1')
$ReviewedInstallerSha256 = '<exact SHA-256 from the independent review evidence>'

$Preview = Invoke-OwnerPinnedInstaller `
  -InstallerPath $InstallerPath `
  -ExpectedInstallerSha256 $ReviewedInstallerSha256 `
  -InstallerArgumentList @(
    '-ConfigPath', $ConfigPath,
    '-OrchestratorPath', $OrchestratorPath,
    '-DryRun')
$Preview.ResultJson
$Preview | Select-Object BundleId, ExternalInstallerSha256,
  InstallerBootstrapSha256
```

Confirm `Success=True`, inspect `ResultJson`, and confirm the independently
reviewed, external-handle, and reported installer hashes are all identical.
Record the exact lowercase 64-character `BundleId` outside the mutable checkout.

Step 2 — type or paste those two reviewed values explicitly. The second call
must not derive either authorization value from a new installer self-report:

```powershell
$ExpectedBundleId = '<exact reviewed BundleId from step 1>'
$ExpectedInstallerSha256 = $ReviewedInstallerSha256
if ($ExpectedBundleId -cnotmatch '^[0-9a-f]{64}$' -or
    $ExpectedInstallerSha256 -cnotmatch '^[0-9a-f]{64}$') {
  throw 'Both exact Owner authorization identities are required.'
}

$Install = Invoke-OwnerPinnedInstaller `
  -InstallerPath $InstallerPath `
  -ExpectedInstallerSha256 $ExpectedInstallerSha256 `
  -InstallerArgumentList @(
    '-ConfigPath', $ConfigPath,
    '-OrchestratorPath', $OrchestratorPath,
    '-ExpectedBundleId', $ExpectedBundleId,
    '-ExpectedInstallerSha256', $ExpectedInstallerSha256)
$Install.ResultJson
```

The external lease prevents a task-user process from replacing, rewriting, or
deleting the installer between the independent hash and any of its execution.
The second call refuses a changed installer before process creation; the
installer then refuses any changed payload before Program Files creation, ACL
changes, or Task Scheduler. An actor already holding local administrator or
SYSTEM authority remains outside this same-administrator threat boundary. Do
not substitute another task identity, provide a stored password, or recalculate
and accept new identities after an unexpected mismatch; return to reviewed
source and investigate first.

Read-only inspection commands are:

```powershell
Get-ScheduledTask -TaskName 'SASHIMI BOY Host Orchestrator' |
  Select-Object TaskName, State

Export-ScheduledTask -TaskName 'SASHIMI BOY Host Orchestrator'
```

The exported definition should show `InteractiveToken`, `HighestAvailable`,
`PT15M`, `StartWhenAvailable`, `WakeToRun`, and `IgnoreNew`.

## Run lifecycle

One task invocation performs these phases:

1. Acquire the global named mutex. If another instance owns it, return a
   successful `AlreadyRunning` no-op. Task Scheduler's `IgnoreNew` is the first
   layer; the mutex is the process-level layer.
2. Verify the installed manifest, exact bundle hashes, PowerShell and module
   provenance, configuration, executable capabilities, the authenticated
   GitHub CLI actor, retry cooldown, and cancellation state.
3. Read all ProjectV2 pages with explicit UTF-8, validate the exact Project
   schema, sort the four queue classes, and select at most one Issue.
4. Pin the Issue and, when present, the exact PR number, URL, base, head
   repository, author, Draft state, title/body content digest, head SHA, and
   head ref.
5. Create a marker-owned run directory and clone into
   `Runs\<run-id>\Repository`.
6. Revalidate the pin and execute exactly one Developer or Reviewer workflow.
7. Validate bounded original Codex JSONL and its explicit result in memory
   before redaction or persistence, then run all required Host checks.
8. Revalidate complete Git-control state after each Unity stage and immediately
   before commit, LFS, and push. Use the configured canonical repository URL
   and an exact source/destination refspec, never mutable `origin.pushurl`.
   Git LFS pull/push also uses the immutable canonical `/info/lfs` endpoint;
   repository-local LFS URLs, custom transfer agents, and `.lfsconfig` are
   terminal failures, and implicit smudging is disabled before the pinned pull.
   Every `.git` control entry except object/LFS payload data is manifested;
   operation pseudorefs/directories, lock files, unknown control state, and
   reparses fail closed, while optional locks and automatic maintenance are
   disabled for Host inspection.
9. Revalidate the authenticated actor and complete pin immediately before every
   remote mutation. Developer delivery also pins the fetched `origin/main` SHA
   and stops before any later delivery push or status transition if the live
   main ref advances.
10. Publish sanitized evidence and only the role-authorized transition.
11. Record the terminal state and clean run-owned processes and eligible
    temporary resources in `finally`.

`State\RunState.json` is the current machine-readable state;
`State\Events.json` is its event trail; `State\Selection.json` records the one
pinned queue result; `State\OwnedHostPids.json` and
`State\OwnedUnityPids.json` record every process launched by that run as a
possibly multi-entry `{ Id, StartTimeUtc }` ledger; and
`State\FinalResult.json` is the terminal structured result. The Developer and
Reviewer runners may briefly create `State\CodexPrompt.txt` and
`State\ReviewerCodexPrompt.txt`, respectively, as strict UTF-8 input and
remove them after the adapter returns.

`Artifacts\RunResult.json` mirrors the metadata-only terminal result. Its
selection and runner summaries retain dispatch identity, pins, outcome flags,
and counts, not Issue/PR prose, conversations, handoff commands, findings, or
raw child streams. Codex evidence under
`Artifacts\Codex` consists of the result schema, validated result, content-free
event metadata, and a content-free process summary. `CodexEvents.jsonl`
contains only sequence/type metadata and hashes; `CodexProcessSummary.json`
contains exit state, byte counts, and hashes. Raw Codex stdout, stderr, command
text, and agent messages are deliberately not retained. Unity evidence is
under `Artifacts\Unity`, whose summary is `UnityValidation.Summary.json`.
That public tree is a recursive exact manifest. Sanitized logs are limited to
8 MiB each, strict NUnit XML to 16 MiB, summaries/diffs/snapshots to 4 MiB,
allowlisted PNG screenshot or preview hooks to 25 MiB, and the complete tree to
128 MiB. The Host measures promoted files twice under no-write-sharing handles.
An unexpected, changing, oversized, invalid-UTF-8, missing, unregistered, or
reparse entry removes the entire public Unity tree through an atomic State
quarantine and no-reparse-traversal deletion before the terminal failure is
reported.
Mode-dependent evidence may include
`DraftPullRequest.md`, `HandoffCompletion.md`, `Failure.md`,
`ReviewFinding.md`, `ReviewFixHandoff.md`, `OwnerVerificationChecklist.md`, or
`ReviewerFailure.md`. Host output supplies the authoritative artifact paths
and native exit information. Never infer PASS from an exit code without the
required JSON, XML, logs, and summaries.

## Retries and cooldown

Live queue lookup retries within one Task invocation are bounded by the
configured retry limit and interruptible cooldown. The final run result keeps
only the allowlisted failure/outcome metadata supplied by the child; detailed
sanitized evidence stays in its bounded run artifacts. Cooldown is not
persisted across separate 15-minute Task
invocations; operators should disable the task while diagnosing a repeating
infrastructure failure.

A retry never relaxes pin checks, changes queue priority, or falls through to a
second Issue. Product decisions, missing source assets, and other non-transient
blockers are not retried as infrastructure errors. Failed work remains in its
current Project state: New Work remains `Ready` until untrusted execution and
exact Git delivery checks finish, Developer resume work remains `In Progress`,
and Reviewer infrastructure failures remain `Review`.

## Cancel an active run

Use the exact active run directory reported in `State\RunState.json` or task
output.
Request cooperative cancellation by creating a marker; its content is never
executed:

```powershell
$RunPath = 'C:\exact\reported\Runs\<run-id>'
New-Item -ItemType File -LiteralPath `
  (Join-Path $RunPath 'cancel.requested') -Force
```

The Host checks the marker between phases and stops starting new work. Before
terminating a child, it requires both the recorded PID and exact UTC process
start time to match, requests full process-tree termination, waits for confirmed
exit, and only then removes that ledger entry. If identity or termination
cannot be confirmed, the entry and run are preserved. Do not place a command
in the marker. Do not kill unrelated Unity processes or use a broad
process-name kill.

If cancellation cannot complete, preserve the run and follow the incident
procedure in [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Host validation gate

Before a Developer push or Reviewer transition, the Host inspects:

- clean import and C# compile;
- full EditMode and full PlayMode suites;
- Issue-specific generator validation and determinism when configured;
- native exit code and strict NUnit XML agreement;
- timeout and crash evidence;
- bounded strict-UTF-8 Unity logs/XML and the recursive exact public-artifact
  manifest, including per-file and 128 MiB total quotas;
- `git diff --check`;
- Git LFS availability and pointer integrity;
- missing, duplicate, and orphan `.meta` files and duplicate GUIDs;
- Missing Script and serialized-reference evidence;
- protected production-scope changes;
- requested screenshot or preview hooks.

Git processes do not inherit system or user-global Git configuration. The Host
supplies a fixed isolated configuration stack that disables hooks, fsmonitor,
external diff/editor/signing/proxy helpers and pins the exact GitHub CLI
credential helper, Git LFS executable, and canonical LFS download/upload URL.
Each run clone receives only the configured Git author name and email. Unity LFS
inspection invokes the exact bound `GitLfsExecutable` directly.

Zero tests, skipped tests, missing XML, malformed or negative counts,
native/XML disagreement, unexpected Console errors, and out-of-scope mutation
are failures. Screenshot and preview artifacts support human review; they do
not replace human verification. The structured Unity report is written to
`Artifacts\Unity\UnityValidation.Summary.json` in a non-DryRun run.

Residual boundary: Unity and editor C# run only after the Host's canonical
repository, authorized-author, exact ref/SHA, workflow, and protected-scope
gates, under the same user's non-elevated token. They are not placed inside a
separate OS sandbox. Sensitive-environment stripping and artifact scanning are
defense in depth, not containment for arbitrary hostile editor code. Operators
with that stronger threat model should use a dedicated least-privilege Windows
account plus VM/container or equivalent OS isolation before unattended Unity
execution.

## Retention and cleanup

After a successful run, immediate cleanup removes only that run's `Repository`
directory; state and artifacts remain. Runs older than 14 days are removed by
retention by default. Retention may remove only a directory below the
configured `RunRoot` whose lexical path, `.sashimi-host-run.json` marker, run
ID, and full no-reparse-point tree all match. It never deletes the run root
itself.

If cleanup validation or deletion fails, the run is retained and the cleanup
error is written to its evidence. Investigate and remove it only after checking
the ownership marker and ensuring no process uses `Repository`. Never bypass a
reparse-point or path-boundary refusal.

Unity raw validation state has a closed cleanup allowlist. Any unexpected file,
directory, or reparse point fails validation and is preserved outside the
publishable `Artifacts` tree for investigation; the Host does not delete,
publish, or silently normalize it.

Public Unity artifacts have a separate closed-tree rule. If their exact
manifest, stable hash/length, encoding, per-file quota, total quota, or
no-reparse invariant fails, the Host atomically renames that entire public root
into the run's State directory and deletes the quarantine without following
reparse targets. If either rename or guarded deletion cannot be confirmed, the
run remains failed and no artifact from that root is publishable.

## Uninstall

Preview removal while preserving all run artifacts:

```powershell
$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Uninstall-SashimiHostAutomation.ps1 `
  -PreserveArtifacts -DryRun
```

After checking the plan, the Owner can unregister the task while retaining
evidence:

```powershell
& $Pwsh -NoLogo -NoProfile -NonInteractive -File `
  .\Tools\HostAutomation\Uninstall-SashimiHostAutomation.ps1 `
  -PreserveArtifacts
```

Uninstallation does not merge or close work and does not mutate GitHub. The
current uninstaller only unregisters the scheduled task and always preserves
run artifacts, source scripts, installed bundles, and staged configuration.
`-PreserveArtifacts` makes that current contract explicit; there is no
artifact- or bundle-deletion path in the uninstaller.
