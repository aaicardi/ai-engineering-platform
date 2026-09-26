using System.Reflection;
using AIHarness.Cli;

namespace AIHarness.Tests;

public sealed class VersionInfoTests
{
    private static readonly Assembly HarnessAssembly = typeof(VersionInfo).Assembly;

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
        var (exitCode, output) = await RunCliAsync(args);

        Assert.Equal(0, exitCode);
        Assert.StartsWith($"AIHarness {VersionInfo.Current.Version}", output);
        Assert.Contains(Environment.Version.ToString(), output);
    }

    private static async Task<(int ExitCode, string Output)> RunCliAsync(string[] args)
    {
        var entryPoint = HarnessAssembly.EntryPoint!;
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var result = entryPoint.Invoke(null, [args]);
            var exitCode = result is Task<int> task ? await task : (int)result!;
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
