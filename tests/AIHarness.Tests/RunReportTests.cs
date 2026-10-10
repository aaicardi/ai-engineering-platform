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
    [InlineData("## Summary\nsin estado")]
    [InlineData("status: DONE")]
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
