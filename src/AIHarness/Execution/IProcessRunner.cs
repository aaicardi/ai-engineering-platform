namespace AIHarness.Execution;

/// <summary>Everything needed to launch a subprocess.</summary>
/// <param name="FileName">Executable name (resolved via PATH) or path.</param>
/// <param name="Arguments">Arguments passed verbatim, without shell interpretation.</param>
/// <param name="WorkingDirectory">Working directory, or <c>null</c> for the current one.</param>
/// <param name="StandardInput">Text written to STDIN before closing it, or <c>null</c> to close it immediately.</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    string? StandardInput = null);

/// <summary>
/// Launches subprocesses and relays their output as it is produced.
/// Abstracts <see cref="System.Diagnostics.Process"/> so callers can be tested without real executables.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs the process to completion and returns its exit code.</summary>
    /// <param name="onStandardOutput">Invoked with each STDOUT chunk as soon as it is read (not line-buffered).</param>
    /// <param name="onStandardError">Invoked with each STDERR chunk as soon as it is read (not line-buffered).</param>
    /// <exception cref="ExecutableNotFoundException">The executable could not be started.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the process tree has been killed.</exception>
    Task<int> RunAsync(
        ProcessRequest request,
        Action<string> onStandardOutput,
        Action<string> onStandardError,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutableNotFoundException(string fileName, Exception inner)
    : Exception($"No se pudo ejecutar '{fileName}': {inner.Message}. Verifique que esté instalado y disponible en el PATH.", inner)
{
    public string FileName { get; } = fileName;
}
