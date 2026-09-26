using System.Reflection;
using System.Runtime.InteropServices;

namespace AIHarness.Cli;

/// <summary>Version and runtime environment details printed by <c>AIHarness --version</c>.</summary>
public sealed record VersionInfo(string Version, string Runtime, string OperatingSystem, string Architecture)
{
    public const string ProductName = "AIHarness";

    public static VersionInfo Current { get; } = FromAssembly(typeof(VersionInfo).Assembly);

    public static VersionInfo FromAssembly(Assembly assembly) => new(
        GetVersion(assembly),
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    // InformationalVersion carries <Version> plus the "+<commit>" suffix when the build embeds source info.
    internal static string GetVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public override string ToString() =>
        $"{ProductName} {Version}{Environment.NewLine}" +
        $"Runtime : {Runtime}{Environment.NewLine}" +
        $"SO      : {OperatingSystem} ({Architecture})";
}
