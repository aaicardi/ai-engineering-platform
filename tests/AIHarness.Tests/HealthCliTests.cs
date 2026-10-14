using System.Text.Json;

namespace AIHarness.Tests;

/// <summary>
/// End-to-end tests of <c>--health</c> restricted to scenarios whose result does not depend on which tools happen
/// to be installed on the machine running the tests (see the implementation plan's *Risks*): a missing
/// <c>CLAUDE.md</c> is always a critical failure, regardless of the environment.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class HealthCliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-health-cli-").FullName;

    public HealthCliTests() => Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Main_MissingClaudeMd_ReportsCriticalFailureAndExitsWithOne()
    {
        var (exitCode, output) = await CliHost.RunAsync("--health", "--root", _root);

        Assert.Equal(1, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.False(root.GetProperty("healthy").GetBoolean());
        var claudeMd = root.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "CLAUDE.md");
        Assert.Equal("critical", claudeMd.GetProperty("level").GetString());
        Assert.False(claudeMd.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Main_Health_PrintsOnlyJsonToStdout()
    {
        var (_, output) = await CliHost.RunAsync("--health", "--root", _root);

        // A stray "[INFO] ..." progress line mixed into STDOUT would make this fail to parse.
        var root = JsonDocument.Parse(output.Trim()).RootElement;
        Assert.Equal(["healthy", "checks"], root.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Main_Health_SendsProgressToStderrNotStdout()
    {
        var (_, output, error) = await CliHost.RunWithStderrAsync("--health", "--root", _root);

        // RF-04: informational progress belongs on STDERR; STDOUT must parse as JSON alone, with no "[INFO]" noise.
        Assert.Contains("[INFO]", error);
        Assert.DoesNotContain("[INFO]", output);
        JsonDocument.Parse(output.Trim());
    }

    [Theory]
    [InlineData("--process-issue", "1")]
    [InlineData("--issue", "1")]
    [InlineData("--repo", "owner/name")]
    [InlineData("--agent", "lead")]
    [InlineData("--spec", "001-demo.md")]
    [InlineData("--generate-spec", null)]
    [InlineData("--execute", null)]
    [InlineData("--orchestrate", null)]
    public async Task Main_HealthCombinedWithIncompatibleFlag_IsRejectedWithExitCodeTwo(string flag, string? value)
    {
        List<string> args = ["--health", flag];
        if (value is not null)
            args.Add(value);

        var (exitCode, _) = await CliHost.RunAsync(args.ToArray());

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Main_HealthWithoutRootAndNoClaudeMdInAnyAncestor_StillRunsAndReportsTheMissingGovernance()
    {
        // Unlike --process-issue/--generate-spec (which fail fast with exit 1 *before* running when no CLAUDE.md
        // is found walking up from the current directory), --health's whole purpose is to diagnose that absence,
        // so RunHealthAsync falls back to Directory.GetCurrentDirectory() instead of bailing out early.
        var (exitCode, output, _) = await CliHost.RunInDirectoryAsync(_root, "--health");

        Assert.Equal(1, exitCode);
        var root = JsonDocument.Parse(output.Trim()).RootElement;
        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        // A command that bailed out early (like --process-issue without --root) would never have produced any
        // check at all; asserting on the full set proves --health actually ran its diagnostics.
        Assert.True(checks.Length >= 3);
        var claudeMd = checks.Single(c => c.GetProperty("name").GetString() == "CLAUDE.md");
        Assert.Equal("critical", claudeMd.GetProperty("level").GetString());
        Assert.False(claudeMd.GetProperty("ok").GetBoolean());
    }
}
