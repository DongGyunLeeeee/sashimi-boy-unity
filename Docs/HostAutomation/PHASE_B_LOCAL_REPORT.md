# Issue 52 / PR 53 — partial Phase B local report

Date: 2026-09-07. This is implementation evidence, not independent review.
Branch: `infra/52-windows-host-orchestrator`.
HEAD: `879072b96da77dd187f27d1444e5e86e12881d83` (unchanged).
SPEC_VERSION: `1.0.2`; blob: `6d7de6e6abef13b18021a3591debc53ac00616d4`.

- PHASE_B_STATUS: PARTIAL
- UNRESOLVED_MAJOR_COUNT: 8
- BLOCKER_REGRESSIONS: PASS (executed fixture regressions only)
- FIXTURE_TESTS: 82/82, 0 failed
- REAL_CODEX_FUNCTIONAL_SMOKE: NOT_RUN
- REAL_UNITY_VALIDATION: NOT_RUN
SAFE_TO_INSTALL: NO

The per-finding M1–M8 dispositions and remaining work are in
[REVIEW_53_REMEDIATION.md](REVIEW_53_REMEDIATION.md). No entire Major is closed.
The installed Codex read/edit capability and scheduled task usefulness are not
established by this checkpoint. No real Codex session or Unity was launched.

## Changes

- Preserved the pre-existing Common/installer/test fixture-isolation changes.
  All generated fixture configuration paths are now unique, including defaults.
- Runtime and installer credential scans also audit decoded JSON string values;
  Unicode-escaped nested tokens cannot evade the recognizable-token check.
- Codex probes/execution use a dedicated suspended kill-on-close job and the
  run ledger supplied by Developer/Reviewer. PID plus creation time is recorded
  before resume; entries are removed only after confirmed termination.
- Uninstaller harness runs refuse the real scheduler path before task lookup.
- Added the default-Plan HOST functional smoke, using the production adapter
  and tiny supplied source/diff content, with fixture coverage and guarded
  cleanup. Real execution requires a later explicit human opt-in.

Modified tracked files (relative to the checkpoint):

```text
Docs/HostAutomation/OPERATIONS.md
Docs/HostAutomation/REVIEW_53_REMEDIATION.md
Docs/HostAutomation/SECURITY.md
Tools/HostAutomation/HostAutomation.Common.ps1
Tools/HostAutomation/Install-SashimiHostAutomation.ps1
Tools/HostAutomation/Invoke-SashimiCodexExec.ps1
Tools/HostAutomation/Invoke-SashimiDeveloperRun.ps1
Tools/HostAutomation/Invoke-SashimiReviewerRun.ps1
Tools/HostAutomation/Tests/Test-SashimiHostAutomation.ps1
Tools/HostAutomation/Uninstall-SashimiHostAutomation.ps1
```

New files: this report and
`Tools/HostAutomation/Test-SashimiCodexFunctionalSmoke.ps1`.
The Owner's checkout/branch was not switched. No commit or push was made.

## Executed local verification

All commands used `C:\Program Files\PowerShell\7\pwsh.exe`, version 7.6.5,
with `-NoLogo -NoProfile -NonInteractive`, from BootstrapRepo.

Focused suite commands used the real fixture entry:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'FixtureCompilerBoundary|FixtureSourceAudit|CustomizedFixtureConfigurations|PowerShell7Parser'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'ConfigurationSchema|FixtureCompilerBoundary'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'UnityKillOnClose|CodexTransportEnvironment|CodexRawOutput|FixtureCompilerBoundary'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'FunctionalSmokePlan|FixtureCompilerBoundary'
```

Respectively: 5/5, 3/3, 5/5 and 3/3 passed; exit 0, each reported zero
external mutations. These counts overlap and must not be summed as unique tests.

The initial functional fixture run with pattern
`FunctionalSmokePlan|FixtureCompilerBoundary|PowerShell7Parser` returned 3 passed,
1 failed, exit 1: `Functional fake harness failed: Production adapter failed for
Developer (exit 1).` The fake result serialized a single changedFiles value as a
scalar; it was corrected to an explicit array and the focused command above
passed. This superseded failure is not represented as PASS.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1
```

First full run: **80/82 passed, 2 failed, exit 1**. It recorded 2,129 fake
invocations, 15 simulated mutations, zero external mutations, and zero scheduler
mutation sentinels. Failures:

