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
        var entryPoint = typeof(VersionInfo).Assembly.EntryPoint!;
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var result = entryPoint.Invoke(null, [args]);
            var exitCode = result is Task<int> task ? await task : (int)result!;
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
