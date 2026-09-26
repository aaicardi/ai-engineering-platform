using System.Globalization;
using System.Text.RegularExpressions;
using AIHarness.Specs;

namespace AIHarness.Orchestration;

/// <summary>Data an OpenSpec provides to the Git lifecycle: the Issue it closes, its title and a branch-safe slug.</summary>
public sealed partial record SpecMetadata(int IssueNumber, string Title, string Slug)
{
    /// <summary>Feature branch for this spec: <c>feature/&lt;issue&gt;-&lt;slug&gt;</c>.</summary>
    public string BranchName => $"feature/{IssueNumber}-{Slug}";

    /// <summary>Parses the spec's <c>Closes #N</c> reference, H1 title and file-name slug.</summary>
    /// <exception cref="InvalidDataException">The spec has no <c>Closes #N</c> Issue reference.</exception>
    public static SpecMetadata Parse(string specFileName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specFileName);
        ArgumentNullException.ThrowIfNull(content);

        var issue = IssueReferenceRegex().Match(content);
        if (!issue.Success)
            throw new InvalidDataException($"La especificación '{specFileName}' no referencia un Issue ('Closes #<número>').");
        var issueNumber = int.Parse(issue.Groups[1].Value, CultureInfo.InvariantCulture);

        // The file name already carries the slug (NNN-<slug>.md); re-slugify it so hand-written names are branch-safe too.
        var baseName = SpecPrefixRegex().Replace(Path.GetFileNameWithoutExtension(specFileName), string.Empty);
        var slug = SpecGenerator.Slugify(baseName, issueNumber);

        var heading = HeadingRegex().Match(content);
        var title = heading.Success ? SpecTitlePrefixRegex().Replace(heading.Groups[1].Value.Trim(), string.Empty) : slug;

        return new SpecMetadata(issueNumber, title.Length == 0 ? slug : title, slug);
    }

    [GeneratedRegex(@"\b(?:Closes|Fixes|Resolves)\s+#(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IssueReferenceRegex();

    [GeneratedRegex(@"^\d+-")]
    private static partial Regex SpecPrefixRegex();

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^OpenSpec\s+\d+\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SpecTitlePrefixRegex();
}
