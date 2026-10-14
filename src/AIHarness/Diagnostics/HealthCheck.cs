namespace AIHarness.Diagnostics;

/// <summary>Severity of a <see cref="HealthCheck"/>: whether a failure blocks <c>--process-issue</c> or merely limits it.</summary>
public enum HealthCheckLevel
{
    /// <summary>Required for <c>--process-issue</c> to run at all (e.g. governance, <c>git</c>, <c>claude</c> and its session).</summary>
    Critical,

    /// <summary>Only needed for a subset of the pipeline (e.g. cloning, private Issues, publishing PRs).</summary>
    Warning,
}

/// <summary>
/// A single diagnostic result. <see cref="Level"/> is serialized in lowercase (<c>"critical"</c>/<c>"warning"</c>),
/// never the default .NET enum name.
/// </summary>
public sealed record HealthCheck(string Name, HealthCheckLevel Level, bool Ok, string Detail)
{
    /// <summary>Lowercase rendering of <see cref="Level"/> for the JSON output.</summary>
    public string LevelName => Level == HealthCheckLevel.Critical ? "critical" : "warning";
}
