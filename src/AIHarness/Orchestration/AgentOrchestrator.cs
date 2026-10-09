using AIHarness.Execution;
using AIHarness.GitHub;
using AIHarness.Prompting;
using AIHarness.Specs;

namespace AIHarness.Orchestration;

/// <summary>Options for <see cref="AgentOrchestrator"/>.</summary>
/// <param name="BaseBranch">Branch the Pull Request targets.</param>
/// <param name="CreatePullRequest">
/// Push the branch and run <c>gh pr create</c>. Pushing requires explicit human approval (CLAUDE.md §3),
/// so it is opt-in; otherwise the commands are only prepared and printed.
/// </param>
/// <param name="Repository">GitHub repository the Pull Request is opened on; <c>null</c> lets gh infer it from the working copy.</param>
/// <param name="AgentTemplatesDirectory">Agent definitions copied into a greenfield repository when it is bootstrapped.</param>
public sealed record OrchestrationOptions(
    string BaseBranch = "main",
    bool CreatePullRequest = false,
    GitHubRepository? Repository = null,
    string? AgentTemplatesDirectory = null);

/// <summary>Lifecycle stage an orchestration stopped at; <see cref="Completed"/> when every stage succeeded.</summary>
public enum OrchestrationStage { Preflight, Bootstrap, Branch, Spec, Agent, Commit, Tests, PullRequest, Completed }

public sealed record OrchestrationResult(OrchestrationStage Stage, int ExitCode, string? Branch = null)
{
    public bool Succeeded => Stage == OrchestrationStage.Completed;
}

/// <summary>
/// Runs an agent inside a managed Git lifecycle on the repository at <c>rootPath</c> (the harness's own or an external
/// one): clean working tree → greenfield bootstrap → feature branch <c>feature/&lt;issue&gt;-&lt;slug&gt;</c> → spec/prompt
/// → agent → auto-commit → the repository's <see cref="QualityGate"/> → Pull Request.
/// Stops at the first failing stage. Progress is logged to the error writer; subprocess output is relayed as produced.
/// </summary>
public sealed class AgentOrchestrator
{
    /// <summary>Commit that records the bootstrap scaffold on the base branch of a repository without commits.</summary>
    public const string BootstrapCommitMessage = "chore: bootstrap AI Harness governance";

    private readonly IProcessRunner processRunner;
    private readonly string rootPath;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly OrchestrationOptions _options;
    private readonly GitAutomationService _git;

    public AgentOrchestrator(IProcessRunner processRunner, string rootPath, TextWriter output, TextWriter error, OrchestrationOptions? options = null)
    {
        this.processRunner = processRunner;
        this.rootPath = rootPath;
        this.output = output;
        this.error = error;
        _options = options ?? new OrchestrationOptions();
        _git = new GitAutomationService(processRunner, rootPath, output, error);
    }

    /// <summary>Runs the lifecycle for an existing spec (<c>--orchestrate</c>).</summary>
    /// <exception cref="OperationCanceledException">Cancelled; the running subprocess has been killed.</exception>
    public Task<OrchestrationResult> RunAsync(PromptContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RunPipelineAsync(() => LoadSpec(context.SpecFileName), () => context, cancellationToken);
    }

    /// <summary>
    /// Runs the lifecycle for a GitHub Issue (<c>--process-issue</c>): the spec is generated on the feature branch
    /// (or reused when it already exists, so a run can be resumed) and <paramref name="buildContext"/> turns its
    /// file name into the agent prompt; returning <c>null</c> fails the <see cref="OrchestrationStage.Spec"/> stage.
    /// </summary>
    /// <exception cref="OperationCanceledException">Cancelled; the running subprocess has been killed.</exception>
    public Task<OrchestrationResult> ProcessIssueAsync(GitHubIssue issue, Func<string, PromptContext?> buildContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(buildContext);
        return RunPipelineAsync(() => SpecMetadata.FromIssue(issue), () => buildContext(GenerateSpec(issue)), cancellationToken);
    }

    /// <summary>Commit message for the agent's changes: the spec title (a Conventional Commit header) closing its Issue.</summary>
    public static string CommitMessage(SpecMetadata spec) => $"{spec.Title} (closes #{spec.IssueNumber})";

