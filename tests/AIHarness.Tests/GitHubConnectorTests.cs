using AIHarness.GitHub;

namespace AIHarness.Tests;

public sealed class GitHubConnectorTests
{
    private const string Secret = "ghp_supersecret123";

    [Theory]
    [InlineData("git@github.com:aaicardi/ai-engineering-platform.git")]
    [InlineData("git@github.com:aaicardi/ai-engineering-platform")]
    [InlineData("ssh://git@github.com/aaicardi/ai-engineering-platform.git")]
    [InlineData("https://github.com/aaicardi/ai-engineering-platform.git")]
    [InlineData("https://github.com/aaicardi/ai-engineering-platform/")]
    [InlineData("https://x-access-token:" + Secret + "@github.com/aaicardi/ai-engineering-platform.git")]
    public void TryParseRemoteUrl_ExtractsOwnerAndName(string url)
    {
        Assert.True(GitHubRepository.TryParseRemoteUrl(url, out var repository));
        Assert.Equal("aaicardi/ai-engineering-platform", repository.ToString());
        Assert.DoesNotContain(Secret, repository.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("/local/path/repo")]
    public void TryParseRemoteUrl_RejectsNonGitHubRemotes(string? url) =>
        Assert.False(GitHubRepository.TryParseRemoteUrl(url, out _));

    [Theory]
    [InlineData("owner/name", true)]
    [InlineData("owner/name.js", true)]
    [InlineData("owner", false)]
    [InlineData("owner/name/extra", false)]
    public void TryParse_ValidatesSlug(string value, bool expected) =>
        Assert.Equal(expected, GitHubRepository.TryParse(value, out _));

    [Fact]
    public void Resolve_PrefersGhTokenOverGithubTokenAndCli()
    {
        var env = new Dictionary<string, string?> { ["GH_TOKEN"] = "a", ["GITHUB_TOKEN"] = "b" };
        var token = GitHubTokenProvider.Resolve(n => env.GetValueOrDefault(n), () => "c");

        Assert.Equal("GH_TOKEN", token?.Source);
        Assert.Equal("a", token?.Value);
    }

    [Fact]
    public void Resolve_FallsBackToGithubTokenThenCli()
    {
        var fromEnv = GitHubTokenProvider.Resolve(n => n == "GITHUB_TOKEN" ? "b" : " ", () => "c");
        var fromCli = GitHubTokenProvider.Resolve(_ => null, () => "c\n");

        Assert.Equal("GITHUB_TOKEN", fromEnv?.Source);
        Assert.Equal("gh auth token", fromCli?.Source);
        Assert.Equal("c", fromCli?.Value);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNoCredentials() =>
        Assert.Null(GitHubTokenProvider.Resolve(_ => null, () => null));

    [Fact]
    public void Token_ToStringNeverExposesValue()
    {
        var token = new GitHubToken(Secret, "GH_TOKEN");

        Assert.DoesNotContain(Secret, token.ToString());
        Assert.DoesNotContain(Secret, $"{token}");
    }
}