- `StalePullRequestPinFailsClosed`: the assertion counted earlier fixture
  compiler bootstrap records as mutations by this pure pin test. It now checks
  that the pin test adds no process invocation at all.
- `CodexRejectsRepositoryScopedConfigurationBeforeAnyLaunch`: the new native
  Codex job route initially bypassed the repository `.codex` pre-launch check.
  This was a production regression and reached the **fake** Codex boundary.
  The check was restored immediately before the native executable lease.
  No real Codex or remote endpoint was reached.

The correction was verified with this focused command: **5/5 passed, exit 0**,
zero external mutations:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Tests\Test-SashimiHostAutomation.ps1 -TestNamePattern 'StalePullRequestPin|CodexRejectsRepositoryScoped|FunctionalSmokePlan|FixtureCompilerBoundary'
```

Second full run: **82/82 passed, 0 failed, exit 0**. The same complete entry ran
after the corrections, with no reduced test selection or changed skip rules.
It recorded **2,129 fake invocations, 15 simulated mutations, zero external
mutations and zero scheduler mutation sentinels**. B1–B6 security regressions and
the existing m1/m2 scheduler/ACL regressions passed. The new functional fake
smoke and all 13-script parser checks also passed. This does not establish
real-model functionality, live permissions or independent security approval.

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiHostAutomation.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -EnvironmentSmoke
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -QueueFixturePath .\Tools\HostAutomation\Tests\Fixtures\Queue.Empty.json -DryRun
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File .\Tools\HostAutomation\Test-SashimiCodexFunctionalSmoke.ps1 -ConfigPath .\Tools\HostAutomation\Config.example.json -DryRun
```

EnvironmentSmoke: 12/12, exit 0; this checks presence and DryRun planning, not
live Codex capabilities. Empty queue: exit 0, NoWork, dispatch 0,
MutationAttempted=false. Functional PLAN: exit 0, both roles Executed=false,
Cleaned=true, real smoke NOT_RUN. The first standalone PLAN returned exit 1
because the relative ConfigPath was forwarded into a different working directory;
the path is now canonicalized and the exact command passed on rerun.

Installer preview used the current `Invoke-OwnerPinnedInstaller` function
extracted from the PowerShell fenced block in OPERATIONS.md and executed in
memory. It externally hashed the current installer for local preview only:

```powershell
$installerPath = [IO.Path]::GetFullPath('.\Tools\HostAutomation\Install-SashimiHostAutomation.ps1')
$previewHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$preview = Invoke-OwnerPinnedInstaller -InstallerPath $installerPath -ExpectedInstallerSha256 $previewHash -InstallerArgumentList @('-ConfigPath',[IO.Path]::GetFullPath('.\Tools\HostAutomation\Config.example.json'),'-OrchestratorPath',[IO.Path]::GetFullPath('.\Tools\HostAutomation\Invoke-SashimiHostOrchestrator.ps1'),'-DryRun')
```

Repeated after the Codex pre-launch correction. Exit 0; Success=true,
DryRun=true, Staged=false, AclVerified=false,
Changed=false; Program Files/SashimiBoyAutomation absent after preview.

```text
Installer SHA-256: 0ec906d3383db28c0c65073ad4d875935d0c10a7bdd8d28aadb10b335edc16f0
Preview BundleId: bc70c6e528a333af7ec4c55dced4bf8f7f8b27e883f83aad647267095430dd99
Manifest SHA-256: e51fa80f0873b424ca08aa984d40f4372a68b2e6e64332bbcd94013e9aeb9154
```

These locally computed preview identities are not independently retained Owner
authorization and must not be used as permission to install.

Parser: all 13 HostAutomation PS1 files parsed with
`[Management.Automation.Language.Parser]::ParseFile`, zero errors.
`git diff --check`: exit 0. Changed/untracked path audit: zero paths outside
Tools/HostAutomation and Docs/HostAutomation. No Assets, Packages or
ProjectSettings changes. Branch and HEAD readbacks match the start checkpoint.

No test fell back to installed Git, gh, Unity or Codex in this continuation.
Compiler bootstrap is the exact Microsoft-signed Framework compiler operating
only on marker-owned fixture inputs/outputs; it is not a fake model invocation.
The real scheduler/ACL/installation/GitHub and Unity rollout gates remain
NOT_RUN. No independent approval, merge readiness or installation safety is
claimed.
