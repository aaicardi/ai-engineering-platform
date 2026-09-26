using AIHarness.Cli;

namespace AIHarness.Tests;

[Collection(ConsoleCollection.Name)]
public sealed class VersionInfoTests
{
    [Fact]
    public void Current_ReportsAssemblyVersionAndDotNetRuntime()
    {
        var info = VersionInfo.Current;

        Assert.StartsWith("1.0.0", info.Version);
        Assert.StartsWith(".NET ", info.Runtime);
        Assert.Contains(Environment.Version.ToString(), info.Runtime);
        Assert.False(string.IsNullOrWhiteSpace(info.OperatingSystem));
    }

    [Fact]
    public void ToString_IncludesProductVersionAndRuntime()
    {
        var text = new VersionInfo("2.3.4+abc", ".NET 10.0.1", "Linux 5.15", "x64").ToString();

        var lines = text.Split(Environment.NewLine);
        Assert.Equal("AIHarness 2.3.4+abc", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Runtime") && l.EndsWith(".NET 10.0.1"));
        Assert.Contains(lines, l => l.Contains("Linux 5.15 (x64)"));
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("--agent", "developer", "--version")]
    public async Task Main_WithVersionFlag_PrintsVersionAndRuntimeAndReturnsZero(params string[] args)
    {
        var (exitCode, output) = await CliHost.RunAsync(args);

        Assert.Equal(0, exitCode);
        Assert.StartsWith($"AIHarness {VersionInfo.Current.Version}", output);
        Assert.Contains(Environment.Version.ToString(), output);
    }
}
