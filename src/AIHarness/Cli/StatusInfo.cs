using System.Globalization;

namespace AIHarness.Cli;

/// <summary>Runtime status details printed by <c>AIHarness --status</c>.</summary>
public sealed record StatusInfo(string Version, string Runtime, string OperatingSystem, TimeSpan SystemUptime)
{
    /// <summary>Captures the current status; uptime comes from the monotonic clock (time since the system started).</summary>
    public static StatusInfo Capture() => FromVersion(VersionInfo.Current, TimeSpan.FromMilliseconds(Environment.TickCount64));

    public static StatusInfo FromVersion(VersionInfo version, TimeSpan systemUptime) =>
        new(version.Version, version.Runtime, version.OperatingSystem, systemUptime);

    /// <summary>Formats an uptime as <c>[Nd ]hh:mm:ss</c>, e.g. <c>3d 04:12:05</c>.</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
            uptime = TimeSpan.Zero;
        var clock = uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return uptime.Days > 0 ? $"{uptime.Days}d {clock}" : clock;
    }

    public override string ToString() =>
        $"=== {VersionInfo.ProductName} — Estado ==={Environment.NewLine}" +
        $"Versión : {Version}{Environment.NewLine}" +
        $"Runtime : {Runtime}{Environment.NewLine}" +
        $"SO      : {OperatingSystem}{Environment.NewLine}" +
        $"Uptime  : {FormatUptime(SystemUptime)}";
}
