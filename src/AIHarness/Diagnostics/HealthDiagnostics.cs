using System.Text;
using AIHarness.Execution;
using AIHarness.GitHub;
using AIHarness.Inspection;

namespace AIHarness.Diagnostics;

/// <summary>
/// Diagnoses whether the repository's governance and the operator's environment are ready to run
/// <c>--process-issue</c>: reuses <see cref="RepositoryInspector"/> for governance and <see cref="IProcessRunner"/>,
/// with a timeout per invocation, for the environment (environment checks run in parallel, see spec 052's RF-02).
/// Never writes to STDOUT — only to <paramref name="error"/>, for progress — so STDOUT stays pure JSON by
/// construction; the caller (<c>Program.cs</c>) prints the single <see cref="HealthReport.ToJson"/> line.
/// </summary>
public sealed class HealthDiagnostics(
    IProcessRunner processRunner,
    string repositoryRoot,
    TextWriter error,
    Func<string, string?>? getEnvironmentVariable = null,
    TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Working directory for every environment check's subprocess: a neutral directory, not
    /// <c>repositoryRoot</c>, so a <c>global.json</c> or other local configuration of the inspected repository
    /// cannot change the result (e.g. <c>dotnet --version</c>; spec 052's RF-03). The user's home rather than the
    /// temp directory: <c>/tmp</c> is shared by every user, so anyone could plant a <c>global.json</c> there, while
    /// the home's ancestors are writable only by root. Falls back to the temp directory when there is no home.
    /// </summary>
    internal static readonly string NeutralWorkingDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home ? home : Path.GetTempPath();

    // Several environment checks now run concurrently (RF-02) and all of them write progress to `error`; wrapping
    // it keeps those writes from racing, since neither StringWriter (used by tests) nor TextWriter in general are
    // thread-safe.
    private readonly TextWriter _error = TextWriter.Synchronized(error);
    private readonly Func<string, string?> _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public async Task<HealthReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var inspection = new RepositoryInspector(repositoryRoot).Inspect();
        // Governance checks first, so their progress lines precede the concurrent environment checks' in STDERR.
        var claudeMdCheck = CheckClaudeMd(inspection);
        var agentsCheck = CheckAgents(inspection);
        var specsCheck = CheckSpecs(inspection);
        var gitTask = CheckVersionAsync("git", HealthCheckLevel.Critical, "git", ["--version"], cancellationToken);
        var dotnetTask = CheckVersionAsync("dotnet", HealthCheckLevel.Warning, "dotnet", ["--version"], cancellationToken);
        var claudeTask = CheckVersionAsync("claude", HealthCheckLevel.Critical, "claude", ["--version"], cancellationToken);
        var sessionTask = CheckSessionAsync("Sesión de Claude Code", HealthCheckLevel.Critical, "claude", ["auth", "status"], cancellationToken);
        var ghTask = CheckVersionAsync("gh", HealthCheckLevel.Warning, "gh", ["--version"], cancellationToken);
        var credentialsTask = CheckGitHubCredentialsAsync(cancellationToken);

        await Task.WhenAll(gitTask, dotnetTask, claudeTask, sessionTask, ghTask, credentialsTask);

        List<HealthCheck> checks =
        [
            claudeMdCheck,
            agentsCheck,
            specsCheck,
            await gitTask,
            await dotnetTask,
            await claudeTask,
            await sessionTask,
            await ghTask,
            await credentialsTask,
        ];
        return new HealthReport(checks);
    }

    private HealthCheck CheckClaudeMd(InspectionReport report)
    {
        const string Name = "CLAUDE.md";
        _error.WriteLine($"[INFO] Comprobando {Name}...");
        var claudeMd = report.ClaudeMd;
        var detail = claudeMd.Exists
            ? $"{claudeMd.LineCount} líneas, {claudeMd.Sections.Count} secciones"
            : $"No existe: {claudeMd.Path}";
        return new HealthCheck(Name, HealthCheckLevel.Critical, claudeMd.Exists, detail);
    }

    private HealthCheck CheckAgents(InspectionReport report)
    {
        const string Name = "Agentes (.claude/agents)";
        _error.WriteLine($"[INFO] Comprobando {Name}...");
        var validCount = report.Agents.Agents.Count(a => a.IsValid);
        var detail = $"{validCount}/{RepositoryInspector.ExpectedAgents.Count} válidos";
        if (!report.Agents.AllPresent)
        {
            var invalid = report.Agents.Agents.Where(a => !a.IsValid).Select(a => $"{a.Name} ({string.Join("; ", a.Problems)})");
            detail += $"; inválidos: {string.Join(", ", invalid)}";
        }
        return new HealthCheck(Name, HealthCheckLevel.Critical, report.Agents.AllPresent, detail);
    }

    private HealthCheck CheckSpecs(InspectionReport report)
    {
        const string Name = "Especificaciones (openspec/specs)";
        _error.WriteLine($"[INFO] Comprobando {Name}...");
        var ok = report.Specs.DirectoryExists && report.Specs.Specs.Count > 0;
        var detail = ok
            ? $"{report.Specs.Specs.Count} especificaciones encontradas"
            : report.Specs.DirectoryExists ? "No hay especificaciones .md" : "Directorio openspec/specs no encontrado";
        return new HealthCheck(Name, HealthCheckLevel.Warning, ok, detail);
    }

    /// <summary>Runs <c>executable arguments</c> and reports its captured STDOUT as the detail.</summary>
    private async Task<HealthCheck> CheckVersionAsync(
        string name, HealthCheckLevel level, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _error.WriteLine($"[INFO] Comprobando {name}...");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        var stdout = new StringBuilder();
        try
        {
            var exitCode = await processRunner.RunAsync(
                new ProcessRequest(executable, arguments, NeutralWorkingDirectory),
                chunk => stdout.Append(chunk),
                _ => { },
                linked.Token);
            return new HealthCheck(name, level, exitCode == 0, stdout.ToString().Trim());
        }
        catch (ExecutableNotFoundException)
        {
            return new HealthCheck(name, level, false, "no encontrado en PATH");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheck(name, level, false, $"tiempo de espera agotado ({_timeout.TotalSeconds:0}s)");
        }
    }

    /// <summary>
    /// Same timeout/<see cref="ExecutableNotFoundException"/> handling as <see cref="CheckVersionAsync"/>, but the
    /// detail never includes the real process output — only "sesión activa"/"sin sesión" — as defense in depth on
    /// top of RF-05 of spec 017 (the original <c>--health</c> spec; never show a token's value), in case that output
    /// ever included something sensitive.
    /// </summary>
    private async Task<HealthCheck> CheckSessionAsync(
        string name, HealthCheckLevel level, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _error.WriteLine($"[INFO] Comprobando {name}...");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        try
        {
            var exitCode = await processRunner.RunAsync(
                new ProcessRequest(executable, arguments, NeutralWorkingDirectory),
                _ => { },
                _ => { },
                linked.Token);
            return new HealthCheck(name, level, exitCode == 0, exitCode == 0 ? "sesión activa" : "sin sesión");
        }
        catch (ExecutableNotFoundException)
        {
            return new HealthCheck(name, level, false, "no encontrado en PATH");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheck(name, level, false, $"tiempo de espera agotado ({_timeout.TotalSeconds:0}s)");
        }
    }

    /// <summary>
    /// First looks at <see cref="GitHubTokenProvider.EnvironmentVariables"/> (never their value, only whether one is
    /// set); falls back to <c>gh auth status</c> via <see cref="IProcessRunner"/> — not <c>GitHubTokenProvider.Resolve()</c>,
    /// whose CLI fallback runs <c>gh auth token</c> and materializes the real token value (see spec 017's
    /// implementation plan's "Approach" for why that would violate its RF-05/RF-06; spec 017 is the original
    /// <c>--health</c> spec, distinct from spec 052's own RF numbering).
    /// </summary>
    private async Task<HealthCheck> CheckGitHubCredentialsAsync(CancellationToken cancellationToken)
    {
        const string Name = "Credenciales de GitHub";
        _error.WriteLine($"[INFO] Comprobando {Name}...");

        foreach (var variable in GitHubTokenProvider.EnvironmentVariables)
        {
            var value = _getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                return new HealthCheck(Name, HealthCheckLevel.Warning, true, variable);
        }

        const string NoCredentialsDetail = "sin credenciales (GH_TOKEN, GITHUB_TOKEN o gh auth status)";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        try
        {
            var exitCode = await processRunner.RunAsync(
                new ProcessRequest("gh", ["auth", "status"], NeutralWorkingDirectory),
                _ => { },
                _ => { },
                linked.Token);
            return exitCode == 0
                ? new HealthCheck(Name, HealthCheckLevel.Warning, true, "gh auth status")
                : new HealthCheck(Name, HealthCheckLevel.Warning, false, NoCredentialsDetail);
        }
        catch (ExecutableNotFoundException)
        {
            return new HealthCheck(Name, HealthCheckLevel.Warning, false, NoCredentialsDetail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheck(Name, HealthCheckLevel.Warning, false, NoCredentialsDetail);
        }
    }
}
