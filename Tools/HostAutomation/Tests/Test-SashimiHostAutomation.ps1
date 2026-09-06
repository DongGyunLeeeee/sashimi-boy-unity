#requires -Version 7.5

[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot,

    [Parameter()]
    [string]$PowerShellPath = 'C:\Program Files\PowerShell\7\pwsh.exe',

    [string]$TestNamePattern = '',

    [switch]$KeepTemporaryFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).ProviderPath
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).ProviderPath
$hostRoot = Join-Path $RepositoryRoot 'Tools\HostAutomation'
$commonPath = Join-Path $hostRoot 'HostAutomation.Common.ps1'
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
    throw "Host automation common library is missing: $commonPath"
}
. $commonPath

$script:results = [Collections.Generic.List[object]]::new()
$script:temporaryRoot = $null
$script:testRunId = [Guid]::NewGuid().ToString('N')
$script:ownedTemporaryRoot = $false
$script:fixtureInvocations = [Collections.Generic.List[object]]::new()
$script:systemMutationSentinels = [Collections.Generic.List[string]]::new()
$script:mutationAudit = $null
$previousHarnessMode = [Environment]::GetEnvironmentVariable('SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS', 'Process')

function Assert-HostTest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw $Message }
}

function Assert-HostThrows {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Body,
        [string]$Pattern = ''
    )
    $caught = $null
    try { & $Body } catch { $caught = $_ }
    Assert-HostTest ($null -ne $caught) 'Expected the operation to throw.'
    if (-not [string]::IsNullOrWhiteSpace($Pattern)) {
        Assert-HostTest ($caught.Exception.Message -match $Pattern) "Unexpected error: $($caught.Exception.Message)"
    }
}

function Invoke-HostTestCase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )
    if (-not [string]::IsNullOrWhiteSpace($TestNamePattern) -and
        $Name -notmatch $TestNamePattern -and $Name -cne 'FixtureIsolationHasNoLiveIssueMutation') {
        return
    }
    $started = [DateTime]::UtcNow
    try {
        & $Body
        $script:results.Add([pscustomobject][ordered]@{
                Name = $Name
                Passed = $true
                DurationMilliseconds = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
                Error = ''
            })
    }
    catch {
        $script:results.Add([pscustomobject][ordered]@{
                Name = $Name
                Passed = $false
                DurationMilliseconds = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
                Error = $_.Exception.Message
            })
    }
}

function Write-HostTestFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertFrom-LastHostJson {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $lines = @($Text -split '\r?\n')
    for ($index = $lines.Count - 1; $index -ge 0; $index--) {
        $candidate = $lines[$index].Trim()
        if (-not $candidate.StartsWith('{')) { continue }
        try { return ($candidate | ConvertFrom-Json -Depth 64 -ErrorAction Stop) } catch { }
    }
    throw "No JSON result was found in output: $Text"
}

function Invoke-HostTestScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [hashtable]$Parameters = @{},
        [hashtable]$Environment = @{},
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 30
    )
    $arguments = [Collections.Generic.List[string]]::new()
    foreach ($value in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $ScriptPath)) {
        $arguments.Add($value)
    }
    foreach ($entry in @($Parameters.GetEnumerator() | Sort-Object Key)) {
        if ($entry.Value -is [bool]) {
            if ([bool]$entry.Value) { $arguments.Add('-' + [string]$entry.Key) }
            continue
        }
        $arguments.Add('-' + [string]$entry.Key)
        $arguments.Add([string]$entry.Value)
    }
    $record = [pscustomobject][ordered]@{
        Kind = 'PowerShellFixture'
        Script = [IO.Path]::GetFileName($ScriptPath)
        Arguments = $arguments.ToArray()
    }
    $script:fixtureInvocations.Add($record)
    $childEnvironment = @{ SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS = '1' }
    foreach ($entry in $Environment.GetEnumerator()) {
        $childEnvironment[[string]$entry.Key] = [string]$entry.Value
    }
    return Invoke-SashimiHostProcess `
        -FilePath $PowerShellPath `
        -ArgumentList $arguments.ToArray() `
        -WorkingDirectory $RepositoryRoot `
        -TimeoutSeconds $TimeoutSeconds `
        -Environment $childEnvironment
}

function New-HostFakeToolAdapters {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    $assemblyPath = Join-Path $Root 'SashimiHostFakeTool.exe'
    $source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

public static class SashimiHostFakeTool
{
    private static string Env(string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? String.Empty;
    }

    private static bool Has(string[] args, string value)
    {
        return args.Any(a => String.Equals(a, value, StringComparison.Ordinal));
    }

    private static string ValueWithPrefix(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg.Substring(prefix.Length);
        }
        return String.Empty;
    }

    private static string ValueAfter(string[] args, string name)
    {
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (String.Equals(args[index], name, StringComparison.Ordinal))
                return args[index + 1];
        }
        return String.Empty;
    }

    private static string JsonString(string value)
    {
        return "\"" + (value ?? String.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t") + "\"";
    }

    private static string ReadScenario(string name)
    {
        string root = Env("SASHIMI_FAKE_SCENARIO_ROOT");
        if (String.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("SASHIMI_FAKE_SCENARIO_ROOT is not set.");
        return File.ReadAllText(Path.Combine(root, name), new UTF8Encoding(false));
    }

    private static void WriteAudit(string tool, string[] args, bool simulatedMutation)
    {
        string path = Env("SASHIMI_FAKE_TOOL_LOG");
        if (String.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("SASHIMI_FAKE_TOOL_LOG is not set.");
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join("\0", args)));
        string line = tool + "\t" + (simulatedMutation ? "1" : "0") + "\t0\t" + encoded + Environment.NewLine;
        File.AppendAllText(path, line, new UTF8Encoding(false));
    }

    private static int RunGit(string[] args, bool isLfs)
    {
        bool mutation = Has(args, "push");
        WriteAudit(isLfs ? "lfs" : "git", args, mutation);

        if (Env("SASHIMI_FAKE_GIT_OPAQUE_FAILURE") == "1")
        {
            Console.WriteLine("opaque git stdout: openai-fixture-9Qx7mV2pL8cR4tN6");
            Console.Error.WriteLine("opaque git stderr: wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
            return 91;
        }

        if (Has(args, "clone"))
        {
            string target = args[args.Length - 1];
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(target, ".git"));
        }
        if (Has(args, "rev-parse"))
        {
            if (Has(args, "refs/remotes/origin/sashimi-pinned") || Has(args, "refs/remotes/origin/sashimi-review-pinned"))
                Console.WriteLine(Env("SASHIMI_FAKE_GIT_PINNED_SHA"));
            else if (Has(args, "refs/remotes/origin/main"))
            {
                string mainSha = Env("SASHIMI_FAKE_GIT_MAIN_SHA");
                Console.WriteLine(String.IsNullOrWhiteSpace(mainSha) ? new String('1', 40) : mainSha);
            }
            else if (Has(args, "HEAD"))
                Console.WriteLine(Env("SASHIMI_FAKE_GIT_HEAD_SHA"));
            else
                Console.WriteLine("true");
        }
        if (Has(args, "ls-remote") && Has(args, "refs/heads/main"))
        {
            string mainSha = Env("SASHIMI_FAKE_GIT_MAIN_SHA_AFTER");
            if (String.IsNullOrWhiteSpace(mainSha))
                mainSha = Env("SASHIMI_FAKE_GIT_MAIN_SHA");
            if (String.IsNullOrWhiteSpace(mainSha))
                mainSha = new String('1', 40);
            Console.WriteLine(mainSha + "\trefs/heads/main");
        }
        if (Has(args, "status"))
        {
            string status = Env("SASHIMI_FAKE_GIT_STATUS");
            string postUnityMarker = Env("SASHIMI_FAKE_POST_UNITY_MARKER");
            if (!String.IsNullOrWhiteSpace(postUnityMarker) && File.Exists(postUnityMarker))
            {
                string driftRelativePath = Env("SASHIMI_FAKE_POST_UNITY_DRIFT_PATH");
                string repositoryPath = ValueAfter(args, "-C");
                if (!String.IsNullOrWhiteSpace(driftRelativePath) && !String.IsNullOrWhiteSpace(repositoryPath))
                {
                    string repositoryRoot = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string driftPath = Path.GetFullPath(Path.Combine(repositoryRoot, driftRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    string repositoryPrefix = repositoryRoot + Path.DirectorySeparatorChar;
                    if (!driftPath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Post-Unity drift fixture escaped its repository.");
                    Directory.CreateDirectory(Path.GetDirectoryName(driftPath));
                    File.WriteAllText(driftPath, "// Unity-simulated Reviewer drift fixture.\n", new UTF8Encoding(false));
                }
                status = Env("SASHIMI_FAKE_GIT_POST_UNITY_STATUS");
            }
            Console.Write(status);
        }
        if (Has(args, "symbolic-ref"))
            Console.WriteLine(Env("SASHIMI_FAKE_GIT_BRANCH"));
        if (Has(args, "remote") && Has(args, "get-url"))
            Console.WriteLine("https://github.com/DongGyunLeeeee/sashimi-boy-unity.git");
        if (Has(args, "config") && Has(args, "--get") && Has(args, "core.hooksPath"))
            Console.WriteLine("NUL");
        if (Has(args, "for-each-ref"))
            Console.WriteLine("refs/remotes/origin/main 1111111111111111111111111111111111111111");
        // Git LFS push is a mutation boundary too, but only a normal Git push
        // changes the synthetic PR head returned by the fake GitHub adapter.
        if (Has(args, "push") && !isLfs)
        {
            string statePath = Env("SASHIMI_FAKE_PUSH_STATE");
            if (!String.IsNullOrWhiteSpace(statePath))
                File.WriteAllText(statePath, "pushed", new UTF8Encoding(false));
        }
        return 0;
    }

    private static int RunGh(string[] args)
    {
        string query = ValueWithPrefix(args, "query=");
        bool mutation = (args.Length > 1 && args[0] == "pr" && (args[1] == "create" || args[1] == "comment")) ||
            (args.Length > 1 && args[0] == "issue" && args[1] == "comment") ||
            query.IndexOf("mutation", StringComparison.OrdinalIgnoreCase) >= 0;
        WriteAudit("gh", args, mutation);

        string scenario = Env("SASHIMI_FAKE_GH_SCENARIO");
        if (args.Length >= 2 && args[0] == "api" && args[1] == "user")
        {
            string login = Env("SASHIMI_FAKE_GH_LOGIN");
            Console.WriteLine(String.IsNullOrWhiteSpace(login) ? "DongGyunLeeeee" : login);
            return 0;
        }
        if (scenario == "queue-pagination")
        {
            string cursor = ValueWithPrefix(args, "cursor=");
            if (query.Contains("HostProjectFields"))
                Console.Write(ReadScenario(String.IsNullOrEmpty(cursor) ? "fields-1.json" : "fields-2.json"));
            else if (query.Contains("HostProjectItems"))
                Console.Write(ReadScenario(String.IsNullOrEmpty(cursor) ? "items-1.json" : "items-2.json"));
            else if (query.Contains("HostIssueComments"))
                Console.Write(ReadScenario("issue-comments.json"));
            else
                throw new InvalidOperationException("Unexpected queue GraphQL operation.");
            return 0;
        }

        if (args.Length > 1 && args[0] == "pr" && args[1] == "view")
        {
            if (scenario == "developer-stale")
                Console.Write(ReadScenario("pr-stale.json"));
            else if (scenario == "developer-content-stale")
                Console.Write(ReadScenario("pr-content-stale.json"));
            else
            {
                string statePath = Env("SASHIMI_FAKE_PUSH_STATE");
                bool pushed = !String.IsNullOrWhiteSpace(statePath) && File.Exists(statePath);
                Console.Write(ReadScenario(pushed ? "pr-after.json" : "pr-before.json"));
            }
            return 0;
        }
        if (args.Length > 1 && args[0] == "pr" && args[1] == "comment")
        {
            string pullNumber = args.Length > 2 ? args[2] : "0";
            string bodyPath = ValueAfter(args, "--body-file");
            string scenarioRoot = Env("SASHIMI_FAKE_SCENARIO_ROOT");
            string body = File.ReadAllText(bodyPath, new UTF8Encoding(false));
            string counterPath = Path.Combine(scenarioRoot, "comment-counter.txt");
            int counter = File.Exists(counterPath) ? Int32.Parse(File.ReadAllText(counterPath)) + 1 : 1;
            File.WriteAllText(counterPath, counter.ToString(), new UTF8Encoding(false));
            string commentUrl = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/" + pullNumber + "#issuecomment-" + (9000 + counter).ToString();
            string record = Convert.ToBase64String(Encoding.UTF8.GetBytes(commentUrl)) + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) + Environment.NewLine;
            File.AppendAllText(Path.Combine(scenarioRoot, "pr-comments.tsv"), record, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(scenarioRoot, "comment-body.b64"), Convert.ToBase64String(Encoding.UTF8.GetBytes(body)), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(scenarioRoot, "comment-url.txt"), commentUrl, new UTF8Encoding(false));
            Console.WriteLine(commentUrl);
            return 0;
        }
        if (args.Length > 1 && args[0] == "pr" && args[1] == "create")
        {
            Console.WriteLine("https://example.invalid/pull/created-fixture");
            return 0;
        }
        if (query.Contains("HostPinIssueComments"))
        {
            Console.Write("{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"totalCount\":0,\"nodes\":[],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}");
            return 0;
        }
        if (query.Contains("HostPinPrComments"))
        {
            string recordsPath = Path.Combine(Env("SASHIMI_FAKE_SCENARIO_ROOT"), "pr-comments.tsv");
            string[] lines = File.Exists(recordsPath) ? File.ReadAllLines(recordsPath, new UTF8Encoding(false)) : new string[0];
            string[] nodes = lines.Where(line => !String.IsNullOrWhiteSpace(line)).Select(line => {
                string[] parts = line.Split('\t');
                string url = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                string body = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                return "{\"body\":" + JsonString(body) + ",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\",\"url\":" + JsonString(url) + ",\"author\":{\"login\":\"DongGyunLeeeee\"},\"authorAssociation\":\"OWNER\",\"includesCreatedEdit\":false}";
            }).ToArray();
            Console.Write("{\"data\":{\"repository\":{\"pullRequest\":{\"comments\":{\"totalCount\":" + nodes.Length.ToString() + ",\"nodes\":[" + String.Join(",", nodes) + "],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}");
            return 0;
        }
        if (query.Contains("HostPinPrReviews"))
        {
            Console.Write("{\"data\":{\"repository\":{\"pullRequest\":{\"reviews\":{\"totalCount\":0,\"nodes\":[],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}");
            return 0;
        }
        if (query.Contains("HostPublishContract"))
        {
            string statusPath = Env("SASHIMI_FAKE_STATUS_STATE");
            bool transitioned = !String.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath);
            Console.Write(ReadScenario(transitioned ? "project-after.json" : "project.json"));
            return 0;
        }
        if (args.Length > 1 && args[0] == "api" && args[1].IndexOf("/issues/comments/", StringComparison.Ordinal) >= 0)
        {
            string scenarioRoot = Env("SASHIMI_FAKE_SCENARIO_ROOT");
            string encodedBody = File.ReadAllText(Path.Combine(scenarioRoot, "comment-body.b64"), new UTF8Encoding(false));
            string body = Encoding.UTF8.GetString(Convert.FromBase64String(encodedBody));
            string commentUrl = File.ReadAllText(Path.Combine(scenarioRoot, "comment-url.txt"), new UTF8Encoding(false));
            Console.Write("{\"body\":" + JsonString(body) + ",\"html_url\":" + JsonString(commentUrl) +
                ",\"user\":{\"login\":\"DongGyunLeeeee\"},\"author_association\":\"OWNER\",\"created_at\":\"2026-01-01T00:00:00Z\",\"updated_at\":\"2026-01-01T00:00:00Z\"}");
            return 0;
        }
        if (query.IndexOf("mutation HostSetStatus", StringComparison.Ordinal) >= 0)
        {
            string statusPath = Env("SASHIMI_FAKE_STATUS_STATE");
            if (!String.IsNullOrWhiteSpace(statusPath))
                File.WriteAllText(statusPath, "transitioned", new UTF8Encoding(false));
            Console.Write(ReadScenario("mutation.json"));
            return 0;
        }
        throw new InvalidOperationException("Unexpected fake gh invocation: " + String.Join(" ", args));
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string executable = Process.GetCurrentProcess().MainModule.FileName;
        string name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        try
        {
            return name.Contains("gh") ? RunGh(args) : RunGit(args, name.Contains("lfs"));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 97;
        }
    }
}
'@
    $sourcePath = Join-Path $Root 'SashimiHostFakeTool.cs'
    Write-HostTestFile -Path $sourcePath -Content $source
    $compilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
        $compilerPath = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
        throw 'The Windows .NET Framework C# compiler required for fake executable adapters is missing.'
    }
    $compile = Invoke-SashimiHostProcess -FilePath $compilerPath -ArgumentList @('/nologo', '/target:exe', "/out:$assemblyPath", $sourcePath) -WorkingDirectory $Root -TimeoutSeconds 30
    if (-not $compile.Succeeded -or -not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Unable to compile fake executable adapters: $($compile.StdErr) $($compile.StdOut)"
    }
    $gitPath = Join-Path $Root 'fake-git.exe'
    $lfsPath = Join-Path $Root 'fake-lfs.exe'
    $ghPath = Join-Path $Root 'fake-gh.exe'
    Copy-Item -LiteralPath $assemblyPath -Destination $gitPath
    Copy-Item -LiteralPath $assemblyPath -Destination $lfsPath
    Copy-Item -LiteralPath $assemblyPath -Destination $ghPath
    return [pscustomobject]@{ Git = $gitPath; GitLfs = $lfsPath; GitHub = $ghPath }
}

function Get-HostFakeToolAudit {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    $records = [Collections.Generic.List[object]]::new()
    foreach ($line in @([IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8))) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split("`t", 4)
        if ($parts.Count -ne 4) { throw "Invalid fake-tool audit line: $line" }
        $argumentText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[3]))
        $records.Add([pscustomobject]@{
                Tool = $parts[0]
                SimulatedMutation = ($parts[1] -ceq '1')
                ExternalMutation = ($parts[2] -ceq '1')
                Arguments = @($argumentText -split "`0")
            })
    }
    return $records.ToArray()
}

function Assert-HostGitHookSuppression {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Records,
        [Parameter(Mandatory = $true)][string]$Context
    )

    Assert-HostTest ($Records.Count -gt 0) "$Context captured no Git invocation."
    foreach ($record in $Records) {
        $arguments = @($record.Arguments | ForEach-Object { [string]$_ })
        Assert-HostTest ($arguments.Count -ge 3) "$Context captured a truncated Git argument vector."
        Assert-HostTest ($arguments[0] -ceq '-c' -and $arguments[1] -ceq 'core.hooksPath=NUL') `
            "$Context did not suppress hooks at Git command scope: $([string]::Join(' ', $arguments))"
        Assert-HostTest (@($arguments | Where-Object { $_ -ceq 'core.hooksPath=NUL' }).Count -eq 1) `
            "$Context supplied an ambiguous duplicate hooksPath override."
    }
}

function New-HostTestConfig {
    [CmdletBinding()]
    param(
        [string]$GitExecutable = '',
        [string]$GitLfsExecutable = '',
        [string]$GitHubCli = '',
        [string]$UnityExecutable = ''
    )
    $source = Join-Path $hostRoot 'Config.example.json'
    $config = [IO.File]::ReadAllText($source, [Text.Encoding]::UTF8) | ConvertFrom-Json -Depth 64
    $config.RunRoot = Join-Path $script:temporaryRoot 'Runs'
    if (-not [string]::IsNullOrWhiteSpace($GitExecutable)) { $config.GitExecutable = $GitExecutable }
    if (-not [string]::IsNullOrWhiteSpace($GitLfsExecutable)) { $config.GitLfsExecutable = $GitLfsExecutable }
    if (-not [string]::IsNullOrWhiteSpace($GitHubCli)) { $config.GitHubCli = $GitHubCli }
    if (-not [string]::IsNullOrWhiteSpace($UnityExecutable)) { $config.UnityExecutable = $UnityExecutable }
    $path = Join-Path $script:temporaryRoot 'Config.json'
    if (-not [string]::IsNullOrWhiteSpace($GitExecutable) -or -not [string]::IsNullOrWhiteSpace($GitLfsExecutable) -or -not [string]::IsNullOrWhiteSpace($GitHubCli) -or
        -not [string]::IsNullOrWhiteSpace($UnityExecutable)) {
        $path = Join-Path $script:temporaryRoot 'Config.FakeTools.json'
    }
    Write-HostTestFile $path (($config | ConvertTo-Json -Depth 64) + "`n")
    return $path
}

function New-HostLiveQueueScenario {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root)

    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $fieldPages = @(
        [ordered]@{
            data = [ordered]@{ user = [ordered]@{ projectV2 = [ordered]@{
                        id = 'PVT_fixture'
                        fields = [ordered]@{
                            totalCount = 4
                            nodes = @(
                                [ordered]@{ id = 'field-status'; name = 'Status'; options = @(
                                        [ordered]@{ id = 's0'; name = 'Backlog' }, [ordered]@{ id = 's1'; name = 'Ready' },
                                        [ordered]@{ id = 's2'; name = 'In Progress' }, [ordered]@{ id = 's3'; name = 'Review' },
                                        [ordered]@{ id = 's4'; name = 'Verification' }, [ordered]@{ id = 's5'; name = 'Done' }) },
                                [ordered]@{ id = 'field-priority'; name = 'Priority'; options = @(
                                        [ordered]@{ id = 'p0'; name = 'P0' }, [ordered]@{ id = 'p1'; name = 'P1' },
                                        [ordered]@{ id = 'p2'; name = 'P2' }, [ordered]@{ id = 'p3'; name = 'P3' }) }
                            )
                            pageInfo = [ordered]@{ hasNextPage = $true; endCursor = 'fixture-fields-page-2' }
                        }
                    } } }
        },
        [ordered]@{
            data = [ordered]@{ user = [ordered]@{ projectV2 = [ordered]@{
                        id = 'PVT_fixture'
                        fields = [ordered]@{
                            totalCount = 4
                            nodes = @(
                                [ordered]@{ id = 'field-area'; name = 'Area' },
                                [ordered]@{ id = 'field-size'; name = 'Size' }
                            )
                            pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null }
                        }
                    } } }
        }
    )
    for ($index = 0; $index -lt $fieldPages.Count; $index++) {
        Write-HostTestFile (Join-Path $Root ("fields-{0}.json" -f ($index + 1))) (($fieldPages[$index] | ConvertTo-Json -Depth 64 -Compress) + "`n")
    }

    function New-LiveReadyNode([int]$Number, [string]$Priority, [string]$UpdatedAt, [string]$Title, [string]$Body) {
        return [ordered]@{
            id = "live-item-$Number"
            updatedAt = $UpdatedAt
            content = [ordered]@{
                __typename = 'Issue'; number = $Number; title = $Title; body = $Body; updatedAt = $UpdatedAt
                url = "https://example.invalid/issues/$Number"; state = 'OPEN'
                repository = [ordered]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
                labels = [ordered]@{ totalCount = 0; nodes = @(); pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null } }
            }
            statusValue = [ordered]@{ name = 'Ready' }
            priorityValue = [ordered]@{ name = $Priority }
            linkedValue = $null
        }
    }
    $koreanTitle = '[P0][인프라] 실시간 페이지 큐'
    $koreanBody = "첫 줄`n둘째 줄`n세 번째 줄 ✅"
    $itemPages = @(
        [ordered]@{ data = [ordered]@{ user = [ordered]@{ projectV2 = [ordered]@{
                        id = 'PVT_fixture'; items = [ordered]@{
                            totalCount = 2
                            nodes = @((New-LiveReadyNode 5290 P1 '2026-01-01T00:00:00Z' 'First page' 'First body'))
                            pageInfo = [ordered]@{ hasNextPage = $true; endCursor = 'fixture-items-page-2' }
                        }
                    } } } },
        [ordered]@{ data = [ordered]@{ user = [ordered]@{ projectV2 = [ordered]@{
                        id = 'PVT_fixture'; items = [ordered]@{
                            totalCount = 2
                            nodes = @((New-LiveReadyNode 5291 P0 '2026-02-01T00:00:00Z' $koreanTitle $koreanBody))
                            pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null }
                        }
                    } } } }
    )
    for ($index = 0; $index -lt $itemPages.Count; $index++) {
        Write-HostTestFile (Join-Path $Root ("items-{0}.json" -f ($index + 1))) (($itemPages[$index] | ConvertTo-Json -Depth 64 -Compress) + "`n")
    }
    $emptyComments = [ordered]@{ data = [ordered]@{ repository = [ordered]@{ issue = [ordered]@{
                    comments = [ordered]@{ totalCount = 0; nodes = @(); pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null } }
                } } } }
    Write-HostTestFile (Join-Path $Root 'issue-comments.json') (($emptyComments | ConvertTo-Json -Depth 32 -Compress) + "`n")
    return [pscustomobject]@{ Root = $Root; IssueNumber = 5291; Title = $koreanTitle; Body = $koreanBody }
}

function New-HostDeveloperGhScenario {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][int]$PullRequestNumber,
        [Parameter(Mandatory = $true)][string]$HeadRef,
        [Parameter(Mandatory = $true)][string]$PinnedSha,
        [Parameter(Mandatory = $true)][string]$AfterSha,
        [Parameter(Mandatory = $true)][string]$StaleSha
    )

    [IO.Directory]::CreateDirectory($Root) | Out-Null
    function New-PrShape([string]$Sha) {
        return [ordered]@{
            number = $PullRequestNumber; state = 'OPEN'; isDraft = $true; baseRefName = 'main'
            title = "Synthetic Issue $IssueNumber delivery"; body = 'Synthetic existing PR body.'
            baseRepository = [ordered]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
            headRefName = $HeadRef; headRefOid = $Sha
            headRepository = [ordered]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
            author = [ordered]@{ login = 'DongGyunLeeeee' }
            url = "https://example.invalid/pull/$PullRequestNumber"
        }
    }
    Write-HostTestFile (Join-Path $Root 'pr-before.json') (((New-PrShape $PinnedSha) | ConvertTo-Json -Depth 32 -Compress) + "`n")
    Write-HostTestFile (Join-Path $Root 'pr-after.json') (((New-PrShape $AfterSha) | ConvertTo-Json -Depth 32 -Compress) + "`n")
    Write-HostTestFile (Join-Path $Root 'pr-stale.json') (((New-PrShape $StaleSha) | ConvertTo-Json -Depth 32 -Compress) + "`n")
    $contentStale = New-PrShape $PinnedSha
    $contentStale.body = 'Synthetic existing PR body changed after selection.'
    Write-HostTestFile (Join-Path $Root 'pr-content-stale.json') (($contentStale | ConvertTo-Json -Depth 32 -Compress) + "`n")
    $project = [ordered]@{
        data = [ordered]@{
            user = [ordered]@{ projectV2 = [ordered]@{
                    id = 'PVT_fixture'; fields = [ordered]@{
                        totalCount = 4
                        pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null }
                        nodes = @(
                            [ordered]@{
                                id = 'status-field'; name = 'Status'; options = @(
                                    [ordered]@{ id = 'backlog'; name = 'Backlog' }, [ordered]@{ id = 'ready'; name = 'Ready' },
                                    [ordered]@{ id = 'in-progress'; name = 'In Progress' }, [ordered]@{ id = 'review'; name = 'Review' },
                                    [ordered]@{ id = 'verification'; name = 'Verification' }, [ordered]@{ id = 'done'; name = 'Done' })
                            },
                            [ordered]@{
                                id = 'priority-field'; name = 'Priority'; options = @(
                                    [ordered]@{ id = 'p0'; name = 'P0' }, [ordered]@{ id = 'p1'; name = 'P1' },
                                    [ordered]@{ id = 'p2'; name = 'P2' }, [ordered]@{ id = 'p3'; name = 'P3' })
                            },
                            [ordered]@{ id = 'area-field'; name = 'Area' },
                            [ordered]@{ id = 'size-field'; name = 'Size' }
                        )
                    }
                } }
            node = [ordered]@{
                id = "fixture-item-$IssueNumber"; project = [ordered]@{ id = 'PVT_fixture' }
                content = [ordered]@{
                    number = $IssueNumber; state = 'OPEN'; updatedAt = '2026-01-01T00:00:00Z'; body = 'Fixture acceptance criteria.'
                    repository = [ordered]@{ nameWithOwner = 'DongGyunLeeeee/sashimi-boy-unity' }
                }
                statusValue = [ordered]@{ name = 'In Progress' }
                linkedValue = [ordered]@{ pullRequests = [ordered]@{
                        totalCount = 1; nodes = @([ordered]@{ number = $PullRequestNumber; state = 'OPEN' }); pageInfo = [ordered]@{ hasNextPage = $false; endCursor = $null }
                    } }
            }
        }
    }
    Write-HostTestFile (Join-Path $Root 'project.json') (($project | ConvertTo-Json -Depth 64 -Compress) + "`n")
    $projectAfter = ($project | ConvertTo-Json -Depth 64 | ConvertFrom-Json -Depth 64)
    $projectAfter.data.node.statusValue.name = 'Review'
    Write-HostTestFile (Join-Path $Root 'project-after.json') (($projectAfter | ConvertTo-Json -Depth 64 -Compress) + "`n")
    Write-HostTestFile (Join-Path $Root 'mutation.json') ("{`"data`":{`"updateProjectV2ItemFieldValue`":{`"projectV2Item`":{`"id`":`"fixture-item-$IssueNumber`"}}}}")
    return $Root
}

