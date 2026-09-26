namespace AIHarness.GitHub;

/// <summary>Issue details relevant to the harness, decoupled from the Octokit model.</summary>
public sealed record GitHubIssue(
    int Number,
    string Title,
    string? Body,
    string Author,
    string State,
    IReadOnlyList<string> Labels,
    string HtmlUrl);

/// <summary>Read-only access to the GitHub API.</summary>
public interface IGitHubConnector
{
    /// <summary>Fetches an Issue by number.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is not positive.</exception>
    /// <exception cref="GitHubIssueNotFoundException">The Issue does not exist or is not visible with the current credentials.</exception>
    /// <exception cref="Octokit.ApiException">Any other API failure (authentication, rate limit, network).</exception>
    Task<GitHubIssue> GetIssueAsync(GitHubRepository repository, int number);
}

public sealed class GitHubIssueNotFoundException(GitHubRepository repository, int number, Exception inner)
    : Exception($"El Issue #{number} no existe en {repository} o no es accesible con las credenciales actuales.", inner)
{
    public GitHubRepository Repository { get; } = repository;
    public int Number { get; } = number;
}
