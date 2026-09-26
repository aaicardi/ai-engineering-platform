using Octokit;

namespace AIHarness.GitHub;

/// <summary><see cref="IGitHubConnector"/> backed by <see cref="GitHubClient"/>.</summary>
public sealed class GitHubConnector(IGitHubClient client) : IGitHubConnector
{
    public static readonly ProductHeaderValue ProductHeader = new("AIHarness");

    /// <summary>Creates a connector authenticated with <paramref name="token"/>, or anonymous when <c>null</c>.</summary>
    public static GitHubConnector Create(GitHubToken? token)
    {
        var client = new GitHubClient(ProductHeader);
        if (token is not null) client.Credentials = new Credentials(token.Value);
        return new GitHubConnector(client);
    }

    /// <summary>Resolves the owner/name of the <c>origin</c> remote of the git repository containing <paramref name="directory"/>.</summary>
    public static GitHubRepository? ResolveRepositoryFromGit(string directory)
    {
        var url = ProcessRunner.TryReadOutput("git", "-C", directory, "remote", "get-url", "origin");
        return GitHubRepository.TryParseRemoteUrl(url, out var repository) ? repository : null;
    }

    public async Task<GitHubIssue> GetIssueAsync(GitHubRepository repository, int number)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        Issue issue;
        try
        {
            issue = await client.Issue.Get(repository.Owner, repository.Name, number).ConfigureAwait(false);
        }
        catch (NotFoundException ex)
        {
            throw new GitHubIssueNotFoundException(repository, number, ex);
        }

        return new GitHubIssue(
            issue.Number,
            issue.Title,
            issue.Body,
            issue.User?.Login ?? "(desconocido)",
            issue.State.StringValue,
            issue.Labels.Select(l => l.Name).ToList(),
            issue.HtmlUrl);
    }
}