function Get-HostQueueFields {
    [CmdletBinding()]
    param()
    return @(
        [ordered]@{ Name = 'Status'; Options = @('Backlog', 'Ready', 'In Progress', 'Review', 'Verification', 'Done') },
        [ordered]@{ Name = 'Priority'; Options = @('P0', 'P1', 'P2', 'P3') },
        [ordered]@{ Name = 'Area'; Options = @() },
        [ordered]@{ Name = 'Size'; Options = @() }
    )
}

function New-HostQueuePullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string]$HeadRef = '',
        [string]$Author = 'DongGyunLeeeee',
        [string]$HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity',
        [string]$BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity',
        [string]$BaseRefName = 'main',
        [string]$Title = 'Synthetic Draft PR',
        [string]$Body = 'Synthetic PR body.'
    )
    if ([string]::IsNullOrWhiteSpace($HeadRef)) { $HeadRef = "infra/fixture-$Number" }
    return [ordered]@{
        Number = $Number
        Url = "https://example.invalid/pull/$Number"
        State = 'OPEN'
        IsDraft = $true
        Title = $Title
        Body = $Body
        ContentSha256 = Get-SashimiPullRequestContentSha256 -Title $Title -Body $Body
        BaseRefName = $BaseRefName
        BaseRepository = $BaseRepository
        HeadRef = $HeadRef
        HeadSha = $HeadSha
        HeadRepository = $HeadRepository
        AuthorLogin = $Author
    }
}

function New-HostQueueItem {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][ValidateSet('Ready', 'In Progress', 'Review', 'Verification', 'Done')][string]$Status,
        [string]$Priority = 'P1',
        [string]$UpdatedAt = '2026-01-01T00:00:00Z',
        [string]$Title = 'Synthetic host automation fixture',
        [string]$Body = "Fixture line one`nFixture line two",
        [ValidateSet('None', 'ReviewFix', 'DeliveryResume')][string]$HandoffMode = 'None',
        [string]$HeadSha = ''
    )
    if ([string]::IsNullOrWhiteSpace($HeadSha)) {
        $digit = [char]([int][char]'a' + ($IssueNumber % 6))
        $HeadSha = ([string]$digit) * 40
    }
    $prNumber = $IssueNumber + 1000
    $pullRequests = @()
    $handoff = $null
    if ($Status -in @('In Progress', 'Review')) {
        $pullRequests = @(New-HostQueuePullRequest -Number $prNumber -HeadSha $HeadSha)
    }
    if ($HandoffMode -ceq 'ReviewFix') {
        $handoff = [ordered]@{
            Mode = 'ReviewFix'; IssueNumber = $IssueNumber; PullRequestNumber = $prNumber
            HeadSha = $HeadSha; SourceRole = 'Reviewer'; Reason = 'review-major'
            FindingUrl = "https://example.invalid/findings/$IssueNumber"; PendingCommand = ''
        }
    }
    elseif ($HandoffMode -ceq 'DeliveryResume') {
        $handoff = [ordered]@{
            Mode = 'DeliveryResume'; IssueNumber = $IssueNumber; PullRequestNumber = $prNumber
            HeadSha = $HeadSha; SourceRole = 'Developer'; Reason = 'required-check-transient'
            FindingUrl = ''; PendingCommand = 'evidence-only --never-execute'
        }
    }
    return [ordered]@{
        ProjectItemId = "fixture-item-$IssueNumber"
        UpdatedAt = $UpdatedAt
        Status = $Status
        Priority = $Priority
        IssueNumber = $IssueNumber
        IssueTitle = $Title
        IssueBody = $Body
        IssueUrl = "https://example.invalid/issues/$IssueNumber"
        IssueState = 'OPEN'
        IssueRepository = 'DongGyunLeeeee/sashimi-boy-unity'
        Labels = @()
        PullRequests = $pullRequests
        Comments = @()
        Reviews = @()
        CurrentHandoff = $handoff
        HandoffResolved = $false
    }
}

function New-HostQueueFixtureFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Items,
        [switch]$TwoPages,
        [switch]$LeaveCursorUnfulfilled
    )
    $pages = @()
    if ($TwoPages -or $LeaveCursorUnfulfilled) {
        $split = [Math]::Max(1, [Math]::Floor($Items.Count / 2))
        $first = if ($Items.Count -eq 0) { @() } else { @($Items[0..($split - 1)]) }
        $pages += [ordered]@{
            TotalCount = $Items.Count
            Nodes = $first
            PageInfo = [ordered]@{ HasNextPage = $true; EndCursor = 'fixture-page-2' }
        }
        if (-not $LeaveCursorUnfulfilled) {
            $second = if ($split -ge $Items.Count) { @() } else { @($Items[$split..($Items.Count - 1)]) }
            $pages += [ordered]@{
                TotalCount = $Items.Count
                Nodes = $second
                PageInfo = [ordered]@{ HasNextPage = $false; EndCursor = $null }
            }
        }
    }
    else {
        $pages = @([ordered]@{
                TotalCount = $Items.Count
                Nodes = @($Items)
                PageInfo = [ordered]@{ HasNextPage = $false; EndCursor = $null }
            })
    }
    $fixture = [ordered]@{
        SchemaVersion = 1
        Encoding = 'UTF-8'
        Fields = @(Get-HostQueueFields)
        Pages = $pages
    }
    $path = Join-Path $script:temporaryRoot ($Name + '.queue.json')
    Write-HostTestFile $path (($fixture | ConvertTo-Json -Depth 64) + "`n")
    return $path
}

function Invoke-HostQueueFixture {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FixturePath)
    return Invoke-HostTestScript `
        -ScriptPath (Join-Path $hostRoot 'Get-SashimiProjectQueue.ps1') `
        -Parameters @{ ConfigPath = $script:configPath; FixturePath = $FixturePath; DryRun = $true }
}

function New-HostCodexResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [string]$HeadSha = ('e' * 40),
        [ValidateSet('Developer', 'Reviewer')][string]$Role = 'Developer',
        [ValidateSet('NewWork', 'ReviewFix', 'DeliveryResume', 'Review')][string]$Mode = 'NewWork',
        [int]$PullRequestNumber = 0,
        [string[]]$ChangedFiles = @(),
        [AllowNull()][string]$IssueValidationId = $null
    )
    return [ordered]@{
        schemaVersion = 1
        runId = $RunId
        role = $Role
        mode = $Mode
        issueNumber = $IssueNumber
        pullRequestNumber = if ($PullRequestNumber -gt 0) { $PullRequestNumber } else { $null }
        headSha = $HeadSha
        issueValidationId = if ([string]::IsNullOrWhiteSpace($IssueValidationId)) { $null } else { $IssueValidationId }
        outcome = 'Succeeded'
        summary = 'Synthetic fixture result.'
        changedFiles = @($ChangedFiles)
        findings = @()
        manualVerification = @('Owner verifies the applicable behavior.')
    }
}

function New-HostCodexFixtureFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Events,
        [AllowNull()][object]$Result,
        [int]$ExitCode = 0,
        [string]$StdErr = '',
        [string]$RootHelp = '--ask-for-approval never',
        [string]$ExecHelp = '--ephemeral --json --color --cd --sandbox workspace-write read-only --ignore-user-config --strict-config --output-schema',
        [AllowNull()][object]$CapabilityProbe = $null
    )
    $fixture = [ordered]@{
        SchemaVersion = 1
        Version = 'codex-cli 0.0.0-fixture'
        RootHelp = $RootHelp
        ExecHelp = $ExecHelp
        ExitCode = $ExitCode
        StdErr = $StdErr
        TimedOut = $false
        JsonlLines = @($Events)
        Result = $Result
    }
    if ($null -ne $CapabilityProbe) { $fixture.CapabilityProbe = $CapabilityProbe }
    $path = Join-Path $script:temporaryRoot ($Name + '.codex.json')
    Write-HostTestFile $path (($fixture | ConvertTo-Json -Depth 64) + "`n")
    return $path
}

function Invoke-HostCodexFixture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FixturePath,
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [string]$HeadSha = ('e' * 40),
        [ValidateSet('Developer', 'Reviewer')][string]$Role = 'Developer',
        [ValidateSet('NewWork', 'ReviewFix', 'DeliveryResume', 'Review')][string]$Mode = 'NewWork',
        [int]$PullRequestNumber = 0,
        [string]$ConfigPath = ''
    )
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = $script:configPath }
    $parameters = @{
        ConfigPath = $ConfigPath
        RepositoryPath = $script:fakeRepository
        ArtifactsPath = (Join-Path $script:temporaryRoot ('codex-artifacts-' + [Guid]::NewGuid().ToString('N')))
        Role = $Role
        Mode = $Mode
        IssueNumber = $IssueNumber
        PinnedHeadSha = $HeadSha
        RunId = $RunId
        Prompt = 'Synthetic fixture prompt; no command execution is allowed.'
        FixturePath = $FixturePath
        DryRun = $true
    }
    if ($PullRequestNumber -gt 0) { $parameters.PullRequestNumber = $PullRequestNumber }
    return Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1') -Parameters $parameters
}

function New-HostUnityFixtureFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [hashtable]$Stages = @{},
        [hashtable]$Git = @{},
        [object[]]$ChangedPaths = @(),
        [AllowNull()][object]$Integrity = $null,
        [AllowNull()][object]$Determinism = $null,
        [switch]$UseFileSystemValidation
    )
    $fixture = [ordered]@{
        SchemaVersion = 1
        SkipFileSystemValidation = -not [bool]$UseFileSystemValidation
        Stages = [pscustomobject]$Stages
        Git = [pscustomobject]$Git
        ChangedPaths = @($ChangedPaths)
        KnownUnityDriftAllowed = $false
    }
    if (-not $UseFileSystemValidation) { $fixture.PointerFiles = @() }
    if ($null -ne $Integrity) { $fixture.Integrity = $Integrity }
    if ($null -ne $Determinism) { $fixture.Determinism = $Determinism }
    $path = Join-Path $script:temporaryRoot ($Name + '.unity.json')
    Write-HostTestFile $path (($fixture | ConvertTo-Json -Depth 64) + "`n")
    return $path
}

function Invoke-HostUnityFixture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FixturePath,
        [int]$IssueNumber = 5280,
        [string]$ConfigPath = '',
        [string]$IssueValidationId = '',
        [string]$ProjectPath = '',
        [string]$ArtifactsPath = ''
    )
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = $script:configPath }
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = $script:fakeRepository }
    if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) { $ArtifactsPath = Join-Path $script:temporaryRoot ('unity-artifacts-' + [Guid]::NewGuid().ToString('N')) }
    $parameters = @{
        ConfigPath = $ConfigPath
        ProjectPath = $ProjectPath
        ArtifactsPath = $ArtifactsPath
        IssueNumber = $IssueNumber
        BaselineRef = 'HEAD'
        ValidationFixturePath = $FixturePath
    }
    if (-not [string]::IsNullOrWhiteSpace($IssueValidationId)) { $parameters.IssueValidationId = $IssueValidationId }
    return Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1') -Parameters $parameters -TimeoutSeconds 60
}

function New-HostUnityFileSystemProject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$Broken
    )

    $root = Join-Path $script:temporaryRoot $Name
    $assets = Join-Path $root 'Assets'
    $data = Join-Path $assets 'FixtureData'
    [IO.Directory]::CreateDirectory($data) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'Packages')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'ProjectSettings')) | Out-Null
    Write-HostTestFile (Join-Path $root 'ProjectSettings\ProjectVersion.txt') "m_EditorVersion: 6000.4.0f1`n"
    Write-HostTestFile (Join-Path $assets 'FixtureData.meta') "fileFormatVersion: 2`nguid: 11111111111111111111111111111111`nfolderAsset: yes`n"

    if (-not $Broken) {
        $scriptGuid = '22222222222222222222222222222222'
        Write-HostTestFile (Join-Path $data 'FixtureScript.cs') "public sealed class FixtureScript { }`n"
        Write-HostTestFile (Join-Path $data 'FixtureScript.cs.meta') "fileFormatVersion: 2`nguid: $scriptGuid`n"
        Write-HostTestFile (Join-Path $data 'Clean.asset') "%YAML 1.1`n--- !u!114 &11400000`nMonoBehaviour:`n  m_Script: {fileID: 11500000, guid: $scriptGuid, type: 3}`n"
        Write-HostTestFile (Join-Path $data 'Clean.asset.meta') "fileFormatVersion: 2`nguid: 33333333333333333333333333333333`n"
        return [pscustomobject]@{
            ProjectPath = $root
            TrackedPaths = @('Assets/FixtureData.meta', 'Assets/FixtureData/FixtureScript.cs', 'Assets/FixtureData/FixtureScript.cs.meta', 'Assets/FixtureData/Clean.asset', 'Assets/FixtureData/Clean.asset.meta')
        }
    }

    $duplicateGuid = '44444444444444444444444444444444'
    Write-HostTestFile (Join-Path $data 'DuplicateOne.asset') "fixture: one`n"
    Write-HostTestFile (Join-Path $data 'DuplicateOne.asset.meta') "fileFormatVersion: 2`nguid: $duplicateGuid`n"
    Write-HostTestFile (Join-Path $data 'DuplicateTwo.asset') "fixture: two`n"
    Write-HostTestFile (Join-Path $data 'DuplicateTwo.asset.meta') "fileFormatVersion: 2`nguid: $duplicateGuid`n"
    Write-HostTestFile (Join-Path $data 'MissingMeta.asset') "fixture: missing-meta`n"
    Write-HostTestFile (Join-Path $data 'Orphan.asset.meta') "fileFormatVersion: 2`nguid: 55555555555555555555555555555555`n"
    Write-HostTestFile (Join-Path $data 'Broken.asset') "%YAML 1.1`n--- !u!114 &11400000`nMonoBehaviour:`n  m_Script: {fileID: 0}`n  missingReference: {fileID: 11400000, guid: ffffffffffffffffffffffffffffffff, type: 2}`n"
    Write-HostTestFile (Join-Path $data 'Broken.asset.meta') "fileFormatVersion: 2`nguid: 66666666666666666666666666666666`n"
    Write-HostTestFile (Join-Path $data 'Pointer.bin') "version https://git-lfs.github.com/spec/v1`noid sha256:7777777777777777777777777777777777777777777777777777777777777777`nsize 12`n"
    Write-HostTestFile (Join-Path $data 'Pointer.bin.meta') "fileFormatVersion: 2`nguid: 88888888888888888888888888888888`n"
    return [pscustomobject]@{
        ProjectPath = $root
        TrackedPaths = @(
            'Assets/FixtureData.meta', 'Assets/FixtureData/DuplicateOne.asset', 'Assets/FixtureData/DuplicateOne.asset.meta',
            'Assets/FixtureData/DuplicateTwo.asset', 'Assets/FixtureData/DuplicateTwo.asset.meta', 'Assets/FixtureData/MissingMeta.asset',
            'Assets/FixtureData/Orphan.asset.meta', 'Assets/FixtureData/Broken.asset', 'Assets/FixtureData/Broken.asset.meta',
            'Assets/FixtureData/Pointer.bin', 'Assets/FixtureData/Pointer.bin.meta'
        )
    }
}

