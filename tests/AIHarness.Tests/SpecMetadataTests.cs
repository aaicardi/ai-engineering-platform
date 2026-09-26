using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class SpecMetadataTests
{
    [Fact]
    public void Parse_GeneratedSpec_ExtractsIssueTitleAndBranch()
    {
        const string content = "# OpenSpec 008: feat(orchestration): Add Git Automation Service\n\n## Issue Reference\nCloses #27\n";

        var spec = SpecMetadata.Parse("008-feat-orchestration-add-git-automation-service.md", content);

        Assert.Equal(27, spec.IssueNumber);
        Assert.Equal("feat(orchestration): Add Git Automation Service", spec.Title);
        Assert.Equal("feat-orchestration-add-git-automation-service", spec.Slug);
        Assert.Equal("feature/27-feat-orchestration-add-git-automation-service", spec.BranchName);
    }

    [Theory]
    [InlineData("Fixes #5")]
    [InlineData("resolves #5")]
    [InlineData("Closes  #5.")]
    public void Parse_AcceptsGitHubClosingKeywords(string reference)
    {
        Assert.Equal(5, SpecMetadata.Parse("001-x.md", $"# T\n{reference}\n").IssueNumber);
    }

    [Fact]
    public void Parse_HandWrittenFileName_IsSlugifiedToBranchSafeCharacters()
    {
        var spec = SpecMetadata.Parse("Mi Spec_Ñandú.md", "Closes #9");

        Assert.Equal("feature/9-mi-spec-nandu", spec.BranchName);
    }

    [Fact]
    public void Parse_WithoutHeading_FallsBackToSlugAsTitle()
    {
        var spec = SpecMetadata.Parse("007-agent-runner.md", "Closes #20\r\n");

        Assert.Equal("agent-runner", spec.Title);
    }

    [Fact]
    public void Parse_WithoutIssueReference_Throws()
    {
        Assert.Throws<InvalidDataException>(() => SpecMetadata.Parse("001-x.md", "# OpenSpec 001: Sin issue\nVer #12"));
    }
}