    private async Task<OrchestrationResult> RunPipelineAsync(
        Func<SpecMetadata> resolveSpec, Func<PromptContext?> prepareContext, CancellationToken cancellationToken)
    {
        var stage = OrchestrationStage.Preflight;
        string? branch = null;
        try
        {
            var spec = resolveSpec();
            branch = spec.BranchName;

            if (!await _git.IsWorkingTreeCleanAsync(cancellationToken))
                return Fail(stage, 1, branch, "El directorio de trabajo tiene cambios sin confirmar; haga commit o stash antes de continuar.");

            int exitCode;
            // A repository without commits (e.g. freshly created on GitHub) has no base for the feature branch:
            // its bootstrap becomes the first commit of the base branch.
            var baseBranchCreated = !await _git.HasCommitsAsync(cancellationToken);
            if (baseBranchCreated)
            {
                stage = OrchestrationStage.Bootstrap;
                exitCode = await CreateBaseBranchAsync(cancellationToken);
                if (exitCode != 0)
                    return Fail(stage, exitCode, branch, $"No se pudo crear la rama base {_options.BaseBranch} con el bootstrap.");
            }

            stage = OrchestrationStage.Branch;
            Info($"Rama de trabajo: {branch}");
            exitCode = await _git.CheckoutFeatureBranchAsync(branch, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, $"No se pudo crear/cambiar a la rama {branch}.");

            // After the checkout, so a resumed feature branch that already carries the scaffold is not bootstrapped
            // again; the scaffold is committed together with the agent's changes.
            stage = OrchestrationStage.Bootstrap;
            Bootstrap();

            stage = OrchestrationStage.Spec;
            var context = prepareContext();
            if (context is null)
                return Fail(stage, 1, branch, "No se pudo construir el prompt del agente.");
            // Title and Issue for the commit and the PR come from the spec actually implemented.
            spec = LoadSpec(context.SpecFileName);

            stage = OrchestrationStage.Agent;
            Info($"Ejecutando agente '{context.AgentName}' con {context.SpecFileName}...");
            exitCode = await new AgentRunner(processRunner, rootPath, output, error).RunAsync(context, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, $"El agente finalizó con código de salida {exitCode}.");

            stage = OrchestrationStage.Commit;
            if (await _git.HasUncommittedChangesAsync(cancellationToken))
            {
                var message = CommitMessage(spec);
                Info($"Confirmando los cambios del agente: {message}");
                exitCode = await _git.CommitAllAsync(message, cancellationToken);
                if (exitCode != 0)
                    return Fail(stage, exitCode, branch, "No se pudieron confirmar los cambios del agente.");
            }
            else
            {
                Info("El agente no dejó cambios pendientes de confirmar.");
            }

            stage = OrchestrationStage.Tests;
            // Detected after the agent ran: in a greenfield repository the agent is the one that creates it.
            var qualityGate = QualityGate.Detect(rootPath);
            if (qualityGate is null)
            {
                Warn("No se detectó un Quality Gate (solución .NET, script 'test' de package.json o target 'test' del Makefile); se omite la validación.");
            }
            else
            {
                Info($"Ejecutando el Quality Gate '{qualityGate}'...");
                exitCode = await processRunner.RunAsync(
                    new ProcessRequest(qualityGate.Executable, qualityGate.Arguments, rootPath), output.Write, error.Write, cancellationToken);
                if (exitCode != 0)
                    return Fail(stage, exitCode, branch, "El Quality Gate falló; no se prepara el Pull Request.");
            }

            stage = OrchestrationStage.PullRequest;
            exitCode = await PublishAsync(spec, context.SpecFileName, branch, baseBranchCreated, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, "No se pudo crear el Pull Request.");

            return new OrchestrationResult(OrchestrationStage.Completed, 0, branch);
        }
        catch (Exception ex) when (ex is GitAutomationException or ExecutableNotFoundException or InvalidDataException or IOException)
        {
            return Fail(stage, ex is ExecutableNotFoundException ? 127 : 1, branch, ex.Message);
        }
    }

    private string SpecsDirectory => Path.Combine(rootPath, SpecGenerator.DefaultSpecsRelativePath);

    private SpecMetadata LoadSpec(string specFileName) =>
        SpecMetadata.Parse(specFileName, File.ReadAllText(Path.Combine(SpecsDirectory, specFileName)));

