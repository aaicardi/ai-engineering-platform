using AIHarness.Cli;

namespace AIHarness.Tests;

[Collection(ConsoleCollection.Name)]
public sealed class StatusInfoTests
{
    [Fact]
    public void Capture_ReportsCurrentVersionAndPositiveSystemUptime()
    {
        var status = StatusInfo.Capture();

        Assert.Equal(VersionInfo.Current.Version, status.Version);
        Assert.Equal(VersionInfo.Current.Runtime, status.Runtime);
        Assert.True(status.SystemUptime > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, "00:00:00")]
    [InlineData(0, 4, 12, 5, "04:12:05")]
    [InlineData(3, 4, 12, 5, "3d 04:12:05")]
    [InlineData(120, 23, 59, 59, "120d 23:59:59")]
    public void FormatUptime_UsesDaysAndClockTime(int days, int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, StatusInfo.FormatUptime(new TimeSpan(days, hours, minutes, seconds)));
    }

    [Fact]
    public void FormatUptime_ClampsNegativeValuesToZero()
    {
        Assert.Equal("00:00:00", StatusInfo.FormatUptime(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void ToString_HasHeaderFollowedByVersionRuntimeOsAndUptimeLines()
    {
        var version = new VersionInfo("2.3.4+abc", ".NET 10.0.1", "Linux 5.15", "x64");
        var text = StatusInfo.FromVersion(version, new TimeSpan(1, 2, 3, 4)).ToString();

        Assert.Equal(
            [
                "=== AIHarness — Estado ===",
                "Versión : 2.3.4+abc",
                "Runtime : .NET 10.0.1",
                "SO      : Linux 5.15",
                "Uptime  : 1d 02:03:04",
            ],
            text.Split(Environment.NewLine));
    }

    [Theory]
    [InlineData("--status")]
    [InlineData("--agent", "developer", "--status")]
    public async Task Main_WithStatusFlag_PrintsStatusIncludingUptimeAndReturnsZero(params string[] args)
    {
        var (exitCode, output) = await CliHost.RunAsync(args);

        Assert.Equal(0, exitCode);
        var lines = output.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("=== AIHarness — Estado ===", lines[0]);
        Assert.Equal($"Versión : {VersionInfo.Current.Version}", lines[1]);
        Assert.StartsWith("Runtime : .NET ", lines[2]);
        Assert.StartsWith("SO      : ", lines[3]);
        Assert.Matches(@"^Uptime  : (\d+d )?\d{2}:\d{2}:\d{2}$", lines[4]);
    }
}
