using System.Globalization;
using System.Text;
using AIHarness.Execution;

namespace AIHarness.Orchestration;

/// <summary>
/// Git and GitHub CLI operations the orchestrator needs around an agent run.
/// Commands run through <see cref="IProcessRunner"/> with arguments passed verbatim (no shell).
/// </summary>
public sealed class GitAutomationService(
    IProcessRunner processRunner,
    string workingDirectory,
    TextWriter output,
    TextWriter error,
    string gitExecutable = GitAutomationService.DefaultGitExecutable,
    string ghExecutable = GitAutomationService.DefaultGhExecutable)
{
    public const string DefaultGitExecutable = "git";
    public const string DefaultGhExecutable = "gh";
    public const string Remote = "origin";

    /// <summary>
    /// <c>true</c> when no tracked file has staged or unstaged changes. Untracked files are ignored:
    /// they survive a checkout untouched (e.g. a freshly generated spec that the agent will implement).
    /// </summary>
    /// <exception cref="GitAutomationException">git failed (e.g. not a repository).</exception>
    public async Task<bool> IsWorkingTreeCleanAsync(CancellationToken cancellationToken = default)
    {
        var status = await CaptureGitAsync(["status", "--porcelain", "--untracked-files=no"], cancellationToken);
        return status.Length == 0;
    }

    /// <summary>
    /// <c>true</c> when any file is modified, staged or untracked (and not ignored),
    /// i.e. there is something for <see cref="CommitAllAsync"/> to record.
    /// </summary>
    /// <exception cref="GitAutomationException">git failed.</exception>
    public async Task<bool> HasUncommittedChangesAsync(CancellationToken cancellationToken = default)
    {
        var status = await CaptureGitAsync(["status", "--porcelain"], cancellationToken);
        return status.Length > 0;
    }

    /// <summary>Stages every change, including untracked files not covered by .gitignore, and commits it.</summary>
    /// <returns>The exit code of the first git command that failed, or 0.</returns>
    public async Task<int> CommitAllAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var exitCode = await RelayAsync(gitExecutable, ["add", "--all"], cancellationToken);
        return exitCode != 0 ? exitCode : await RelayAsync(gitExecutable, ["commit", "-m", message], cancellationToken);
    }

    /// <summary>Current branch name, or <c>null</c> on a detached HEAD.</summary>
    /// <exception cref="GitAutomationException">git failed.</exception>
    public async Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
    {
        var branch = await CaptureGitAsync(["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        return branch == "HEAD" ? null : branch;
    }

    /// <summary>
    /// Checks out <paramref name="branch"/>, creating it from the current HEAD when it does not exist yet,
    /// so re-running the orchestrator for the same spec resumes on the same branch.
    /// </summary>
    /// <returns>The git exit code.</returns>
    public async Task<int> CheckoutFeatureBranchAsync(string branch, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        if (await GetCurrentBranchAsync(cancellationToken) == branch)
            return 0;

        var (exists, _, _) = await CaptureAsync(["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], cancellationToken);
        string[] arguments = exists == 0 ? ["checkout", branch] : ["checkout", "-b", branch];
        return await RelayAsync(gitExecutable, arguments, cancellationToken);
    }

    /// <summary>Number of commits on HEAD that are not on <paramref name="baseBranch"/>.</summary>
    /// <exception cref="GitAutomationException">git failed (e.g. the base branch does not exist).</exception>
    public async Task<int> CountCommitsAheadAsync(string baseBranch, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseBranch);

        var count = await CaptureGitAsync(["rev-list", "--count", $"{baseBranch}..HEAD"], cancellationToken);
        return int.Parse(count, CultureInfo.InvariantCulture);
    }

    /// <summary>Command that publishes <paramref name="branch"/> to <see cref="Remote"/>.</summary>
    public static string[] PushArguments(string branch) => ["push", "--set-upstream", Remote, branch];

    /// <summary>Command that opens the Pull Request for <paramref name="request"/>.</summary>
    public static string[] PullRequestArguments(PullRequestRequest request) =>
        ["pr", "create", "--base", request.BaseBranch, "--head", request.HeadBranch, "--title", request.Title, "--body", request.Body];

    /// <summary>Pushes <paramref name="branch"/> to <see cref="Remote"/> and sets its upstream.</summary>
    /// <returns>The git exit code.</returns>
    public Task<int> PushAsync(string branch, CancellationToken cancellationToken = default) =>
        RelayAsync(gitExecutable, PushArguments(branch), cancellationToken);

    /// <summary>Opens the Pull Request with <c>gh pr create</c>.</summary>
    /// <returns>The gh exit code.</returns>
    public Task<int> CreatePullRequestAsync(PullRequestRequest request, CancellationToken cancellationToken = default) =>
        RelayAsync(ghExecutable, PullRequestArguments(request), cancellationToken);

    /// <summary>Shell-ready rendering of a command, for showing the user what to run manually.</summary>
    public static string FormatCommand(string executable, IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Prepend(executable).Select(Quote));

    private static string Quote(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || "-_./:=@".Contains(c))
            ? value
            : $"'{value.Replace("'", "'\\''")}'";

    private async Task<string> CaptureGitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await CaptureAsync(arguments, cancellationToken);
        if (exitCode != 0)
            throw new GitAutomationException(FormatCommand(gitExecutable, arguments), exitCode, stderr);
        return stdout;
    }

    // git output used for decisions is captured, not shown to the user.
    private async Task<(int ExitCode, string Output, string Error)> CaptureAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = await processRunner.RunAsync(
            new ProcessRequest(gitExecutable, arguments, workingDirectory),
            chunk => stdout.Append(chunk),
            chunk => stderr.Append(chunk),
            cancellationToken);
        return (exitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private Task<int> RelayAsync(string executable, string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(executable, arguments, workingDirectory),
            output.Write,
            error.Write,
            cancellationToken);
}

/// <summary>Parameters of <c>gh pr create</c>.</summary>
public sealed record PullRequestRequest(string BaseBranch, string HeadBranch, string Title, string Body);

public sealed class GitAutomationException(string command, int exitCode, string detail)
    : Exception($"'{command}' falló con código {exitCode}{(detail.Length == 0 ? "." : $": {detail}")}")
{
    public int ExitCode { get; } = exitCode;
}
