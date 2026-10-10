using System.Text.RegularExpressions;

namespace AIHarness.Orchestration;

public enum RunReportStatus { Completed, Blocked, Unknown }

/// <summary>
/// The execution report the <c>lead</c> agent writes to <see cref="RelativePath"/>: a <c>status: COMPLETED | BLOCKED</c>
/// line followed by Markdown sections (Summary, Acceptance Criteria, Agents, Open Findings, Blockers).
/// </summary>
public sealed partial record RunReport(RunReportStatus Status, string Content)
{
    /// <summary>Report location inside the target repository; never committed.</summary>
    public static readonly string RelativePath = Path.Combine(".aiharness", "run-report.md");

    /// <summary>Directory of <see cref="RelativePath"/>, excluded from commits.</summary>
    public const string DirectoryName = ".aiharness";

    public static RunReport Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var match = StatusRegex().Match(content);
        var status = !match.Success ? RunReportStatus.Unknown : match.Groups[1].Value.ToUpperInvariant() switch
        {
            "COMPLETED" => RunReportStatus.Completed,
            "BLOCKED" => RunReportStatus.Blocked,
            _ => RunReportStatus.Unknown,
        };
        return new RunReport(status, content.ReplaceLineEndings("\n").Trim());
    }

    /// <summary>Body of the <c>## heading</c> section, or <c>null</c> when the report does not have it.</summary>
    public string? Section(string heading)
    {
        var lines = Content.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == $"## {heading}");
        if (start < 0)
            return null;
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..(end < 0 ? lines.Length : end)]).Trim();
    }

    /// <summary>The report without its <c>status:</c> line, for the Pull Request description.</summary>
    public string Body => StatusRegex().Replace(Content, string.Empty, 1).Trim();

    // `status: COMPLETED`, possibly inside a fenced block or with Markdown emphasis.
    [GeneratedRegex(@"^\W*status\W*:\W*([A-Za-z]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StatusRegex();
}
