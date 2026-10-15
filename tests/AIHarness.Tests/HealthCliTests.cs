using System.Text.Json;
using AIHarness.Cli;
using AIHarness.Execution;
using AIHarness.Inspection;

namespace AIHarness.Tests;

/// <summary>
/// End-to-end tests of <c>--health</c>. RF-06 (spec 052) requires these tests to not launch real tools: the
/// constructor substitutes <see cref="HealthProcessRunnerFactory.Create"/> with a <see cref="FakeProcessRunner"/>
/// (the one declared in <c>HealthDiagnosticsTests.cs</c>, same assembly/namespace) that reports every tool as
/// available, and <see cref="Dispose"/> restores the real factory so later tests (and other test classes) are not
/// affected by this process-wide seam.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class HealthCliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-health-cli-").FullName;
    private readonly Func<IProcessRunner> _originalProcessRunnerFactory = HealthProcessRunnerFactory.Create;

    public HealthCliTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));
        HealthProcessRunnerFactory.Create = () => new FakeProcessRunner()
            .On("git --version").On("dotnet --version").On("claude --version")
            .On("claude auth status").On("gh --version").On("gh auth status");
    }

    public void Dispose()
    {
        HealthProcessRunnerFactory.Create = _originalProcessRunnerFactory;
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Writes a fully valid governance tree (CLAUDE.md, all expected agents, a spec) into <paramref name="root"/>.</summary>
    private static void WriteFullGovernance(string root)
    {
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# Governance\n## 1. Contexto\n");
        var agentsDir = Directory.CreateDirectory(Path.Combine(root, ".claude", "agents")).FullName;
        foreach (var name in RepositoryInspector.ExpectedAgents)
            File.WriteAllText(Path.Combine(agentsDir, name + ".md"), $"---\nname: {name}\ndescription: Agente {name}\n---\n# Agent\n");
        Directory.CreateDirectory(Path.Combine(root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(root, "openspec", "specs", "001-demo.md"), "# OpenSpec 001: Demo\n");
    }

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

    // AC-01 (spec 052): --target-dir, --base and --create-pr join the flags --health already rejected; the
    // assertion on STDERR (not just the exit code) proves the error message actually names the incompatible flag,
    // not just that *some* error happened to be printed.
    [Theory]
    [InlineData("--process-issue", "1")]
    [InlineData("--issue", "1")]
    [InlineData("--repo", "owner/name")]
    [InlineData("--agent", "lead")]
    [InlineData("--spec", "001-demo.md")]
    [InlineData("--generate-spec", null)]
    [InlineData("--execute", null)]
    [InlineData("--orchestrate", null)]
    [InlineData("--target-dir", "/tmp/aiharness-health-target")]
    [InlineData("--base", "main")]
    [InlineData("--create-pr", null)]
    public async Task Main_HealthCombinedWithIncompatibleFlag_IsRejectedWithExitCodeTwoAndNamesTheFlagInStderr(string flag, string? value)
    {
        List<string> args = ["--health", flag];
        if (value is not null)
            args.Add(value);

        var (exitCode, _, error) = await CliHost.RunWithStderrAsync(args.ToArray());

        Assert.Equal(2, exitCode);
        Assert.Contains(flag, error);
    }

    // AC-02 (spec 052): --root stays accepted alongside --health; a fully valid repository with every tool
    // reported as available (the FakeProcessRunner installed in the constructor) exits 0, not 2.
    [Fact]
    public async Task Main_HealthWithRootFlag_IsAcceptedAndRunsDiagnostics()
    {
        WriteFullGovernance(_root);

        var (exitCode, output) = await CliHost.RunAsync("--health", "--root", _root);

        Assert.NotEqual(2, exitCode);
        Assert.Equal(0, exitCode);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("healthy").GetBoolean());
    }

    // AC-05 (spec 052/RF-04): the CLAUDE.md governance check's detail is "{N} líneas, {M} secciones"; STDOUT must
    // carry the accented word literally, not as the í escape sequence the default JSON encoder would produce.
    // Asserted against the raw STDOUT string on purpose — deserializing with JsonDocument/JsonSerializer would
    // normalize the escape back into the same character and hide a regression.
    [Fact]
    public async Task Main_Health_ClaudeMdDetailWithAccentedCharacters_AppearsLiterallyInStdoutNotAsUnicodeEscape()
    {
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Title\n## Section one\n## Section two\n");

        var (_, output) = await CliHost.RunAsync("--health", "--root", _root);

        Assert.Contains("líneas", output);
        Assert.DoesNotContain("\\u00ed", output);
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

    // SEC-4: the seam never accepts null, so a test cannot leave --health without a process runner.
    [Fact]
    public void HealthProcessRunnerFactory_Null_IsRejectedAndKeepsTheCurrentFactory()
    {
        var current = HealthProcessRunnerFactory.Create;

        Assert.Throws<ArgumentNullException>(() => HealthProcessRunnerFactory.Create = null!);
        Assert.Same(current, HealthProcessRunnerFactory.Create);
    }
}
