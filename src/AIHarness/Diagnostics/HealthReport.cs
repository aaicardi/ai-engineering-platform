using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace AIHarness.Diagnostics;

/// <summary>Result of <see cref="HealthDiagnostics.RunAsync"/>: every <see cref="HealthCheck"/> performed.</summary>
public sealed record HealthReport(IReadOnlyList<HealthCheck> Checks)
{
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

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
        // Allowing every Unicode range leaves non-ASCII characters (e.g. accents) unescaped (spec 052's RF-04) while
        // still escaping HTML-sensitive characters (<, >, &, ') and whatever JSON itself requires, unlike
        // UnsafeRelaxedJsonEscaping, so the output stays safe if it is ever embedded in a web page.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = Encoder });
    }
}
