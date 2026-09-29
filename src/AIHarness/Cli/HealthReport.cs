using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHarness.Inspection;

namespace AIHarness.Cli;

public sealed record HealthChecks(bool RepositoryRoot, bool ClaudeMd, bool ClaudeDirectory, bool AgentsDirectory, bool SpecsDirectory);

public sealed record AgentsHealth(int Expected, int Found, IReadOnlyList<string> Missing, IReadOnlyList<string> Unexpected);

public sealed record SpecsHealth(int Count);

public sealed record EnvironmentHealth(string Version, string Runtime, string OperatingSystem, string Architecture);

/// <summary>
/// Platform diagnostics printed as JSON by <c>AIHarness --health</c>. The platform is healthy when the core
/// components are present: CLAUDE.md, the .claude folder with every expected agent and the openspec/specs
/// directory. The specs count is informational (a fresh repository may have none yet).
/// </summary>
public sealed record HealthReport(
    string? Root, HealthChecks Checks, AgentsHealth Agents, SpecsHealth Specs, EnvironmentHealth Environment)
{
    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep paths and non-ASCII text readable instead of \uXXXX escapes; the output is not embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [JsonPropertyOrder(-1)]
    public string Status => IsHealthy ? Healthy : Unhealthy;

    [JsonIgnore]
    public bool IsHealthy =>
        Checks.RepositoryRoot && Checks.ClaudeMd && Checks.ClaudeDirectory && Checks.AgentsDirectory
        && Checks.SpecsDirectory && Agents.Missing.Count == 0;

    [JsonIgnore]
    public int ExitCode => IsHealthy ? 0 : 1;

    /// <summary>Diagnoses the repository at <paramref name="root"/>; <c>null</c> means no root could be located.</summary>
    public static HealthReport Capture(string? root, VersionInfo version)
    {
        var environment = new EnvironmentHealth(version.Version, version.Runtime, version.OperatingSystem, version.Architecture);
        if (root is null || !Directory.Exists(root))
        {
            return new HealthReport(root, new HealthChecks(false, false, false, false, false),
                new AgentsHealth(RepositoryInspector.ExpectedAgents.Count, 0, RepositoryInspector.ExpectedAgents, []),
                new SpecsHealth(0), environment);
        }

        var report = new RepositoryInspector(root).Inspect();
        var agents = report.Agents.Agents;
        return new HealthReport(
            root,
            new HealthChecks(
                RepositoryRoot: true,
                report.ClaudeMd.Exists,
                ClaudeDirectory: Directory.Exists(Path.Combine(root, ".claude")),
                report.Agents.DirectoryExists,
                report.Specs.DirectoryExists),
            new AgentsHealth(
                agents.Count,
                agents.Count(a => a.Present),
                agents.Where(a => !a.Present).Select(a => a.Name).ToList(),
                report.Agents.Unexpected),
            new SpecsHealth(report.Specs.Specs.Count),
            environment);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
