using AIHarness.Diagnostics;
using AIHarness.Execution;
using AIHarness.Inspection;

namespace AIHarness.Tests;

/// <summary>
/// Like <see cref="ScriptedProcessRunner"/> but can also script an <see cref="ExecutableNotFoundException"/> or a
/// command that never completes, to exercise <see cref="HealthDiagnostics"/>'s timeout and PATH handling.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, Func<CancellationToken, Action<string>, Task<int>>> _script = [];

    public List<ProcessRequest> Requests { get; } = [];

    public FakeProcessRunner On(string commandLine, int exitCode = 0, string stdout = "")
    {
        _script[commandLine] = (_, onStandardOutput) =>
        {
            if (stdout.Length > 0)
                onStandardOutput(stdout);
            return Task.FromResult(exitCode);
        };
        return this;
    }

    public FakeProcessRunner NotFound(string commandLine)
    {
        _script[commandLine] = (_, _) => throw new ExecutableNotFoundException(commandLine.Split(' ')[0], new InvalidOperationException("not installed"));
        return this;
    }

    public FakeProcessRunner Hang(string commandLine)
    {
        _script[commandLine] = async (ct, _) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        };
        return this;
    }

    public Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var commandLine = ScriptedProcessRunner.CommandLine(request);
        return _script.TryGetValue(commandLine, out var handler)
            ? handler(cancellationToken, onStandardOutput)
            : Task.FromResult(0);
    }
}

