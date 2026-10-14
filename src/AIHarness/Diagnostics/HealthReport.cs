using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIHarness.Diagnostics;

/// <summary>Result of <see cref="HealthDiagnostics.RunAsync"/>: every <see cref="HealthCheck"/> performed.</summary>
public sealed record HealthReport(IReadOnlyList<HealthCheck> Checks)
{
    /// <summary><c>false</c> when any <see cref="HealthCheckLevel.Critical"/> check failed; computed, no mutable state.</summary>
    public bool Healthy => !Checks.Any(c => c.Level == HealthCheckLevel.Critical && !c.Ok);

    /// <summary>
    /// <c>{"healthy": bool, "checks": [{"name":, "level":, "ok":, "detail":}, ...]}</c>, built by hand
    /// (same pattern as <see cref="AIHarness.Orchestration.AgentSession"/>) to control the casing of each field.
    /// </summary>
    public string ToJson()
    {
        var checks = new JsonArray(Checks
            .Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name,
                ["level"] = c.LevelName,
                ["ok"] = c.Ok,
                ["detail"] = c.Detail,
            })
            .ToArray());
        var root = new JsonObject { ["healthy"] = Healthy, ["checks"] = checks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
