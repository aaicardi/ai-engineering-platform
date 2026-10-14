using AIHarness.Cli;

namespace AIHarness.Tests;

/// <summary>Serializes tests that redirect the process-wide <see cref="Console.Out"/>.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}

/// <summary>Invokes the AIHarness entry point in-process and captures its standard output.</summary>
internal static class CliHost
{
    public static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var (exitCode, output, _) = await RunWithStderrAsync(args);
        return (exitCode, output);
    }

    /// <summary>Like <see cref="RunAsync"/> but also captures STDERR, to assert RF-04's STDOUT/STDERR split.</summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunWithStderrAsync(params string[] args)
    {
        var entryPoint = typeof(VersionInfo).Assembly.EntryPoint!;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var result = entryPoint.Invoke(null, [args]);
            var exitCode = result is Task<int> task ? await task : (int)result!;
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Runs with the process's current directory temporarily set to <paramref name="directory"/>, to exercise
    /// commands (like <c>--health</c> without <c>--root</c>) that fall back to <see cref="Directory.GetCurrentDirectory"/>.
    /// <see cref="Directory.CurrentDirectory"/> is process-wide state, so callers must stay in <see cref="ConsoleCollection"/>
    /// (which disables parallelization) just like the STDOUT/STDERR redirection above.
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunInDirectoryAsync(string directory, params string[] args)
    {
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(directory);
        try
        {
            return await RunWithStderrAsync(args);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }
}
