using AIHarness.Execution;
using AIHarness.GitHub;
using AIHarness.Orchestration;
using AIHarness.Prompting;

namespace AIHarness.Tests;

/// <summary>
/// <see cref="ScriptedProcessRunner"/> whose <c>claude</c> launches write the agent's report into the repository, and
/// that records whether <c>.aiharness/</c> still existed when changes were staged.
/// </summary>
internal sealed class ReportingProcessRunner(ScriptedProcessRunner inner, string root, string? report, int claudeExitCode = 0) : IProcessRunner
{
    public bool ReportDirectoryExistedAtStaging { get; private set; }

    public async Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default)
    {
        if (request.FileName == "git" && request.Arguments.FirstOrDefault() == "add")
            ReportDirectoryExistedAtStaging |= Directory.Exists(Path.Combine(root, RunReport.DirectoryName));
        var exitCode = await inner.RunAsync(request, onStandardOutput, onStandardError, cancellationToken);
        if (request.FileName != "claude")
            return exitCode;
        if (report is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, RunReport.DirectoryName));
            File.WriteAllText(Path.Combine(root, RunReport.RelativePath), report);
        }
        return claudeExitCode;
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
        .On("git rev-list --count main..HEAD", stdout: "1\n")
        .On("git rev-parse --git-path info/exclude", stdout: ".git/info/exclude\n");

    private ReportingProcessRunner? _lastRunner;

    private async Task<(OrchestrationResult Result, ScriptedProcessRunner Runner, AgentSession? Session)> RunAsync(
        string? report, bool createPr = false, string agent = "lead", bool withSession = true, int claudeExitCode = 0,
        Action<ScriptedProcessRunner>? script = null)
    {
        var scripted = HappyPath();
        script?.Invoke(scripted);
        AgentSession? session = null;
        var options = new OrchestrationOptions(CreatePullRequest: createPr, Repository: new GitHubRepository("acme", "shop"),
            SessionFactory: withSession ? () => session = _suite.Prepare(agent) : null);
        _lastRunner = new ReportingProcessRunner(scripted, Root, report, claudeExitCode);
        var orchestrator = new AgentOrchestrator(_lastRunner, Root, TextWriter.Null, _error, options);
        var result = await orchestrator.RunAsync(new PromptContext(agent, SpecFileName, "# Prompt unificado"));
        return (result, scripted, session);
    }

    private bool ReportDirectoryExists => Directory.Exists(Path.Combine(Root, RunReport.DirectoryName));

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
        Assert.False(ReportDirectoryExists);
        Assert.False(_lastRunner!.ReportDirectoryExistedAtStaging);
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
    public async Task MissingReportFromLead_StopsWithExitCode3()
    {
        var (result, runner, _) = await RunAsync(report: null);

        Assert.Equal(OrchestrationStage.Report, result.Stage);
        Assert.Equal(AgentOrchestrator.BlockedExitCode, result.ExitCode);
        Assert.DoesNotContain(runner.CommandLines, c => c.StartsWith("git add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingReportFromASingleSpecialist_ContinuesWithTheDefaultDescription()
    {
        var (result, _, session) = await RunAsync(report: null, agent: "developer");

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("Informe de ejecución", File.ReadAllText(Path.Combine(session!.RunDirectory, "pr-body.md")));
    }

    [Theory]
    [InlineData("status: COMPLETED | BLOCKED\n\n## Summary\nx")]
    [InlineData("## Summary\nsin estado")]
    public async Task UnrecognizedStatus_StopsWithExitCode3InsteadOfPublishing(string report)
    {
        var (result, runner, _) = await RunAsync(report, createPr: true);

        Assert.Equal(OrchestrationStage.Report, result.Stage);
        Assert.Equal(AgentOrchestrator.BlockedExitCode, result.ExitCode);
        Assert.DoesNotContain(runner.Requests, r => r.FileName == "gh");
    }

    [Fact]
    public async Task AgentFailsAfterWritingTheReport_LeavesNoReportDirectory()
    {
        var (result, _, _) = await RunAsync(CompletedReport, claudeExitCode: 1);

        Assert.Equal(OrchestrationStage.Agent, result.Stage);
        Assert.False(ReportDirectoryExists);
    }

    [Fact]
    public async Task StaleReportFromAnEarlierRun_IsArchivedAndNeverRead()
    {
        Directory.CreateDirectory(Path.Combine(Root, RunReport.DirectoryName));
        File.WriteAllText(Path.Combine(Root, RunReport.RelativePath), "status: COMPLETED\n\n## Summary\nviejo");

        var (result, _, session) = await RunAsync(report: null, agent: "developer");

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("viejo", File.ReadAllText(Path.Combine(session!.RunDirectory, "pr-body.md")));
        Assert.True(File.Exists(Path.Combine(session.RunDirectory, "previous-aiharness", "run-report.md")));
        Assert.False(ReportDirectoryExists);
    }

    [Fact]
    public async Task WithoutSession_AReportOnDiskIsIgnoredAndKept()
    {
        Directory.CreateDirectory(Path.Combine(Root, RunReport.DirectoryName));
        File.WriteAllText(Path.Combine(Root, RunReport.RelativePath), "status: BLOCKED\n");

        var (result, runner, _) = await RunAsync(report: null, agent: "developer", withSession: false);

        Assert.True(result.Succeeded);
        Assert.Equal(["-p"], runner.Requests.Single(r => r.FileName == "claude").Arguments);
        Assert.True(ReportDirectoryExists);
    }

    [Fact]
    public async Task VersionedReportDirectory_FailsBeforeRunningTheAgent()
    {
        var (result, runner, _) = await RunAsync(CompletedReport,
            script: r => r.On($"git ls-files -- {RunReport.DirectoryName}", stdout: ".aiharness/run-report.md\n"));

        Assert.Equal(OrchestrationStage.Agent, result.Stage);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Requests, r => r.FileName == "claude");
    }

    [Fact]
    public async Task ReportWithSecretLikeText_IsKeptOutOfThePullRequest()
    {
        var (result, _, session) = await RunAsync(CompletedReport + "token = ghp_abcdefghijklmnopqrstuvwxyz0123\n");

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("ghp_", File.ReadAllText(Path.Combine(session!.RunDirectory, "pr-body.md")));
        Assert.Contains("forma de credencial", _error.ToString());
    }

    [Fact]
    public async Task ReportClosingKeywordsAndMentions_AreNeutralizedInThePullRequest()
    {
        var (_, _, session) = await RunAsync(CompletedReport + "Fixes #12, closes acme/other#3. cc @octocat\n");

        var body = File.ReadAllText(Path.Combine(session!.RunDirectory, "pr-body.md"));
        Assert.StartsWith("Closes #42", body);
        Assert.DoesNotContain("Fixes #12", body, StringComparison.Ordinal);
        Assert.DoesNotContain("acme/other#3", body, StringComparison.Ordinal);
        Assert.DoesNotContain("@octocat", body, StringComparison.Ordinal);
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
