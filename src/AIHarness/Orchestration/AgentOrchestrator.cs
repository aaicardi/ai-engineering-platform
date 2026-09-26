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
public sealed record OrchestrationOptions(string BaseBranch = "main", bool CreatePullRequest = false);

/// <summary>Lifecycle stage an orchestration stopped at; <see cref="Completed"/> when every stage succeeded.</summary>
public enum OrchestrationStage { Preflight, Branch, Spec, Agent, Commit, Tests, PullRequest, Completed }

public sealed record OrchestrationResult(OrchestrationStage Stage, int ExitCode, string? Branch = null)
{
    public bool Succeeded => Stage == OrchestrationStage.Completed;
}

/// <summary>
/// Runs an agent inside a managed Git lifecycle: clean working tree → feature branch
/// <c>feature/&lt;issue&gt;-&lt;slug&gt;</c> → spec/prompt → agent → auto-commit → <c>dotnet test</c> → Pull Request.
/// Stops at the first failing stage. Progress is logged to the error writer; subprocess output is relayed as produced.
/// </summary>
public sealed class AgentOrchestrator
{
    public const string DotnetExecutable = "dotnet";
    public static readonly IReadOnlyList<string> TestArguments = ["test"];

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

            stage = OrchestrationStage.Branch;
            Info($"Rama de trabajo: {branch}");
            var exitCode = await _git.CheckoutFeatureBranchAsync(branch, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, $"No se pudo crear/cambiar a la rama {branch}.");

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
            Info($"Ejecutando '{GitAutomationService.FormatCommand(DotnetExecutable, TestArguments)}'...");
            exitCode = await processRunner.RunAsync(
                new ProcessRequest(DotnetExecutable, TestArguments, rootPath), output.Write, error.Write, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, "Las pruebas fallaron; no se prepara el Pull Request.");

            stage = OrchestrationStage.PullRequest;
            exitCode = await PublishAsync(spec, context.SpecFileName, branch, cancellationToken);
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

    private async Task<int> PublishAsync(SpecMetadata spec, string specFileName, string branch, CancellationToken cancellationToken)
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
            $"Closes #{spec.IssueNumber}\n\nImplementa `openspec/specs/{specFileName}` (generado por AI Harness).");

        if (!_options.CreatePullRequest)
        {
            // Pushing needs explicit human approval: hand the prepared commands to the operator.
            Info("Pull Request preparado. Para publicarlo (requiere aprobación humana), ejecute:");
            error.WriteLine($"  {GitAutomationService.FormatCommand(GitAutomationService.DefaultGitExecutable, GitAutomationService.PushArguments(branch))}");
            error.WriteLine($"  {GitAutomationService.FormatCommand(GitAutomationService.DefaultGhExecutable, GitAutomationService.PullRequestArguments(request))}");
            return 0;
        }

        Info($"Publicando {branch} en {GitAutomationService.Remote}...");
        var exitCode = await _git.PushAsync(branch, cancellationToken);
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