public sealed class HealthDiagnosticsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-health-").FullName;
    private readonly string _agents;
    private readonly StringWriter _error = new();

    public HealthDiagnosticsTests()
    {
        _agents = Directory.CreateDirectory(Path.Combine(_root, ".claude", "agents")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Governance\n## 1. Contexto\n");
        File.WriteAllText(Path.Combine(_root, "openspec", "specs", "001-demo.md"), "# OpenSpec 001: Demo\n");
        foreach (var name in RepositoryInspector.ExpectedAgents)
            File.WriteAllText(Path.Combine(_agents, name + ".md"), $"---\nname: {name}\ndescription: Agente {name}\n---\n# Agent\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static FakeProcessRunner AllToolsAvailable() => new FakeProcessRunner()
        .On("git --version").On("dotnet --version").On("claude --version")
        .On("claude auth status").On("gh --version").On("gh auth status");

    private HealthDiagnostics Create(IProcessRunner processRunner, Func<string, string?>? getEnvironmentVariable = null, TimeSpan? timeout = null) =>
        new(processRunner, _root, _error, getEnvironmentVariable, timeout);

    private static HealthCheck Find(HealthReport report, string name) => report.Checks.Single(c => c.Name == name);

    [Fact]
    public async Task RunAsync_FullGovernanceAndTools_ReportsHealthy()
    {
        var report = await Create(AllToolsAvailable(), _ => null).RunAsync();

        Assert.True(report.Healthy);
        Assert.All(report.Checks, c => Assert.True(c.Ok));
    }

    [Fact]
    public async Task RunAsync_MissingClaudeMd_ReportsCriticalFailureAndUnhealthy()
    {
        File.Delete(Path.Combine(_root, "CLAUDE.md"));

        var report = await Create(AllToolsAvailable(), _ => null).RunAsync();

        var claudeMd = Find(report, "CLAUDE.md");
        Assert.Equal(HealthCheckLevel.Critical, claudeMd.Level);
        Assert.False(claudeMd.Ok);
        Assert.False(report.Healthy);
    }

    [Fact]
    public async Task RunAsync_ClaudeNotInPath_ReportsCriticalFailure()
    {
        var runner = AllToolsAvailable();
        // Overwrite the scripted success with a PATH failure.
        runner.NotFound("claude --version");

        var report = await Create(runner, _ => null).RunAsync();

        var claude = Find(report, "claude");
        Assert.Equal(HealthCheckLevel.Critical, claude.Level);
        Assert.False(claude.Ok);
        Assert.Equal("no encontrado en PATH", claude.Detail);
        Assert.False(report.Healthy);
    }

    [Fact]
    public async Task RunAsync_NoGhAndNoCredentials_AreWarningsAndStayHealthy()
    {
        var runner = new FakeProcessRunner()
            .On("git --version").On("dotnet --version").On("claude --version").On("claude auth status")
            .NotFound("gh --version").NotFound("gh auth status");

        var report = await Create(runner, _ => null).RunAsync();

        var gh = Find(report, "gh");
        var credentials = Find(report, "Credenciales de GitHub");
        Assert.Equal(HealthCheckLevel.Warning, gh.Level);
        Assert.False(gh.Ok);
        Assert.Equal(HealthCheckLevel.Warning, credentials.Level);
        Assert.False(credentials.Ok);
        Assert.True(report.Healthy);
    }

    [Theory]
    [InlineData("GH_TOKEN", "ghp_xxxxxxxxxxxxxxxxxxxx")]
    [InlineData("GITHUB_TOKEN", "ghs_yyyyyyyyyyyyyyyyyyyy")]
    public async Task RunAsync_GitHubTokenEnvironmentVariable_ReportsSourceNotValueAndSkipsGhAuthStatus(string variable, string tokenValue)
    {
        var runner = AllToolsAvailable();

        var report = await Create(runner, name => name == variable ? tokenValue : null).RunAsync();

        var credentials = Find(report, "Credenciales de GitHub");
        Assert.True(credentials.Ok);
        Assert.Equal(variable, credentials.Detail);
        Assert.DoesNotContain("gh auth status", runner.Requests.Select(ScriptedProcessRunner.CommandLine));
        // Guards the whole JSON document, not just Detail: no field may ever carry the credential's value (RF-05).
        Assert.DoesNotContain(tokenValue, report.ToJson());
        // Guards the progress stream too: RF-05 says "never shows the token value", not just "not in the JSON".
        Assert.DoesNotContain(tokenValue, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_BothGitHubTokenVariablesSet_PrefersGhTokenOverGithubToken()
    {
        var runner = AllToolsAvailable();

        var report = await Create(runner, name => name switch
        {
            "GH_TOKEN" => "ghp_xxxxxxxxxxxxxxxxxxxx",
            "GITHUB_TOKEN" => "ghs_yyyyyyyyyyyyyyyyyyyy",
            _ => null,
        }).RunAsync();

        Assert.Equal("GH_TOKEN", Find(report, "Credenciales de GitHub").Detail);
    }

    [Fact]
    public async Task RunAsync_GhTokenIsWhitespaceOnly_IsTreatedAsUnsetAndFallsBackToGhAuthStatus()
    {
        var runner = AllToolsAvailable();

        var report = await Create(runner, name => name is "GH_TOKEN" or "GITHUB_TOKEN" ? "   " : null).RunAsync();

        var credentials = Find(report, "Credenciales de GitHub");
        Assert.Contains("gh auth status", runner.Requests.Select(ScriptedProcessRunner.CommandLine));
        Assert.True(credentials.Ok);
        Assert.Equal("gh auth status", credentials.Detail);
    }

    [Fact]
    public async Task RunAsync_NoCredentialsAtAll_ReportsWarningWithoutRunningAnyCheckAsCritical()
    {
        var runner = AllToolsAvailable();
        runner.NotFound("gh auth status");

        var report = await Create(runner, _ => null).RunAsync();

        var credentials = Find(report, "Credenciales de GitHub");
        Assert.Equal(HealthCheckLevel.Warning, credentials.Level);
        Assert.False(credentials.Ok);
        Assert.Equal("sin credenciales (GH_TOKEN, GITHUB_TOKEN o gh auth status)", credentials.Detail);
        Assert.True(report.Healthy);
    }

    [Fact]
    public async Task RunAsync_ProcessNeverCompletes_FailsAsTimeoutWithoutWaitingForExternalCancellation()
    {
        var runner = AllToolsAvailable();
        runner.Hang("git --version");

        var runTask = Create(runner, _ => null, timeout: TimeSpan.FromMilliseconds(50)).RunAsync();

        // Watchdog: if HealthDiagnostics ever failed to honor its own per-check timeout, awaiting runTask directly
        // would hang this test (and the whole suite) instead of failing it. Bound the wait explicitly (RF-06).
        var winner = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(runTask, winner);

        var report = await runTask;
        var git = Find(report, "git");
        Assert.False(git.Ok);
        Assert.Contains("tiempo de espera agotado", git.Detail);
    }

    [Fact]
    public async Task RunAsync_VersionCheckCapturesCommandOutputAsDetail()
    {
        var runner = AllToolsAvailable();
        runner.On("git --version", stdout: "git version 2.43.0\n");

        var report = await Create(runner, _ => null).RunAsync();

        Assert.Equal("git version 2.43.0", Find(report, "git").Detail);
    }

    [Fact]
    public async Task RunAsync_SessionCheck_NeverIncludesRawProcessOutputInDetail()
    {
        var runner = AllToolsAvailable();
        // Even if "claude auth status" ever printed something sensitive, the session check's detail must not
        // surface it: defense in depth beyond RF-05, which already covers the token checks specifically.
        const string SensitiveLookingOutput = "user=alice token=should-never-appear";
        runner.On("claude auth status", stdout: SensitiveLookingOutput);

        var report = await Create(runner, _ => null).RunAsync();

        var session = Find(report, "Sesión de Claude Code");
        Assert.True(session.Ok);
        Assert.Equal("sesión activa", session.Detail);
        Assert.DoesNotContain(SensitiveLookingOutput, report.ToJson());
    }

    [Fact]
    public async Task RunAsync_NoSpecsDirectory_SpecsCheckIsWarningNotCritical()
    {
        Directory.Delete(Path.Combine(_root, "openspec", "specs"), recursive: true);

        var report = await Create(AllToolsAvailable(), _ => null).RunAsync();

        var specs = Find(report, "Especificaciones (openspec/specs)");
        Assert.Equal(HealthCheckLevel.Warning, specs.Level);
        Assert.False(specs.Ok);
        Assert.True(report.Healthy);
    }

    [Fact]
    public async Task RunAsync_InvalidAgentFrontmatter_ReportsCriticalFailureWithProblemDetail()
    {
        var firstAgent = RepositoryInspector.ExpectedAgents.First();
        File.WriteAllText(Path.Combine(_agents, firstAgent + ".md"), "no frontmatter here");

        var report = await Create(AllToolsAvailable(), _ => null).RunAsync();

        var agents = Find(report, "Agentes (.claude/agents)");
        Assert.Equal(HealthCheckLevel.Critical, agents.Level);
        Assert.False(agents.Ok);
        Assert.Contains(firstAgent, agents.Detail);
        Assert.False(report.Healthy);
    }

    [Fact]
    public async Task RunAsync_MissingAgentsDirectory_ReportsCriticalFailure()
    {
        Directory.Delete(_agents, recursive: true);

        var report = await Create(AllToolsAvailable(), _ => null).RunAsync();

        var agents = Find(report, "Agentes (.claude/agents)");
        Assert.Equal(HealthCheckLevel.Critical, agents.Level);
        Assert.False(agents.Ok);
        Assert.False(report.Healthy);
    }

    [Fact]
    public void ToJson_SerializesLevelInLowercase()
    {
        var report = new HealthReport([new HealthCheck("CLAUDE.md", HealthCheckLevel.Critical, true, "10 líneas, 1 secciones")]);

        var json = report.ToJson();

        Assert.Contains("\"level\": \"critical\"", json);
        Assert.Contains("\"healthy\": true", json);
    }

    [Fact]
    public void ToJson_SerializesWarningLevelAndFalseFieldsInLowercase()
    {
        var report = new HealthReport([new HealthCheck("gh", HealthCheckLevel.Warning, false, "no encontrado en PATH")]);

        var json = report.ToJson();

        Assert.Contains("\"level\": \"warning\"", json);
        Assert.Contains("\"ok\": false", json);
        Assert.Contains("\"healthy\": true", json); // a Warning failure alone never flips Healthy.
    }

    [Fact]
    public void Healthy_FalseOnlyWhenACriticalCheckFails()
    {
        var onlyWarningFails = new HealthReport(
        [
            new HealthCheck("CLAUDE.md", HealthCheckLevel.Critical, true, "ok"),
            new HealthCheck("gh", HealthCheckLevel.Warning, false, "no encontrado en PATH"),
        ]);
        var criticalFails = onlyWarningFails with
        {
            Checks = [onlyWarningFails.Checks[0] with { Ok = false }, onlyWarningFails.Checks[1]],
        };

        Assert.True(onlyWarningFails.Healthy);
        Assert.False(criticalFails.Healthy);
    }
}
