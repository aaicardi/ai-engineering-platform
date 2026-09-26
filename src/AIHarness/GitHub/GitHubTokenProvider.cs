using System.Diagnostics;

namespace AIHarness.GitHub;

/// <summary>
/// A resolved access token. <see cref="ToString"/> only exposes the source so the
/// token is never written to the console or logs by accident.
/// </summary>
public sealed class GitHubToken(string value, string source)
{
    internal string Value { get; } = value;

    /// <summary>Where the token came from (e.g. <c>GH_TOKEN</c>); safe to display.</summary>
    public string Source { get; } = source;

    public override string ToString() => $"GitHubToken(source: {Source})";
}

/// <summary>
/// Resolves the GitHub token in order: <c>GH_TOKEN</c>, <c>GITHUB_TOKEN</c>, active <c>gh</c> CLI session.
/// </summary>
public static class GitHubTokenProvider
{
    public static readonly IReadOnlyList<string> EnvironmentVariables = ["GH_TOKEN", "GITHUB_TOKEN"];

    /// <summary>Returns the first available token, or <c>null</c> to fall back to anonymous access.</summary>
    public static GitHubToken? Resolve() => Resolve(Environment.GetEnvironmentVariable, ReadGhCliToken);

    internal static GitHubToken? Resolve(Func<string, string?> getEnvironmentVariable, Func<string?> readGhCliToken)
    {
        foreach (var name in EnvironmentVariables)
        {
            var value = getEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return new(value.Trim(), name);
        }

        var cliToken = readGhCliToken();
        return string.IsNullOrWhiteSpace(cliToken) ? null : new(cliToken.Trim(), "gh auth token");
    }

    private static string? ReadGhCliToken() => ProcessRunner.TryReadOutput("gh", "auth", "token");
}

internal static class ProcessRunner
{
    /// <summary>
    /// Runs a command capturing stdout and stderr (never forwarded to the console).
    /// Returns trimmed stdout on exit code 0, otherwise <c>null</c>.
    /// </summary>
    public static string? TryReadOutput(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return process.ExitCode == 0 ? stdout.Result.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null; // command not installed
        }
    }
}
