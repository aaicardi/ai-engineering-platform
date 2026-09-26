using AIHarness.Execution;
using AIHarness.Orchestration;
using AIHarness.Prompting;

namespace AIHarness.Tests;

/// <summary>Replays canned results per command line (<c>"git status ..."</c>) and records every launch.</summary>
internal sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, (int ExitCode, string Stdout)> _script = [];

    public List<ProcessRequest> Requests { get; } = [];

    public IEnumerable<string> CommandLines => Requests.Select(CommandLine);

    public ScriptedProcessRunner On(string commandLine, int exitCode = 0, string stdout = "")
    {
        _script[commandLine] = (exitCode, stdout);
        return this;
    }

    public Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        // Unscripted commands succeed silently.
        var (exitCode, stdout) = _script.GetValueOrDefault(CommandLine(request), (0, ""));
        if (stdout.Length > 0)
            onStandardOutput(stdout);
        return Task.FromResult(exitCode);
    }

    public static string CommandLine(ProcessRequest request) => string.Join(' ', request.Arguments.Prepend(request.FileName));
}

public sealed class AgentOrchestratorTests : IDisposable
{
    private const string SpecFileName = "008-feat-orchestration-add-git-automation.md";
    private const string Branch = "feature/27-feat-orchestration-add-git-automation";

    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-orch-").FullName;
    private readonly PromptContext _context = new("developer", SpecFileName, "# Prompt unificado");
    private readonly StringWriter _error = new();