function New-HostResumeFixtureBundle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateSet('ReviewFix', 'DeliveryResume')][string]$Mode,
        [Parameter(Mandatory = $true)][int]$IssueNumber,
        [Parameter(Mandatory = $true)][string]$PinnedSha,
        [Parameter(Mandatory = $true)][string]$DeliverySha,
        [Parameter(Mandatory = $true)][string]$StaleSha
    )

    $pr = $IssueNumber + 1000
    $headRef = "infra/existing-pr-$pr"
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
    $config = Import-SashimiHostConfig $script:fakeConfigPath
    $run = New-SashimiRunWorkspace -RunRoot ([string]$config.RunRoot) -RunId $runId
    Write-SashimiUtf8File (Join-Path $run.StatePath 'OwnedUnityPids.json') '{"SchemaVersion":1,"ProcessIds":[]}'
    $selection = [ordered]@{
        SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
        Role = 'Developer'; Mode = $Mode; ProjectItemId = "fixture-item-$IssueNumber"
        Status = 'In Progress'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
        IssueUpdatedAt = '2026-01-01T00:00:00Z'
        IssueNumber = $IssueNumber; IssueTitle = "Synthetic $Mode"; IssueBody = 'Fixture acceptance criteria.'
        IssueBodySha256 = (Get-SashimiTextSha256 -Text 'Fixture acceptance criteria.')
        IssueUrl = "https://example.invalid/issues/$IssueNumber"
        PullRequestNumber = $pr; PullRequestUrl = "https://example.invalid/pull/$pr"
        PullRequestTitle = "Synthetic Issue $IssueNumber delivery"
        PullRequestBody = 'Synthetic existing PR body.'
        PullRequestContentSha256 = Get-SashimiPullRequestContentSha256 -Title "Synthetic Issue $IssueNumber delivery" -Body 'Synthetic existing PR body.'
        PullRequestHeadSha = $PinnedSha; PullRequestHeadRef = $headRef
        PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
        PendingCommand = 'evidence-only --never-execute'
        LatestHandoffUrl = "https://example.invalid/handoff/$IssueNumber"
        Conversation = @()
        ConversationSha256 = (Get-SashimiConversationSha256 -Records @())
    }
    $selectionPath = Join-Path $run.StatePath 'Selection.json'
    Write-HostTestFile $selectionPath (($selection | ConvertTo-Json -Depth 32) + "`n")

    $codexResult = New-HostCodexResult -RunId $runId -IssueNumber $IssueNumber -HeadSha $PinnedSha -Role Developer -Mode $Mode -PullRequestNumber $pr
    $codexText = $codexResult | ConvertTo-Json -Depth 32 -Compress
    $codexFixture = New-HostCodexFixtureFile -Name ("resume-$IssueNumber") -Result $codexResult -Events @(
        [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = "resume-message-$IssueNumber"; type = 'agent_message'; text = $codexText } },
        [ordered]@{ type = 'turn.completed' }
    )
    $unityFixture = New-HostUnityFixtureFile -Name ("resume-$IssueNumber")
    $scenarioRoot = New-HostDeveloperGhScenario -Root (Join-Path $script:temporaryRoot "developer-gh-$IssueNumber") `
        -IssueNumber $IssueNumber -PullRequestNumber $pr -HeadRef $headRef -PinnedSha $PinnedSha -AfterSha $DeliverySha -StaleSha $StaleSha
    return [pscustomobject]@{
        Run = $run; SelectionPath = $selectionPath; Selection = [pscustomobject]$selection
        CodexFixture = $codexFixture; UnityFixture = $unityFixture; ScenarioRoot = $scenarioRoot
        Branch = "agent/$IssueNumber-$($runId.Substring($runId.Length - 8))"
        PushState = Join-Path $run.StatePath 'fake-push.completed'
        StatusState = Join-Path $run.StatePath 'fake-status-transition.completed'
        DeliverySha = $DeliverySha
    }
}

function Remove-HostTestRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    Assert-HostTest ($full.StartsWith($temp + '\', [StringComparison]::OrdinalIgnoreCase)) "Test root escaped TEMP: $full"
    Assert-HostTest ((Split-Path -Leaf $full) -match '^SashimiBoyHostTests-[0-9a-f]{32}$') "Unexpected test root name: $full"
    $markerPath = Join-Path $full '.host-tests-owner.json'
    Assert-HostTest (Test-Path -LiteralPath $markerPath -PathType Leaf) "Test ownership marker is missing: $markerPath"
    $marker = [IO.File]::ReadAllText($markerPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-HostTest ([string]$marker.RunId -ceq $script:testRunId) 'Test ownership marker RunId mismatch.'
    Assert-SashimiNoReparsePoint -Path $full -Recurse
    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
}

try {
    [Environment]::SetEnvironmentVariable('SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS', '1', 'Process')
    $candidate = Join-Path ([IO.Path]::GetTempPath()) ('SashimiBoyHostTests-' + $script:testRunId)
    Assert-SashimiNoReparsePoint -Path $candidate
    if (Test-Path -LiteralPath $candidate) { throw "Refusing pre-existing test root: $candidate" }
    [IO.Directory]::CreateDirectory($candidate) | Out-Null
    $script:temporaryRoot = [IO.Path]::GetFullPath($candidate)
    $script:ownedTemporaryRoot = $true
    Write-HostTestFile (Join-Path $script:temporaryRoot '.host-tests-owner.json') (([ordered]@{
                SchemaVersion = 1
                RunId = $script:testRunId
                Root = $script:temporaryRoot
            } | ConvertTo-Json -Compress) + "`n")
    $script:configPath = New-HostTestConfig
    $script:fakeToolLogPath = Join-Path $script:temporaryRoot 'fake-tool-audit.tsv'
    $script:fakeTools = New-HostFakeToolAdapters -Root $script:temporaryRoot
    $script:fakeConfigPath = New-HostTestConfig -GitExecutable $script:fakeTools.Git -GitLfsExecutable $script:fakeTools.GitLfs -GitHubCli $script:fakeTools.GitHub -UnityExecutable $script:fakeTools.Git
    Import-SashimiHostConfig $script:fakeConfigPath | Out-Null
    $script:fakeRepository = Join-Path $script:temporaryRoot 'fake-repository'
    [IO.Directory]::CreateDirectory((Join-Path $script:fakeRepository '.git')) | Out-Null

    Invoke-HostTestCase 'RequiredFilesExist' {
        $required = @(
            'Config.example.json',
            'HostAutomation.Common.ps1',
            'Install-SashimiHostAutomation.ps1',
            'Uninstall-SashimiHostAutomation.ps1',
            'Test-SashimiHostAutomation.ps1',
            'Invoke-SashimiHostOrchestrator.ps1',
            'Get-SashimiProjectQueue.ps1',
            'Invoke-SashimiCodexExec.ps1',
            'Invoke-SashimiDeveloperRun.ps1',
            'Invoke-SashimiReviewerRun.ps1',
            'Invoke-SashimiUnityValidation.ps1',
            'Publish-SashimiRunResult.ps1',
            'Tests\Test-SashimiHostAutomation.ps1'
        )
        foreach ($relative in $required) {
            Assert-HostTest (Test-Path -LiteralPath (Join-Path $hostRoot $relative) -PathType Leaf) "Required HostAutomation file is missing: $relative"
        }
    }

    Invoke-HostTestCase 'PowerShell7ParserAcceptsEveryScript' {
        Assert-HostTest ($PSVersionTable.PSEdition -ceq 'Core' -and $PSVersionTable.PSVersion.Major -ge 7) "Tests require PowerShell 7; found $($PSVersionTable.PSVersion)."
        Assert-HostTest (Test-Path -LiteralPath $PowerShellPath -PathType Leaf) "Stable PowerShell 7 executable is missing: $PowerShellPath"
        $scripts = @(Get-ChildItem -LiteralPath $hostRoot -Filter '*.ps1' -File -Recurse)
        Assert-HostTest ($scripts.Count -ge 12) 'Expected all HostAutomation scripts before parser validation.'
        foreach ($scriptFile in $scripts) {
            $tokens = $null
            $errors = $null
            [void][Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$errors)
            Assert-HostTest ($errors.Count -eq 0) "Parser errors in $($scriptFile.Name): $([string]::Join('; ', [string[]]$errors))"
        }
    }

    Invoke-HostTestCase 'Utf8KoreanTitleAndMultilineBodyRoundTrip' {
        $title = '[P0][인프라] 윈도우 호스트 자동화'
        $body = "첫 번째 줄`r`n둘째 줄: 회 뜨기`n셋째 줄 ✅"
        $path = Join-Path $script:temporaryRoot 'utf8-korean.json'
        Write-SashimiUtf8File $path (([ordered]@{ Title = $title; Body = $body } | ConvertTo-SashimiJson) + "`n")
        $bytes = [IO.File]::ReadAllBytes($path)
        Assert-HostTest (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'Fixture unexpectedly has a UTF-8 BOM.'
        $roundTrip = Read-SashimiJsonFile $path
        Assert-HostTest ([string]$roundTrip.Title -ceq $title) 'Korean title changed during UTF-8 JSON round trip.'
        Assert-HostTest ([string]$roundTrip.Body -ceq $body) 'Multiline body changed during UTF-8 JSON round trip.'
        $common = [IO.File]::ReadAllText($commonPath)
        Assert-HostTest ($common -match 'StandardOutputEncoding\s*=\s*\[Text\.UTF8Encoding\]') 'Child stdout encoding is not explicitly UTF-8.'
        Assert-HostTest ($common -match 'StandardErrorEncoding\s*=\s*\[Text\.UTF8Encoding\]') 'Child stderr encoding is not explicitly UTF-8.'

        $queueFixture = New-HostQueueFixtureFile -Name 'utf8-korean' -Items @(
            (New-HostQueueItem -IssueNumber 5201 -Status Ready -Priority P0 -Title $title -Body $body)
        )
        $queue = Invoke-HostQueueFixture $queueFixture
        Assert-HostTest ($queue.ExitCode -eq 0) "Korean queue fixture failed: $($queue.StdErr) $($queue.StdOut)"
        $queueJson = ConvertFrom-LastHostJson $queue.StdOut
        Assert-HostTest ([string]$queueJson.Encoding -ceq 'UTF-8') 'Queue result does not declare UTF-8.'
        Assert-HostTest ([string]$queueJson.IssueTitle -ceq $title) 'Queue changed the Korean Issue title.'
        Assert-HostTest ([string]$queueJson.IssueBody -ceq $body) 'Queue changed the multiline Issue body.'
        Assert-HostTest ([string]$queueJson.ConversationSha256 -ceq (Get-SashimiConversationSha256 -Records @())) 'Queue did not emit the canonical empty-conversation digest.'
    }

    Invoke-HostTestCase 'ProjectV2PaginationIsCompleteAndFailsClosed' {
        $items = @(
            (New-HostQueueItem -IssueNumber 5210 -Status Ready -Priority P1),
            (New-HostQueueItem -IssueNumber 5211 -Status Ready -Priority P2)
        )
        $fixture = New-HostQueueFixtureFile -Name 'two-pages' -Items $items -TwoPages
        $result = Invoke-HostQueueFixture $fixture
        Assert-HostTest ($result.ExitCode -eq 0) "Two-page queue failed: $($result.StdErr) $($result.StdOut)"
        $json = ConvertFrom-LastHostJson $result.StdOut
        Assert-HostTest ([int]$json.ProjectPageCount -eq 2) 'Queue did not consume both fixture pages.'
        Assert-HostTest ([int]$json.CandidateCount -eq 2) 'Queue did not combine paginated candidates.'
        Assert-HostTest (-not [bool]$json.MutationAttempted) 'Read-only queue reported a mutation.'

        $incompleteFixture = New-HostQueueFixtureFile -Name 'unfulfilled-cursor' -Items $items -LeaveCursorUnfulfilled
        $incomplete = Invoke-HostQueueFixture $incompleteFixture
        Assert-HostTest ($incomplete.ExitCode -ne 0) 'Queue accepted an unfulfilled pagination cursor.'
        $failure = ConvertFrom-LastHostJson $incomplete.StdOut
        Assert-HostTest (-not [bool]$failure.Selected -and -not [bool]$failure.MutationAttempted) 'Incomplete pagination selected or mutated work.'

        $scenario = New-HostLiveQueueScenario -Root (Join-Path $script:temporaryRoot 'live-queue-scenario')
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $live = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Get-SashimiProjectQueue.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GH_SCENARIO = 'queue-pagination'
            SASHIMI_FAKE_SCENARIO_ROOT = $scenario.Root
        }
        Assert-HostTest ($live.ExitCode -eq 0) "Fake-gh live queue failed: $($live.StdErr) $($live.StdOut)"
        $liveJson = ConvertFrom-LastHostJson $live.StdOut
        Assert-HostTest ([string]$liveJson.DataSource -ceq 'Live' -and [int]$liveJson.ProjectPageCount -eq 2) 'Live queue path did not consume both ProjectV2 item pages.'
        Assert-HostTest ([int]$liveJson.IssueNumber -eq $scenario.IssueNumber -and [string]$liveJson.IssueTitle -ceq $scenario.Title -and [string]$liveJson.IssueBody -ceq $scenario.Body) 'Live fake-gh pagination/UTF-8 round trip changed the selected Korean Issue.'
        Assert-HostTest ([string]$liveJson.ConversationSha256 -ceq (Get-SashimiConversationSha256 -Records @($liveJson.Conversation))) 'Live queue conversation digest is not the stable canonical serialization of all eligibility records.'
        $liveCalls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore | Where-Object Tool -eq 'gh')
        Assert-HostTest ($liveCalls.Count -eq 6) "Expected six live GraphQL calls (two field, two item, two Issue comment); observed $($liveCalls.Count)."
        $serializedCalls = $liveCalls.Arguments -join "`n"
        Assert-HostTest ($serializedCalls -match 'cursor=fixture-fields-page-2' -and $serializedCalls -match 'cursor=fixture-items-page-2') 'Live GraphQL cursor values were not sent to fake gh.'
        Assert-HostTest (@($liveCalls | Where-Object SimulatedMutation).Count -eq 0) 'Read-only live queue issued a mutation-shaped gh command.'
    }

    Invoke-HostTestCase 'QueueOrderIsReviewThenReviewFixThenDeliveryResumeThenReady' {
        $review = New-HostQueueItem -IssueNumber 5221 -Status Review -Priority P3 -UpdatedAt '2026-04-04T00:00:00Z'
        $reviewFix = New-HostQueueItem -IssueNumber 5222 -Status 'In Progress' -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z' -HandoffMode ReviewFix
        $delivery = New-HostQueueItem -IssueNumber 5223 -Status 'In Progress' -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z' -HandoffMode DeliveryResume
        $ready = New-HostQueueItem -IssueNumber 5224 -Status Ready -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $bands = @(
            [pscustomobject]@{ Items = @($review, $reviewFix, $delivery, $ready); Mode = 'Review'; Issue = 5221; Role = 'Reviewer' },
            [pscustomobject]@{ Items = @($reviewFix, $delivery, $ready); Mode = 'ReviewFix'; Issue = 5222; Role = 'Developer' },
            [pscustomobject]@{ Items = @($delivery, $ready); Mode = 'DeliveryResume'; Issue = 5223; Role = 'Developer' },
            [pscustomobject]@{ Items = @($ready); Mode = 'NewWork'; Issue = 5224; Role = 'Developer' }
        )
        $index = 0
        foreach ($band in $bands) {
            $index++
            $fixture = New-HostQueueFixtureFile -Name "class-order-$index" -Items $band.Items
            $process = Invoke-HostQueueFixture $fixture
            Assert-HostTest ($process.ExitCode -eq 0) "Queue class-order fixture $index failed: $($process.StdOut)"
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest ([string]$json.Mode -ceq $band.Mode -and [string]$json.Role -ceq $band.Role -and [int]$json.IssueNumber -eq $band.Issue) "Queue class order was wrong for fixture $index."
            if ($band.Mode -cne 'NewWork') {
                Assert-HostTest ([string]$json.PullRequestContentSha256 -ceq
                    (Get-SashimiPullRequestContentSha256 -Title ([string]$json.PullRequestTitle) -Body ([string]$json.PullRequestBody))) `
                    "Queue omitted or changed the exact PR title/body content pin for fixture $index."
            }
        }
    }

    Invoke-HostTestCase 'QueueSortUsesPriorityAgeThenIssueAndDispatchesExactlyOne' {
        $priorityWinner = New-HostQueueItem -IssueNumber 5230 -Status Ready -Priority P0 -UpdatedAt '2026-05-01T00:00:00Z'
        $lowerPriority = New-HostQueueItem -IssueNumber 5229 -Status Ready -Priority P1 -UpdatedAt '2025-01-01T00:00:00Z'
        $fixture = New-HostQueueFixtureFile -Name 'priority-order' -Items @($lowerPriority, $priorityWinner)
        $json = ConvertFrom-LastHostJson (Invoke-HostQueueFixture $fixture).StdOut
        Assert-HostTest ([int]$json.IssueNumber -eq 5230) 'Priority did not precede age.'

        $older = New-HostQueueItem -IssueNumber 5232 -Status Ready -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $newer = New-HostQueueItem -IssueNumber 5231 -Status Ready -Priority P0 -UpdatedAt '2026-02-01T00:00:00Z'
        $fixture = New-HostQueueFixtureFile -Name 'age-order' -Items @($newer, $older)
        $json = ConvertFrom-LastHostJson (Invoke-HostQueueFixture $fixture).StdOut
        Assert-HostTest ([int]$json.IssueNumber -eq 5232) 'Oldest Project update did not win within a priority.'

        $highNumber = New-HostQueueItem -IssueNumber 5234 -Status Ready -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $lowNumber = New-HostQueueItem -IssueNumber 5233 -Status Ready -Priority P0 -UpdatedAt '2026-01-01T00:00:00Z'
        $fixture = New-HostQueueFixtureFile -Name 'issue-order' -Items @($highNumber, $lowNumber, $priorityWinner)
        $json = ConvertFrom-LastHostJson (Invoke-HostQueueFixture $fixture).StdOut
        Assert-HostTest ([int]$json.IssueNumber -eq 5233) 'Issue number did not break an exact sort tie.'
        Assert-HostTest ([int]$json.DispatchCount -eq 1 -and [bool]$json.Selected) 'Queue dispatched more or less than exactly one Issue.'
    }

    Invoke-HostTestCase 'QueueAndPublisherRejectRecognizableSensitiveContent' {
        $uriSecret = 'https://fixture-user:fixture-password@example.invalid/private'
        $queueFixture = New-HostQueueFixtureFile -Name 'sensitive-queue-prose' -Items @(
            (New-HostQueueItem -IssueNumber 5299 -Status Ready -Priority P0 -Body "Acceptance criteria include $uriSecret")
        )
        $queue = Invoke-HostQueueFixture $queueFixture
        Assert-HostTest ($queue.ExitCode -ne 0) 'Queue persisted credential-bearing Issue prose.'
        $queueText = [string]$queue.StdOut + "`n" + [string]$queue.StdErr
        Assert-HostTest ($queueText.IndexOf('fixture-password',[StringComparison]::Ordinal) -lt 0 -and
            [string](ConvertFrom-LastHostJson $queue.StdOut).Error -match 'sensitive') `
            'Queue failure leaked or did not identify suppressed sensitive source content.'

        $config = Import-SashimiHostConfig $script:configPath
        $run = New-SashimiRunWorkspace -RunRoot ([string]$config.RunRoot)
        $exactSecret = 'opaque-publisher-' + [Guid]::NewGuid().ToString('N')
        $basicSecret = 'Authorization: Basic Zml4dHVyZS11c2VyOmZpeHR1cmUtcGFzc3dvcmQ='
        Assert-HostTest (Test-SashimiRecognizableSensitiveText -Text $basicSecret) 'Basic Authorization content is not recognized as sensitive.'
        $bodyPath = Join-Path $run.StatePath 'unsafe-publication-body.md'
        Write-HostTestFile $bodyPath "Fixture evidence $exactSecret`n$basicSecret`n"
        $publishFixturePath = Join-Path $script:temporaryRoot 'sensitive-publication.json'
        $publishFixture = [ordered]@{
            SchemaVersion=1; AuthenticatedLogin='DongGyunLeeeee'; CurrentStatus='In Progress'; OpenPullRequestCount=0
            IssueUpdatedAt='2026-09-05T00:00:00Z'; IssueBodySha256=('3' * 64); LiveConversationRecords=@()
        }
        Write-HostTestFile $publishFixturePath (($publishFixture | ConvertTo-Json -Depth 16) + "`n")
        $publish = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$script:configPath; Action='Comment'; Role='Developer'; IssueNumber=5300
            ProjectItemId='fixture-item-5300'; CommentTarget='Issue'; BodyPath=$bodyPath
            PinnedIssueUpdatedAt='2026-09-05T00:00:00Z'; PinnedIssueBodySha256=('3' * 64)
            PinnedConversationSha256=(Get-SashimiConversationSha256 -Records @()); FixturePath=$publishFixturePath
        } -Environment @{ SASHIMI_FIXTURE_PASSWORD=$exactSecret }
        Assert-HostTest ($publish.ExitCode -ne 0) 'Publisher accepted recognizable or inherited sensitive content.'
        $publishJson = ConvertFrom-LastHostJson $publish.StdOut
        $publishText = [string]$publish.StdOut + "`n" + [string]$publish.StdErr
        Assert-HostTest (-not [bool]$publishJson.MutationAttempted -and @($publishJson.Commands | Where-Object Mutation).Count -eq 0) `
            'Sensitive publication reached a mutation boundary.'
        Assert-HostTest ($publishText.IndexOf($exactSecret,[StringComparison]::Ordinal) -lt 0 -and
            $publishText.IndexOf('Zml4dHVyZS11c2VyOmZpeHR1cmUtcGFzc3dvcmQ=',[StringComparison]::Ordinal) -lt 0) `
            'Sensitive publication failure returned credential material.'
    }

    Invoke-HostTestCase 'OrchestratorDryRunDispatchesExactlyOneWithoutMutation' {
        $fixture = New-HostQueueFixtureFile -Name 'orchestrator-one-issue' -Items @(
            (New-HostQueueItem -IssueNumber 5235 -Status Review -Priority P2),
            (New-HostQueueItem -IssueNumber 5236 -Status 'In Progress' -Priority P0 -HandoffMode ReviewFix),
            (New-HostQueueItem -IssueNumber 5237 -Status Ready -Priority P0)
        ) -TwoPages
        $runRootBefore = @(Get-ChildItem -LiteralPath ([string](Import-SashimiHostConfig $script:configPath).RunRoot) -Force -ErrorAction SilentlyContinue).Count
        $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1') -Parameters @{
            ConfigPath = $script:configPath
            QueueFixturePath = $fixture
            MutexName = ('Global\SashimiBoyOrchestratorTest-' + $script:testRunId)
            DryRun = $true
        }
        Assert-HostTest ($process.ExitCode -eq 0) "Orchestrator DryRun failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.Success -and [bool]$json.ExactlyOneIssue -and [string]$json.State -ceq 'DryRunPlanned') 'Orchestrator did not produce an exactly-one DryRun plan.'
        Assert-HostTest ([int]$json.Selection.DispatchCount -eq 1 -and [int]$json.Selection.IssueNumber -eq 5235) 'Orchestrator dispatched the wrong number or Issue.'
        Assert-HostTest (@($json.Commands | Where-Object Stage -eq 'Dispatch exactly one dry-run role').Count -eq 1) 'Orchestrator planned more or less than one role dispatch.'
        Assert-HostTest ([string]::IsNullOrWhiteSpace([string]$json.RunPath)) 'Orchestrator DryRun created a run workspace.'
        $runRootAfter = @(Get-ChildItem -LiteralPath ([string](Import-SashimiHostConfig $script:configPath).RunRoot) -Force -ErrorAction SilentlyContinue).Count
        Assert-HostTest ($runRootAfter -eq $runRootBefore) 'Orchestrator DryRun mutated the configured RunRoot.'
    }

    Invoke-HostTestCase 'OrchestratorOutputIsMetadataOnly' {
        $issue = 5292
        $head = '2' * 40
        $titleMarker = 'SENSITIVE_ISSUE_TITLE_5292'
        $bodyMarker = 'SENSITIVE_ISSUE_BODY_5292'
        $pullBodyMarker = 'SENSITIVE_PULL_BODY_5292'
        $pendingMarker = 'SENSITIVE_PENDING_COMMAND_5292'
        $findingMarker = 'SENSITIVE_FINDING_BODY_5292'
        $conversationMarker = 'SENSITIVE_CONVERSATION_5292'
        $findingUrl = 'https://example.invalid/findings/5292'
        $item = New-HostQueueItem -IssueNumber $issue -Status 'In Progress' -Priority P0 -HandoffMode ReviewFix -HeadSha $head
        $item.IssueTitle = $titleMarker
        $item.IssueBody = $bodyMarker
        $item.PullRequests[0]['Title'] = 'SENSITIVE_PULL_TITLE_5292'
        $item.PullRequests[0]['Body'] = $pullBodyMarker
        $item.CurrentHandoff.PendingCommand = $pendingMarker
        $item.CurrentHandoff.FindingUrl = $findingUrl
        $item.Comments = @([ordered]@{
                Url = $findingUrl; CreatedAt = '2026-01-01T00:00:01Z'; AuthorLogin = 'DongGyunLeeeee'
                Body = "$findingMarker $conversationMarker"
            })
        $fixture = New-HostQueueFixtureFile -Name 'orchestrator-metadata-only' -Items @($item)
        $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1') -Parameters @{
            ConfigPath = $script:configPath
            QueueFixturePath = $fixture
            MutexName = ('Global\SashimiBoyOrchestratorMetadataTest-' + $script:testRunId)
            DryRun = $true
        }
        Assert-HostTest ($process.ExitCode -eq 0) "Metadata-only Orchestrator DryRun failed: $($process.StdErr) $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([int]$json.Selection.IssueNumber -eq $issue -and [string]$json.Selection.Mode -ceq 'ReviewFix') 'Metadata-only result lost required dispatch identity.'
        foreach ($name in @('IssueTitle','IssueBody','PullRequestTitle','PullRequestBody','PendingCommand','FindingBody','Conversation')) {
            Assert-HostTest ($null -eq $json.Selection.PSObject.Properties[$name]) "Orchestrator selection summary retained sensitive property '$name'."
        }
        foreach ($marker in @($titleMarker,$bodyMarker,$pullBodyMarker,$pendingMarker,$findingMarker,$conversationMarker)) {
            Assert-HostTest ($process.StdOut -notmatch [regex]::Escape($marker)) "Orchestrator output retained sensitive marker '$marker'."
        }

        $source = [IO.File]::ReadAllText((Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1'), [Text.Encoding]::UTF8)
        Assert-HostTest ($source -notmatch '(?m)\[Console\]::(?:Out|Error)\.Write(?:Line)?\(\s*\$child\.Standard(?:Output|Error)') `
            'Protected entry point relays raw linked-child stdout or stderr.'
        $parseIndex = $source.IndexOf('$childJsonLines=@($child.StandardOutput', [StringComparison]::Ordinal)
        $validatedIndex = $source.IndexOf('$validatedChildJson=ConvertTo-OrchestratorJson $childResult', [StringComparison]::Ordinal)
        $relayIndex = $source.IndexOf('[Console]::Out.WriteLine($protectedChildJson)', [StringComparison]::Ordinal)
        Assert-HostTest ($parseIndex -ge 0 -and $validatedIndex -gt $parseIndex -and $relayIndex -gt $validatedIndex) `
            'Linked-child output is not parsed, contract-checked, sanitized, then emitted in that order.'
    }

    Invoke-HostTestCase 'OrchestratorMutexContentionIsSuccessfulNoOp' {
        $fixture = New-HostQueueFixtureFile -Name 'orchestrator-mutex' -Items @(
            (New-HostQueueItem -IssueNumber 5238 -Status Ready -Priority P0)
        )
        $mutexName = 'Global\SashimiBoyOrchestratorHeld-' + $script:testRunId
        $lease = Enter-SashimiHostMutex -Name $mutexName
        Assert-HostTest ([bool]$lease.Acquired) 'Could not hold orchestrator test mutex.'
        try {
            $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1') -Parameters @{
                ConfigPath = $script:configPath
                QueueFixturePath = $fixture
                MutexName = $mutexName
                DryRun = $true
            }
            Assert-HostTest ($process.ExitCode -eq 0) "Contended orchestrator did not no-op successfully: $($process.StdOut)"
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest ([bool]$json.Success -and [bool]$json.AlreadyRunning -and [string]$json.State -ceq 'AlreadyRunning') 'Contended orchestrator did not report AlreadyRunning.'
            Assert-HostTest (@($json.Commands).Count -eq 0 -and $null -eq $json.Selection) 'Contended orchestrator read or dispatched the queue.'
        }
        finally { Exit-SashimiHostMutex $lease }
    }

    Invoke-HostTestCase 'VerificationAndDoneAreNeverSelected' {
        $fixture = New-HostQueueFixtureFile -Name 'terminal-statuses' -Items @(
            (New-HostQueueItem -IssueNumber 5240 -Status Verification -Priority P0),
            (New-HostQueueItem -IssueNumber 5241 -Status Done -Priority P0)
        )
        $process = Invoke-HostQueueFixture $fixture
        Assert-HostTest ($process.ExitCode -eq 0) "Terminal-status queue fixture failed unexpectedly: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (-not [bool]$json.Selected -and [int]$json.DispatchCount -eq 0) 'Verification or Done was selected.'
        Assert-HostTest (@($json.ExcludedCandidates).Count -eq 2) 'Terminal statuses were not explicitly excluded.'
    }

    Invoke-HostTestCase 'ConfigIsFixedAndRetentionDefaultsTo14Days' {
        $configPath = New-HostTestConfig
        $config = Import-SashimiHostConfig $configPath
        Assert-HostTest ([string]$config.Repository -ceq 'DongGyunLeeeee/sashimi-boy-unity') 'Repository scope changed.'
        Assert-HostTest ([string]$config.ProjectOwner -ceq 'DongGyunLeeeee' -and [int]$config.ProjectNumber -eq 1) 'Project scope changed.'
        Assert-HostTest ([string]$config.DefaultBranch -ceq 'main') 'Default branch is not main.'
        Assert-HostTest ([int]$config.ArtifactRetentionDays -eq 14) 'Default artifact retention is not 14 days.'
        Assert-HostTest ([string]$config.Task.MultipleInstances -ceq 'IgnoreNew') 'Task config does not require IgnoreNew.'
        Assert-HostTest ([string]$config.GitExecutable -ceq 'C:\Program Files\Git\cmd\git.exe') 'Config does not pin the canonical Git executable.'
        Assert-HostTest ([string]$config.GitLfsExecutable -ceq 'C:\Program Files\Git\cmd\git-lfs.exe') 'Config does not pin the canonical Git LFS executable.'
        Assert-HostTest ([string]$config.GitAuthorName -ceq 'DongGyunLeeeee' -and [string]$config.GitAuthorEmail -ceq '83210475+DongGyunLeeeee@users.noreply.github.com') 'Config does not pin the explicit Git author identity.'
        Assert-HostTest ([string]$config.GitHubCli -ceq 'C:\Program Files\GitHub CLI\gh.exe') 'Config does not pin the canonical GitHub CLI executable.'
        Assert-HostTest ([string]$config.CodexExecutable -ceq 'C:\Users\02031\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe') 'Config does not pin the canonical Codex executable.'
        Assert-HostTest ([string]$config.PowerShellExecutable -ceq 'C:\Program Files\PowerShell\7\pwsh.exe') 'Config does not pin stable PowerShell.'
        Assert-HostTest ([string]$config.UnityExecutable -ceq 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe') 'Config does not pin the expected Unity executable.'
    }

    Invoke-HostTestCase 'ProtectedEntrypointDropsElevationBeforeRuntimeTrust' {
        $orchestratorPath = Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1'
        $source = [IO.File]::ReadAllText($orchestratorPath, [Text.Encoding]::UTF8)
        $integrityIndex = $source.IndexOf('$script:integrityResult = Assert-OrchestratorRuntimeIntegrity', [StringComparison]::Ordinal)
        $tokenIndex = $source.IndexOf('$currentProcessElevated=Test-OrchestratorTokenElevated', [StringComparison]::Ordinal)
        $relaunchIndex = $source.IndexOf('$child=Invoke-OrchestratorUnelevated', [StringComparison]::Ordinal)
        $commonIndex = $source.IndexOf(". (Join-Path `$PSScriptRoot 'HostAutomation.Common.ps1')", [StringComparison]::Ordinal)
        $configIndex = $source.IndexOf('$script:orchestratorConfig = Import-SashimiHostConfig', [StringComparison]::Ordinal)
        $executableIdentityIndex = $source.IndexOf('$verifiedExecutableCount=Assert-OrchestratorExecutableIdentity -Path $expectedExecutableIdentity', [StringComparison]::Ordinal)
        Assert-HostTest ($integrityIndex -ge 0 -and $tokenIndex -gt $integrityIndex -and $relaunchIndex -gt $tokenIndex -and
            $executableIdentityIndex -ge 0 -and $executableIdentityIndex -lt $integrityIndex -and
            $commonIndex -gt $relaunchIndex -and $configIndex -gt $commonIndex) `
            'Integrity, linked-token relaunch, Common import, and configuration import are not in the required trust order.'
        Assert-HostTest ($source.Contains("'ExecutableIdentity.json'") -and $source.Contains('ExecutablesVerified=$verifiedExecutableCount')) `
            'The protected manifest gate no longer binds and reports executable identity verification.'
        Assert-HostTest ($source.Contains('TokenLinkedToken = 19') -and
            $source.Contains('linkedToken == IntPtr.Zero || IsTokenElevated(linkedToken)') -and
            $source.Contains('WindowsIdentity.GetCurrent().User.Value') -and
            $source.Contains('linkedIdentity.User.Value')) `
            'The elevated-parent relaunch does not visibly require a non-elevated linked token for the same SID.'
        Assert-HostTest ($source.Contains("'-IntegrityManifestPath',`$IntegrityManifestPath,'-UnelevatedChild'")) `
            'The linked-token child is not forced to revalidate the exact installed manifest.'
        $powerShellRecheckIndex = $source.IndexOf('[void](Assert-OrchestratorExecutableIdentity -Path (Join-Path $PSScriptRoot $script:ExecutableIdentityName))', [StringComparison]::Ordinal)
        $linkedLaunchIndex = $source.IndexOf('[SashimiBoyAutomation.LinkedTokenProcess]::RunUnelevated', [StringComparison]::Ordinal)
        Assert-HostTest ($powerShellRecheckIndex -ge 0 -and $linkedLaunchIndex -gt $powerShellRecheckIndex) `
            'The stable PowerShell identity is not rehashed immediately before the linked-token launch.'

        $suspendedCreateIndex = $source.IndexOf('CREATE_NO_WINDOW | CREATE_SUSPENDED', [StringComparison]::Ordinal)
        $jobAssignIndex = $source.IndexOf('AssignProcessToJobObject(job, process.hProcess)', [StringComparison]::Ordinal)
        $resumeIndex = $source.IndexOf('ResumeThread(process.hThread)', [StringComparison]::Ordinal)
        Assert-HostTest ($suspendedCreateIndex -ge 0 -and $jobAssignIndex -gt $suspendedCreateIndex -and $resumeIndex -gt $jobAssignIndex) `
            'The linked-token child is not created suspended, assigned to its job, and only then resumed.'
        Assert-HostTest ($source.Contains('JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE') -and
            $source.Contains('LINKED_CHILD_TIMEOUT_MS = 11u * 60u * 60u * 1000u') -and
            $source.Contains('TERMINATION_CONFIRM_TIMEOUT_MS = 10000u') -and
            $source.Contains('PIPE_DRAIN_TIMEOUT_MS = 10000') -and
            $source.Contains('TerminateJobObject(job, FORCED_TERMINATION_EXIT_CODE)') -and
            $source.Contains('CloseNativeHandle(ref job)')) `
            'The elevated parent no longer enforces the fixed deadline and kill-on-close process-tree contract.'
        Assert-HostTest (-not $source.Contains('WaitForSingleObject(process.hProcess, INFINITE)')) `
            'The linked-token launcher contains an unbounded child wait.'
        $timeoutStartIndex = $source.IndexOf('if (waitResult == WAIT_TIMEOUT)', [StringComparison]::Ordinal)
        $timeoutEndIndex = $source.IndexOf('if (waitResult == WAIT_FAILED)', $timeoutStartIndex, [StringComparison]::Ordinal)
        Assert-HostTest ($timeoutStartIndex -ge 0 -and $timeoutEndIndex -gt $timeoutStartIndex) `
            'The linked-token timeout branch is missing or malformed.'
        $timeoutBlock = $source.Substring($timeoutStartIndex, $timeoutEndIndex - $timeoutStartIndex)
        Assert-HostTest ($timeoutBlock.Contains('RequestProcessTreeTermination') -and
            $timeoutBlock.Contains('WaitForSingleObject(process.hProcess, TERMINATION_CONFIRM_TIMEOUT_MS)') -and
            -not $timeoutBlock.Contains('GetAwaiter().GetResult()') -and
            -not $timeoutBlock.Contains('StandardOutput') -and
            -not $timeoutBlock.Contains('StandardError')) `
            'The linked-token timeout path does not terminate and confirm within a bound without draining or relaying child output.'

        $productionProbe = Invoke-HostTestScript -ScriptPath $orchestratorPath -Parameters @{
            ConfigPath = $script:configPath
        } -Environment @{ SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS = '' }
        Assert-HostTest ($productionProbe.ExitCode -ne 0) 'Source-tree production execution without a manifest did not fail closed.'
        $probeJson = ConvertFrom-LastHostJson $productionProbe.StdOut
        Assert-HostTest (-not [bool]$probeJson.Success -and [string]$probeJson.Error -ceq 'HostOrchestratorFailed' -and
            [bool]$probeJson.Integrity.Required -and -not [bool]$probeJson.Integrity.Verified -and
            [string]$probeJson.PrivilegeBoundary.Reason -ceq 'NotChecked') `
            'Source-tree production rejection did not retain metadata-only fail-closed integrity evidence.'
        Assert-HostTest (@($probeJson.Commands).Count -eq 0 -and [string]::IsNullOrWhiteSpace([string]$probeJson.RunId)) `
            'Source-tree production rejection reached configured tools or created run state.'
    }

    Invoke-HostTestCase 'GlobalMutexRejectsSecondProcess' {
        $mutexName = 'Global\SashimiBoyHostTests-' + $script:testRunId
        $lease = Enter-SashimiHostMutex -Name $mutexName
        Assert-HostTest ([bool]$lease.Acquired) 'Primary test process could not acquire its unique mutex.'
        try {
            $probePath = Join-Path $script:temporaryRoot 'mutex-probe.ps1'
            $escapedCommon = $commonPath.Replace("'", "''")
            $escapedName = $mutexName.Replace("'", "''")
            Write-HostTestFile $probePath @"
`$ErrorActionPreference = 'Stop'
. '$escapedCommon'
`$lease = Enter-SashimiHostMutex -Name '$escapedName' -TimeoutMilliseconds 0
[Console]::Out.WriteLine((ConvertTo-SashimiJson ([ordered]@{ Acquired = [bool]`$lease.Acquired })))
if (`$lease.Acquired) { Exit-SashimiHostMutex `$lease }
"@
            $probe = Invoke-HostTestScript $probePath
            Assert-HostTest ($probe.ExitCode -eq 0) "Mutex child probe failed: $($probe.StdErr)"
            $json = ConvertFrom-LastHostJson $probe.StdOut
            Assert-HostTest (-not [bool]$json.Acquired) 'A second process acquired the same global mutex.'
        }
        finally { Exit-SashimiHostMutex $lease }
    }

    Invoke-HostTestCase 'OwnedProcessLedgerIsAdditiveAndStdinTimeoutIsBounded' {
        $ledgerPath = Join-Path $script:temporaryRoot 'owned-process-ledger.json'
        Write-SashimiUtf8File $ledgerPath '{"SchemaVersion":1,"ProcessIds":[],"Processes":[]}'
        $firstStart = '2026-09-05T00:00:00.0000000Z'
        $secondStart = '2026-09-05T00:00:01.0000000Z'
        Update-SashimiOwnedProcessLedger -Path $ledgerPath -Action Add -ProcessId 41001 -StartTimeUtc $firstStart
        Update-SashimiOwnedProcessLedger -Path $ledgerPath -Action Add -ProcessId 41002 -StartTimeUtc $secondStart
        Update-SashimiOwnedProcessLedger -Path $ledgerPath -Action Remove -ProcessId 41001 -StartTimeUtc $firstStart
        $ledger = Read-SashimiJsonFile $ledgerPath
        Assert-HostTest (@($ledger.Processes).Count -eq 1 -and [int]$ledger.Processes[0].Id -eq 41002) 'Removing one owned PID erased or corrupted another invocation record.'

        Write-SashimiUtf8File $ledgerPath '{"SchemaVersion":1,"ProcessIds":[],"Processes":[]}'
        $payload = 'x' * 2097152
        $process = Invoke-SashimiHostProcess `
            -FilePath $PowerShellPath `
            -ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 10') `
            -StandardInput $payload `
            -TimeoutSeconds 1 `
            -OwnedProcessRecordPath $ledgerPath
        $ledger = Read-SashimiJsonFile $ledgerPath
        Assert-HostTest (-not $process.Succeeded -and $process.TimedOut -and $process.TerminationConfirmed -and $process.ExitCode -eq 124) 'A child that did not read stdin escaped the timeout/process-tree contract.'
        Assert-HostTest (@($ledger.Processes).Count -eq 0) 'A confirmed-terminated process remained in the owned PID ledger.'
    }

    Invoke-HostTestCase 'ForkBaseAndUnauthorizedAuthorAreRejected' {
        $sha = 'a' * 40
        $trusted = [pscustomobject]@{
            State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'
            BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'
            HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
            AuthorLogin = 'DongGyunLeeeee'; HeadSha = $sha; HeadRef = 'infra/fixture-5201'
        }
        Assert-HostTest ([bool](Test-SashimiPullRequestTrust $trusted 'DongGyunLeeeee/sashimi-boy-unity' @('DongGyunLeeeee')).Trusted) 'Trusted same-repository PR was rejected.'

        $fork = $trusted.PSObject.Copy(); $fork.HeadRepository = 'attacker/fork'
        $forkResult = Test-SashimiPullRequestTrust $fork 'DongGyunLeeeee/sashimi-boy-unity' @('DongGyunLeeeee')
        Assert-HostTest (-not $forkResult.Trusted -and $forkResult.Reasons -contains 'ForkPullRequestRejected') 'Fork PR was not rejected.'

        $wrongBase = $trusted.PSObject.Copy(); $wrongBase.BaseRefName = 'release'
        Assert-HostTest (-not (Test-SashimiPullRequestTrust $wrongBase 'DongGyunLeeeee/sashimi-boy-unity' @('DongGyunLeeeee')).Trusted) 'Non-main PR base was accepted.'

        $unauthorized = $trusted.PSObject.Copy(); $unauthorized.AuthorLogin = 'untrusted-user'
        $authorResult = Test-SashimiPullRequestTrust $unauthorized 'DongGyunLeeeee/sashimi-boy-unity' @('DongGyunLeeeee')
        Assert-HostTest (-not $authorResult.Trusted -and $authorResult.Reasons -contains 'UnauthorizedPullRequestAuthor') 'Unauthorized PR author was accepted.'
    }

    Invoke-HostTestCase 'StalePullRequestPinFailsClosed' {
        $pinned = [pscustomobject]@{
            Number = 5252; HeadSha = ('a' * 40); HeadRef = 'infra/fixture-5252'
            HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'; BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'
            BaseRefName = 'main'; State = 'OPEN'; IsDraft = $true
            ContentSha256 = Get-SashimiPullRequestContentSha256 -Title 'Pinned title' -Body 'Pinned body'
        }
        $live = $pinned.PSObject.Copy(); $live.HeadSha = 'b' * 40
        $result = Test-SashimiPinnedPullRequest $pinned $live
        Assert-HostTest (-not $result.Current -and [string]$result.ChangedField -ceq 'HeadSha') 'Stale PR head was not detected.'
        $liveContent = $pinned.PSObject.Copy(); $liveContent.ContentSha256 = Get-SashimiPullRequestContentSha256 -Title 'Pinned title' -Body 'Edited body'
        $contentResult = Test-SashimiPinnedPullRequest $pinned $liveContent
        Assert-HostTest (-not $contentResult.Current -and [string]$contentResult.ChangedField -ceq 'ContentSha256') 'Stale PR title/body content was not detected.'
        Assert-HostTest ($script:fixtureInvocations.Count -eq 0 -or @($script:fixtureInvocations | Where-Object Kind -ne 'PowerShellFixture').Count -eq 0) 'Stale-pin test attempted an external mutation.'
    }

    Invoke-HostTestCase 'StaleHeadCausesNoPushStatusOrCommentMutation' {
        $pinnedSha = 'a' * 40
        $fixturePath = Join-Path $script:temporaryRoot 'stale-publish.json'
        $fixture = [ordered]@{
            SchemaVersion = 1
            LivePullRequest = [ordered]@{
                Number = 6252; State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'
                BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                HeadRef = 'infra/fixture-6252'; HeadSha = ('b' * 40)
                AuthorLogin = 'DongGyunLeeeee'; Url = 'https://example.invalid/pull/6252'
            }
            CurrentStatus = 'In Progress'
        }
        Write-HostTestFile $fixturePath (($fixture | ConvertTo-Json -Depth 32) + "`n")
        $publish = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath = $script:configPath
            Action = 'Transition'
            Role = 'Developer'
            IssueNumber = 5252
            ProjectItemId = 'fixture-item-5252'
            PullRequestNumber = 6252
            PinnedHeadSha = $pinnedSha
            PinnedHeadRef = 'infra/fixture-6252'
            FromStatus = 'In Progress'
            ToStatus = 'Review'
            FixturePath = $fixturePath
        }
        Assert-HostTest ($publish.ExitCode -ne 0) 'Stale publish fixture unexpectedly succeeded.'
        $json = ConvertFrom-LastHostJson $publish.StdOut
        Assert-HostTest (-not [bool]$json.PinCurrent) 'Stale head was reported current.'
        Assert-HostTest (-not [bool]$json.MutationAttempted) 'Stale head attempted a status mutation.'
        Assert-HostTest (@($json.Commands | Where-Object Mutation).Count -eq 0) 'Stale head planned a status/comment mutation.'
        $developerSource = [IO.File]::ReadAllText((Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1'))
        $pinIndex = $developerSource.IndexOf('Pre-delivery exact PR pin recheck', [StringComparison]::Ordinal)
        $pushIndex = $developerSource.IndexOf('Normal push exact existing PR branch', [StringComparison]::Ordinal)
        $transitionIndex = $developerSource.IndexOf("'-FromStatus','In Progress','-ToStatus','Review'", [StringComparison]::Ordinal)
        Assert-HostTest ($pinIndex -ge 0 -and $pushIndex -gt $pinIndex -and $transitionIndex -gt $pushIndex) 'Developer resume no longer revalidates the exact pin before push and status transition.'

        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber 5253 -PinnedSha $pinnedSha -DeliverySha ('d' * 40) -StaleSha ('b' * 40)
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $bundle.SelectionPath
            RunPath = $bundle.Run.RunPath
            CodexFixturePath = $bundle.CodexFixture
            UnityFixturePath = $bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GH_SCENARIO = 'developer-stale'
            SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
            SASHIMI_FAKE_GIT_PINNED_SHA = $pinnedSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $bundle.DeliverySha
            SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
            SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
            SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
            SASHIMI_FAKE_GIT_STATUS = ''
        } -TimeoutSeconds 60
        Assert-HostTest ($developer.ExitCode -ne 0) 'End-to-end stale Developer fixture unexpectedly succeeded.'
        $developerJson = ConvertFrom-LastHostJson $developer.StdOut
        Assert-HostTest (-not [bool]$developerJson.Pushed -and -not [bool]$developerJson.TransitionedToReview) 'End-to-end stale Developer reported a push or status transition.'
        $staleCalls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
        Assert-HostTest (@($staleCalls | Where-Object SimulatedMutation).Count -eq 0) 'End-to-end stale Developer reached a fake push, status, comment, or PR mutation boundary.'
        Assert-HostTest (@($staleCalls | Where-Object { $_.Tool -eq 'gh' -and @($_.Arguments) -contains 'view' }).Count -ge 1) 'End-to-end stale Developer never performed the live PR pin query.'
    }

    Invoke-HostTestCase 'PullRequestContentDriftAtSameHeadCausesNoPushOrStatusMutation' {
        $pinnedSha = '5' * 40
        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber 5295 -PinnedSha $pinnedSha -DeliverySha ('6' * 40) -StaleSha ('7' * 40)
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $bundle.SelectionPath
            RunPath = $bundle.Run.RunPath
            CodexFixturePath = $bundle.CodexFixture
            UnityFixturePath = $bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GH_SCENARIO = 'developer-content-stale'
            SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
            SASHIMI_FAKE_GIT_PINNED_SHA = $pinnedSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $bundle.DeliverySha
            SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
            SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
            SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
            SASHIMI_FAKE_GIT_STATUS = ''
        } -TimeoutSeconds 60

        Assert-HostTest ($developer.ExitCode -ne 0) 'Developer accepted edited PR prose at the same head SHA/ref.'
        $developerJson = ConvertFrom-LastHostJson $developer.StdOut
        Assert-HostTest (-not [bool]$developerJson.Pushed -and -not [bool]$developerJson.TransitionedToReview) 'PR-content-stale Developer reported a push or status transition.'
        $calls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
        Assert-HostTest (@($calls | Where-Object SimulatedMutation).Count -eq 0) 'PR-content drift reached a fake push, status, comment, or PR mutation boundary.'
        Assert-HostTest (@($calls | Where-Object { $_.Tool -eq 'gh' -and @($_.Arguments) -contains 'view' }).Count -ge 1) 'PR-content-stale Developer never queried exact live PR content.'
    }

    Invoke-HostTestCase 'LatestMainAdvanceCausesNoDeliveryPushOrStatusMutation' {
        $pinnedSha = '8' * 40
        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber 5298 -PinnedSha $pinnedSha -DeliverySha ('9' * 40) -StaleSha ('a' * 40)
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath=$script:fakeConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            CodexFixturePath=$bundle.CodexFixture; UnityFixturePath=$bundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG=$script:fakeToolLogPath; SASHIMI_FAKE_GH_SCENARIO='developer-current'
            SASHIMI_FAKE_SCENARIO_ROOT=$bundle.ScenarioRoot; SASHIMI_FAKE_GIT_PINNED_SHA=$pinnedSha
            SASHIMI_FAKE_GIT_HEAD_SHA=$bundle.DeliverySha; SASHIMI_FAKE_GIT_BRANCH=$bundle.Branch
            SASHIMI_FAKE_PUSH_STATE=$bundle.PushState; SASHIMI_FAKE_STATUS_STATE=$bundle.StatusState
            SASHIMI_FAKE_GIT_MAIN_SHA=('1' * 40); SASHIMI_FAKE_GIT_MAIN_SHA_AFTER=('2' * 40)
            SASHIMI_FAKE_GIT_STATUS=''
        } -TimeoutSeconds 60
        Assert-HostTest ($developer.ExitCode -ne 0) 'Developer accepted an origin/main advance after validation.'
        $json = ConvertFrom-LastHostJson $developer.StdOut
        Assert-HostTest (-not [bool]$json.Pushed -and -not [bool]$json.TransitionedToReview -and [string]$json.Error -match 'origin/main advanced') `
            'Latest-main drift was not reported as a closed delivery gate.'
        $calls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
        $pushCalls = @($calls | Where-Object { $_.Tool -in @('git','lfs') -and @($_.Arguments) -contains 'push' })
        Assert-HostTest ($pushCalls.Count -eq 0 -and -not (Test-Path -LiteralPath $bundle.PushState) -and
            -not (Test-Path -LiteralPath $bundle.StatusState)) `
            'Latest-main drift reached a Git/LFS push or Project status mutation boundary.'
        Assert-HostTest (@($calls | Where-Object { $_.Tool -eq 'git' -and @($_.Arguments) -contains 'ls-remote' -and @($_.Arguments) -contains 'refs/heads/main' }).Count -ge 1) `
            'Developer did not perform the exact live origin/main recheck.'
    }

    Invoke-HostTestCase 'NewerConversationAtSameHeadRefCausesNoPushOrStatusMutation' {
        $pinnedSha = '6' * 40
        $issue = 5294
        $baseRecords = @(
            [ordered]@{
                Kind='IssueComment'; Url="https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/$issue#issuecomment-8101"
                CreatedAt='2026-09-05T00:00:00Z'; UpdatedAt='2026-09-05T00:00:00Z'; SubmittedAt=''
                AuthorLogin='DongGyunLeeeee'; AuthorAssociation='OWNER'; WasEdited=$false
                Body="기존 인계 근거`n둘째 줄"; ReviewState=''; CommitOid=''
            },
            [ordered]@{
                Kind='PullRequestReview'; Url="https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$($issue + 1000)#pullrequestreview-8102"
                CreatedAt='2026-09-05T00:01:00Z'; UpdatedAt='2026-09-05T00:01:00Z'; SubmittedAt='2026-09-05T00:01:00Z'
                AuthorLogin='DongGyunLeeeee'; AuthorAssociation='OWNER'; WasEdited=$false
                Body='Reviewed pinned state.'; ReviewState='CHANGES_REQUESTED'; CommitOid=$pinnedSha
            }
        )
        $pinnedConversation = Get-SashimiConversationSha256 -Records $baseRecords
        $reorderedConversation = Get-SashimiConversationSha256 -Records @($baseRecords[1],$baseRecords[0])
        Assert-HostTest ($pinnedConversation -ceq $reorderedConversation) 'Canonical conversation digest depends on pagination or input ordering.'
        $newerRecord = [ordered]@{
            Kind='PullRequestComment'; Url="https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$($issue + 1000)#issuecomment-8103"
            CreatedAt='2026-09-05T00:02:00Z'; UpdatedAt='2026-09-05T00:02:00Z'; SubmittedAt=''
            AuthorLogin='DongGyunLeeeee'; AuthorAssociation='OWNER'; WasEdited=$false
            Body='A newer current-handoff-affecting comment.'; ReviewState=''; CommitOid=''
        }
        $liveRecords = @($baseRecords) + @($newerRecord)
        Assert-HostTest ((Get-SashimiConversationSha256 -Records $liveRecords) -cne $pinnedConversation) 'A newer comment did not change the canonical conversation digest.'

        $issueOnlyPinned = Get-SashimiConversationSha256 -Records @($baseRecords[0])
        $issueOnlyLive = @($baseRecords[0]) + @([ordered]@{
            Kind='IssueComment'; Url="https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/$issue#issuecomment-8104"
            CreatedAt='2026-09-05T00:03:00Z'; UpdatedAt='2026-09-05T00:03:00Z'; SubmittedAt=''
            AuthorLogin='DongGyunLeeeee'; AuthorAssociation='OWNER'; WasEdited=$false
            Body='New Owner conversation after Ready selection.'; ReviewState=''; CommitOid=''
        })
        $newWorkPublishFixturePath = Join-Path $script:temporaryRoot 'newwork-conversation-stale.publish.json'
        Write-HostTestFile $newWorkPublishFixturePath (([ordered]@{
            SchemaVersion=1; CurrentStatus='Ready'; OpenPullRequestCount=0
            IssueUpdatedAt='2026-01-01T00:00:00Z'; IssueBodySha256=(Get-SashimiTextSha256 -Text 'Fixture acceptance criteria.')
            LiveConversationRecords=$issueOnlyLive
        } | ConvertTo-Json -Depth 64) + "`n")
        $newWorkTransition = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$script:configPath; Action='Transition'; Role='Developer'; IssueNumber=$issue
            ProjectItemId="fixture-item-$issue"; FromStatus='Ready'; ToStatus='In Progress'
            PinnedIssueUpdatedAt='2026-01-01T00:00:00Z'; PinnedIssueBodySha256=(Get-SashimiTextSha256 -Text 'Fixture acceptance criteria.')
            PinnedConversationSha256=$issueOnlyPinned; FixturePath=$newWorkPublishFixturePath
        }
        Assert-HostTest ($newWorkTransition.ExitCode -ne 0) 'NewWork transition accepted a newer Issue comment with unchanged Issue body/updatedAt.'
        $newWorkJson = ConvertFrom-LastHostJson $newWorkTransition.StdOut
        Assert-HostTest (-not [bool]$newWorkJson.PinCurrent -and -not [bool]$newWorkJson.MutationAttempted -and @($newWorkJson.Commands | Where-Object Mutation).Count -eq 0) 'NewWork conversation drift reached a Project mutation boundary.'

        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $issue -PinnedSha $pinnedSha -DeliverySha ('7' * 40) -StaleSha ('8' * 40)
        $bundle.Selection | Add-Member -NotePropertyName Conversation -NotePropertyValue $baseRecords -Force
        $bundle.Selection | Add-Member -NotePropertyName ConversationSha256 -NotePropertyValue $pinnedConversation -Force
        Write-HostTestFile $bundle.SelectionPath (($bundle.Selection | ConvertTo-Json -Depth 64) + "`n")

        $publishFixture = [ordered]@{
            SchemaVersion=1; CurrentStatus='In Progress'; OpenPullRequestCount=1
            OpenPullRequestNumbers=@([int]$bundle.Selection.PullRequestNumber)
            IssueUpdatedAt=[string]$bundle.Selection.IssueUpdatedAt; IssueBodySha256=[string]$bundle.Selection.IssueBodySha256
            LivePullRequest=[ordered]@{
                Number=[int]$bundle.Selection.PullRequestNumber; State='OPEN'; IsDraft=$true; BaseRefName='main'
                BaseRepository='DongGyunLeeeee/sashimi-boy-unity'; HeadRepository='DongGyunLeeeee/sashimi-boy-unity'
                HeadRef=[string]$bundle.Selection.PullRequestHeadRef; HeadSha=$pinnedSha
                AuthorLogin='DongGyunLeeeee'; Url=[string]$bundle.Selection.PullRequestUrl
            }
            LiveConversationRecords=$liveRecords
        }
        $publishFixturePath = Join-Path $script:temporaryRoot 'conversation-stale.publish.json'
        Write-HostTestFile $publishFixturePath (($publishFixture | ConvertTo-Json -Depth 64) + "`n")

        $direct = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$script:configPath; Action='RevalidatePin'; Role='Developer'; IssueNumber=$issue
            PullRequestNumber=[int]$bundle.Selection.PullRequestNumber; PinnedHeadSha=$pinnedSha
            PinnedHeadRef=[string]$bundle.Selection.PullRequestHeadRef; PinnedConversationSha256=$pinnedConversation
            FixturePath=$publishFixturePath
        }
        Assert-HostTest ($direct.ExitCode -eq 0) "Conversation revalidation fixture failed structurally: $($direct.StdErr) $($direct.StdOut)"
        $directJson = ConvertFrom-LastHostJson $direct.StdOut
        Assert-HostTest (-not [bool]$directJson.Result.Current -and [string]$directJson.Result.ChangedField -ceq 'ConversationSha256') 'Same-head/ref conversation drift was not identified as stale.'
        Assert-HostTest (-not [bool]$directJson.MutationAttempted -and @($directJson.Commands | Where-Object Mutation).Count -eq 0) 'Conversation-only revalidation reached a mutation boundary.'

        $executionFixture = [ordered]@{
            SchemaVersion=1; StatusLines=@(); StagedPaths=@(); UnstagedPaths=@(); UntrackedPaths=@()
            FetchedHead=$pinnedSha; LocalHeads=@(('7' * 40),('7' * 40),('7' * 40),('7' * 40),('7' * 40),('7' * 40))
        }
        $executionFixturePath = Join-Path $script:temporaryRoot 'conversation-stale.developer-execution.json'
        Write-HostTestFile $executionFixturePath (($executionFixture | ConvertTo-Json -Depth 64) + "`n")
        $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath=$script:fakeConfigPath; SelectionPath=$bundle.SelectionPath; RunPath=$bundle.Run.RunPath
            CodexFixturePath=$bundle.CodexFixture; UnityFixturePath=$bundle.UnityFixture
            PublishFixturePath=$publishFixturePath; ExecutionFixturePath=$executionFixturePath
        } -TimeoutSeconds 60
        Assert-HostTest ($developer.ExitCode -ne 0) 'Developer accepted a newer conversation at the same PR SHA/ref.'
        $developerJson = ConvertFrom-LastHostJson $developer.StdOut
        Assert-HostTest ([string]$developerJson.Error -match 'stale' -and -not [bool]$developerJson.Pushed -and -not [bool]$developerJson.TransitionedToReview) 'Conversation-stale Developer did not fail closed before push/status mutation.'
        Assert-HostTest (@($developerJson.Commands | Where-Object { $_.Stage -in @('Push required Git LFS objects for exact delivery commit','Normal push exact existing PR branch','In Progress to Review') }).Count -eq 0) 'Conversation-stale Developer planned a push or status transition.'
    }

    Invoke-HostTestCase 'DraftPrRequiresExactRemoteBranchShaBeforeMutation' {
        $issue = 5291
        $pinnedSha = 'a' * 40
        $branch = "issue/$issue-host-$('b' * 32)"
        $config = Import-SashimiHostConfig $script:configPath
        $run = New-SashimiRunWorkspace -RunRoot ([string]$config.RunRoot)
        $bodyPath = Join-Path $run.ArtifactsPath 'DraftPullRequest.md'
        Write-HostTestFile $bodyPath "Closes #$issue`n"
        $updatedAt = '2026-09-05T00:00:00Z'
        $bodySha = Get-SashimiTextSha256 -Text 'Pinned fixture body.'
        $fixturePath = Join-Path $script:temporaryRoot 'stale-newwork-remote-branch.publish.json'
        $fixture = [ordered]@{
            SchemaVersion = 1
            CurrentStatus = 'In Progress'
            OpenPullRequestCount = 0
            IssueUpdatedAt = $updatedAt
            IssueBodySha256 = $bodySha
            LiveRemoteBranch = [ordered]@{
                Ref = "refs/heads/$branch"
                ObjectType = 'commit'
                Sha = ('c' * 40)
            }
        }
        Write-HostTestFile $fixturePath (($fixture | ConvertTo-Json -Depth 32) + "`n")
        $publish = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath = $script:configPath
            Action = 'CreateDraftPullRequest'
            Role = 'Developer'
            IssueNumber = $issue
            ProjectItemId = "fixture-item-$issue"
            PinnedHeadSha = $pinnedSha
            PinnedIssueUpdatedAt = $updatedAt
            PinnedIssueBodySha256 = $bodySha
            Branch = $branch
            Title = 'Fixture Draft PR'
            BodyPath = $bodyPath
            FixturePath = $fixturePath
        }
        Assert-HostTest ($publish.ExitCode -ne 0) 'Draft PR creation accepted a stale canonical remote branch SHA.'
        $json = ConvertFrom-LastHostJson $publish.StdOut
        Assert-HostTest (-not [bool]$json.PinCurrent -and -not [bool]$json.MutationAttempted) 'Stale remote branch did not fail as a pre-mutation pin check.'
        Assert-HostTest (@($json.Commands | Where-Object Mutation).Count -eq 0) 'Stale remote branch reached the Draft PR mutation boundary.'

        $lateFixturePath = Join-Path $script:temporaryRoot 'late-stale-newwork-remote-branch.publish.json'
        $lateFixture = [ordered]@{
            SchemaVersion = 1
            CurrentStatus = 'In Progress'
            OpenPullRequestCount = 0
            IssueUpdatedAt = $updatedAt
            IssueBodySha256 = $bodySha
            LiveRemoteBranch = [ordered]@{ Ref="refs/heads/$branch"; ObjectType='commit'; Sha=$pinnedSha }
            LiveRemoteBranchImmediatelyBeforeMutation = [ordered]@{ Ref="refs/heads/$branch"; ObjectType='commit'; Sha=('d' * 40) }
        }
        Write-HostTestFile $lateFixturePath (($lateFixture | ConvertTo-Json -Depth 32) + "`n")
        $latePublish = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$script:configPath; Action='CreateDraftPullRequest'; Role='Developer'; IssueNumber=$issue
            ProjectItemId="fixture-item-$issue"; PinnedHeadSha=$pinnedSha; PinnedIssueUpdatedAt=$updatedAt
            PinnedIssueBodySha256=$bodySha; Branch=$branch; Title='Fixture Draft PR'; BodyPath=$bodyPath; FixturePath=$lateFixturePath
        }
        Assert-HostTest ($latePublish.ExitCode -ne 0) 'Draft PR creation accepted a branch move after its first preflight.'
        $lateJson = ConvertFrom-LastHostJson $latePublish.StdOut
        Assert-HostTest (-not [bool]$lateJson.PinCurrent -and -not [bool]$lateJson.MutationAttempted -and
            @($lateJson.Commands | Where-Object Mutation).Count -eq 0) `
            'Late remote-branch drift reached the Draft PR mutation boundary.'
    }

    Invoke-HostTestCase 'UnauthorizedAuthenticatedActorCausesNoGitHubMutation' {
        $fixturePath = Join-Path $script:temporaryRoot 'unauthorized-publisher-actor.json'
        $fixture = [ordered]@{
            SchemaVersion=1
            AuthenticatedLogin='untrusted-fixture-actor'
            CurrentStatus='In Progress'
            OpenPullRequestCount=0
            IssueUpdatedAt='2026-09-05T00:00:00Z'
            IssueBodySha256=('1' * 64)
        }
        Write-HostTestFile $fixturePath (($fixture | ConvertTo-Json -Depth 16) + "`n")
        $publish = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Publish-SashimiRunResult.ps1') -Parameters @{
            ConfigPath=$script:configPath; Action='Transition'; Role='Developer'; IssueNumber=5297
            ProjectItemId='fixture-item-5297'; PinnedIssueUpdatedAt='2026-09-05T00:00:00Z'
            PinnedIssueBodySha256=('1' * 64); PinnedConversationSha256=(Get-SashimiConversationSha256 -Records @())
            FromStatus='In Progress'; ToStatus='Review'; FixturePath=$fixturePath
        }
        Assert-HostTest ($publish.ExitCode -ne 0) 'An unauthorized authenticated GitHub actor was accepted.'
        $json = ConvertFrom-LastHostJson $publish.StdOut
        Assert-HostTest (-not [bool]$json.MutationAttempted -and @($json.Commands | Where-Object Mutation).Count -eq 0 -and
            [string]$json.Error -match 'not authorized') 'Unauthorized GitHub actor did not fail before all mutation boundaries.'
    }

    Invoke-HostTestCase 'CodexExitZeroWithTurnFailedIsFailure' {
        $runId = '20260905T000000Z-' + ('1' * 32)
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5260
        $fixture = New-HostCodexFixtureFile -Name 'turn-failed' -Result $resultObject -Events @(
            [ordered]@{ type = 'turn.started' },
            [ordered]@{ type = 'turn.failed'; error = [ordered]@{ message = 'synthetic failure' } },
            [ordered]@{ type = 'turn.completed' }
        )
        $process = Invoke-HostCodexFixture -FixturePath $fixture -RunId $runId -IssueNumber 5260
        Assert-HostTest ($process.ExitCode -ne 0) 'Codex exit-zero turn.failed fixture was accepted.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (-not [bool]$json.Success -and [string]$json.Error -match 'fatal|turn.failed') 'Codex failure did not identify the fatal JSONL event.'
    }

    Invoke-HostTestCase 'CodexRejectsUnfinishedMissingResultAndCapabilities' {
        $runId = '20260905T000001Z-' + ('2' * 32)
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5261
        $unfinished = New-HostCodexFixtureFile -Name 'unfinished-command' -Result $resultObject -Events @(
            [ordered]@{ type = 'item.started'; item = [ordered]@{ id = 'cmd-fixture'; type = 'command_execution' } },
            [ordered]@{ type = 'turn.completed' }
        )
        $process = Invoke-HostCodexFixture -FixturePath $unfinished -RunId $runId -IssueNumber 5261
        Assert-HostTest ($process.ExitCode -ne 0 -and (ConvertFrom-LastHostJson $process.StdOut).Error -match 'unfinished') 'Unfinished Codex command was accepted.'

        $missingResult = New-HostCodexFixtureFile -Name 'missing-result' -Result $null -Events @(
            [ordered]@{ type = 'turn.completed' }
        )
        $process = Invoke-HostCodexFixture -FixturePath $missingResult -RunId $runId -IssueNumber 5261
        Assert-HostTest ($process.ExitCode -ne 0 -and (ConvertFrom-LastHostJson $process.StdOut).Error -match 'Result|result') 'Missing explicit Codex result was accepted.'

        $badCapabilities = New-HostCodexFixtureFile -Name 'missing-capability' -Result $resultObject -Events @([ordered]@{ type = 'turn.completed' }) -ExecHelp '--json --color'
        $process = Invoke-HostCodexFixture -FixturePath $badCapabilities -RunId $runId -IssueNumber 5261
        Assert-HostTest ($process.ExitCode -ne 0 -and (ConvertFrom-LastHostJson $process.StdOut).Error -match 'capability') 'Incomplete Codex CLI capability probe was accepted.'
    }

    Invoke-HostTestCase 'CodexFailureDiagnosticsAreContentFree' {
        $runId = '20260905T000009Z-' + ('9' * 32)
        $issue = 5299
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber $issue
        $markers = [ordered]@{
            probe = 'opaqueProbe' + [Guid]::NewGuid().ToString('N')
            event = 'opaqueEvent' + [Guid]::NewGuid().ToString('N')
            schema = 'opaqueSchema' + [Guid]::NewGuid().ToString('N')
            fallthrough = 'opaqueFallthrough' + [Guid]::NewGuid().ToString('N')
        }
        foreach ($marker in $markers.Values) {
            Assert-HostTest ([string]::Equals([string]$marker, (Protect-SashimiText ([string]$marker)), [StringComparison]::Ordinal)) `
                'Codex diagnostic marker is recognizable to ordinary redaction and cannot prove content-free handling.'
        }

        $probeFixture = New-HostCodexFixtureFile -Name 'codex-content-free-probe' -Result $resultObject `
            -Events @([ordered]@{ type = 'turn.completed' }) `
            -CapabilityProbe ([ordered]@{ ExitCode = 73; StdOut = ''; StdErr = $markers.probe; TimedOut = $false })
        $probeProcess = Invoke-HostCodexFixture -FixturePath $probeFixture -RunId $runId -IssueNumber $issue
        $probeJson = ConvertFrom-LastHostJson $probeProcess.StdOut
        Assert-HostTest ($probeProcess.ExitCode -ne 0 -and [string]$probeJson.Error -match 'CODEX_CAPABILITY_PROBE_VERSION_FAILED') 'Opaque capability-probe failure was not rejected with a stable code.'
        Assert-HostTest ([string]$probeJson.Error -notmatch [regex]::Escape($markers.probe) -and
            [string]$probeJson.Error -match (Get-SashimiTextSha256 -Text $markers.probe)) 'Capability-probe Error retained opaque stderr or omitted its hash.'

        $eventFixture = New-HostCodexFixtureFile -Name 'codex-content-free-event' -Result $resultObject -Events @(
            [ordered]@{ type = 'item.started'; item = [ordered]@{ id = $markers.event; type = 'command_execution'; command = 'Get-Content README.md' } },
            [ordered]@{ type = 'turn.completed' }
        )
        $eventProcess = Invoke-HostCodexFixture -FixturePath $eventFixture -RunId $runId -IssueNumber $issue
        $eventJson = ConvertFrom-LastHostJson $eventProcess.StdOut
        Assert-HostTest ($eventProcess.ExitCode -ne 0 -and [string]$eventJson.Error -match 'CODEX_JSONL_UNFINISHED_COMMANDS') 'Opaque unfinished-event failure was not rejected with a stable code.'
        Assert-HostTest ([string]$eventJson.Error -notmatch [regex]::Escape($markers.event) -and
            [string]$eventJson.Error -match (Get-SashimiTextSha256 -Text $markers.event)) 'JSONL Error retained an opaque item ID or omitted its hash.'

        $schemaResult = New-HostCodexResult -RunId $runId -IssueNumber $issue -IssueValidationId $markers.schema
        $schemaFixture = New-HostCodexFixtureFile -Name 'codex-content-free-schema' -Result $schemaResult -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'schema-message'; type = 'agent_message'; text = ($schemaResult | ConvertTo-Json -Depth 32 -Compress) } },
            [ordered]@{ type = 'turn.completed' }
        )
        $schemaProcess = Invoke-HostCodexFixture -FixturePath $schemaFixture -RunId $runId -IssueNumber $issue
        $schemaJson = ConvertFrom-LastHostJson $schemaProcess.StdOut
        Assert-HostTest ($schemaProcess.ExitCode -ne 0 -and [string]$schemaJson.Error -match 'CODEX_RESULT_VALIDATION_ID_NOT_ALLOWLISTED') 'Opaque schema-field failure was not rejected with a stable code.'
        Assert-HostTest ([string]$schemaJson.Error -notmatch [regex]::Escape($markers.schema) -and
            [string]$schemaJson.Error -match (Get-SashimiTextSha256 -Text $markers.schema)) 'Schema Error retained an opaque result value or omitted its hash.'

        $fallthroughFixture = New-HostCodexFixtureFile -Name 'codex-content-free-fallthrough' -Result $resultObject -Events @(
            [ordered]@{ type = $markers.fallthrough }
        )
        $fallthroughProcess = Invoke-HostCodexFixture -FixturePath $fallthroughFixture -RunId $runId -IssueNumber $issue
        $fallthroughJson = ConvertFrom-LastHostJson $fallthroughProcess.StdOut
        Assert-HostTest ($fallthroughProcess.ExitCode -ne 0 -and [string]$fallthroughJson.Error -ceq 'Codex adapter failure; code=CODEX_ADAPTER_INTERNAL_FAILURE.') `
            'A nonconforming internal diagnostic escaped instead of collapsing to the fixed content-free code.'

        foreach ($process in @($probeProcess, $eventProcess, $schemaProcess, $fallthroughProcess)) {
            $combined = [string]$process.StdOut + "`n" + [string]$process.StdErr
            foreach ($marker in $markers.Values) {
                Assert-HostTest ($combined -notmatch [regex]::Escape([string]$marker)) 'A Codex failure process result retained opaque child-controlled content.'
            }
        }
    }

    Invoke-HostTestCase 'CodexCleanJsonlAndExplicitResultSucceed' {
        $runId = '20260905T000002Z-' + ('3' * 32)
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5262
        $resultText = $resultObject | ConvertTo-Json -Depth 32 -Compress
        $fixture = New-HostCodexFixtureFile -Name 'codex-success' -Result $resultObject -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'message-fixture'; type = 'agent_message'; text = $resultText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $process = Invoke-HostCodexFixture -FixturePath $fixture -RunId $runId -IssueNumber 5262
        Assert-HostTest ($process.ExitCode -eq 0) "Valid Codex fixture failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.Success -and [int]$json.EventCount -eq 2) 'Valid Codex JSONL/result did not succeed.'
        Assert-HostTest ([string]$json.ApprovalPolicy -ceq 'never' -and [string]$json.Sandbox -ceq 'workspace-write') 'Codex Developer sandbox/approval contract changed.'
        $arguments = @($json.PlannedArguments)
        foreach ($required in @('--ephemeral', '--json', '--color', '--ignore-user-config', '--strict-config', '--output-schema')) {
            Assert-HostTest ($arguments -ccontains $required) "Codex plan is missing $required."
        }
        Assert-HostTest ($arguments -cnotcontains 'danger-full-access' -and $arguments -cnotcontains '--dangerously-bypass-approvals-and-sandbox') 'Codex plan contains a dangerous bypass.'
    }

    Invoke-HostTestCase 'CodexIssueGeneratorSelectionIsExplicitAndAllowlisted' {
        $issue = 5290
        $runId = '20260905T000005Z-' + ('6' * 32)
        $validationId = 'fixture-generator-5290'
        $config = Read-SashimiJsonFile $script:configPath
        $definition = [pscustomobject][ordered]@{
            IssueNumber = $issue
            UnityExecuteMethod = 'Fixture.Generator.Run'
            Arguments = @()
            DeterminismPaths = @('Assets/Generated')
            ScreenshotPaths = @()
            PreviewPaths = @()
            AllowedProtectedPathPatterns = @('Assets/Generated/**')
        }
        $config.IssueValidations | Add-Member -NotePropertyName $validationId -NotePropertyValue $definition
        $configPath = Join-Path $script:temporaryRoot 'codex-generator-selection.config.json'
        Write-HostTestFile $configPath (($config | ConvertTo-Json -Depth 64) + "`n")

        $allowedResult = New-HostCodexResult -RunId $runId -IssueNumber $issue -IssueValidationId $validationId
        $allowedText = $allowedResult | ConvertTo-Json -Depth 32 -Compress
        $allowedFixture = New-HostCodexFixtureFile -Name 'codex-generator-allowed' -Result $allowedResult -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'allowed-generator'; type = 'agent_message'; text = $allowedText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $allowedProcess = Invoke-HostCodexFixture -FixturePath $allowedFixture -RunId $runId -IssueNumber $issue -ConfigPath $configPath
        Assert-HostTest ($allowedProcess.ExitCode -eq 0) "Allowlisted issueValidationId failed: $($allowedProcess.StdOut)"
        $allowedJson = ConvertFrom-LastHostJson $allowedProcess.StdOut
        Assert-HostTest ([string]$allowedJson.IssueValidationId -ceq $validationId -and [string]$allowedJson.Result.issueValidationId -ceq $validationId) 'Validated issueValidationId was not exposed to the Developer runner.'

        $blockedResult = New-HostCodexResult -RunId $runId -IssueNumber $issue -IssueValidationId 'not-allowlisted'
        $blockedText = $blockedResult | ConvertTo-Json -Depth 32 -Compress
        $blockedFixture = New-HostCodexFixtureFile -Name 'codex-generator-blocked' -Result $blockedResult -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'blocked-generator'; type = 'agent_message'; text = $blockedText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $blockedProcess = Invoke-HostCodexFixture -FixturePath $blockedFixture -RunId $runId -IssueNumber $issue -ConfigPath $configPath
        Assert-HostTest ($blockedProcess.ExitCode -ne 0 -and (ConvertFrom-LastHostJson $blockedProcess.StdOut).Error -match 'NOT_ALLOWLISTED') 'A non-allowlisted issueValidationId was accepted.'

        $reviewRunId = '20260905T000006Z-' + ('7' * 32)
        $reviewResult = New-HostCodexResult -RunId $reviewRunId -IssueNumber $issue -Role Reviewer -Mode Review -PullRequestNumber 6290 -IssueValidationId $validationId
        $reviewText = $reviewResult | ConvertTo-Json -Depth 32 -Compress
        $reviewFixture = New-HostCodexFixtureFile -Name 'codex-reviewer-generator-blocked' -Result $reviewResult -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'review-generator'; type = 'agent_message'; text = $reviewText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $reviewProcess = Invoke-HostCodexFixture -FixturePath $reviewFixture -RunId $reviewRunId -IssueNumber $issue -Role Reviewer -Mode Review -PullRequestNumber 6290 -ConfigPath $configPath
        Assert-HostTest ($reviewProcess.ExitCode -ne 0 -and (ConvertFrom-LastHostJson $reviewProcess.StdOut).Error -match 'NOT_ALLOWLISTED') 'Reviewer was allowed to select a generator instead of leaving selection to the Host.'

        $developerSource = [IO.File]::ReadAllText((Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1'))
        Assert-HostTest ($developerSource.Contains("Get-SashimiPropertyValue `$codexResult 'IssueValidationId'")) 'Developer no longer consumes the validated adapter issueValidationId.'
    }

    Invoke-HostTestCase 'CodexCommandAuditRejectsWrappersAndRetainsMetadataOnly' {
        $runId = '20260905T000004Z-' + ('5' * 32)
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5264
        $resultText = $resultObject | ConvertTo-Json -Depth 32 -Compress
        $badCommands = @(
            'pwsh -NoProfile -File .\unsafe.ps1',
            'python -c "print(1)"',
            'pwsh -Command "Get-Content ..\profile-secret.txt"',
            'pwsh -Command "Get-Content README.md; Start-Process gh -ArgumentList issue,close,52"',
            'Get-ChildItem Env:',
            'Get-Content ~\.codex\auth.json',
            'Get-Content C:Windows\win.ini',
            'Get-Content /etc/passwd',
            'Get-Content FileSystem::C:\Windows\win.ini',
            'Get-Content .git/config',
            'git --ext-diff diff',
            'git --textconv show HEAD:README.md',
            'git --filters cat-file blob HEAD:README.md',
            'git --config-env=core.pager=SASHIMI_PAGER log',
            'git -c core.pager=calc log',
            'git --git-dir=.git log',
            'git --paginate log',
            'git grep --open-files-in-pager=calc needle',
            'rg --pre=calc needle .'
        )
        $case = 0
        foreach ($badCommand in $badCommands) {
            $case++
            $fixture = New-HostCodexFixtureFile -Name "codex-forbidden-command-$case" -Result $resultObject -Events @(
                [ordered]@{ type = 'item.started'; item = [ordered]@{ id = "bad-$case"; type = 'command_execution'; command = $badCommand } },
                [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = "bad-$case"; type = 'command_execution'; command = $badCommand } },
                [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'message'; type = 'agent_message'; text = $resultText } },
                [ordered]@{ type = 'turn.completed' }
            )
            $process = Invoke-HostCodexFixture -FixturePath $fixture -RunId $runId -IssueNumber 5264
            Assert-HostTest ($process.ExitCode -ne 0) "Codex command audit accepted forbidden wrapper command: $badCommand"
        }

        $secretMarker = 'super-sensitive-fixture-value-5264'
        $safeCommand = 'pwsh -NoProfile -Command "Get-Content README.md | Select-Object -First 2"'
        $unknownEventType = 'ghp_fixtureeventcredential5264'
        $unknownItemType = 'ghp_fixtureitemcredential5264'
        $fixture = New-HostCodexFixtureFile -Name 'codex-metadata-only' -Result $resultObject -StdErr ('password=' + $secretMarker) -Events @(
            [ordered]@{ type = 'thread.started'; debug = ('"access_token":"' + $secretMarker + '"') },
            [ordered]@{ type = $unknownEventType },
            [ordered]@{ type = 'item.updated'; item = [ordered]@{ id = 'unknown-item'; type = $unknownItemType } },
            [ordered]@{ type = 'item.started'; item = [ordered]@{ id = 'safe-command'; type = 'command_execution'; command = $safeCommand } },
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'safe-command'; type = 'command_execution'; command = $safeCommand } },
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'message'; type = 'agent_message'; text = $resultText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $artifactPath = Join-Path $script:temporaryRoot 'codex-metadata-artifacts'
        $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1') -Parameters @{
            ConfigPath = $script:configPath
            RepositoryPath = $script:fakeRepository
            ArtifactsPath = $artifactPath
            Role = 'Developer'
            Mode = 'NewWork'
            IssueNumber = 5264
            PinnedHeadSha = ('e' * 40)
            RunId = $runId
            Prompt = 'Synthetic metadata-retention fixture.'
            FixturePath = $fixture
        }
        Assert-HostTest ($process.ExitCode -eq 0) "Metadata-only Codex fixture failed: $($process.StdOut)"
        $eventsText = [IO.File]::ReadAllText((Join-Path $artifactPath 'CodexEvents.jsonl'))
        $summaryText = [IO.File]::ReadAllText((Join-Path $artifactPath 'CodexProcessSummary.json'))
        Assert-HostTest ($eventsText -notmatch [regex]::Escape($safeCommand) -and $eventsText -notmatch [regex]::Escape($secretMarker) -and
            $eventsText -notmatch [regex]::Escape($unknownEventType) -and $eventsText -notmatch [regex]::Escape($unknownItemType) -and
            $eventsText -notmatch 'Synthetic fixture result') 'Codex metadata JSONL retained raw command, event, or result content.'
        Assert-HostTest ($eventsText -match (Get-SashimiTextSha256 -Text $unknownEventType) -and
            $eventsText -match (Get-SashimiTextSha256 -Text $unknownItemType)) 'Unknown Codex event/item types were not reduced to one-way hashes.'
        Assert-HostTest ($eventsText -match 'commandSha256' -and $summaryText -match 'StdOutSha256' -and $summaryText -match 'StdErrSha256') 'Codex content-free audit hashes were not retained.'
        Assert-HostTest ($summaryText -notmatch [regex]::Escape($secretMarker)) 'Codex process summary retained raw stderr.'
        Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $artifactPath 'Codex.stderr.log'))) 'Legacy raw Codex stderr artifact was created.'
    }

    Invoke-HostTestCase 'CodexResultRejectsRecognizableAndInheritedSensitiveContent' {
        $runId = '20260905T000010Z-' + ('a' * 32)
        $issue = 5300
        $uriCredential = 'https://alice:s3cr3t@example.invalid/private'
        $inheritedCredential = 'opaqueInherited' + [Guid]::NewGuid().ToString('N')
        $cases = @(
            [pscustomobject]@{ Name = 'uri'; Value = $uriCredential; Environment = @{} },
            [pscustomobject]@{ Name = 'inherited'; Value = $inheritedCredential; Environment = @{ SASHIMI_FIXTURE_PASSWORD = $inheritedCredential } }
        )

        foreach ($case in $cases) {
            $resultObject = New-HostCodexResult -RunId $runId -IssueNumber $issue
            $resultObject.summary = 'Synthetic result containing ' + [string]$case.Value
            $resultText = $resultObject | ConvertTo-Json -Depth 32 -Compress
            $fixture = New-HostCodexFixtureFile -Name ('codex-sensitive-result-' + $case.Name) -Result $resultObject -Events @(
                [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'sensitive-result'; type = 'agent_message'; text = $resultText } },
                [ordered]@{ type = 'turn.completed' }
            )
            $artifactPath = Join-Path $script:temporaryRoot ('codex-sensitive-result-artifacts-' + $case.Name)
            $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1') -Parameters @{
                ConfigPath = $script:configPath
                RepositoryPath = $script:fakeRepository
                ArtifactsPath = $artifactPath
                Role = 'Developer'
                Mode = 'NewWork'
                IssueNumber = $issue
                PinnedHeadSha = ('e' * 40)
                RunId = $runId
                Prompt = 'Synthetic sensitive-result fixture.'
                FixturePath = $fixture
            } -Environment $case.Environment

            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest ($process.ExitCode -ne 0 -and [string]$json.Error -match 'CODEX_RESULT_SENSITIVE_CONTENT') `
                "Codex accepted $($case.Name) sensitive content in its explicit result."
            Assert-HostTest (([string]$process.StdOut + [string]$process.StdErr) -notmatch [regex]::Escape([string]$case.Value)) `
                "Codex emitted $($case.Name) sensitive content in process output."
            Assert-HostTest (-not (Test-Path -LiteralPath (Join-Path $artifactPath 'CodexResult.json'))) `
                "Codex persisted $($case.Name) sensitive content in CodexResult.json."
        }
    }

    Invoke-HostTestCase 'CodexFailureDiagnosticsDoNotReachDeveloperOrReviewerArtifacts' {
        $developerMarker = 'opaqueDeveloper' + [Guid]::NewGuid().ToString('N')
        $reviewerMarker = 'opaqueReviewer' + [Guid]::NewGuid().ToString('N')
        foreach ($marker in @($developerMarker, $reviewerMarker)) {
            Assert-HostTest ([string]::Equals($marker, (Protect-SashimiText $marker), [StringComparison]::Ordinal)) `
                'Runner diagnostic marker is recognizable to ordinary redaction and cannot prove content-free handling.'
        }

        $developerIssue = 5297
        $developerSha = 'c' * 40
        $developerBundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $developerIssue `
            -PinnedSha $developerSha -DeliverySha $developerSha -StaleSha ('b' * 40)
        $developerResult = New-HostCodexResult -RunId $developerBundle.Run.RunId -IssueNumber $developerIssue `
            -HeadSha $developerSha -Role Developer -Mode ReviewFix -PullRequestNumber ($developerIssue + 1000)
        $developerCodexFixture = New-HostCodexFixtureFile -Name 'developer-content-free-failure' -Result $developerResult -Events @(
            [ordered]@{ type = 'item.started'; item = [ordered]@{ id = $developerMarker; type = 'command_execution'; command = 'Get-Content README.md' } },
            [ordered]@{ type = 'turn.completed' }
        )
        $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $developerBundle.SelectionPath
            RunPath = $developerBundle.Run.RunPath
            CodexFixturePath = $developerCodexFixture
            UnityFixturePath = $developerBundle.UnityFixture
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GH_SCENARIO = 'developer-current'
            SASHIMI_FAKE_SCENARIO_ROOT = $developerBundle.ScenarioRoot
            SASHIMI_FAKE_GIT_PINNED_SHA = $developerSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $developerSha
            SASHIMI_FAKE_GIT_BRANCH = $developerBundle.Branch
            SASHIMI_FAKE_PUSH_STATE = $developerBundle.PushState
            SASHIMI_FAKE_STATUS_STATE = $developerBundle.StatusState
            SASHIMI_FAKE_GIT_STATUS = ''
        } -TimeoutSeconds 60
        Assert-HostTest ($developer.ExitCode -ne 0) 'Content-free Developer Codex fixture did not fail.'
        Assert-HostTest (([string]$developer.StdOut + [string]$developer.StdErr) -notmatch [regex]::Escape($developerMarker)) 'Developer runner output retained the opaque Codex item ID.'
        $developerFailure = Join-Path $developerBundle.Run.ArtifactsPath 'Failure.md'
        Assert-HostTest (Test-Path -LiteralPath $developerFailure -PathType Leaf) 'Developer did not retain Failure.md for the Codex failure.'
        foreach ($artifact in @(Get-ChildItem -LiteralPath $developerBundle.Run.ArtifactsPath -File -Recurse -ErrorAction Stop)) {
            Assert-HostTest ([IO.File]::ReadAllText($artifact.FullName).IndexOf($developerMarker, [StringComparison]::Ordinal) -lt 0) `
                "Developer artifact retained an opaque Codex diagnostic value: $($artifact.Name)"
        }

        $reviewerIssue = 5298
        $reviewerPr = $reviewerIssue + 1000
        $reviewerSha = 'd' * 40
        $reviewerMainSha = 'e' * 40
        $reviewerBody = 'Content-free Reviewer failure fixture.'
        $reviewerConfig = Import-SashimiHostConfig $script:fakeConfigPath
        $reviewerRunId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
        $reviewerRun = New-SashimiRunWorkspace -RunRoot ([string]$reviewerConfig.RunRoot) -RunId $reviewerRunId
        $reviewerSelection = [ordered]@{
            SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
            Role = 'Reviewer'; Mode = 'Review'; ProjectItemId = "fixture-item-$reviewerIssue"
            Status = 'Review'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
            IssueUpdatedAt = '2026-01-01T00:00:00Z'; IssueNumber = $reviewerIssue
            IssueTitle = 'Synthetic content-free Reviewer'; IssueBody = $reviewerBody
            IssueBodySha256 = (Get-SashimiTextSha256 -Text $reviewerBody)
            IssueUrl = "https://example.invalid/issues/$reviewerIssue"
            PullRequestNumber = $reviewerPr; PullRequestUrl = "https://example.invalid/pull/$reviewerPr"
            PullRequestHeadSha = $reviewerSha; PullRequestHeadRef = "infra/existing-pr-$reviewerPr"
            PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
            Conversation = @(); ConversationSha256 = (Get-SashimiConversationSha256 -Records @())
        }
        $reviewerSelectionPath = Join-Path $reviewerRun.StatePath 'Selection.json'
        Write-HostTestFile $reviewerSelectionPath (($reviewerSelection | ConvertTo-Json -Depth 32) + "`n")
        $reviewerResult = New-HostCodexResult -RunId $reviewerRunId -IssueNumber $reviewerIssue `
            -HeadSha $reviewerSha -Role Reviewer -Mode Review -PullRequestNumber $reviewerPr
        $reviewerCodexFixture = New-HostCodexFixtureFile -Name 'reviewer-content-free-failure' -Result $reviewerResult -Events @(
            [ordered]@{ type = 'item.started'; item = [ordered]@{ id = $reviewerMarker; type = 'command_execution'; command = 'Get-Content README.md' } },
            [ordered]@{ type = 'turn.completed' }
        )
        $reviewerPublishPath = Join-Path $script:temporaryRoot 'reviewer-content-free-failure.publish.json'
        $reviewerPublish = [ordered]@{
            SchemaVersion = 1; CurrentStatus = 'Review'; OpenPullRequestCount = 1
            OpenPullRequestNumbers = @($reviewerPr); IssueUpdatedAt = [string]$reviewerSelection.IssueUpdatedAt
            IssueBodySha256 = [string]$reviewerSelection.IssueBodySha256; LiveConversationRecords = @()
            LivePullRequest = [ordered]@{
                Number = $reviewerPr; State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'
                BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'; HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                HeadRef = [string]$reviewerSelection.PullRequestHeadRef; HeadSha = $reviewerSha; AuthorLogin = 'DongGyunLeeeee'
                Url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$reviewerPr"
            }
            CommentUrl = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$reviewerPr#issuecomment-95298"
        }
        Write-HostTestFile $reviewerPublishPath (($reviewerPublish | ConvertTo-Json -Depth 32) + "`n")
        $reviewer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath; SelectionPath = $reviewerSelectionPath; RunPath = $reviewerRun.RunPath
            CodexFixturePath = $reviewerCodexFixture; PublishFixturePath = $reviewerPublishPath
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GIT_PINNED_SHA = $reviewerSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $reviewerSha
            SASHIMI_FAKE_GIT_MAIN_SHA = $reviewerMainSha
            SASHIMI_FAKE_GIT_STATUS = ''
        } -TimeoutSeconds 60
        Assert-HostTest ($reviewer.ExitCode -ne 0) 'Content-free Reviewer Codex fixture did not fail.'
        Assert-HostTest (([string]$reviewer.StdOut + [string]$reviewer.StdErr) -notmatch [regex]::Escape($reviewerMarker)) 'Reviewer runner output retained the opaque Codex item ID.'
        $reviewerFailure = Join-Path $reviewerRun.ArtifactsPath 'ReviewerFailure.md'
        Assert-HostTest (Test-Path -LiteralPath $reviewerFailure -PathType Leaf) 'Reviewer did not retain ReviewerFailure.md for the Codex failure.'
        foreach ($artifact in @(Get-ChildItem -LiteralPath $reviewerRun.ArtifactsPath -File -Recurse -ErrorAction Stop)) {
            Assert-HostTest ([IO.File]::ReadAllText($artifact.FullName).IndexOf($reviewerMarker, [StringComparison]::Ordinal) -lt 0) `
                "Reviewer artifact retained an opaque Codex diagnostic value: $($artifact.Name)"
        }
    }

    Invoke-HostTestCase 'CodexEnvironmentIsAllowlistedAndCredentialStoreOnly' {
        $variableName = 'SASHIMI_FIXTURE_PASSWORD'
        $apiKeyName = 'OPENAI_API_KEY'
        $previousValue = [Environment]::GetEnvironmentVariable($variableName, 'Process')
        $previousApiKey = [Environment]::GetEnvironmentVariable($apiKeyName, 'Process')
        try {
            $sensitiveValue = 'fixture-' + (Get-SashimiTextSha256 -Text ([Guid]::NewGuid().ToString('N')))
            [Environment]::SetEnvironmentVariable($variableName, $sensitiveValue, 'Process')
            [Environment]::SetEnvironmentVariable($apiKeyName, $sensitiveValue, 'Process')
            $policy = Get-SashimiCodexEnvironmentPolicy
            Assert-HostTest ([string]$policy.Mode -ceq 'AllowList' -and [string]$policy.Authentication -ceq 'CredentialStoreOnly') 'Codex environment policy is not credential-store-only allowlist mode.'
            Assert-HostTest (@($policy.RemoveNames) -ccontains $variableName) 'A password-shaped inherited environment variable was preserved for Codex.'
            Assert-HostTest (@($policy.RemoveNames) -ccontains $apiKeyName) 'OPENAI_API_KEY was preserved for Codex.'
            if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('PATH', 'Process'))) {
                Assert-HostTest (@($policy.RemoveNames) -notcontains 'PATH') 'The operational PATH required by Codex commands was removed.'
            }
            Assert-HostTest (Test-SashimiRecognizableSensitiveText -Text ("prefix " + $sensitiveValue + " suffix") -SensitiveValues @($sensitiveValue)) 'Exact sensitive inherited environment content was not recognized.'
            $adapterSource = [IO.File]::ReadAllText((Join-Path $hostRoot 'Invoke-SashimiCodexExec.ps1'))
            Assert-HostTest ($adapterSource.Contains('Get-SashimiCodexEnvironmentPolicy') -and $adapterSource.Contains('RemoveEnvironmentVariables')) 'Codex adapter no longer applies the shared environment policy at process launch.'
        }
        finally {
            [Environment]::SetEnvironmentVariable($variableName, $previousValue, 'Process')
            [Environment]::SetEnvironmentVariable($apiKeyName, $previousApiKey, 'Process')
        }
    }

    Invoke-HostTestCase 'GitAndGitHubEnvironmentIsScrubbedBeforeHostOverrides' {
        $probePath = Join-Path $script:temporaryRoot 'tool-environment-probe.ps1'
        Write-HostTestFile $probePath @'
$names = @(
    'PATH','GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY','GIT_EXEC_PATH',
    'GIT_CONFIG_COUNT','GIT_CONFIG_KEY_0','GIT_CONFIG_VALUE_0','GIT_ASKPASS','GIT_SSH_COMMAND',
    'GIT_EDITOR','GIT_PAGER','GIT_EXTERNAL_DIFF','GIT_LFS_SKIP_SMUDGE','GIT_TERMINAL_PROMPT',
    'GCM_INTERACTIVE','SSH_ASKPASS','HTTPS_PROXY','GH_HOST','GH_CONFIG_DIR',
    'GH_DEBUG','GH_PAGER','GH_EDITOR','GH_BROWSER','GH_PROMPT_DISABLED','GH_FORCE_TTY'
)
$result = [ordered]@{}
foreach ($name in $names) { $result[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$result['GitHubAuthInputsAbsent'] = [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('GH_ENTERPRISE_TOKEN', 'Process')) -and
    [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('GITHUB_TOKEN', 'Process'))
[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress))
'@
        $poisoned = [ordered]@{
            GIT_DIR='fixture-poison'; GIT_WORK_TREE='fixture-poison'; GIT_INDEX_FILE='fixture-poison'
            GIT_OBJECT_DIRECTORY='fixture-poison'; GIT_EXEC_PATH='fixture-poison'; GIT_CONFIG_COUNT='1'
            GIT_CONFIG_KEY_0='core.hooksPath'; GIT_CONFIG_VALUE_0='fixture-poison'; GIT_ASKPASS='fixture-poison'
            GIT_SSH_COMMAND='fixture-poison'; GIT_EDITOR='fixture-poison'; GIT_PAGER='fixture-poison'
            GIT_EXTERNAL_DIFF='fixture-poison'; GIT_LFS_SKIP_SMUDGE='0'; GIT_TERMINAL_PROMPT='1'
            GCM_INTERACTIVE='Always'; SSH_ASKPASS='fixture-poison'; HTTPS_PROXY='http://fixture.invalid:1'
            GH_HOST='fixture.invalid'; GH_CONFIG_DIR='fixture-poison'; GH_ENTERPRISE_TOKEN='fixture-poison'
            GH_DEBUG='api'; GH_PAGER='fixture-poison'; GH_EDITOR='fixture-poison'; GH_BROWSER='fixture-poison'
            GH_PROMPT_DISABLED='0'; GH_FORCE_TTY='always'; GITHUB_TOKEN='fixture-poison'
        }
        $previous = @{}
        foreach ($entry in $poisoned.GetEnumerator()) {
            $previous[[string]$entry.Key] = [Environment]::GetEnvironmentVariable([string]$entry.Key, 'Process')
            [Environment]::SetEnvironmentVariable([string]$entry.Key, [string]$entry.Value, 'Process')
        }
        try {
            $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',$probePath)
            $gitProbe = Invoke-SashimiHostProcess -FilePath $PowerShellPath -ArgumentList $arguments -Kind Git -TimeoutSeconds 30 -Environment @{
                GIT_LFS_SKIP_SMUDGE='1'; GIT_TERMINAL_PROMPT='0'; GCM_INTERACTIVE='Never'
            }
            Assert-HostTest $gitProbe.Succeeded "Git environment probe failed: $($gitProbe.StdErr)"
            $gitEnvironment = ConvertFrom-LastHostJson $gitProbe.StdOut
            foreach ($name in @('GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY','GIT_EXEC_PATH','GIT_ASKPASS','GIT_SSH_COMMAND','GIT_EDITOR','GIT_PAGER','GIT_EXTERNAL_DIFF','SSH_ASKPASS','HTTPS_PROXY')) {
                Assert-HostTest ([string]::IsNullOrEmpty([string](Get-SashimiPropertyValue $gitEnvironment $name $null))) "Git inherited '$name' was not scrubbed."
            }
            Assert-HostTest ([int]$gitEnvironment.GIT_CONFIG_COUNT -gt 20 -and
                [string]$gitEnvironment.GIT_CONFIG_KEY_0 -ceq 'core.hooksPath' -and
                [string]$gitEnvironment.GIT_CONFIG_VALUE_0 -ceq 'NUL') `
                'Git did not replace poisoned command config with the fixed Host config stack.'
            Assert-HostTest ([string]$gitEnvironment.GIT_LFS_SKIP_SMUDGE -ceq '1' -and
                [string]$gitEnvironment.GIT_TERMINAL_PROMPT -ceq '0' -and
                [string]$gitEnvironment.GCM_INTERACTIVE -ceq 'Never') `
                'Git fixed Host overrides did not win over poisoned inherited values.'
            Assert-HostTest (-not [string]::IsNullOrWhiteSpace([string]$gitEnvironment.PATH)) 'Git lost PATH needed for its installed helpers and credential-store integration.'

            $githubProbe = Invoke-SashimiHostProcess -FilePath $PowerShellPath -ArgumentList $arguments -Kind GitHub -TimeoutSeconds 30 -Environment @{
                GH_PROMPT_DISABLED='1'; GIT_TERMINAL_PROMPT='0'
            }
            Assert-HostTest $githubProbe.Succeeded "GitHub environment probe failed: $($githubProbe.StdErr)"
            $githubEnvironment = ConvertFrom-LastHostJson $githubProbe.StdOut
            foreach ($name in @('GH_HOST','GH_CONFIG_DIR','GH_DEBUG','GH_PAGER','GH_EDITOR','GH_BROWSER','GIT_DIR','GIT_CONFIG_COUNT','GIT_ASKPASS','SSH_ASKPASS','HTTPS_PROXY')) {
                Assert-HostTest ([string]::IsNullOrEmpty([string](Get-SashimiPropertyValue $githubEnvironment $name $null))) "GitHub inherited '$name' was not scrubbed."
            }
            Assert-HostTest ([bool]$githubEnvironment.GitHubAuthInputsAbsent) 'GitHub inherited token-shaped authentication inputs were not scrubbed.'
            Assert-HostTest ([string]$githubEnvironment.GH_PROMPT_DISABLED -ceq '1' -and
                [string]$githubEnvironment.GIT_TERMINAL_PROMPT -ceq '0' -and
                [string]$githubEnvironment.GH_FORCE_TTY -ceq 'never') `
                'GitHub fixed Host overrides did not win over poisoned inherited values.'

            $codexProbe = Invoke-SashimiHostProcess -FilePath $PowerShellPath -ArgumentList $arguments -Kind Codex -TimeoutSeconds 30 `
                -RemoveEnvironmentVariables @('GIT_TERMINAL_PROMPT','GCM_INTERACTIVE') `
                -Environment @{ GIT_TERMINAL_PROMPT='0'; GCM_INTERACTIVE='Never' }
            Assert-HostTest $codexProbe.Succeeded "Codex environment ordering probe failed: $($codexProbe.StdErr)"
            $codexEnvironment = ConvertFrom-LastHostJson $codexProbe.StdOut
            Assert-HostTest ([string]$codexEnvironment.GIT_TERMINAL_PROMPT -ceq '0' -and
                [string]$codexEnvironment.GCM_INTERACTIVE -ceq 'Never') `
                'Caller-requested removals ran after Codex Host overrides.'

            Assert-HostThrows {
                Invoke-SashimiHostProcess -FilePath $PowerShellPath -ArgumentList $arguments -Kind Git -TimeoutSeconds 30 -Environment @{ GIT_DIR='fixture-poison' } | Out-Null
            } 'fixed Host allowlist'
        }
        finally {
            foreach ($entry in $previous.GetEnumerator()) {
                [Environment]::SetEnvironmentVariable([string]$entry.Key, $entry.Value, 'Process')
            }
        }
    }

    Invoke-HostTestCase 'AmbientGitConfigCannotExecuteHelpersOrFsmonitor' {
        $gitCommand = Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $gitCommand) { $gitCommand = Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1 }
        $repository = Join-Path $script:temporaryRoot 'ambient-git-config-repository'
        $homeRoot = Join-Path $script:temporaryRoot 'ambient-git-home'
        [IO.Directory]::CreateDirectory($homeRoot) | Out-Null
        $sentinel = Join-Path $script:temporaryRoot 'ambient-git-command-executed.tsv'
        $maliciousConfig = Join-Path $homeRoot '.gitconfig'
        $maliciousHelper = ([string]$script:fakeTools.Git).Replace('\','/')
        Write-HostTestFile $maliciousConfig "[core]`n`tfsmonitor = `"$maliciousHelper`"`n[credential]`n`thelper = !`"$maliciousHelper`"`n"

        $previous = @{
            HOME=[Environment]::GetEnvironmentVariable('HOME','Process')
            GIT_CONFIG_GLOBAL=[Environment]::GetEnvironmentVariable('GIT_CONFIG_GLOBAL','Process')
            SASHIMI_FAKE_TOOL_LOG=[Environment]::GetEnvironmentVariable('SASHIMI_FAKE_TOOL_LOG','Process')
        }
        try {
            [Environment]::SetEnvironmentVariable('HOME',$homeRoot,'Process')
            [Environment]::SetEnvironmentVariable('GIT_CONFIG_GLOBAL',$maliciousConfig,'Process')
            [Environment]::SetEnvironmentVariable('SASHIMI_FAKE_TOOL_LOG',$sentinel,'Process')
            $init = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList @('init','--quiet',$repository) -WorkingDirectory $script:temporaryRoot -TimeoutSeconds 30 -Kind Git
            Assert-HostTest $init.Succeeded "Isolated local Git fixture initialization failed: $($init.StdErr)"
            $status = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList @('-C',$repository,'status','--porcelain=v1') -WorkingDirectory $repository -TimeoutSeconds 30 -Kind Git
            Assert-HostTest $status.Succeeded "Git status failed under isolated Host config: $($status.StdErr)"
            Assert-HostTest (-not (Test-Path -LiteralPath $sentinel)) 'Ambient Git fsmonitor/credential helper executable ran despite Host config isolation.'
            $helpers = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList @('-C',$repository,'config','--show-origin','--get-regexp','^credential\..*helper$') -WorkingDirectory $repository -TimeoutSeconds 30 -Kind Git
            Assert-HostTest $helpers.Succeeded "Fixed credential-helper inspection failed: $($helpers.StdErr)"
            Assert-HostTest ($helpers.StdOut.IndexOf('auth git-credential',[StringComparison]::Ordinal) -ge 0 -and
                $helpers.StdOut.IndexOf('ambient-git-home',[StringComparison]::OrdinalIgnoreCase) -lt 0) `
                'Git did not replace the ambient credential helper with the exact Host helper.'
        }
        finally {
            foreach ($entry in $previous.GetEnumerator()) { [Environment]::SetEnvironmentVariable([string]$entry.Key,$entry.Value,'Process') }
        }
    }

    Invoke-HostTestCase 'GitCredentialEnvironmentAndOpaqueFailureOutputNeverPersist' {
        $openAiFixtureValue = 'openai-fixture-9Qx7mV2pL8cR4tN6'
        $awsFixtureValue = 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY'
        Assert-HostTest ([string]::Equals($openAiFixtureValue, (Protect-SashimiText $openAiFixtureValue), [StringComparison]::Ordinal)) `
            'OpenAI fixture value is recognizable to ordinary pattern redaction and cannot prove exact-value scrubbing.'
        Assert-HostTest ([string]::Equals($awsFixtureValue, (Protect-SashimiText $awsFixtureValue), [StringComparison]::Ordinal)) `
            'AWS fixture value is recognizable to ordinary pattern redaction and cannot prove exact-value scrubbing.'

        $environment = [ordered]@{
            OPENAI_API_KEY = $openAiFixtureValue
            AWS_SECRET_ACCESS_KEY = $awsFixtureValue
            SASHIMI_FAKE_GIT_OPAQUE_FAILURE = '1'
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
        }
        $previous = @{}
        foreach ($entry in $environment.GetEnumerator()) {
            $previous[[string]$entry.Key] = [Environment]::GetEnvironmentVariable([string]$entry.Key, 'Process')
            [Environment]::SetEnvironmentVariable([string]$entry.Key, [string]$entry.Value, 'Process')
        }
        try {
            $direct = Invoke-SashimiHostProcess -FilePath $script:fakeTools.Git `
                -ArgumentList @('-c','core.hooksPath=NUL','status','--porcelain=v1') -Kind Git -TimeoutSeconds 30
            Assert-HostTest (-not $direct.Succeeded -and $direct.ExitCode -eq 91) 'Secret-emitting fake Git did not return its expected failure.'
            Assert-HostTest ($direct.StdOut -notmatch [regex]::Escape($openAiFixtureValue) -and
                $direct.StdErr -notmatch [regex]::Escape($awsFixtureValue)) `
                'Common returned an exact sensitive inherited-environment value in Git output.'
            Assert-HostTest ($direct.StdOut -match '\[REDACTED_SECRET\]' -and $direct.StdErr -match '\[REDACTED_SECRET\]') `
                'Common did not exact-value redact both Git stdout and stderr.'

            $pinnedSha = '9' * 40
            $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber 5296 -PinnedSha $pinnedSha -DeliverySha $pinnedSha -StaleSha ('8' * 40)
            $runner = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
                ConfigPath = $script:fakeConfigPath
                SelectionPath = $bundle.SelectionPath
                RunPath = $bundle.Run.RunPath
                CodexFixturePath = $bundle.CodexFixture
                UnityFixturePath = $bundle.UnityFixture
            } -Environment @{
                OPENAI_API_KEY = $openAiFixtureValue
                AWS_SECRET_ACCESS_KEY = $awsFixtureValue
                SASHIMI_FAKE_GIT_OPAQUE_FAILURE = '1'
                SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
                SASHIMI_FAKE_GH_SCENARIO = 'developer-current'
                SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
                SASHIMI_FAKE_GIT_PINNED_SHA = $pinnedSha
                SASHIMI_FAKE_GIT_HEAD_SHA = $pinnedSha
                SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
                SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
                SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
                SASHIMI_FAKE_GIT_STATUS = ''
            } -TimeoutSeconds 60
            Assert-HostTest ($runner.ExitCode -ne 0) 'Secret-emitting fake Git did not fail the Developer runner.'
            $runnerText = [string]$runner.StdOut + "`n" + [string]$runner.StdErr
            Assert-HostTest ($runnerText -notmatch [regex]::Escape($openAiFixtureValue) -and
                $runnerText -notmatch [regex]::Escape($awsFixtureValue)) `
                'Developer runner output retained an opaque sensitive inherited-environment value.'
            $failurePath = Join-Path $bundle.Run.ArtifactsPath 'Failure.md'
            Assert-HostTest (Test-Path -LiteralPath $failurePath -PathType Leaf) 'Developer failure did not create sanitized Failure.md evidence.'
            foreach ($file in @(Get-ChildItem -LiteralPath $bundle.Run.ArtifactsPath -File -Recurse -ErrorAction Stop)) {
                $artifactText = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
                Assert-HostTest ($artifactText -notmatch [regex]::Escape($openAiFixtureValue) -and
                    $artifactText -notmatch [regex]::Escape($awsFixtureValue)) `
                    "Artifact retained an opaque sensitive inherited-environment value: $($file.Name)"
            }
        }
        finally {
            foreach ($entry in $previous.GetEnumerator()) {
                [Environment]::SetEnvironmentVariable([string]$entry.Key, $entry.Value, 'Process')
            }
        }
    }

    Invoke-HostTestCase 'DeveloperRejectsSensitiveContentAndForbiddenRenameBeforeCommit' {
        $cases = @(
            [pscustomobject]@{ Name = 'sensitive-content'; Issue = 5292; Sensitive = $true; ForbiddenRename = $false },
            [pscustomobject]@{ Name = 'forbidden-rename'; Issue = 5293; Sensitive = $false; ForbiddenRename = $true }
        )
        foreach ($case in $cases) {
            $pinned = if ($case.Sensitive) { '1' * 40 } else { '2' * 40 }
            $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber $case.Issue -PinnedSha $pinned -DeliverySha $pinned -StaleSha ('f' * 40)
            $safePath = "Assets/Tests/$($case.Name).txt"
            $sensitiveValue = 'fixture-' + (Get-SashimiTextSha256 -Text ([Guid]::NewGuid().ToString('N')))
            $repositoryFiles = [ordered]@{}
            $repositoryFiles[$safePath] = if ($case.Sensitive) { "fixture-value=$sensitiveValue" } else { 'safe fixture content' }
            $executionFixture = [ordered]@{
                SchemaVersion = 1
                RepositoryFiles = [pscustomobject]$repositoryFiles
                StatusLines = @()
                UnstagedPaths = @()
                UntrackedPaths = if ($case.Sensitive) { @($safePath) } else { @() }
                StagedPaths = if ($case.ForbiddenRename) { @($safePath) } else { @() }
                StagedPathRecords = if ($case.ForbiddenRename) { @('R100', $safePath, 'UserSettings/credential-copy.txt') } else { @() }
                FetchedHead = $pinned
                LocalHeads = @($pinned, $pinned, $pinned, $pinned)
            }
            $executionFixturePath = Join-Path $script:temporaryRoot "$($case.Name).developer-execution.json"
            Write-HostTestFile $executionFixturePath (($executionFixture | ConvertTo-Json -Depth 64) + "`n")

            $publishFixture = [ordered]@{
                SchemaVersion = 1
                CurrentStatus = 'In Progress'
                OpenPullRequestCount = 1
                OpenPullRequestNumbers = @([int]$bundle.Selection.PullRequestNumber)
                IssueUpdatedAt = [string]$bundle.Selection.IssueUpdatedAt
                IssueBodySha256 = [string]$bundle.Selection.IssueBodySha256
                LivePullRequest = [ordered]@{
                    Number = [int]$bundle.Selection.PullRequestNumber
                    State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'
                    BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                    HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                    HeadRef = [string]$bundle.Selection.PullRequestHeadRef
                    HeadSha = $pinned; AuthorLogin = 'DongGyunLeeeee'
                    Url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$($bundle.Selection.PullRequestNumber)"
                }
            }
            $publishFixturePath = Join-Path $script:temporaryRoot "$($case.Name).publish.json"
            Write-HostTestFile $publishFixturePath (($publishFixture | ConvertTo-Json -Depth 64) + "`n")

            $environment = @{}
            if ($case.Sensitive) { $environment.SASHIMI_FIXTURE_PASSWORD = $sensitiveValue }
            $developer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
                ConfigPath = $script:fakeConfigPath
                SelectionPath = $bundle.SelectionPath
                RunPath = $bundle.Run.RunPath
                CodexFixturePath = $bundle.CodexFixture
                UnityFixturePath = $bundle.UnityFixture
                PublishFixturePath = $publishFixturePath
                ExecutionFixturePath = $executionFixturePath
            } -Environment $environment -TimeoutSeconds 60
            Assert-HostTest ($developer.ExitCode -ne 0) "$($case.Name) Developer fixture unexpectedly succeeded."
            $json = ConvertFrom-LastHostJson $developer.StdOut
            if ($case.Sensitive) {
                Assert-HostTest ([string]$json.Error -match 'SensitiveChangedContent') 'Sensitive changed content was not rejected by the pre-stage boundary.'
                Assert-HostTest ($developer.StdOut -notmatch [regex]::Escape($sensitiveValue)) 'Sensitive changed content leaked into Developer output.'
            }
            else {
                Assert-HostTest ([string]$json.Error -match 'private paths cannot be delivered') 'The forbidden destination of a staged rename was not rejected.'
            }
            Assert-HostTest (-not [bool]$json.Pushed -and -not [bool]$json.TransitionedToReview) "$($case.Name) reached push or Review transition."
            Assert-HostTest (@($json.Commands | Where-Object { $_.Stage -in @('Commit focused changes','Push required Git LFS objects for exact delivery commit','Normal push exact existing PR branch') }).Count -eq 0) "$($case.Name) reached a commit or push command boundary."
        }
    }

    Invoke-HostTestCase 'ReviewerCodexIsReadOnly' {
        $runId = '20260905T000003Z-' + ('4' * 32)
        $resultObject = New-HostCodexResult -RunId $runId -IssueNumber 5263 -Role Reviewer -Mode Review -PullRequestNumber 6263
        $resultText = $resultObject | ConvertTo-Json -Depth 32 -Compress
        $fixture = New-HostCodexFixtureFile -Name 'reviewer-read-only' -Result $resultObject -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'review-message'; type = 'agent_message'; text = $resultText } },
            [ordered]@{ type = 'turn.completed' }
        )
        $process = Invoke-HostCodexFixture -FixturePath $fixture -RunId $runId -IssueNumber 5263 -Role Reviewer -Mode Review -PullRequestNumber 6263
        Assert-HostTest ($process.ExitCode -eq 0) "Reviewer Codex fixture failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([string]$json.Sandbox -ceq 'read-only') 'Reviewer Codex sandbox is not read-only.'
    }

    Invoke-HostTestCase 'UnityTimeoutCrashAndMissingXmlAreFailures' {
        $cases = @(
            [pscustomobject]@{
                Name = 'timeout'
                Stages = @{ CompileImport = @{ TimedOut = $true } }
                FailureCode = 'UnityTimeout'
                Stage = 'CompileImport'
            },
            [pscustomobject]@{
                Name = 'crash'
                Stages = @{ CompileImport = @{ Crashed = $true; LogContent = "Unity has crashed`n" } }
                FailureCode = 'UnityCrash'
                Stage = 'CompileImport'
            },
            [pscustomobject]@{
                Name = 'missing-xml'
                Stages = @{ EditMode = @{ CreateXml = $false } }
                FailureCode = 'UnityResultXmlMissing'
                Stage = 'EditMode'
            }
        )
        foreach ($case in $cases) {
            $fixture = New-HostUnityFixtureFile -Name $case.Name -Stages $case.Stages
            $process = Invoke-HostUnityFixture $fixture
            Assert-HostTest ($process.ExitCode -ne 0) "Unity $($case.Name) fixture unexpectedly passed."
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest (-not [bool]$json.Success) "Unity $($case.Name) result reported success."
            $failure = @($json.Failures | Where-Object { [string]$_.Code -ceq $case.FailureCode -and [string]$_.Stage -ceq $case.Stage })
            Assert-HostTest ($failure.Count -ge 1) "Unity $($case.Name) did not report $($case.FailureCode) for $($case.Stage)."
        }
    }

    Invoke-HostTestCase 'UnityNativeAndXmlMustAgree' {
        $fixture = New-HostUnityFixtureFile -Name 'native-xml-disagreement' -Stages @{
            EditMode = @{ ExitCode = 1; XmlContent = '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0" />' }
        }
        $process = Invoke-HostUnityFixture $fixture
        Assert-HostTest ($process.ExitCode -ne 0) 'Unity native/XML disagreement unexpectedly passed.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (@($json.Failures | Where-Object Code -eq 'UnityNativeXmlDisagreement').Count -ge 1) 'Native/XML disagreement was not recorded.'
    }

    Invoke-HostTestCase 'UnityFixturePlansAndPassesEveryHostValidationLayer' {
        $fixture = New-HostUnityFixtureFile -Name 'unity-success'
        $process = Invoke-HostUnityFixture $fixture
        Assert-HostTest ($process.ExitCode -eq 0) "Valid Unity fixture failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.Success -and [bool]$json.Stages.CompileImport.Success -and [bool]$json.Stages.EditMode.Success -and [bool]$json.Stages.PlayMode.Success) 'Compile/EditMode/PlayMode fixture did not strictly pass.'
        $commandNames = @($json.Commands | ForEach-Object { [string]$_.Name })
        foreach ($name in @('CompileImport', 'EditMode', 'PlayMode', 'DiffCheck', 'ChangedPaths', 'UntrackedPaths', 'LfsLsFiles', 'TrackedPaths')) {
            Assert-HostTest ($commandNames -ccontains $name) "Unity validation did not execute fixture stage $name."
        }
        $configured = Import-SashimiHostConfig $script:configPath
        $lfsCommands = @($json.Commands | Where-Object Name -eq 'LfsLsFiles')
        Assert-HostTest ($lfsCommands.Count -eq 1 -and [string]$lfsCommands[0].FilePath -ceq [string]$configured.GitLfsExecutable -and
            @($lfsCommands[0].Arguments) -cnotcontains 'lfs') 'Unity validation did not invoke the exact bound Git LFS executable directly.'
        $checkNames = @($json.Checks | ForEach-Object { [string]$_.Name })
        foreach ($name in @('GitDiffCheck', 'ProtectedProductionScope', 'GitLfsAndPointerScan', 'MetaGuidAndSerializedReferences', 'ScreenshotPreviewArtifactHooks')) {
            Assert-HostTest ($checkNames -ccontains $name) "Unity validation did not report check $name."
        }
    }

    Invoke-HostTestCase 'UnityRawOutputIsSanitizedBeforeArtifactPromotion' {
        $variableName = 'SASHIMI_OPAQUE_RUNTIME_VALUE'
        $previousValue = [Environment]::GetEnvironmentVariable($variableName, 'Process')
        $opaqueValue = 'opaque-fixture-value-' + [Guid]::NewGuid().ToString('N')
        $credentialValue = 'github_pat_fixtureCredential1234567890'
        $artifactsPath = Join-Path $script:temporaryRoot ('unity-sanitized-artifacts-' + [Guid]::NewGuid().ToString('N'))
        try {
            Assert-HostTest (-not (Test-SashimiSensitiveEnvironmentName -Name $variableName)) 'Opaque Unity fixture variable unexpectedly matched the sensitive-name denylist.'
            Assert-HostTest ([string]::Equals($opaqueValue, (Protect-SashimiText $opaqueValue), [StringComparison]::Ordinal)) 'Opaque Unity fixture value is recognizable to the ordinary denylist and cannot prove exact-value sanitization.'
            [Environment]::SetEnvironmentVariable($variableName, $opaqueValue, 'Process')
            $xmlContent = '<test-run id="2" testcasecount="1" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" duration="0.1"><output>' + $opaqueValue + ' ' + $credentialValue + '</output></test-run>'
            $fixture = New-HostUnityFixtureFile -Name 'unity-raw-sanitize-promote' -Stages @{
                CompileImport = @{ LogContent = "Fixture compile output $opaqueValue $credentialValue`n" }
                EditMode = @{ XmlContent = $xmlContent }
            }
            $fixtureData = [IO.File]::ReadAllText($fixture, [Text.Encoding]::UTF8) | ConvertFrom-Json -Depth 32
            $rawFixtureLog = [string]$fixtureData.Stages.CompileImport.LogContent
            $rawFixtureXml = [string]$fixtureData.Stages.EditMode.XmlContent
            Assert-HostTest ($rawFixtureLog.Contains($opaqueValue, [StringComparison]::Ordinal) -and $rawFixtureLog.Contains($credentialValue, [StringComparison]::Ordinal)) 'Unity fixture raw log payload was pre-redacted or incomplete.'
            Assert-HostTest ($rawFixtureXml.Contains($opaqueValue, [StringComparison]::Ordinal) -and $rawFixtureXml.Contains($credentialValue, [StringComparison]::Ordinal)) 'Unity fixture raw XML payload was pre-redacted or incomplete.'

            $process = Invoke-HostUnityFixture -FixturePath $fixture -ArtifactsPath $artifactsPath
            Assert-HostTest ($process.ExitCode -eq 0) "Sanitized Unity promotion fixture failed: $($process.StdErr) $($process.StdOut)"
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest ([bool]$json.Success) 'Sanitized Unity promotion did not report success.'
            $compileLogPath = Join-Path $artifactsPath 'CompileImport.log'
            $editXmlPath = Join-Path $artifactsPath 'EditMode.xml'
            Assert-HostTest ((Test-Path -LiteralPath $compileLogPath -PathType Leaf) -and (Test-Path -LiteralPath $editXmlPath -PathType Leaf)) 'Sanitized Unity log/XML artifacts were not promoted.'
            $compileText = [IO.File]::ReadAllText($compileLogPath, [Text.UTF8Encoding]::new($false, $true))
            $editXmlText = [IO.File]::ReadAllText($editXmlPath, [Text.UTF8Encoding]::new($false, $true))
            [xml]$parsedXml = $editXmlText
            Assert-HostTest ($null -ne $parsedXml.'test-run') 'Promoted sanitized Unity XML is not valid NUnit XML.'
            Assert-HostTest ($compileText.Contains('[REDACTED_SECRET]', [StringComparison]::Ordinal) -and $editXmlText.Contains('[REDACTED_SECRET]', [StringComparison]::Ordinal)) 'Promoted Unity evidence did not retain explicit redaction markers.'
            foreach ($artifact in @(Get-ChildItem -LiteralPath $artifactsPath -File -Recurse -Force)) {
                $artifactText = [IO.File]::ReadAllText($artifact.FullName, [Text.UTF8Encoding]::new($false, $true))
                Assert-HostTest ($artifactText.IndexOf($opaqueValue, [StringComparison]::Ordinal) -lt 0) "Opaque inherited value leaked into Unity artifact '$($artifact.Name)'."
                Assert-HostTest ($artifactText.IndexOf($credentialValue, [StringComparison]::Ordinal) -lt 0) "Credential-shaped value leaked into Unity artifact '$($artifact.Name)'."
            }
            $rawStateRoot = Join-Path (Split-Path -Parent $artifactsPath) 'State\raw-validation'
            Assert-HostTest (-not (Test-Path -LiteralPath $rawStateRoot)) 'Successfully promoted Unity raw State was not removed.'
        }
        finally {
            [Environment]::SetEnvironmentVariable($variableName, $previousValue, 'Process')
        }
    }

    Invoke-HostTestCase 'UnityUnexpectedRawStateFailsCleanupAndPreservesEvidence' {
        $artifactsPath = Join-Path $script:temporaryRoot ('unity-unexpected-raw-artifacts-' + [Guid]::NewGuid().ToString('N'))
        $normalizedArtifactsPath = [IO.Path]::GetFullPath($artifactsPath)
        $rawLeaf = 'fixture-' + (Get-SashimiTextSha256 -Text $normalizedArtifactsPath.ToLowerInvariant()).Substring(0,16)
        $rawPath = Join-Path (Split-Path -Parent $artifactsPath) (Join-Path 'State\raw-validation' $rawLeaf)
        $unexpectedName = 'unexpected-sensitive-state.bin'
        $unexpectedPath = Join-Path $rawPath $unexpectedName
        $sentinel = 'raw-cleanup-sentinel-' + [Guid]::NewGuid().ToString('N')
        Write-HostTestFile $unexpectedPath $sentinel
        $fixture = New-HostUnityFixtureFile -Name 'unity-unexpected-raw-state'
        $process = Invoke-HostUnityFixture -FixturePath $fixture -ArtifactsPath $artifactsPath
        Assert-HostTest ($process.ExitCode -ne 0) 'Unity validation accepted unexpected raw State as a successful cleanup.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        $cleanupFailures = @($json.Failures | Where-Object Code -eq 'RawValidationStateCleanupFailed')
        $cleanupChecks = @($json.Checks | Where-Object Name -eq 'RawValidationStateCleanup')
        Assert-HostTest (-not [bool]$json.Success -and $cleanupFailures.Count -eq 1 -and $cleanupChecks.Count -eq 1 -and -not [bool]$cleanupChecks[0].Passed) `
            'Unexpected raw State did not fail the closed cleanup gate exactly once.'
        Assert-HostTest (Test-Path -LiteralPath $unexpectedPath -PathType Leaf) 'Unexpected raw evidence was deleted instead of being quarantined.'
        Assert-HostTest (-not (Test-SashimiPathWithin -Path $unexpectedPath -Root $artifactsPath)) 'Unexpected raw evidence was retained inside publishable Artifacts.'
        $publicText = [string]$process.StdOut + "`n" + [string]$process.StdErr
        foreach ($artifact in @(Get-ChildItem -LiteralPath $artifactsPath -File -Recurse -Force -ErrorAction Stop)) {
            $publicText += "`n" + [IO.File]::ReadAllText($artifact.FullName,[Text.UTF8Encoding]::new($false,$true))
        }
        foreach ($forbidden in @($sentinel,$unexpectedName,$rawLeaf,$rawPath)) {
            Assert-HostTest ($publicText.IndexOf($forbidden,[StringComparison]::OrdinalIgnoreCase) -lt 0) 'Unexpected raw-State identity or content leaked into publishable evidence.'
        }
    }

    Invoke-HostTestCase 'UnityUnconfirmedTerminationPreservesRawStateWithoutPromotion' {
        $variableName = 'SASHIMI_OPAQUE_UNCONFIRMED_VALUE'
        $previousValue = [Environment]::GetEnvironmentVariable($variableName, 'Process')
        $opaqueValue = 'unconfirmed-fixture-value-' + [Guid]::NewGuid().ToString('N')
        $artifactsPath = Join-Path $script:temporaryRoot ('unity-unconfirmed-artifacts-' + [Guid]::NewGuid().ToString('N'))
        try {
            [Environment]::SetEnvironmentVariable($variableName, $opaqueValue, 'Process')
            $fixture = New-HostUnityFixtureFile -Name 'unity-unconfirmed-termination' -Stages @{
                CompileImport = @{
                    TerminationConfirmed = $false
                    LogContent = "Unconfirmed raw output $opaqueValue`n"
                }
            }
            $process = Invoke-HostUnityFixture -FixturePath $fixture -ArtifactsPath $artifactsPath
            Assert-HostTest ($process.ExitCode -ne 0) 'Unconfirmed Unity termination unexpectedly passed.'
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest (-not [bool]$json.Success) 'Unconfirmed Unity termination reported success.'
            Assert-HostTest (@($json.Failures | Where-Object Code -eq 'UnityTerminationUnconfirmed').Count -eq 1) 'Unconfirmed Unity termination did not emit its stage failure.'
            Assert-HostTest (@($json.Failures | Where-Object Code -eq 'RawValidationStatePreserved').Count -eq 1) 'Unconfirmed Unity termination did not record deliberate raw-State preservation.'
            $cleanupCheck = @($json.Checks | Where-Object Name -eq 'RawValidationStateCleanup')
            Assert-HostTest ($cleanupCheck.Count -eq 1 -and -not [bool]$cleanupCheck[0].Passed -and [string]$cleanupCheck[0].Detail -match 'deliberately preserved') 'Unconfirmed Unity termination did not report the failed/preserved raw-State cleanup check.'

            Assert-HostTest (@(Get-ChildItem -LiteralPath $artifactsPath -File -Recurse -Force | Where-Object Extension -in @('.log', '.xml')).Count -eq 0) 'Unconfirmed Unity output was promoted into a log or XML artifact.'
            $normalizedArtifactsPath = [IO.Path]::GetFullPath($artifactsPath)
            $rawLeaf = 'fixture-' + (Get-SashimiTextSha256 -Text $normalizedArtifactsPath.ToLowerInvariant()).Substring(0, 16)
            $rawPath = Join-Path (Split-Path -Parent $artifactsPath) (Join-Path 'State\raw-validation' $rawLeaf)
            $rawLogPath = Join-Path $rawPath 'CompileImport.raw.log'
            Assert-HostTest (Test-Path -LiteralPath $rawLogPath -PathType Leaf) 'Unconfirmed Unity raw log was not preserved in run-owned State.'
            $rawText = [IO.File]::ReadAllText($rawLogPath, [Text.UTF8Encoding]::new($false, $true))
            Assert-HostTest ($rawText.Contains($opaqueValue, [StringComparison]::Ordinal)) 'Preserved unconfirmed Unity State was unexpectedly pre-redacted or overwritten.'
            Assert-HostTest (-not (Test-SashimiPathWithin -Path $rawPath -Root $artifactsPath)) 'Unconfirmed raw Unity state was placed inside Artifacts.'
            Assert-HostTest ($process.StdOut.IndexOf($rawPath, [StringComparison]::OrdinalIgnoreCase) -lt 0 -and $process.StdOut.IndexOf($rawLeaf, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'Serialized Unity result exposed the absolute raw-State path or unique raw leaf.'
            $summaryText = [IO.File]::ReadAllText((Join-Path $artifactsPath 'UnityValidation.Summary.json'), [Text.UTF8Encoding]::new($false, $true))
            Assert-HostTest ($summaryText.IndexOf($rawPath, [StringComparison]::OrdinalIgnoreCase) -lt 0 -and $summaryText.IndexOf($rawLeaf, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'Unity summary artifact exposed the raw-State path.'
        }
        finally {
            [Environment]::SetEnvironmentVariable($variableName, $previousValue, 'Process')
        }
    }

    Invoke-HostTestCase 'UnityFileSystemScannersDetectRealMetaGuidReferenceAndPointerState' {
        $cleanProject = New-HostUnityFileSystemProject -Name 'unity-filesystem-clean'
        $cleanFixture = New-HostUnityFixtureFile -Name 'unity-filesystem-clean' -UseFileSystemValidation -Git @{
            TrackedPaths = @{ StdOut = ($cleanProject.TrackedPaths -join "`n") }
        }
        $clean = Invoke-HostUnityFixture -FixturePath $cleanFixture -ConfigPath $script:fakeConfigPath -ProjectPath $cleanProject.ProjectPath
        Assert-HostTest ($clean.ExitCode -eq 0) "Clean real-filesystem Unity fixture failed: $($clean.StdErr) $($clean.StdOut)"
        $cleanJson = ConvertFrom-LastHostJson $clean.StdOut
        Assert-HostTest ([bool]$cleanJson.Integrity.Passed -and [int]$cleanJson.Integrity.MetaFiles -eq 3) 'Clean filesystem meta/GUID scan did not inspect the expected real meta files.'
        Assert-HostTest ([int]$cleanJson.Integrity.MissingMetaCount -eq 0 -and [int]$cleanJson.Integrity.OrphanMetaCount -eq 0 -and
            [int]$cleanJson.Integrity.DuplicateGuidCount -eq 0 -and [int]$cleanJson.Integrity.MissingScriptCount -eq 0 -and
            [int]$cleanJson.Integrity.MissingReferenceCount -eq 0) 'Clean filesystem fixture reported an integrity defect.'
        $cleanPointers = @($cleanJson.Lfs.WorkingTreePointers | Where-Object { $null -ne $_ })
        Assert-HostTest ([bool]$cleanJson.Lfs.Passed -and $cleanPointers.Count -eq 0) 'Clean filesystem pointer scan did not pass.'

        $brokenProject = New-HostUnityFileSystemProject -Name 'unity-filesystem-broken' -Broken
        $brokenFixture = New-HostUnityFixtureFile -Name 'unity-filesystem-broken' -UseFileSystemValidation -Git @{
            TrackedPaths = @{ StdOut = ($brokenProject.TrackedPaths -join "`n") }
        }
        $broken = Invoke-HostUnityFixture -FixturePath $brokenFixture -ConfigPath $script:fakeConfigPath -ProjectPath $brokenProject.ProjectPath
        Assert-HostTest ($broken.ExitCode -ne 0) 'Broken real-filesystem Unity fixture unexpectedly passed.'
        $brokenJson = ConvertFrom-LastHostJson $broken.StdOut
        Assert-HostTest (-not [bool]$brokenJson.Integrity.Passed -and [int]$brokenJson.Integrity.MissingMetaCount -eq 1 -and
            [int]$brokenJson.Integrity.OrphanMetaCount -eq 1 -and [int]$brokenJson.Integrity.DuplicateGuidCount -eq 1 -and
            [int]$brokenJson.Integrity.MissingScriptCount -eq 1 -and [int]$brokenJson.Integrity.MissingReferenceCount -eq 1) `
            'Broken filesystem fixture did not detect each real meta/GUID/Missing Script/reference defect exactly once.'
        Assert-HostTest (-not [bool]$brokenJson.Lfs.Passed -and @($brokenJson.Lfs.WorkingTreePointers) -ccontains 'Assets/FixtureData/Pointer.bin') 'Real working-tree Git LFS pointer was not detected.'
        Assert-HostTest (@($brokenJson.Failures | Where-Object Code -eq 'UnityAssetIntegrityFailed').Count -eq 1 -and
            @($brokenJson.Failures | Where-Object Code -eq 'GitLfsPointerFailure').Count -eq 1) 'Broken filesystem scanner failures were not reflected in the validation gate.'
    }

    Invoke-HostTestCase 'IssueGeneratorRunsTwiceAndRequiresDeterminism' {
        $config = [IO.File]::ReadAllText($script:configPath, [Text.Encoding]::UTF8) | ConvertFrom-Json -Depth 64
        $definition = [ordered]@{
            IssueNumber = 5281
            UnityExecuteMethod = 'Synthetic.Generator.Run'
            Arguments = @('--fixture')
            DeterminismPaths = @('Assets/Generated')
            ScreenshotPaths = @()
            PreviewPaths = @()
            AllowedProtectedPathPatterns = @('Assets/Generated/**')
        }
        $config.IssueValidations | Add-Member -NotePropertyName 'fixture-generator' -NotePropertyValue ([pscustomobject]$definition)
        $configPath = Join-Path $script:temporaryRoot 'generator-config.json'
        Write-HostTestFile $configPath (($config | ConvertTo-Json -Depth 64) + "`n")
        $snapshot = @([ordered]@{ Path = 'Assets/Generated/result.asset'; Length = 10; Sha256 = ('a' * 64) })
        $determinism = [ordered]@{ Run1 = $snapshot; Run2 = $snapshot }
        $fixture = New-HostUnityFixtureFile -Name 'generator-deterministic' -Determinism $determinism
        $process = Invoke-HostUnityFixture -FixturePath $fixture -IssueNumber 5281 -ConfigPath $configPath -IssueValidationId 'fixture-generator'
        Assert-HostTest ($process.ExitCode -eq 0) "Deterministic generator fixture failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.Determinism.Required -and [bool]$json.Determinism.Passed) 'Generator determinism was not required and passed.'
        Assert-HostTest ([bool]$json.Stages.GeneratorRun1.Success -and [bool]$json.Stages.GeneratorRun2.Success) 'Generator did not run twice.'

        $differentSnapshot = @([ordered]@{ Path = 'Assets/Generated/result.asset'; Length = 11; Sha256 = ('b' * 64) })
        $fixture = New-HostUnityFixtureFile -Name 'generator-nondeterministic' -Determinism ([ordered]@{ Run1 = $snapshot; Run2 = $differentSnapshot })
        $process = Invoke-HostUnityFixture -FixturePath $fixture -IssueNumber 5281 -ConfigPath $configPath -IssueValidationId 'fixture-generator'
        Assert-HostTest ($process.ExitCode -ne 0) 'Non-deterministic generator fixture passed.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (@($json.Failures | Where-Object Code -eq 'GeneratorNonDeterministic').Count -eq 1) 'Generator nondeterminism was not reported.'
    }

    Invoke-HostTestCase 'ProtectedScopeAndMissingScriptIntegrityFailClosed' {
        $fixture = New-HostUnityFixtureFile -Name 'protected-scope' -ChangedPaths @('Assets/Scenes/Production.unity')
        $process = Invoke-HostUnityFixture $fixture
        Assert-HostTest ($process.ExitCode -ne 0) 'Protected production scope change passed validation.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (@($json.Failures | Where-Object Code -eq 'PreUnityProtectedProductionScopeChanged').Count -eq 1) 'Protected scope was not rejected before Unity project load.'
        Assert-HostTest (@($json.Failures | Where-Object Code -eq 'ProtectedProductionScopeChanged').Count -eq 1) 'Protected scope failure was not reported.'
        $executedUnityStages = @($json.Stages.PSObject.Properties | ForEach-Object { [string]$_.Name } | Where-Object { $_ -in @('CompileImport', 'GeneratorRun1', 'GeneratorRun2', 'EditMode', 'PlayMode') })
        Assert-HostTest ($executedUnityStages.Count -eq 0) "Protected scope still executed Unity stage(s): $($executedUnityStages -join ', ')."

        $knownUnityDrift = @(
            '-  targetPixelDensity: 0', '+  targetPixelDensity: 30',
            '-  buildNumber: {}', '+  buildNumber:', '+    Standalone: 0', '+    VisionOS: 0', '+    iPhone: 0', '+    tvOS: 0',
            '-  iOSTargetOSVersionString: ', '+  iOSTargetOSVersionString: 15.0',
            '-  tvOSTargetOSVersionString: ', '+  tvOSTargetOSVersionString: 15.0',
            '-  VisionOSTargetOSVersionString: ', '+  VisionOSTargetOSVersionString: 1.0',
            '-  macOSTargetOSVersion: ', '+  macOSTargetOSVersion: 12.0'
        ) -join "`n"
        $fixture = New-HostUnityFixtureFile -Name 'developer-known-projectsettings-drift' `
            -ChangedPaths @('ProjectSettings/ProjectSettings.asset') `
            -Git @{ KnownUnityDriftDiff = @{ StdOut = $knownUnityDrift } }
        $process = Invoke-HostUnityFixture $fixture
        Assert-HostTest ($process.ExitCode -ne 0) 'Known Unity ProjectSettings drift was allowlisted for a deliverable validation.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.KnownUnityDefaultDrift.Detected -and -not [bool]$json.KnownUnityDefaultDrift.Allowed) 'Known ProjectSettings drift was not retained as evidence and failed closed.'

        $integrity = [ordered]@{
            Passed = $false; MissingMetaCount = 0; OrphanMetaCount = 0; InvalidMetaCount = 0
            DuplicateGuidCount = 0; MissingScriptCount = 1; MissingReferenceCount = 0
        }
        $fixture = New-HostUnityFixtureFile -Name 'missing-script' -Integrity $integrity
        $process = Invoke-HostUnityFixture $fixture
        Assert-HostTest ($process.ExitCode -ne 0) 'Missing Script integrity fixture passed validation.'
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest (@($json.Failures | Where-Object Code -eq 'UnityAssetIntegrityFailed').Count -eq 1) 'Missing Script integrity failure was not reported.'
    }

    Invoke-HostTestCase 'ValidationOnlyResumeReusesExactExistingBranchAndNoNewPr' {
        foreach ($mode in @('ReviewFix', 'DeliveryResume')) {
            $issue = if ($mode -ceq 'ReviewFix') { 5270 } else { 5271 }
            $pr = $issue + 1000
            $sha = if ($mode -ceq 'ReviewFix') { '7' * 40 } else { '8' * 40 }
            $headRef = "infra/existing-pr-$pr"
            $sentinel = Join-Path $script:temporaryRoot "$mode-pending-command-executed.txt"
            $selection = [ordered]@{
                SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
                Role = 'Developer'; Mode = $mode; ProjectItemId = "fixture-item-$issue"
                Status = 'In Progress'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
                IssueUpdatedAt = '2026-01-01T00:00:00Z'
                IssueNumber = $issue; IssueTitle = "Synthetic $mode"; IssueBody = 'Fixture acceptance criteria.'
                IssueBodySha256 = (Get-SashimiTextSha256 -Text 'Fixture acceptance criteria.')
                IssueUrl = "https://example.invalid/issues/$issue"
                PullRequestNumber = $pr; PullRequestUrl = "https://example.invalid/pull/$pr"
                PullRequestHeadSha = $sha; PullRequestHeadRef = $headRef
                PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                PendingCommand = "Set-Content -LiteralPath '$sentinel' -Value unsafe"
                LatestHandoffUrl = "https://example.invalid/handoff/$issue"
            }
            $selectionPath = Join-Path $script:temporaryRoot "$mode-selection.json"
            Write-HostTestFile $selectionPath (($selection | ConvertTo-Json -Depth 32) + "`n")
            $runPath = Join-Path $script:temporaryRoot ("20260905T00000{0}Z-{1}" -f ($issue % 10), ([Guid]::NewGuid().ToString('N')))
            $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
                ConfigPath = $script:configPath
                SelectionPath = $selectionPath
                RunPath = $runPath
                DryRun = $true
            }
            Assert-HostTest ($process.ExitCode -eq 0) "$mode validation-only DryRun failed: $($process.StdOut)"
            $json = ConvertFrom-LastHostJson $process.StdOut
            Assert-HostTest ([bool]$json.Success -and -not [bool]$json.Pushed) "$mode validation-only resume planned a push."
            Assert-HostTest (-not [bool]$json.CreatedPullRequest) "$mode created a new PR."
            Assert-HostTest (-not (Test-Path -LiteralPath $sentinel)) "$mode executed pendingCommand."
            Assert-HostTest (@($json.Commands | Where-Object Stage -eq 'Create linked Draft PR').Count -eq 0) "$mode planned a new PR."
            $fetch = @($json.Commands | Where-Object Stage -eq 'Fetch exact existing PR branch')
            Assert-HostTest ($fetch.Count -eq 1 -and @($fetch[0].Arguments) -ccontains "+refs/heads/${headRef}:refs/remotes/origin/sashimi-pinned") "$mode did not fetch the exact existing PR ref."
            $plannedDeliveryPush = @($json.Commands | Where-Object {
                    $_.Stage -in @('Push required Git LFS objects for exact delivery commit', 'Normal push exact existing PR branch')
                })
            Assert-HostTest ($plannedDeliveryPush.Count -eq 0) "$mode validation-only plan contained a delivery push."
        }
        $source = [IO.File]::ReadAllText((Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1'))
        Assert-HostTest ($source.Contains('"HEAD:$headRef"')) 'Resume push is not hard-bound to the existing head ref variable.'
        Assert-HostTest ($source -match "mode -ceq 'NewWork'[\s\S]{0,1500}Create linked Draft PR") 'Draft PR creation is not visibly restricted to NewWork.'

        foreach ($mode in @('ReviewFix', 'DeliveryResume')) {
            $offset = if ($mode -ceq 'ReviewFix') { 0 } else { 1 }
            $issue = 5272 + $offset
            $pinned = ([string](4 + $offset)) * 40
            $bundle = New-HostResumeFixtureBundle -Mode $mode -IssueNumber $issue -PinnedSha $pinned -DeliverySha $pinned -StaleSha ('f' * 40)
            $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
            $resume = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
                ConfigPath = $script:fakeConfigPath
                SelectionPath = $bundle.SelectionPath
                RunPath = $bundle.Run.RunPath
                CodexFixturePath = $bundle.CodexFixture
                UnityFixturePath = $bundle.UnityFixture
            } -Environment @{
                SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
                SASHIMI_FAKE_GH_SCENARIO = 'developer-current'
                SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
                SASHIMI_FAKE_GIT_PINNED_SHA = $pinned
                SASHIMI_FAKE_GIT_HEAD_SHA = $pinned
                SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
                SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
                SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
                SASHIMI_FAKE_GIT_STATUS = ''
            } -TimeoutSeconds 60
            Assert-HostTest ($resume.ExitCode -eq 0) "$mode validation-only fake-boundary run failed: $($resume.StdErr) $($resume.StdOut)"
            $resumeJson = ConvertFrom-LastHostJson $resume.StdOut
            Assert-HostTest ([bool]$resumeJson.Success -and -not [bool]$resumeJson.Pushed -and -not [bool]$resumeJson.CreatedPullRequest) "$mode validation-only fake-boundary run pushed or created a PR."
            $resumeCalls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
            Assert-HostTest (@($resumeCalls | Where-Object { $_.Tool -eq 'git' -and @($_.Arguments) -contains 'push' }).Count -eq 0) "$mode validation-only fake-boundary run invoked push."
            Assert-HostTest (@($resumeCalls | Where-Object { $_.Tool -eq 'gh' -and @($_.Arguments) -contains 'create' }).Count -eq 0) "$mode validation-only fake-boundary run invoked PR creation."
            $exactFetch = @($resumeCalls | Where-Object { $_.Tool -eq 'git' -and (@($_.Arguments) -join ' ') -match [regex]::Escape("+refs/heads/$($bundle.Selection.PullRequestHeadRef):refs/remotes/origin/sashimi-pinned") })
            Assert-HostTest ($exactFetch.Count -eq 1) "$mode fake-boundary run did not fetch the exact existing PR branch."
        }

        foreach ($mode in @('ReviewFix', 'DeliveryResume')) {
            $offset = if ($mode -ceq 'ReviewFix') { 0 } else { 1 }
            $issue = 5274 + $offset
            $pinned = ([string](6 + $offset)) * 40
            $delivery = ([string](8 + $offset)) * 40
            $bundle = New-HostResumeFixtureBundle -Mode $mode -IssueNumber $issue -PinnedSha $pinned -DeliverySha $delivery -StaleSha ('f' * 40)
            $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
            $resume = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
                ConfigPath = $script:fakeConfigPath
                SelectionPath = $bundle.SelectionPath
                RunPath = $bundle.Run.RunPath
                CodexFixturePath = $bundle.CodexFixture
                UnityFixturePath = $bundle.UnityFixture
            } -Environment @{
                SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
                SASHIMI_FAKE_GH_SCENARIO = 'developer-current'
                SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
                SASHIMI_FAKE_GIT_PINNED_SHA = $pinned
                SASHIMI_FAKE_GIT_HEAD_SHA = $delivery
                SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
                SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
                SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
                SASHIMI_FAKE_GIT_STATUS = ''
            } -TimeoutSeconds 60
            Assert-HostTest ($resume.ExitCode -eq 0) "$mode changed fake-boundary run failed: $($resume.StdErr) $($resume.StdOut)"
            $resumeJson = ConvertFrom-LastHostJson $resume.StdOut
            Assert-HostTest ([bool]$resumeJson.Success -and [bool]$resumeJson.Pushed -and -not [bool]$resumeJson.CreatedPullRequest) "$mode changed fake-boundary run did not push exactly the existing PR."
            $resumeCalls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
            $pushes = @($resumeCalls | Where-Object {
                    $_.Tool -eq 'git' -and @($_.Arguments) -contains 'push' -and @($_.Arguments) -notcontains 'lfs'
                })
            Assert-HostTest ($pushes.Count -eq 1 -and @($pushes[0].Arguments) -ccontains "HEAD:$($bundle.Selection.PullRequestHeadRef)") "$mode changed fake-boundary run did not push HEAD to the exact pinned ref."
            Assert-HostTest (@($resumeCalls | Where-Object { $_.Tool -eq 'gh' -and @($_.Arguments) -contains 'create' }).Count -eq 0) "$mode changed fake-boundary run invoked PR creation."
        }
    }

    Invoke-HostTestCase 'ReviewerUsesSyntheticMergeAndNeverPushes' {
        $issue = 5275
        $pr = 6275
        $sha = '9' * 40
        $selection = [ordered]@{
            SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
            Role = 'Reviewer'; Mode = 'Review'; ProjectItemId = "fixture-item-$issue"
            Status = 'Review'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
            IssueUpdatedAt = '2026-01-01T00:00:00Z'
            IssueNumber = $issue; IssueTitle = 'Synthetic independent review'; IssueBody = 'Review fixture criteria.'
            IssueBodySha256 = (Get-SashimiTextSha256 -Text 'Review fixture criteria.')
            IssueUrl = "https://example.invalid/issues/$issue"
            PullRequestNumber = $pr; PullRequestUrl = "https://example.invalid/pull/$pr"
            PullRequestHeadSha = $sha; PullRequestHeadRef = "infra/existing-pr-$pr"
            PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
        }
        $selectionPath = Join-Path $script:temporaryRoot 'reviewer-selection.json'
        Write-HostTestFile $selectionPath (($selection | ConvertTo-Json -Depth 32) + "`n")
        $runPath = Join-Path $script:temporaryRoot ('20260905T000005Z-' + [Guid]::NewGuid().ToString('N'))
        $process = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath = $script:configPath; SelectionPath = $selectionPath; RunPath = $runPath; DryRun = $true
        }
        Assert-HostTest ($process.ExitCode -eq 0) "Reviewer DryRun failed: $($process.StdOut)"
        $json = ConvertFrom-LastHostJson $process.StdOut
        Assert-HostTest ([bool]$json.Success -and -not [bool]$json.ReviewerPushAttempted) 'Reviewer reported a push attempt.'
        Assert-HostTest (@($json.Commands | Where-Object { @($_.Arguments) -ccontains 'push' }).Count -eq 0) 'Reviewer plan contains git push.'
        $merge = @($json.Commands | Where-Object Stage -eq 'Normal synthetic merge')
        Assert-HostTest ($merge.Count -eq 1 -and @($merge[0].Arguments) -ccontains '--no-ff' -and @($merge[0].Arguments) -ccontains $sha) 'Reviewer did not plan the exact synthetic merge.'
        Assert-HostTest ([string]$json.Transition -ceq 'Review->Verification') 'Clean Reviewer DryRun did not plan Review -> Verification.'
    }

    Invoke-HostTestCase 'ReviewerRejectsPostUnityNonProtectedDriftBeforePublication' {
        $issue = 5296
        $pr = 6296
        $headSha = 'a' * 40
        $mainSha = 'b' * 40
        $headRef = "infra/existing-pr-$pr"
        $issueBody = 'Fixture acceptance criteria.'
        $config = Import-SashimiHostConfig $script:fakeConfigPath
        $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
        $run = New-SashimiRunWorkspace -RunRoot ([string]$config.RunRoot) -RunId $runId
        $conversation = @()
        $conversationSha = Get-SashimiConversationSha256 -Records $conversation
        $selection = [ordered]@{
            SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
            Role = 'Reviewer'; Mode = 'Review'; ProjectItemId = "fixture-item-$issue"
            Status = 'Review'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
            IssueUpdatedAt = '2026-01-01T00:00:00Z'; IssueNumber = $issue
            IssueTitle = 'Synthetic post-Unity Reviewer drift'; IssueBody = $issueBody
            IssueBodySha256 = (Get-SashimiTextSha256 -Text $issueBody)
            IssueUrl = "https://example.invalid/issues/$issue"
            PullRequestNumber = $pr; PullRequestUrl = "https://example.invalid/pull/$pr"
            PullRequestHeadSha = $headSha; PullRequestHeadRef = $headRef
            PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
            Conversation = $conversation; ConversationSha256 = $conversationSha
        }
        $selectionPath = Join-Path $run.StatePath 'Selection.json'
        Write-HostTestFile $selectionPath (($selection | ConvertTo-Json -Depth 64) + "`n")

        $codexResult = New-HostCodexResult -RunId $runId -IssueNumber $issue -HeadSha $headSha -Role Reviewer -Mode Review -PullRequestNumber $pr
        $codexFixture = New-HostCodexFixtureFile -Name 'reviewer-post-unity-drift' -Result $codexResult -Events @(
            [ordered]@{ type = 'item.completed'; item = [ordered]@{ id = 'reviewer-drift-result'; type = 'agent_message'; text = ($codexResult | ConvertTo-Json -Depth 32 -Compress) } },
            [ordered]@{ type = 'turn.completed' }
        )
        $unityFixture = New-HostUnityFixtureFile -Name 'reviewer-post-unity-drift'
        $publishFixturePath = Join-Path $script:temporaryRoot 'reviewer-post-unity-drift.publish.json'
        $publishFixture = [ordered]@{
            SchemaVersion = 1; CurrentStatus = 'Review'; OpenPullRequestCount = 1
            OpenPullRequestNumbers = @($pr); IssueUpdatedAt = [string]$selection.IssueUpdatedAt
            IssueBodySha256 = [string]$selection.IssueBodySha256; LiveConversationRecords = @()
            LivePullRequest = [ordered]@{
                Number = $pr; State = 'OPEN'; IsDraft = $true; BaseRefName = 'main'
                BaseRepository = 'DongGyunLeeeee/sashimi-boy-unity'; HeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
                HeadRef = $headRef; HeadSha = $headSha; AuthorLogin = 'DongGyunLeeeee'
                Url = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$pr"
            }
            CommentUrl = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/pull/$pr#issuecomment-95296"
        }
        Write-HostTestFile $publishFixturePath (($publishFixture | ConvertTo-Json -Depth 64) + "`n")

        $driftRelativePath = 'Assets/Tests/Editor/PostUnityDriftTests.cs'
        $driftPath = Join-Path $run.RepositoryPath $driftRelativePath.Replace('/', '\')
        $unitySummaryPath = Join-Path $run.ArtifactsPath 'Unity\UnityValidation.Summary.json'
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $reviewer = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath; SelectionPath = $selectionPath; RunPath = $run.RunPath
            CodexFixturePath = $codexFixture; UnityFixturePath = $unityFixture; PublishFixturePath = $publishFixturePath
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GIT_PINNED_SHA = $headSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $headSha
            SASHIMI_FAKE_GIT_MAIN_SHA = $mainSha
            SASHIMI_FAKE_GIT_STATUS = ''
            SASHIMI_FAKE_POST_UNITY_MARKER = $unitySummaryPath
            SASHIMI_FAKE_POST_UNITY_DRIFT_PATH = $driftRelativePath
            SASHIMI_FAKE_GIT_POST_UNITY_STATUS = "?? $driftRelativePath`n"
        } -TimeoutSeconds 90

        Assert-HostTest ($reviewer.ExitCode -ne 0) 'Reviewer accepted non-protected C#/test drift created after Unity validation.'
        $json = ConvertFrom-LastHostJson $reviewer.StdOut
        Assert-HostTest (-not [bool]$json.Success -and [string]::IsNullOrEmpty([string]$json.Transition)) 'Post-Unity drift reached a Reviewer Project transition.'
        Assert-HostTest ([string]$json.Error -match 'Unity validation crossed the read-only Reviewer boundary') 'Reviewer failure did not identify the post-Unity read-only boundary.'
        Assert-HostTest (Test-Path -LiteralPath $driftPath -PathType Leaf) 'The Unity-timed fixture did not create its non-protected C#/test drift file.'
        Assert-HostTest (Test-Path -LiteralPath $unitySummaryPath -PathType Leaf) 'The drift fixture was not gated on completed Unity validation evidence.'
        Assert-HostTest (-not [bool]$json.ReviewerPushAttempted) 'Reviewer reported a push attempt after post-Unity drift.'
        $forbiddenStages = @('Post focused review finding','Post current ReviewFix handoff','Review to In Progress','Post Owner verification checklist','Review to Verification')
        Assert-HostTest (@($json.Commands | Where-Object { $forbiddenStages -ccontains [string]$_.Stage }).Count -eq 0) 'Post-Unity drift reached finding, handoff, success-checklist, or Project-transition publication.'
        Assert-HostTest (@($json.Commands | Where-Object Stage -eq 'Publish sanitized failure evidence without transition').Count -eq 1) 'Post-Unity drift did not retain the allowed sanitized failure-evidence publication path.'
        Assert-HostTest (@($json.Events | Where-Object { [string]$_.Name -ceq 'FailureEvidence' -and [string]$_.Value -ceq 'Published' }).Count -eq 1) 'Sanitized failure evidence was not published through the fixture boundary.'
        $audit = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore)
        $gitAudit = @($audit | Where-Object Tool -eq 'git')
        Assert-HostGitHookSuppression -Records $gitAudit -Context 'Post-Unity drift Reviewer boundary'
        Assert-HostTest (@($gitAudit | Where-Object { @($_.Arguments) -ccontains 'push' }).Count -eq 0) 'Reviewer invoked Git push after post-Unity drift.'
        Assert-HostTest (@($audit | Where-Object { $_.Tool -eq 'gh' -or $_.SimulatedMutation }).Count -eq 0) 'Fixture Reviewer drift test reached a live-like GitHub or mutation boundary.'
    }

    Invoke-HostTestCase 'DeveloperAndReviewerGitHooksAreSuppressedFromFirstInvocation' {
        $developerIssue = 5282
        $developerBody = 'Git hook suppression plan fixture.'
        $developerSelection = [ordered]@{
            SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
            Role = 'Developer'; Mode = 'NewWork'; ProjectItemId = "fixture-item-$developerIssue"
            Status = 'Ready'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
            IssueUpdatedAt = '2026-01-01T00:00:00Z'; IssueNumber = $developerIssue
            IssueTitle = 'Synthetic hook-safe Developer'; IssueBody = $developerBody
            IssueBodySha256 = (Get-SashimiTextSha256 -Text $developerBody)
            IssueUrl = "https://example.invalid/issues/$developerIssue"
            PullRequestNumber = 0; PullRequestUrl = ''; PullRequestHeadSha = ''; PullRequestHeadRef = ''
            PullRequestHeadRepository = ''
        }
        $developerSelectionPath = Join-Path $script:temporaryRoot 'hook-safe-developer-selection.json'
        Write-HostTestFile $developerSelectionPath (($developerSelection | ConvertTo-Json -Depth 32) + "`n")
        $developerDryRun = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $developerSelectionPath
            RunPath = (Join-Path $script:temporaryRoot ('20260905T000010Z-' + [Guid]::NewGuid().ToString('N')))
            DryRun = $true
        }
        Assert-HostTest ($developerDryRun.ExitCode -eq 0) "Developer hook-suppression DryRun failed: $($developerDryRun.StdErr) $($developerDryRun.StdOut)"
        $developerJson = ConvertFrom-LastHostJson $developerDryRun.StdOut
        $developerGitPlans = @($developerJson.Commands | Where-Object { [string]$_.FilePath -like '*fake-git.exe' })
        Assert-HostGitHookSuppression -Records $developerGitPlans -Context 'Developer plan'
        Assert-HostTest ([string]$developerGitPlans[0].Stage -ceq 'Audit Git URL rewrites') 'Developer first planned Git boundary changed unexpectedly.'
        Assert-HostTest (@($developerGitPlans | Where-Object Stage -eq 'Fresh standalone clone').Count -eq 1) 'Developer plan did not cover the standalone clone.'

        $reviewerIssue = 5283
        $reviewerPr = 6283
        $reviewerSha = '3' * 40
        $reviewerBody = 'Git hook suppression review fixture.'
        $reviewerSelection = [ordered]@{
            SchemaVersion = 1; Success = $true; Selected = $true; DispatchCount = 1
            Role = 'Reviewer'; Mode = 'Review'; ProjectItemId = "fixture-item-$reviewerIssue"
            Status = 'Review'; Priority = 'P0'; UpdatedAt = '2026-01-01T00:00:00Z'
            IssueUpdatedAt = '2026-01-01T00:00:00Z'; IssueNumber = $reviewerIssue
            IssueTitle = 'Synthetic hook-safe Reviewer'; IssueBody = $reviewerBody
            IssueBodySha256 = (Get-SashimiTextSha256 -Text $reviewerBody)
            IssueUrl = "https://example.invalid/issues/$reviewerIssue"
            PullRequestNumber = $reviewerPr; PullRequestUrl = "https://example.invalid/pull/$reviewerPr"
            PullRequestHeadSha = $reviewerSha; PullRequestHeadRef = "infra/existing-pr-$reviewerPr"
            PullRequestHeadRepository = 'DongGyunLeeeee/sashimi-boy-unity'
        }
        $reviewerSelectionPath = Join-Path $script:temporaryRoot 'hook-safe-reviewer-selection.json'
        Write-HostTestFile $reviewerSelectionPath (($reviewerSelection | ConvertTo-Json -Depth 32) + "`n")
        $reviewerDryRun = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $reviewerSelectionPath
            RunPath = (Join-Path $script:temporaryRoot ('20260905T000011Z-' + [Guid]::NewGuid().ToString('N')))
            DryRun = $true
        }
        Assert-HostTest ($reviewerDryRun.ExitCode -eq 0) "Reviewer hook-suppression DryRun failed: $($reviewerDryRun.StdErr) $($reviewerDryRun.StdOut)"
        $reviewerJson = ConvertFrom-LastHostJson $reviewerDryRun.StdOut
        $reviewerGitPlans = @($reviewerJson.Commands | Where-Object { [string]$_.FilePath -like '*fake-git.exe' })
        Assert-HostGitHookSuppression -Records $reviewerGitPlans -Context 'Reviewer plan'
        Assert-HostTest ([string]$reviewerGitPlans[0].Stage -ceq 'Audit Git URL rewrites') 'Reviewer first planned Git boundary changed unexpectedly.'
        Assert-HostTest (@($reviewerGitPlans | Where-Object Stage -eq 'Fresh standalone review clone').Count -eq 1) 'Reviewer plan did not cover the standalone review clone.'

        # Exercise the native fake-Git boundary, stopping at a fixture Codex
        # failure before Unity can add independent read-only Git processes.
        $pinnedSha = '4' * 40
        $bundle = New-HostResumeFixtureBundle -Mode ReviewFix -IssueNumber 5284 -PinnedSha $pinnedSha -DeliverySha $pinnedSha -StaleSha ('5' * 40)
        $failedCodex = New-HostCodexFixtureFile -Name 'hook-safe-codex-stop' -Result $null -Events @(
            [ordered]@{ type = 'turn.failed'; error = [ordered]@{ message = 'Synthetic stop before Unity.' } }
        )
        $auditBefore = @(Get-HostFakeToolAudit $script:fakeToolLogPath).Count
        $fakeBoundaryRun = Invoke-HostTestScript -ScriptPath (Join-Path $hostRoot 'Invoke-SashimiDeveloperRun.ps1') -Parameters @{
            ConfigPath = $script:fakeConfigPath
            SelectionPath = $bundle.SelectionPath
            RunPath = $bundle.Run.RunPath
            CodexFixturePath = $failedCodex
        } -Environment @{
            SASHIMI_FAKE_TOOL_LOG = $script:fakeToolLogPath
            SASHIMI_FAKE_GH_SCENARIO = 'developer-current'
            SASHIMI_FAKE_SCENARIO_ROOT = $bundle.ScenarioRoot
            SASHIMI_FAKE_GIT_PINNED_SHA = $pinnedSha
            SASHIMI_FAKE_GIT_HEAD_SHA = $pinnedSha
            SASHIMI_FAKE_GIT_BRANCH = $bundle.Branch
            SASHIMI_FAKE_PUSH_STATE = $bundle.PushState
            SASHIMI_FAKE_STATUS_STATE = $bundle.StatusState
            SASHIMI_FAKE_GIT_STATUS = ''
        } -TimeoutSeconds 60
        Assert-HostTest ($fakeBoundaryRun.ExitCode -ne 0) 'Fake-boundary Developer run did not stop at the intentional Codex failure.'
        $fakeGitCalls = @((Get-HostFakeToolAudit $script:fakeToolLogPath) | Select-Object -Skip $auditBefore | Where-Object Tool -eq 'git')
        Assert-HostGitHookSuppression -Records $fakeGitCalls -Context 'Developer fake-process boundary'
        Assert-HostTest (@($fakeGitCalls | Where-Object { @($_.Arguments) -ccontains 'clone' }).Count -eq 1) 'Fake-process coverage did not include exactly one clone.'

        # Prove the exact override leaves a known executable local hook inert.
        # This owned fixture performs no clone, fetch, commit, push, or network operation.
        $gitCommand = Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $gitCommand) { $gitCommand = Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1 }
        $hookRepository = Join-Path $script:temporaryRoot 'hook-sentinel-repository'
        $initArguments = @('-c','core.hooksPath=NUL','-c','init.templateDir=','init','--quiet',$hookRepository)
        $initialized = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList $initArguments -WorkingDirectory $script:temporaryRoot -TimeoutSeconds 30 -Kind Git
        Assert-HostTest $initialized.Succeeded "Local hook sentinel repository initialization failed: $($initialized.StdErr)"
        $hookPath = Join-Path $hookRepository '.git\hooks\pre-commit.exe'
        $hookSentinel = Join-Path $hookRepository '.git\hook-executed.sentinel'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $hookPath)) | Out-Null
        Copy-Item -LiteralPath $script:fakeTools.Git -Destination $hookPath -ErrorAction Stop
        $controlArguments = @('-c','core.hooksPath=.git/hooks','-C',$hookRepository,'hook','run','pre-commit')
        $control = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList $controlArguments -WorkingDirectory $hookRepository -TimeoutSeconds 30 -Kind Git -Environment @{ SASHIMI_FAKE_TOOL_LOG = $hookSentinel }
        Assert-HostTest ($control.Succeeded -and (Test-Path -LiteralPath $hookSentinel -PathType Leaf)) 'The local sentinel hook was not executable in the owned fixture.'
        Remove-Item -LiteralPath $hookSentinel -Force -ErrorAction Stop
        $safeArguments = @('-c','core.hooksPath=NUL','-C',$hookRepository,'hook','run','--ignore-missing','pre-commit')
        $safeRun = Invoke-SashimiHostProcess -FilePath $gitCommand.Source -ArgumentList $safeArguments -WorkingDirectory $hookRepository -TimeoutSeconds 30 -Kind Git -Environment @{ SASHIMI_FAKE_TOOL_LOG = $hookSentinel }
        Assert-HostTest $safeRun.Succeeded "Hook-suppressed Git invocation failed: $($safeRun.StdErr)"
        Assert-HostTest (-not (Test-Path -LiteralPath $hookSentinel)) 'core.hooksPath=NUL allowed the executable local sentinel hook to run.'
        $sentinelArguments = @($initArguments + $controlArguments + $safeArguments)
        Assert-HostTest (@($sentinelArguments | Where-Object { $_ -in @('clone','fetch','commit','push','pull') }).Count -eq 0) `
            'The local hook sentinel test crossed its no-clone/fetch/commit/network boundary.'
    }

    Invoke-HostTestCase 'ExecutableIdentityRejectsPathShadowAndChangedBinaryBeforeLaunch' {
        $shadowRoot = Join-Path $script:temporaryRoot 'path-shadow'
        [IO.Directory]::CreateDirectory($shadowRoot) | Out-Null
        $shadowGit = Join-Path $shadowRoot 'git.exe'
        Copy-Item -LiteralPath $script:fakeTools.Git -Destination $shadowGit -ErrorAction Stop
        $shadowSentinel = Join-Path $shadowRoot 'launched.sentinel'
        $shadowConfig = Read-SashimiJsonFile $script:configPath
        $shadowConfig.GitExecutable = 'git.exe'
        $shadowConfigPath = Join-Path $shadowRoot 'Config.PathShadow.json'
        Write-HostTestFile $shadowConfigPath (($shadowConfig | ConvertTo-Json -Depth 64) + "`n")
        $previousPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
        $previousLog = [Environment]::GetEnvironmentVariable('SASHIMI_FAKE_TOOL_LOG', 'Process')
        try {
            [Environment]::SetEnvironmentVariable('PATH', $shadowRoot + [IO.Path]::PathSeparator + $previousPath, 'Process')
            [Environment]::SetEnvironmentVariable('SASHIMI_FAKE_TOOL_LOG', $shadowSentinel, 'Process')
            Assert-HostThrows { Import-SashimiHostConfig $shadowConfigPath | Out-Null } 'PATH-resolved names are forbidden'
            Assert-HostTest (-not (Test-Path -LiteralPath $shadowSentinel)) 'A PATH-shadowed Git executable ran while configuration was being rejected.'
        }
        finally {
            [Environment]::SetEnvironmentVariable('PATH', $previousPath, 'Process')
            [Environment]::SetEnvironmentVariable('SASHIMI_FAKE_TOOL_LOG', $previousLog, 'Process')
        }

        $identityRoot = Join-Path $script:temporaryRoot 'executable-identity'
        [IO.Directory]::CreateDirectory($identityRoot) | Out-Null
        $identityConfig = Read-SashimiJsonFile $script:configPath
        $identityConfigPaths = [ordered]@{
            CodexExecutable = (Join-Path $identityRoot 'bound-codex.exe')
            GitExecutable = (Join-Path $identityRoot 'bound-git.exe')
            GitLfsExecutable = (Join-Path $identityRoot 'bound-git-lfs.exe')
            GitHubCli = (Join-Path $identityRoot 'bound-github-cli.exe')
            PowerShellExecutable = $PowerShellPath
            UnityExecutable = (Join-Path $identityRoot 'bound-unity.exe')
        }
        foreach ($name in @('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','UnityExecutable')) {
            Copy-Item -LiteralPath $script:fakeTools.Git -Destination ([string]$identityConfigPaths[$name]) -ErrorAction Stop
        }
        foreach ($name in $identityConfigPaths.Keys) { $identityConfig.$name = [string]$identityConfigPaths[$name] }
        $identityConfigPath = Join-Path $identityRoot 'Config.json'
        Write-HostTestFile $identityConfigPath (($identityConfig | ConvertTo-Json -Depth 64) + "`n")
        $identityEntries = foreach ($name in @('CodexExecutable','GitExecutable','GitLfsExecutable','GitHubCli','PowerShellExecutable','UnityExecutable')) {
            $path = [string]$identityConfigPaths[$name]
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            [ordered]@{
                Name = $name
                Path = $item.FullName
                Length = [int64]$item.Length
                Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
            }
        }
        $identityPath = Join-Path $identityRoot 'ExecutableIdentity.json'
        Write-HostTestFile $identityPath (([ordered]@{ SchemaVersion=1; Executables=@($identityEntries) } | ConvertTo-Json -Depth 16) + "`n")

        $launchSentinel = Join-Path $identityRoot 'changed-binary-launched.sentinel'
        $recordSentinel = Join-Path $identityRoot 'changed-binary-process-record.json'
        try {
            $boundConfig = Import-SashimiHostConfig $identityConfigPath
            Assert-HostTest ([string]$boundConfig.GitExecutable -ceq [string]$identityConfigPaths.GitExecutable) 'Bound config changed the exact Git path.'
            $lfsStream = [IO.File]::Open([string]$identityConfigPaths.GitLfsExecutable, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            try { $lfsStream.WriteByte(0) } finally { $lfsStream.Dispose() }
            Assert-HostThrows {
                Invoke-SashimiHostProcess -FilePath ([string]$identityConfigPaths.GitExecutable) `
                    -ArgumentList @('-c','core.hooksPath=NUL','status','--porcelain=v1') -Kind Git -TimeoutSeconds 30 `
                    -Environment @{ SASHIMI_FAKE_TOOL_LOG=$launchSentinel } | Out-Null
            } 'GitLfsExecutable changed after executable identity verification'
            Assert-HostTest (-not (Test-Path -LiteralPath $launchSentinel)) 'Git started after its bound Git LFS helper changed identity.'
            Copy-Item -LiteralPath $script:fakeTools.Git -Destination ([string]$identityConfigPaths.GitLfsExecutable) -Force -ErrorAction Stop
            $stream = [IO.File]::Open([string]$identityConfigPaths.GitExecutable, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            try { $stream.WriteByte(0) } finally { $stream.Dispose() }
            Assert-HostThrows {
                Invoke-SashimiHostProcess -FilePath ([string]$identityConfigPaths.GitExecutable) `
                    -ArgumentList @('-c','core.hooksPath=NUL','status','--porcelain=v1') -Kind Git -TimeoutSeconds 30 `
                    -InvocationRecordPath $recordSentinel -Environment @{ SASHIMI_FAKE_TOOL_LOG=$launchSentinel } | Out-Null
            } 'changed after executable identity verification'
            Assert-HostTest (-not (Test-Path -LiteralPath $launchSentinel)) 'A hash-changed bound Git executable ran before identity rejection.'
            Assert-HostTest (-not (Test-Path -LiteralPath $recordSentinel)) 'Hash-change rejection wrote a post-launch process record.'
        }
        finally {
            # Return the shared process to source-tree/harness mode with no
            # generated sibling identity so later fixture tools remain usable.
            Import-SashimiHostConfig $script:fakeConfigPath | Out-Null
        }
    }

    Invoke-HostTestCase 'InstallerDryRunHasExactTaskContractAndNoMutation' {
        $schedulerSentinel = Join-Path $script:temporaryRoot 'installer-register-scheduled-task.called'
        $script:systemMutationSentinels.Add($schedulerSentinel)
        $wrapperPath = Join-Path $script:temporaryRoot 'installer-scheduler-sentinel.ps1'
        Write-HostTestFile $wrapperPath @'
param([string]$Target, [string]$Config, [string]$Orchestrator, [string]$Sentinel)
function Register-ScheduledTask {
    [CmdletBinding()]
    param([string]$TaskName, [string]$Xml, [switch]$Force)
    [IO.File]::WriteAllText($Sentinel, 'called', [Text.UTF8Encoding]::new($false))
    throw 'Register-ScheduledTask must not run during DryRun.'
}
& $Target -ConfigPath $Config -OrchestratorPath $Orchestrator -StartBoundary '2026-09-05T09:00:00' -DryRun
'@
        $installer = Invoke-HostTestScript -ScriptPath $wrapperPath -Parameters @{
            Target = (Join-Path $hostRoot 'Install-SashimiHostAutomation.ps1')
            Config = $script:configPath
            Orchestrator = (Join-Path $hostRoot 'Invoke-SashimiHostOrchestrator.ps1')
            Sentinel = $schedulerSentinel
        }
        Assert-HostTest ($installer.ExitCode -eq 0) "Installer DryRun failed: $($installer.StdOut)"
        $json = ConvertFrom-LastHostJson $installer.StdOut
        Assert-HostTest ([bool]$json.Success -and [bool]$json.DryRun -and -not [bool]$json.Changed) 'Installer DryRun mutated or failed.'
        Assert-HostTest ([string]$json.TaskName -ceq 'SASHIMI BOY Host Orchestrator') 'Installer task name changed.'
        Assert-HostTest ([string]$json.UserId -match '(?:^|\\)02031$') 'Installer task identity is not user 02031.'
        Assert-HostTest ([string]$json.LogonType -ceq 'InteractiveToken' -and [string]$json.RunLevel -ceq 'HighestAvailable') 'Installer principal contract changed.'
        Assert-HostTest ([string]$json.MultipleInstances -ceq 'IgnoreNew' -and [string]$json.RepetitionInterval -ceq 'PT15M') 'Installer IgnoreNew/repetition contract changed.'
        Assert-HostTest ([string]$json.PowerShellPath -ceq 'C:\Program Files\PowerShell\7\pwsh.exe') 'Installer does not use stable PowerShell 7.'
        Assert-HostTest ([int]$json.BoundExecutableCount -eq 6) 'Installer did not bind exactly six executable identities.'
        Assert-HostTest ([string]$json.ExecutableIdentityPath -like '*\ExecutableIdentity.json') 'Installer did not report the generated executable identity path.'
        Assert-HostTest (@($json.BundleFiles | Where-Object { [string]$_.RelativePath -ceq 'ExecutableIdentity.json' }).Count -eq 1) `
            'ExecutableIdentity.json is not covered exactly once by the content-addressed bundle manifest.'
        $xml = [string]$json.TaskXml
        foreach ($fragment in @('<LogonType>InteractiveToken</LogonType>', '<RunLevel>HighestAvailable</RunLevel>', '<Interval>PT15M</Interval>', '<StartWhenAvailable>true</StartWhenAvailable>', '<WakeToRun>true</WakeToRun>', '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>')) {
            Assert-HostTest ($xml.Contains($fragment)) "Task XML is missing $fragment."
        }
        Assert-HostTest ($xml -notmatch '(?i)<Password>|/RP\s|--password') 'Task XML contains a password contract.'
        Assert-HostTest (-not (Test-Path -LiteralPath $schedulerSentinel)) 'Installer DryRun called the instrumented Register-ScheduledTask boundary.'
    }

    Invoke-HostTestCase 'UninstallerDryRunPreservesArtifactsAndSchedulerState' {
        $artifact = Join-Path $script:temporaryRoot 'preserved-artifacts\evidence.json'
        Write-HostTestFile $artifact '{"preserve":true}'
        $before = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
        $schedulerQuerySentinel = Join-Path $script:temporaryRoot 'uninstaller-get-scheduled-task.called'
        $schedulerMutationSentinel = Join-Path $script:temporaryRoot 'uninstaller-unregister-scheduled-task.called'
        $script:systemMutationSentinels.Add($schedulerMutationSentinel)
        $wrapperPath = Join-Path $script:temporaryRoot 'uninstaller-scheduler-sentinel.ps1'
        Write-HostTestFile $wrapperPath @'
param([string]$Target, [string]$QuerySentinel, [string]$MutationSentinel)
function Get-ScheduledTask {
    [CmdletBinding()]
    param([string]$TaskName)
    [IO.File]::WriteAllText($QuerySentinel, 'called', [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ TaskName = $TaskName }
}
function Unregister-ScheduledTask {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$TaskName)
    [IO.File]::WriteAllText($MutationSentinel, 'called', [Text.UTF8Encoding]::new($false))
    throw 'Unregister-ScheduledTask must not run during DryRun.'
}
& $Target -DryRun -PreserveArtifacts
'@
        $uninstaller = Invoke-HostTestScript -ScriptPath $wrapperPath -Parameters @{
            Target = (Join-Path $hostRoot 'Uninstall-SashimiHostAutomation.ps1')
            QuerySentinel = $schedulerQuerySentinel
            MutationSentinel = $schedulerMutationSentinel
        }
        Assert-HostTest ($uninstaller.ExitCode -eq 0) "Uninstaller DryRun failed: $($uninstaller.StdOut)"
        $json = ConvertFrom-LastHostJson $uninstaller.StdOut
        Assert-HostTest ([bool]$json.Success -and [bool]$json.DryRun -and -not [bool]$json.Changed) 'Uninstaller DryRun mutated scheduler state.'
        Assert-HostTest ([bool]$json.ArtifactsPreserved) 'Uninstaller did not preserve artifacts.'
        Assert-HostTest (Test-Path -LiteralPath $artifact -PathType Leaf) 'Uninstaller deleted a fixture artifact.'
        Assert-HostTest ((Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash -ceq $before) 'Uninstaller modified a fixture artifact.'
        Assert-HostTest (-not (Test-Path -LiteralPath $schedulerQuerySentinel)) 'Uninstaller DryRun queried Task Scheduler despite its no-access contract.'
        Assert-HostTest (-not (Test-Path -LiteralPath $schedulerMutationSentinel)) 'Uninstaller DryRun called the instrumented Unregister-ScheduledTask boundary.'
    }

    Invoke-HostTestCase 'PendingCommandAndNaturalLanguageRemainInert' {
        $sentinel = Join-Path $script:temporaryRoot 'pending-command-executed.txt'
        $body = @"
<!-- sashimi-boy-automation-handoff:v1
mode: DeliveryResume
issue: 5203
pr: 6203
head: cccccccccccccccccccccccccccccccccccccccc
sourceRole: Developer
reason: required-check-transient
findingUrl: $([string]::Empty)
pendingCommand: Set-Content -LiteralPath '$sentinel' -Value unsafe
-->
"@
        $handoff = ConvertFrom-SashimiHandoffMarker $body
        Assert-HostTest ($null -ne $handoff) 'Safe parser rejected the fixture handoff.'
        Assert-HostTest ([string]$handoff.PendingCommand -match 'Set-Content') 'pendingCommand was not retained as evidence.'
        Assert-HostTest (-not (Test-Path -LiteralPath $sentinel)) 'pendingCommand was executed.'
        $production = @(Get-ChildItem -LiteralPath $hostRoot -Filter '*.ps1' -File | Where-Object Name -ne 'Test-SashimiHostAutomation.ps1')
        foreach ($file in $production) {
            $content = [IO.File]::ReadAllText($file.FullName)
            Assert-HostTest ($content -notmatch '(?i)Invoke-Expression|\biex\b|ScriptBlock\]::Create') "Unsafe dynamic evaluation exists in $($file.Name)."
        }
    }

    Invoke-HostTestCase 'ForbiddenAuthorityIsEnforced' {
        Assert-HostTest (Assert-SashimiTransition Developer 'Ready' 'In Progress') 'Developer Ready transition was rejected.'
        Assert-HostTest (Assert-SashimiTransition Developer 'In Progress' 'Review') 'Developer Review transition was rejected.'
        Assert-HostTest (Assert-SashimiTransition Reviewer 'Review' 'In Progress') 'Reviewer finding transition was rejected.'
        Assert-HostTest (Assert-SashimiTransition Reviewer 'Review' 'Verification') 'Reviewer verification transition was rejected.'
        Assert-HostThrows { Assert-SashimiTransition Developer 'In Progress' 'Verification' } 'Forbidden'
        Assert-HostThrows { Assert-SashimiTransition Reviewer 'Review' 'Done' } 'Done'
        Assert-HostThrows { Assert-SashimiSafeCommand git @('reset', '--hard') Git } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand git @('clean', '-fdx') Git } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand git @('rebase', 'origin/main') Git } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand git @('push', '--force', 'origin', 'HEAD:main') Git } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand gh @('pr', 'merge', '6204') GitHub } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand gh @('issue', 'close', '5204') GitHub } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand codex @('exec', '--dangerously-bypass-approvals-and-sandbox') Codex } 'Forbidden'
        Assert-HostThrows { Assert-SashimiSafeCommand codex @('exec', '-s', 'danger-full-access') Codex } 'Forbidden'
    }

    Invoke-HostTestCase 'SecretsProfileAndSavePathsAreRedacted' {
        $profile = [Environment]::GetFolderPath('UserProfile')
        $input = "Bearer ghp_1234567890abcdef token=plain-secret $profile\AppData\LocalLow\Studio\SaveData\slot1.json"
        $protected = Protect-SashimiText $input
        Assert-HostTest ($protected -notmatch 'ghp_1234567890abcdef|plain-secret') 'A secret survived redaction.'
        Assert-HostTest ([string]::IsNullOrWhiteSpace($profile) -or $protected.IndexOf($profile, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'User-profile path survived redaction.'
        Assert-HostTest ($protected -match '\[REDACTED_') 'Redaction did not leave an explicit marker.'
    }

    Invoke-HostTestCase 'CleanupFailurePreservesEvidence' {
        $runRoot = Join-Path $script:temporaryRoot 'cleanup-failure-runs'
        $run = New-SashimiRunWorkspace -RunRoot $runRoot -RunId ('20260905T000000Z-' + ('d' * 32))
        $evidence = Join-Path $run.ArtifactsPath 'evidence.json'
        Write-SashimiUtf8File $evidence '{"retained":true}'
        Write-SashimiUtf8File $run.MarkerPath '{"SchemaVersion":1,"RunId":"wrong","RunPath":"wrong"}'
        $cleanup = Remove-SashimiRunRepository -RunPath $run.RunPath -RunRoot $runRoot
        Assert-HostTest (-not $cleanup.Success -and $cleanup.Preserved) 'Cleanup ownership failure did not report preserved evidence.'
        Assert-HostTest (Test-Path -LiteralPath $evidence -PathType Leaf) 'Cleanup failure deleted the evidence artifact.'

        # Windows denies deletion of a file held without FILE_SHARE_DELETE. This
        # exercises the real Remove-Item catch path with a valid ownership marker.
        $lockedRun = New-SashimiRunWorkspace -RunRoot $runRoot -RunId ('20260905T000001Z-' + ('e' * 32))
        $lockedEvidence = Join-Path $lockedRun.ArtifactsPath 'evidence.json'
        $lockedFile = Join-Path $lockedRun.RepositoryPath 'locked.txt'
        Write-SashimiUtf8File $lockedEvidence '{"retained":true,"failure":"delete-lock"}'
        Write-SashimiUtf8File $lockedFile 'held without delete sharing'
        $lockStream = [IO.File]::Open($lockedFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $lockedCleanup = Remove-SashimiRunRepository -RunPath $lockedRun.RunPath -RunRoot $runRoot
            Assert-HostTest (-not $lockedCleanup.Success -and $lockedCleanup.Preserved -and -not $lockedCleanup.Removed) 'Valid-marker locked-file deletion did not fail closed and preserve the run.'
            Assert-HostTest (-not [string]::IsNullOrWhiteSpace([string]$lockedCleanup.Error)) 'Valid-marker deletion failure did not retain its exact error evidence.'
            Assert-HostTest (Test-Path -LiteralPath $lockedEvidence -PathType Leaf) 'Valid-marker deletion failure removed the evidence artifact.'
            Assert-HostTest (Test-Path -LiteralPath $lockedRun.RepositoryPath -PathType Container) 'Valid-marker deletion failure removed the locked repository directory.'
        }
        finally {
            $lockStream.Dispose()
        }
    }

    Invoke-HostTestCase 'FixtureIsolationHasNoLiveIssueMutation' {
        $testFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse)
        foreach ($file in $testFiles) {
            $content = [IO.File]::ReadAllText($file.FullName)
            Assert-HostTest ($content -notmatch '(?i)github\.com/DongGyunLeeeee/sashimi-boy-unity/(?:issues|pull)/(?:20|26|30)(?:\D|$)') "Fixture references protected live work in $($file.Name)."
        }
        foreach ($invocation in $script:fixtureInvocations) {
            $serialized = $invocation | ConvertTo-Json -Depth 16 -Compress
            Assert-HostTest ($serialized -notmatch '(?i)(?:issues|pull)[/\\](?:20|26|30)(?:\D|$)') 'A fixture invocation targeted protected live work.'
        }
        $fakeRecords = @(Get-HostFakeToolAudit $script:fakeToolLogPath)
        $externalFakeRecords = @($fakeRecords | Where-Object ExternalMutation)
        $triggeredSchedulerMutations = @($script:systemMutationSentinels | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
        $script:mutationAudit = [pscustomobject][ordered]@{
            FakeInvocationCount = $fakeRecords.Count
            SimulatedMutationCount = @($fakeRecords | Where-Object SimulatedMutation).Count
            ExternalFakeMutationCount = $externalFakeRecords.Count
            SchedulerMutationSentinelCount = $triggeredSchedulerMutations.Count
            ExternalMutationCount = $externalFakeRecords.Count + $triggeredSchedulerMutations.Count
        }
        Assert-HostTest ($fakeRecords.Count -gt 0) 'No fake executable invocation was captured; the mutation audit did not exercise its boundary.'
        if ([string]::IsNullOrWhiteSpace($TestNamePattern)) {
            Assert-HostTest ($script:mutationAudit.SimulatedMutationCount -gt 0) 'No simulated mutation was captured; mutation-boundary assertions could be vacuous.'
        }
        Assert-HostTest ($script:mutationAudit.ExternalMutationCount -eq 0) "Fixture audit observed $($script:mutationAudit.ExternalMutationCount) external mutation(s)."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SASHIMI_BOY_HOST_AUTOMATION_TEST_HARNESS', $previousHarnessMode, 'Process')
    if ($script:ownedTemporaryRoot -and -not $KeepTemporaryFiles) {
        Remove-HostTestRoot $script:temporaryRoot
    }
}

$failed = @($script:results | Where-Object { -not $_.Passed })
$externalMutationCount = if ($null -eq $script:mutationAudit) { -1 } else { [int]$script:mutationAudit.ExternalMutationCount }
$summary = [ordered]@{
    SchemaVersion = 1
    Success = ($failed.Count -eq 0)
    PowerShell = $PSVersionTable.PSVersion.ToString()
    RepositoryRoot = $RepositoryRoot
    Passed = @($script:results | Where-Object Passed).Count
    Failed = $failed.Count
    Tests = $script:results.ToArray()
    TemporaryRoot = if ($KeepTemporaryFiles) { $script:temporaryRoot } else { $null }
    ExternalMutationCount = $externalMutationCount
    MutationAudit = $script:mutationAudit
}
[Console]::Out.WriteLine((ConvertTo-SashimiJson $summary))
if ($failed.Count -gt 0) { exit 1 }
exit 0
