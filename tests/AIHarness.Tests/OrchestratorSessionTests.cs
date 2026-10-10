using AIHarness.Execution;
using AIHarness.GitHub;
using AIHarness.Orchestration;
using AIHarness.Prompting;

namespace AIHarness.Tests;

/// <summary><see cref="ScriptedProcessRunner"/> whose <c>claude</c> launches write the agent's report into the repository.</summary>
internal sealed class ReportingProcessRunner(ScriptedProcessRunner inner, string root, string? report) : IProcessRunner
{
    public Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default)
    {
        if (request.FileName == "claude" && report is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, RunReport.DirectoryName));
            File.WriteAllText(Path.Combine(root, RunReport.RelativePath), report);
        }
        return inner.RunAsync(request, onStandardOutput, onStandardError, cancellationToken);
    }
}

public sealed class OrchestratorSessionTests : IDisposable
{
    private const string SpecFileName = "014-feat-export.md";
    private const string Branch = "feature/42-feat-export";

    private readonly SuiteFixture _suite = new();
    private readonly StringWriter _error = new();
    private string Root => _suite.Target;

    public OrchestratorSessionTests()
    {
        var specs = Directory.CreateDirectory(Path.Combine(Root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(specs.FullName, SpecFileName), "# OpenSpec 014: feat: Export\n\n## Issue Reference\nCloses #42\n");
        File.WriteAllText(Path.Combine(Root, "CLAUDE.md"), "# Governance\n");
        File.WriteAllText(Path.Combine(Root, "App.sln"), "");
        Directory.CreateDirectory(Path.Combine(Root, ".git"));
    }

    public void Dispose() => _suite.Dispose();

    private static ScriptedProcessRunner HappyPath() => new ScriptedProcessRunner()
        .On("git rev-parse --abbrev-ref HEAD", stdout: "main\n")
        .On($"git rev-parse --verify --quiet refs/heads/{Branch}", exitCode: 1)
        .On("git status --porcelain", stdout: " M src/Export.cs\n")
        .On("git rev-list --count main..HEAD", stdout: "1\n");

    private async Task<(OrchestrationResult Result, ScriptedProcessRunner Runner, AgentSession? Session)> RunAsync(
        string? report, bool createPr = false)
    {
        var scripted = HappyPath();
        AgentSession? session = null;
        var options = new OrchestrationOptions(CreatePullRequest: createPr, Repository: new GitHubRepository("acme", "shop"),
            SessionFactory: () => session = _suite.Prepare());
        var orchestrator = new AgentOrchestrator(new ReportingProcessRunner(scripted, Root, report), Root, TextWriter.Null, _error, options);
        var result = await orchestrator.RunAsync(new PromptContext("lead", SpecFileName, "# Prompt unificado"));
        return (result, scripted, session);
    }

    private const string CompletedReport = "status: COMPLETED\n\n## Summary\nExporta a CSV.\n\n## Open Findings\n- LOW · CR-1 · a.cs:3 — nombre\n";

    [Fact]
    public async Task Session_RunsClaudeWithTheSessionArgumentsAndOnlyTheTask()
    {
        var (result, runner, session) = await RunAsync(CompletedReport);

        Assert.True(result.Succeeded);
        var claude = runner.Requests.Single(r => r.FileName == "claude");
        Assert.Equal(session!.CliArguments, claude.Arguments);
        Assert.Equal(Root, claude.WorkingDirectory);
        Assert.Contains($"openspec/specs/{SpecFileName}", claude.StandardInput);
        Assert.Contains("requires_issue: true", claude.StandardInput);
        Assert.Contains("Rama base: main", claude.StandardInput);
        Assert.DoesNotContain("# Prompt unificado", claude.StandardInput);
        Assert.Equal(claude.StandardInput, File.ReadAllText(Path.Combine(session.RunDirectory, "task.md")));
    }

    [Fact]
    public async Task Session_ExcludesTheReportDirectoryFromGitBeforeTheAgentRuns()
    {
        await RunAsync(CompletedReport);

        Assert.Contains(".aiharness/", File.ReadAllLines(Path.Combine(Root, ".git", "info", "exclude")));
    }

    [Fact]
    public async Task CompletedReport_IsArchivedRemovedAndAddedToThePullRequestBody()
    {
        var (result, runner, session) = await RunAsync(CompletedReport);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(Root, RunReport.DirectoryName)));
        Assert.Equal(CompletedReport, File.ReadAllText(Path.Combine(session!.RunDirectory, "run-report.md")));

        var body = File.ReadAllText(Path.Combine(session.RunDirectory, "pr-body.md"));
        Assert.StartsWith("Closes #42", body);
        Assert.Contains("## Informe de ejecución", body);
        Assert.Contains("- LOW · CR-1 · a.cs:3 — nombre", body);
        Assert.DoesNotContain("status: COMPLETED", body);
        // Without --create-pr the printed command reads the same file.
        Assert.Contains($"--body-file {Path.Combine(session.RunDirectory, "pr-body.md")}", _error.ToString());
    }

    [Fact]
    public async Task CompletedReport_WithCreatePr_OpensThePullRequestFromTheBodyFile()
    {
        var (result, runner, session) = await RunAsync(CompletedReport, createPr: true);

        Assert.True(result.Succeeded);
        var pr = runner.Requests.Single(r => r.FileName == "gh");
        Assert.Contains("--body-file", pr.Arguments);
        Assert.Contains(Path.Combine(session!.RunDirectory, "pr-body.md"), pr.Arguments);
        Assert.DoesNotContain("--body", pr.Arguments);
    }

    [Fact]
    public async Task BlockedReport_StopsAtReportWithExitCode3WithoutCommitTestsOrPullRequest()
    {
        var (result, runner, _) = await RunAsync("status: BLOCKED\n\n## Blockers\nFalta definir el formato del CSV.\n");

        Assert.Equal(OrchestrationStage.Report, result.Stage);
        Assert.Equal(AgentOrchestrator.BlockedExitCode, result.ExitCode);
        Assert.Equal(Branch, result.Branch);
        Assert.DoesNotContain(runner.CommandLines, c => c.StartsWith("git add", StringComparison.Ordinal) || c.StartsWith("git commit", StringComparison.Ordinal));
        Assert.DoesNotContain("dotnet test", runner.CommandLines);
        Assert.DoesNotContain(runner.Requests, r => r.FileName == "gh");
        Assert.Contains("Falta definir el formato del CSV.", _error.ToString());
        Assert.False(Directory.Exists(Path.Combine(Root, RunReport.DirectoryName)));
    }

    [Fact]
    public async Task MissingReport_WarnsAndContinuesWithTheDefaultDescription()
    {
        var (result, _, session) = await RunAsync(report: null);

        Assert.True(result.Succeeded);
        Assert.Contains("[WARN] El agente no dejó el informe", _error.ToString());
        Assert.DoesNotContain("Informe de ejecución", File.ReadAllText(Path.Combine(session!.RunDirectory, "pr-body.md")));
    }

    [Fact]
    public async Task SessionWarnings_AreShownToTheOperator()
    {
        Directory.CreateDirectory(Path.Combine(Root, ".claude"));
        File.WriteAllText(Path.Combine(Root, ".claude", "settings.json"), "{}");

        await RunAsync(CompletedReport);

        Assert.Contains("[WARN] Se ignora la configuración de Claude Code del repositorio destino", _error.ToString());
    }
}
