using System.Text.Json;
using AIHarness.Cli;
using AIHarness.Inspection;

namespace AIHarness.Tests;

[Collection(ConsoleCollection.Name)]
public sealed class HealthReportTests : IDisposable
{
    private static readonly VersionInfo Version = new("2.3.4+abc", ".NET 10.0.1", "Linux 5.15", "x64");

    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-health-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateHealthyRepository(int specCount = 2)
    {
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Test\n## 1. Contexto\n");
        var agentsDir = Directory.CreateDirectory(Path.Combine(_root, ".claude", "agents")).FullName;
        foreach (var agent in RepositoryInspector.ExpectedAgents)
            File.WriteAllText(Path.Combine(agentsDir, $"{agent}.md"), $"# Agent: {agent}\n");
        var specsDir = Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs")).FullName;
        for (var i = 1; i <= specCount; i++)
            File.WriteAllText(Path.Combine(specsDir, $"{i:000}-spec.md"), $"# Spec {i}\n");
    }

    [Fact]
    public void Capture_CompleteRepository_IsHealthyWithAgentsAndSpecsCounts()
    {
        CreateHealthyRepository(specCount: 3);

        var report = HealthReport.Capture(_root, Version);

        Assert.True(report.IsHealthy);
        Assert.Equal(HealthReport.Healthy, report.Status);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(new HealthChecks(true, true, true, true, true), report.Checks);
        Assert.Equal(RepositoryInspector.ExpectedAgents.Count, report.Agents.Expected);
        Assert.Equal(RepositoryInspector.ExpectedAgents.Count, report.Agents.Found);
        Assert.Empty(report.Agents.Missing);
        Assert.Equal(3, report.Specs.Count);
        Assert.Equal(new EnvironmentHealth("2.3.4+abc", ".NET 10.0.1", "Linux 5.15", "x64"), report.Environment);
    }

    [Fact]
    public void Capture_EmptySpecsDirectory_IsStillHealthy()
    {
        CreateHealthyRepository(specCount: 0);

        var report = HealthReport.Capture(_root, Version);

        Assert.True(report.IsHealthy);
        Assert.Equal(0, report.Specs.Count);
    }

    [Fact]
    public void Capture_MissingAgent_IsUnhealthyAndListsIt()
    {
        CreateHealthyRepository();
        File.Delete(Path.Combine(_root, ".claude", "agents", "tester.md"));

        var report = HealthReport.Capture(_root, Version);

        Assert.False(report.IsHealthy);
        Assert.Equal(HealthReport.Unhealthy, report.Status);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(new[] { "tester" }, report.Agents.Missing);
        Assert.Equal(RepositoryInspector.ExpectedAgents.Count - 1, report.Agents.Found);
    }

    [Fact]
    public void Capture_UnexpectedAgent_IsReportedButDoesNotAffectHealth()
    {
        CreateHealthyRepository();
        File.WriteAllText(Path.Combine(_root, ".claude", "agents", "extra.md"), "# extra\n");

        var report = HealthReport.Capture(_root, Version);

        Assert.True(report.IsHealthy);
        Assert.Equal(new[] { "extra" }, report.Agents.Unexpected);
    }

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData(".claude")]
    [InlineData("openspec/specs")]
    public void Capture_MissingCoreComponent_IsUnhealthy(string relativePath)
    {
        CreateHealthyRepository();
        var path = Path.Combine(_root, relativePath);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);

        var report = HealthReport.Capture(_root, Version);

        Assert.False(report.IsHealthy);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void Capture_MissingClaudeDirectory_FailsClaudeAndAgentsChecks()
    {
        CreateHealthyRepository();
        Directory.Delete(Path.Combine(_root, ".claude"), recursive: true);

        var report = HealthReport.Capture(_root, Version);

        Assert.False(report.Checks.ClaudeDirectory);
        Assert.False(report.Checks.AgentsDirectory);
        Assert.Equal(0, report.Agents.Found);
        Assert.Equal(RepositoryInspector.ExpectedAgents, report.Agents.Missing);
    }

    [Fact]
    public void Capture_NoRoot_IsUnhealthyWithAllChecksFailed()
    {
        var report = HealthReport.Capture(null, Version);

        Assert.False(report.IsHealthy);
        Assert.Equal(1, report.ExitCode);
        Assert.Null(report.Root);
        Assert.Equal(new HealthChecks(false, false, false, false, false), report.Checks);
        Assert.Equal(RepositoryInspector.ExpectedAgents, report.Agents.Missing);
    }

    [Fact]
    public void ToJson_ProducesCamelCaseDocumentWithStatusFirst()
    {
        CreateHealthyRepository(specCount: 1);

        using var json = JsonDocument.Parse(HealthReport.Capture(_root, Version).ToJson());
        var doc = json.RootElement;

        Assert.Equal("status", doc.EnumerateObject().First().Name);
        Assert.Equal("healthy", doc.GetProperty("status").GetString());
        Assert.Equal(_root, doc.GetProperty("root").GetString());
        Assert.True(doc.GetProperty("checks").GetProperty("claudeDirectory").GetBoolean());
        Assert.Equal(7, doc.GetProperty("agents").GetProperty("found").GetInt32());
        Assert.Equal(1, doc.GetProperty("specs").GetProperty("count").GetInt32());
        Assert.Equal(".NET 10.0.1", doc.GetProperty("environment").GetProperty("runtime").GetString());
        Assert.False(doc.TryGetProperty("isHealthy", out _));
        Assert.False(doc.TryGetProperty("exitCode", out _));
    }

    [Fact]
    public async Task Main_WithHealthFlagOnHealthyRoot_PrintsJsonAndReturnsZero()
    {
        CreateHealthyRepository();

        var (exitCode, output) = await CliHost.RunAsync("--health", "--root", _root);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("specs").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Main_WithHealthFlagOnIncompleteRoot_PrintsJsonAndReturnsOne()
    {
        CreateHealthyRepository();
        Directory.Delete(Path.Combine(_root, "openspec"), recursive: true);

        var (exitCode, output) = await CliHost.RunAsync("--health", "--root", _root);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("checks").GetProperty("specsDirectory").GetBoolean());
    }
}
