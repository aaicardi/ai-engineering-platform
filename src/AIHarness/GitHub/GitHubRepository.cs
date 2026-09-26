using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AIHarness.GitHub;

/// <summary>A GitHub repository identified by <c>owner/name</c>.</summary>
public sealed partial record GitHubRepository(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";

    /// <summary>Parses <c>owner/name</c>.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out GitHubRepository? repository)
    {
        repository = null;
        var match = SlugPattern().Match(value?.Trim() ?? "");
        if (!match.Success) return false;
        repository = new(match.Groups["owner"].Value, match.Groups["name"].Value);
        return true;
    }

    /// <summary>
    /// Extracts <c>owner/name</c> from a github.com remote URL (SSH or HTTPS).
    /// Any credentials embedded in the URL are discarded.
    /// </summary>
    public static bool TryParseRemoteUrl(string? url, [NotNullWhen(true)] out GitHubRepository? repository)
    {
        repository = null;
        var match = RemotePattern().Match(url?.Trim() ?? "");
        if (!match.Success) return false;
        repository = new(match.Groups["owner"].Value, match.Groups["name"].Value);
        return true;
    }

    [GeneratedRegex(@"^(?<owner>[A-Za-z0-9-]+)/(?<name>[A-Za-z0-9._-]+)$")]
    private static partial Regex SlugPattern();

    // git@github.com:owner/name.git | ssh://git@github.com/owner/name | https://[user[:secret]@]github.com/owner/name.git
    [GeneratedRegex(@"^(?:git@github\.com:|(?:ssh|https?)://(?:[^@/]+@)?github\.com/)(?<owner>[A-Za-z0-9-]+)/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RemotePattern();
}