    /// <returns>File name of the generated spec, or of the existing one for the same Issue.</returns>
    private string GenerateSpec(GitHubIssue issue)
    {
        try
        {
            var spec = new SpecGenerator(SpecsDirectory).Generate(issue);
            Info($"Especificación generada: {spec.FilePath}");
            return spec.FileName;
        }
        catch (SpecAlreadyExistsException ex)
        {
            Info($"Se reutiliza la especificación existente: {ex.ExistingPath}");
            return Path.GetFileName(ex.ExistingPath);
        }
    }

    /// <summary>Scaffolds the governance of a greenfield repository; does nothing when it already has a CLAUDE.md.</summary>
    private void Bootstrap()
    {
        var created = new WorkspaceBootstrapper(rootPath, _options.AgentTemplatesDirectory).Bootstrap();
        if (created.Count > 0)
            Info($"Repositorio greenfield: se generó la gobernanza inicial ({string.Join(", ", created)}).");
    }

    /// <summary>Records the bootstrap as the first commit of the base branch of a repository without commits.</summary>
    /// <returns>The exit code of the first git command that failed, or 0.</returns>
    private async Task<int> CreateBaseBranchAsync(CancellationToken cancellationToken)
    {
        Bootstrap();
        Info($"Repositorio sin commits: se crea {_options.BaseBranch} con el bootstrap como primer commit.");
        var exitCode = await _git.SetUnbornBranchAsync(_options.BaseBranch, cancellationToken);
        return exitCode != 0 ? exitCode : await _git.CommitAllAsync(BootstrapCommitMessage, cancellationToken);
    }

    private async Task<int> PublishAsync(
        SpecMetadata spec, string specFileName, string branch, bool baseBranchCreated, CancellationToken cancellationToken)
    {
        if (await _git.CountCommitsAheadAsync(_options.BaseBranch, cancellationToken) == 0)
        {
            Warn($"La rama {branch} no tiene commits respecto a {_options.BaseBranch}; no hay nada que publicar.");
            return 0;
        }

        var request = new PullRequestRequest(
            _options.BaseBranch,
            branch,
            spec.Title,
            $"Closes #{spec.IssueNumber}\n\nImplementa `openspec/specs/{specFileName}` (generado por AI Harness).",
            _options.Repository);

        if (!_options.CreatePullRequest)
        {
            // Pushing needs explicit human approval: hand the prepared commands to the operator.
            Info("Pull Request preparado. Para publicarlo (requiere aprobación humana), ejecute:");
            if (baseBranchCreated)
                error.WriteLine($"  {GitAutomationService.FormatCommand(GitAutomationService.DefaultGitExecutable, GitAutomationService.PushArguments(_options.BaseBranch))}");
            error.WriteLine($"  {GitAutomationService.FormatCommand(GitAutomationService.DefaultGitExecutable, GitAutomationService.PushArguments(branch))}");
            error.WriteLine($"  {GitAutomationService.FormatCommand(GitAutomationService.DefaultGhExecutable, GitAutomationService.PullRequestArguments(request))}");
            return 0;
        }

        int exitCode;
        // A bootstrapped empty repository has no base branch on the remote yet; the PR needs one to target.
        if (baseBranchCreated && !await _git.RemoteBranchExistsAsync(_options.BaseBranch, cancellationToken))
        {
            Info($"Publicando la rama base {_options.BaseBranch} en {GitAutomationService.Remote}...");
            exitCode = await _git.PushAsync(_options.BaseBranch, cancellationToken);
            if (exitCode != 0)
                return exitCode;
        }

        Info($"Publicando {branch} en {GitAutomationService.Remote}...");
        exitCode = await _git.PushAsync(branch, cancellationToken);
        if (exitCode != 0)
            return exitCode;

        Info("Creando Pull Request...");
        return await _git.CreatePullRequestAsync(request, cancellationToken);
    }

    private OrchestrationResult Fail(OrchestrationStage stage, int exitCode, string? branch, string message)
    {
        error.WriteLine($"[ERROR] [{stage}] {message}");
        return new OrchestrationResult(stage, exitCode, branch);
    }

    private void Info(string message) => error.WriteLine($"[INFO] {message}");

    private void Warn(string message) => error.WriteLine($"[WARN] {message}");
}
