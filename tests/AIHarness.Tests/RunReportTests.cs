using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class RunReportTests
{
    private const string Completed = """
        status: COMPLETED

        ## Summary
        Exporta a CSV.

        ## Open Findings
        - LOW · CR-1 · a.cs:3 — nombre
        """;

    [Fact]
    public void Parse_Completed_ReadsStatusAndSections()
    {
        var report = RunReport.Parse(Completed);

        Assert.Equal(RunReportStatus.Completed, report.Status);
        Assert.Equal("Exporta a CSV.", report.Section("Summary"));
        Assert.Equal("- LOW · CR-1 · a.cs:3 — nombre", report.Section("Open Findings"));
        Assert.Null(report.Section("Blockers"));
    }

    [Theory]
    [InlineData("status: BLOCKED\n\n## Blockers\nFalta X")]
    [InlineData("```markdown\nstatus: blocked\n```")]
    [InlineData("**status:** BLOCKED")]
    public void Parse_Blocked_IsRecognizedInCommonFormats(string content) =>
        Assert.Equal(RunReportStatus.Blocked, RunReport.Parse(content).Status);

    [Theory]
    [InlineData("estado: COMPLETADO")]
    [InlineData("**Status:** completed")]
    public void Parse_SpanishAndEmphasizedCompleted_IsCompleted(string content) =>
        Assert.Equal(RunReportStatus.Completed, RunReport.Parse(content).Status);

    [Fact]
    public void Parse_BlockedInSpanish_IsBlocked() =>
        Assert.Equal(RunReportStatus.Blocked, RunReport.Parse("estado: BLOQUEADO").Status);

    [Fact]
    public void Parse_ReportWrappedInAFence_DropsTheFence()
    {
        var report = RunReport.Parse("```markdown\nstatus: BLOCKED\n\n## Blockers\nFalta X\n```");

        Assert.Equal("Falta X", report.Section("Blockers"));
        Assert.DoesNotContain("```", report.Body);
    }

    [Fact]
    public void ForPullRequest_TruncatesLongReports()
    {
        var report = RunReport.Parse("status: COMPLETED\n" + new string('x', RunReport.MaxPullRequestLength + 10));

        var text = report.ForPullRequest("/runs/1/run-report.md");

        Assert.True(text.Length < RunReport.MaxPullRequestLength + 200);
        Assert.Contains("/runs/1/run-report.md", text);
    }

    [Theory]
    [InlineData("## Summary\nsin estado")]
    [InlineData("status: DONE")]
    [InlineData("status: COMPLETED | BLOCKED")]
    public void Parse_MissingOrUnknownStatus_IsUnknown(string content) =>
        Assert.Equal(RunReportStatus.Unknown, RunReport.Parse(content).Status);

    [Fact]
    public void Body_DropsOnlyTheStatusLine()
    {
        var body = RunReport.Parse(Completed.Replace("Exporta a CSV.", "status: no confundir")).Body;

        Assert.StartsWith("## Summary", body);
        Assert.Contains("status: no confundir", body);
    }
}
