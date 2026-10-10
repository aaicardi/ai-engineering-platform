using System.Text.RegularExpressions;

namespace AIHarness.Orchestration;

public enum RunReportStatus { Completed, Blocked, Unknown }

/// <summary>
/// The execution report the <c>lead</c> agent writes to <see cref="RelativePath"/>: a <c>status: COMPLETED | BLOCKED</c>
/// line followed by Markdown sections (Summary, Acceptance Criteria, Agents, Open Findings, Blockers).
/// </summary>
/// <remarks>The report is written by a model steered by an untrusted Issue: it is parsed strictly and sanitized before publishing.</remarks>
public sealed partial record RunReport(RunReportStatus Status, string Content)
{
    /// <summary>Report location inside the target repository; never committed.</summary>
    public static readonly string RelativePath = Path.Combine(".aiharness", "run-report.md");

    /// <summary>Directory of <see cref="RelativePath"/>, excluded from commits.</summary>
    public const string DirectoryName = ".aiharness";

    /// <summary>GitHub rejects longer Pull Request descriptions; the full report stays in the run directory.</summary>
    public const int MaxPullRequestLength = 60_000;

    public static RunReport Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        content = StripEnclosingFence(content.ReplaceLineEndings("\n").Trim());
        var match = StatusRegex().Match(content);
        var value = match.Success ? match.Groups[1].Value : string.Empty;
        // The template line `status: COMPLETED | BLOCKED` copied verbatim is not a decision.
        var status = value.Contains('|') ? RunReportStatus.Unknown : FirstWordRegex().Match(value).Value.ToUpperInvariant() switch
        {
            "COMPLETED" or "COMPLETADO" => RunReportStatus.Completed,
            "BLOCKED" or "BLOQUEADO" => RunReportStatus.Blocked,
            _ => RunReportStatus.Unknown,
        };
        return new RunReport(status, content);
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

    /// <summary>The report without its <c>status:</c> line.</summary>
    public string Body => StatusRegex().Replace(Content, string.Empty, 1).Trim();

    /// <summary><c>true</c> when the report contains something that looks like a credential; it must not be published.</summary>
    public bool ContainsSecretLikeText => SecretRegex().IsMatch(Content);

    /// <summary>
    /// <see cref="Body"/> made safe for a Pull Request description: Issue-closing keywords and @mentions are neutralized
    /// (the harness writes the only <c>Closes #N</c>), and the text is truncated to <see cref="MaxPullRequestLength"/>.
    /// </summary>
    public string ForPullRequest(string fullReportLocation)
    {
        var body = ClosingKeywordRegex().Replace(Body, "$1$2#​$3");
        body = MentionRegex().Replace(body, "@​$1");
        return body.Length <= MaxPullRequestLength
            ? body
            : body[..MaxPullRequestLength] + $"\n\n_(Informe truncado; versión completa en `{fullReportLocation}`.)_";
    }

    // A report wrapped as a whole in one ```markdown fence, as the agent template shows it.
    private static string StripEnclosingFence(string content)
    {
        var lines = content.Split('\n');
        return lines.Length >= 2 && lines[0].StartsWith("```", StringComparison.Ordinal) && lines[^1].Trim() == "```"
               && !lines[1..^1].Any(l => l.StartsWith("```", StringComparison.Ordinal))
            ? string.Join('\n', lines[1..^1]).Trim()
            : content;
    }

    // `status: COMPLETED`, possibly with Markdown emphasis or in Spanish (`estado:`).
    [GeneratedRegex(@"^\W*(?:status|estado)\W*:[\s*_`]*([^\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex FirstWordRegex();

    // GitHub closing keywords followed by an Issue reference (`fixes #12`, `Closes: owner/repo#3`).
    [GeneratedRegex(@"\b(close[sd]?|fix(?:e[sd])?|resolve[sd]?)(\s*:?\s*[\w.-]*/?[\w.-]*)#(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingKeywordRegex();

    [GeneratedRegex(@"(?<![\w.])@([A-Za-z0-9][A-Za-z0-9-]*)")]
    private static partial Regex MentionRegex();

    // GitHub tokens, private keys, AWS keys, OpenAI/Anthropic-style keys and `password=...`-style assignments.
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|\bsk-[A-Za-z0-9_-]{20,}|\b(?:password|passwd|secret|token|api[_-]?key)\s*[:=]\s*['""]?[^\s'""]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();
}
