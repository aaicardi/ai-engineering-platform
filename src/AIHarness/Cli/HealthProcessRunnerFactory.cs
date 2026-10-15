using AIHarness.Execution;

namespace AIHarness.Cli;

/// <summary>
/// Test seam for <c>--health</c>: <c>RunHealthAsync</c> (<c>Program.cs</c>) builds its <see cref="IProcessRunner"/>
/// through <see cref="Create"/> instead of calling <c>new ProcessRunner()</c> directly, so
/// <c>AIHarness.Tests</c> (via <c>InternalsVisibleTo</c>) can substitute a fake runner and keep the CLI tests fast and
/// hermetic. Mutable process-wide state: tests must restore the default in their <c>Dispose()</c>.
/// </summary>
internal static class HealthProcessRunnerFactory
{
    private static Func<IProcessRunner> _create = static () => new ProcessRunner();

    public static Func<IProcessRunner> Create
    {
        get => _create;
        set => _create = value ?? throw new ArgumentNullException(nameof(value));
    }
}
