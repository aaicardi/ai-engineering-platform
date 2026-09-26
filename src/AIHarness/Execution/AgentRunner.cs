using AIHarness.Prompting;

namespace AIHarness.Execution;

/// <summary>
/// Executes an agent by sending its unified prompt to the Claude Code CLI in non-interactive mode
/// (<c>claude -p</c>, prompt on STDIN) and relaying the CLI output to the given writers in real time.
/// </summary>
/// <remarks>
/// The prompt travels over STDIN rather than argv, so its size is not bound by the OS argument limit.
/// Permissions are not bypassed: the CLI applies the repository's own Claude Code settings.
/// </remarks>
public sealed class AgentRunner(
    IProcessRunner processRunner,
    string workingDirectory,
    TextWriter output,
    TextWriter error,
    string executable = AgentRunner.DefaultExecutable)
{
    public const string DefaultExecutable = "claude";

    public static readonly IReadOnlyList<string> CliArguments = ["-p"];

    /// <summary>Runs the agent and returns the CLI exit code.</summary>
    /// <exception cref="ExecutableNotFoundException">The Claude CLI is not installed or not on the PATH.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the CLI process has been killed.</exception>
    public Task<int> RunAsync(PromptContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = new ProcessRequest(executable, CliArguments, workingDirectory, context.Prompt);
        return processRunner.RunAsync(request, Relay(output), Relay(error), cancellationToken);
    }

    // Flush per chunk so partial lines appear immediately instead of waiting for a newline.
    private static Action<string> Relay(TextWriter writer) => chunk =>
    {
        writer.Write(chunk);
        writer.Flush();
    };
}
