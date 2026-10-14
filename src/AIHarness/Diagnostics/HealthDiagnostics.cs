using System.Text;
using AIHarness.Execution;
using AIHarness.GitHub;
using AIHarness.Inspection;

namespace AIHarness.Diagnostics;

/// <summary>
/// Diagnoses whether the repository's governance and the operator's environment are ready to run
/// <c>--process-issue</c>: reuses <see cref="RepositoryInspector"/> for governance (RF-01) and <see cref="IProcessRunner"/>,
/// with a timeout per invocation, for the environment (RF-02, RF-06). Never writes to STDOUT — only to
/// <paramref name="error"/>, for progress — so STDOUT stays pure JSON by construction (RF-04); the caller
/// (<c>Program.cs</c>) prints the single <see cref="HealthReport.ToJson"/> line.
/// </summary>
public sealed class HealthDiagnostics(
    IProcessRunner processRunner,
    string repositoryRoot,
    TextWriter error,
    Func<string, string?>? getEnvironmentVariable = null,
    TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public async Task<HealthReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var inspection = new RepositoryInspector(repositoryRoot).Inspect();
        List<HealthCheck> checks =
        [
            CheckClaudeMd(inspection),
            CheckAgents(inspection),
            CheckSpecs(inspection),
            await CheckVersionAsync("git", HealthCheckLevel.Critical, "git", ["--version"], cancellationToken),
            await CheckVersionAsync("dotnet", HealthCheckLevel.Warning, "dotnet", ["--version"], cancellationToken),
            await CheckVersionAsync("claude", HealthCheckLevel.Critical, "claude", ["--version"], cancellationToken),
            await CheckSessionAsync("Sesión de Claude Code", HealthCheckLevel.Critical, "claude", ["auth", "status"], cancellationToken),
            await CheckVersionAsync("gh", HealthCheckLevel.Warning, "gh", ["--version"], cancellationToken),
            await CheckGitHubCredentialsAsync(cancellationToken),
        ];
        return new HealthReport(checks);
    }

    private HealthCheck CheckClaudeMd(InspectionReport report)
    {
        const string Name = "CLAUDE.md";
        error.WriteLine($"[INFO] Comprobando {Name}...");
        var claudeMd = report.ClaudeMd;
        var detail = claudeMd.Exists
            ? $"{claudeMd.LineCount} líneas, {claudeMd.Sections.Count} secciones"
            : $"No existe: {claudeMd.Path}";
        return new HealthCheck(Name, HealthCheckLevel.Critical, claudeMd.Exists, detail);
    }

    private HealthCheck CheckAgents(InspectionReport report)
    {
        const string Name = "Agentes (.claude/agents)";
        error.WriteLine($"[INFO] Comprobando {Name}...");
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
        error.WriteLine($"[INFO] Comprobando {Name}...");
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
        error.WriteLine($"[INFO] Comprobando {name}...");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        var stdout = new StringBuilder();
        try
        {
            var exitCode = await processRunner.RunAsync(
                new ProcessRequest(executable, arguments, repositoryRoot),
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
    /// top of RF-05, in case that output ever included something sensitive.
    /// </summary>
    private async Task<HealthCheck> CheckSessionAsync(
        string name, HealthCheckLevel level, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        error.WriteLine($"[INFO] Comprobando {name}...");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        try
        {
            var exitCode = await processRunner.RunAsync(
                new ProcessRequest(executable, arguments, repositoryRoot),
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
    /// whose CLI fallback runs <c>gh auth token</c> and materializes the real token value (see the implementation plan's
    /// "Approach" for why that would violate RF-05/RF-06).
    /// </summary>
    private async Task<HealthCheck> CheckGitHubCredentialsAsync(CancellationToken cancellationToken)
    {
        const string Name = "Credenciales de GitHub";
        error.WriteLine($"[INFO] Comprobando {Name}...");

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
                new ProcessRequest("gh", ["auth", "status"], repositoryRoot),
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
