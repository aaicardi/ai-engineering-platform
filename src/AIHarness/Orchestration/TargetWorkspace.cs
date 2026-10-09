using AIHarness.Execution;
using AIHarness.GitHub;

namespace AIHarness.Orchestration;

/// <summary>
/// Repository an orchestration works on: the agent runs at its root, the spec is written to it, its quality gate is
/// executed and the Pull Request is opened against it. It may be the harness repository itself or an external one.
/// </summary>
/// <param name="RootPath">Absolute path of the working copy.</param>
/// <param name="Repository">GitHub repository, or <c>null</c> when it is not known yet (resolved from the <c>origin</c> remote).</param>
/// <param name="RequiresClone">The working copy does not exist yet and must be cloned from <see cref="Repository"/>.</param>
public sealed record TargetWorkspace(string RootPath, GitHubRepository? Repository, bool RequiresClone)
{
    /// <summary>Where external repositories are cloned when no <c>--target-dir</c> is given: <c>~/.aiharness/workspaces</c>.</summary>
    public static string DefaultWorkspacesHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aiharness", "workspaces");

    /// <summary>
    /// Resolves the target of <c>--process-issue</c>:
    /// <list type="bullet">
    /// <item><paramref name="targetDirectory"/> (<c>--target-dir</c>) is used as is; it is cloned from <paramref name="repository"/> when missing or empty.</item>
    /// <item>Otherwise a <paramref name="repository"/> (<c>--repo</c>) other than the harness's own is worked on in
    /// <c>&lt;workspacesHome&gt;/&lt;owner&gt;/&lt;name&gt;</c>, cloned on first use.</item>
    /// <item>Otherwise the harness repository itself.</item>
    /// </list>
    /// </summary>
    public static TargetWorkspace Resolve(
        string harnessRoot, GitHubRepository? repository, string? targetDirectory, GitHubRepository? harnessRepository, string workspacesHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacesHome);

        if (targetDirectory is not null)
        {
            var root = Path.GetFullPath(targetDirectory);
            return new TargetWorkspace(root, repository, repository is not null && IsMissingOrEmpty(root));
        }

        if (repository is null || repository.Matches(harnessRepository))
            return new TargetWorkspace(Path.GetFullPath(harnessRoot), repository ?? harnessRepository, false);

        var workspace = Path.GetFullPath(Path.Combine(workspacesHome, repository.Owner, repository.Name));
        return new TargetWorkspace(workspace, repository, IsMissingOrEmpty(workspace));
    }

    /// <summary><c>gh</c> arguments that clone <paramref name="repository"/> into <paramref name="directory"/>.</summary>
    public static string[] CloneArguments(GitHubRepository repository, string directory) =>
        ["repo", "clone", repository.ToString(), directory];

    /// <summary>Clones <see cref="Repository"/> into <see cref="RootPath"/> with <c>gh repo clone</c>, creating its parent directory.</summary>
    /// <returns>The gh exit code.</returns>
    /// <exception cref="InvalidOperationException">The workspace has no <see cref="Repository"/>.</exception>
    /// <exception cref="ExecutableNotFoundException">gh is not installed or not on the PATH.</exception>
    public Task<int> CloneAsync(
        IProcessRunner processRunner, TextWriter output, TextWriter error, CancellationToken cancellationToken = default,
        string ghExecutable = GitAutomationService.DefaultGhExecutable)
    {
        if (Repository is null)
            throw new InvalidOperationException("No se puede clonar un workspace sin repositorio de GitHub.");

        var parent = Directory.CreateDirectory(Path.GetDirectoryName(RootPath)!).FullName;
        return processRunner.RunAsync(
            new ProcessRequest(ghExecutable, CloneArguments(Repository, RootPath), parent), output.Write, error.Write, cancellationToken);
    }

    private static bool IsMissingOrEmpty(string directory) =>
        !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any();
}