    public AgentOrchestratorTests()
    {
        var specs = Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(specs.FullName, SpecFileName),
            "# OpenSpec 008: feat(orchestration): Add Git Automation\n\n## Issue Reference\nCloses #27\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Happy path: on main, branch does not exist yet, one commit produced by the agent.
    private static ScriptedProcessRunner HappyPath() => new ScriptedProcessRunner()
        .On("git rev-parse --abbrev-ref HEAD", stdout: "main\n")
        .On($"git rev-parse --verify --quiet refs/heads/{Branch}", exitCode: 1)
        .On("git rev-list --count main..HEAD", stdout: "1\n");

    private Task<OrchestrationResult> RunAsync(IProcessRunner runner, OrchestrationOptions? options = null) =>
        new AgentOrchestrator(runner, _root, TextWriter.Null, _error, options).RunAsync(_context);

    [Fact]
    public async Task RunAsync_HappyPath_RunsStagesInOrderAndOnlyPreparesThePullRequest()
    {
        var runner = HappyPath();

        var result = await RunAsync(runner);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Branch, result.Branch);
        Assert.Equal(
            [
                "git status --porcelain --untracked-files=no",
                "git rev-parse --abbrev-ref HEAD",
                $"git rev-parse --verify --quiet refs/heads/{Branch}",
                $"git checkout -b {Branch}",
                "claude -p",
                "dotnet test",
                "git rev-list --count main..HEAD",
            ],
            runner.CommandLines);
        Assert.All(runner.Requests, r => Assert.Equal(_root, r.WorkingDirectory));
        // Without --create-pr nothing is pushed: the commands are printed for the operator.
        Assert.Contains($"git push --set-upstream origin {Branch}", _error.ToString());
        Assert.Contains($"gh pr create --base main --head {Branch}", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_DirtyWorkingTree_StopsBeforeTouchingBranches()
    {
        var runner = HappyPath().On("git status --porcelain --untracked-files=no", stdout: " M src/Program.cs\n");

        var result = await RunAsync(runner);

        Assert.Equal(OrchestrationStage.Preflight, result.Stage);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["git status --porcelain --untracked-files=no"], runner.CommandLines);
    }

    [Fact]
    public async Task RunAsync_NotAGitRepository_FailsInPreflight()
    {
        var runner = HappyPath().On("git status --porcelain --untracked-files=no", exitCode: 128);

        var result = await RunAsync(runner);

        Assert.Equal(OrchestrationStage.Preflight, result.Stage);
        Assert.Contains("git status", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_SpecWithoutIssueReference_FailsWithoutRunningCommands()
    {
        File.WriteAllText(Path.Combine(_root, "openspec", "specs", SpecFileName), "# OpenSpec 008: sin issue\n");
        var runner = HappyPath();

        var result = await RunAsync(runner);

        Assert.Equal(OrchestrationStage.Preflight, result.Stage);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task RunAsync_ExistingBranch_IsCheckedOutInsteadOfCreated()
    {
        var runner = HappyPath().On($"git rev-parse --verify --quiet refs/heads/{Branch}", exitCode: 0);

        await RunAsync(runner);

        Assert.Contains($"git checkout {Branch}", runner.CommandLines);
        Assert.DoesNotContain($"git checkout -b {Branch}", runner.CommandLines);
    }

    [Fact]
    public async Task RunAsync_AlreadyOnFeatureBranch_SkipsCheckout()
    {
        var runner = HappyPath().On("git rev-parse --abbrev-ref HEAD", stdout: Branch + "\n");

        var result = await RunAsync(runner);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(runner.CommandLines, c => c.StartsWith("git checkout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_CheckoutFails_DoesNotRunAgent()
    {
        var runner = HappyPath().On($"git checkout -b {Branch}", exitCode: 128);

        var result = await RunAsync(runner);

        Assert.Equal(OrchestrationStage.Branch, result.Stage);
        Assert.Equal(128, result.ExitCode);
        Assert.DoesNotContain("claude -p", runner.CommandLines);
    }

    [Fact]
    public async Task RunAsync_AgentFails_SkipsTestsAndPullRequest()
    {
        var runner = HappyPath().On("claude -p", exitCode: 3);

        var result = await RunAsync(runner);

        Assert.Equal(OrchestrationStage.Agent, result.Stage);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("claude -p", runner.CommandLines.Last());
    }

    [Fact]
    public async Task RunAsync_AgentReceivesThePrompt_AfterBranchCheckout()
    {
        var runner = HappyPath();

        await RunAsync(runner);

        var agent = Assert.Single(runner.Requests, r => r.FileName == "claude");
        Assert.Equal(_context.Prompt, agent.StandardInput);
    }

    [Fact]
    public async Task RunAsync_TestsFail_DoesNotPreparePullRequest()
    {
        var runner = HappyPath().On("dotnet test", exitCode: 1);

        var result = await RunAsync(runner, new OrchestrationOptions(CreatePullRequest: true));

        Assert.Equal(OrchestrationStage.Tests, result.Stage);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.CommandLines, c => c.StartsWith("git push", StringComparison.Ordinal) || c.StartsWith("gh ", StringComparison.Ordinal));
        Assert.DoesNotContain("gh pr create", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_CreatePullRequest_PushesAndRunsGhPrCreate()
    {
        var runner = HappyPath().On("git rev-list --count develop..HEAD", stdout: "2");

        var result = await RunAsync(runner, new OrchestrationOptions("develop", CreatePullRequest: true));

        Assert.True(result.Succeeded);
        var commands = runner.CommandLines.ToList();
        Assert.Equal($"git push --set-upstream origin {Branch}", commands[^2]);
        var pr = runner.Requests[^1];
        Assert.Equal("gh", pr.FileName);
        Assert.Equal(
            ["pr", "create", "--base", "develop", "--head", Branch, "--title", "feat(orchestration): Add Git Automation",
             "--body", $"Closes #27\n\nImplementa `openspec/specs/{SpecFileName}` (generado por AI Harness)."],
            pr.Arguments);
    }

    [Fact]
    public async Task RunAsync_PushFails_DoesNotCreatePullRequest()
    {
        var runner = HappyPath().On($"git push --set-upstream origin {Branch}", exitCode: 1);

        var result = await RunAsync(runner, new OrchestrationOptions(CreatePullRequest: true));

        Assert.Equal(OrchestrationStage.PullRequest, result.Stage);
        Assert.DoesNotContain(runner.Requests, r => r.FileName == "gh");
    }

    [Fact]
    public async Task RunAsync_NoCommitsAhead_SkipsPullRequestWithWarning()
    {
        var runner = HappyPath().On("git rev-list --count main..HEAD", stdout: "0");

        var result = await RunAsync(runner, new OrchestrationOptions(CreatePullRequest: true));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(runner.Requests, r => r.FileName == "gh" || r.Arguments.FirstOrDefault() == "push");
        Assert.Contains("[WARN]", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_Returns127()
    {
        var runner = new ThrowingProcessRunner();

        var result = await new AgentOrchestrator(runner, _root, TextWriter.Null, _error).RunAsync(_context);

        Assert.Equal(OrchestrationStage.Preflight, result.Stage);
        Assert.Equal(127, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_NullContext_Throws()
    {
        var orchestrator = new AgentOrchestrator(HappyPath(), _root, TextWriter.Null, TextWriter.Null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.RunAsync(null!));
    }

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        public Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default) =>
            throw new ExecutableNotFoundException(request.FileName, new System.ComponentModel.Win32Exception(2));
    }
}
