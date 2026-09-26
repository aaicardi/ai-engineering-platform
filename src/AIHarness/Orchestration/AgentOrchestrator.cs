using AIHarness.Execution;
using AIHarness.Prompting;

namespace AIHarness.Orchestration;

/// <summary>Options for <see cref="AgentOrchestrator"/>.</summary>
/// <param name="BaseBranch">Branch the Pull Request targets.</param>
/// <param name="CreatePullRequest">
/// Push the branch and run <c>gh pr create</c>. Pushing requires explicit human approval (CLAUDE.md §3),
/// so it is opt-in; otherwise the commands are only prepared and printed.
/// </param>
public sealed record OrchestrationOptions(string BaseBranch = "main", bool CreatePullRequest = false);

/// <summary>Lifecycle stage an orchestration stopped at; <see cref="Completed"/> when every stage succeeded.</summary>
public enum OrchestrationStage { Preflight, Branch, Agent, Tests, PullRequest, Completed }

public sealed record OrchestrationResult(OrchestrationStage Stage, int ExitCode, string? Branch = null)
{
    public bool Succeeded => Stage == OrchestrationStage.Completed;
}

/// <summary>
/// Runs an agent inside a managed Git lifecycle: clean working tree → feature branch
/// <c>feature/&lt;issue&gt;-&lt;slug&gt;</c> → agent → <c>dotnet test</c> → Pull Request.
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

    /// <exception cref="OperationCanceledException">Cancelled; the running subprocess has been killed.</exception>
    public async Task<OrchestrationResult> RunAsync(PromptContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stage = OrchestrationStage.Preflight;
        string? branch = null;
        try
        {
            var spec = LoadSpec(context.SpecFileName);
            branch = spec.BranchName;

            if (!await _git.IsWorkingTreeCleanAsync(cancellationToken))
                return Fail(stage, 1, branch, "El directorio de trabajo tiene cambios sin confirmar; haga commit o stash antes de continuar.");

            stage = OrchestrationStage.Branch;
            Info($"Rama de trabajo: {branch}");
            var exitCode = await _git.CheckoutFeatureBranchAsync(branch, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, $"No se pudo crear/cambiar a la rama {branch}.");

            stage = OrchestrationStage.Agent;
            Info($"Ejecutando agente '{context.AgentName}' con {context.SpecFileName}...");
            exitCode = await new AgentRunner(processRunner, rootPath, output, error).RunAsync(context, cancellationToken);
            if (exitCode != 0)
                return Fail(stage, exitCode, branch, $"El agente finalizó con código de salida {exitCode}.");

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

    private SpecMetadata LoadSpec(string specFileName)
    {
        var path = Path.Combine(rootPath, "openspec", "specs", specFileName);
        return SpecMetadata.Parse(specFileName, File.ReadAllText(path));
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
