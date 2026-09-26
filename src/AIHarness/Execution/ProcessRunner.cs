using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AIHarness.Execution;

/// <summary><see cref="IProcessRunner"/> backed by <see cref="Process"/>.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        ProcessRequest request,
        Action<string> onStandardOutput,
        Action<string> onStandardError,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (request.WorkingDirectory is not null)
            startInfo.WorkingDirectory = request.WorkingDirectory;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ExecutableNotFoundException(request.FileName, ex);
        }

        using var registration = cancellationToken.Register(() => KillTree(process));

        // Pumps start before writing STDIN so a child that writes while reading never deadlocks on a full pipe.
        var stdout = PumpAsync(process.StandardOutput, onStandardOutput);
        var stderr = PumpAsync(process.StandardError, onStandardError);

        try
        {
            if (request.StandardInput is not null)
                await process.StandardInput.WriteAsync(request.StandardInput);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child exited without consuming all of STDIN; its exit code tells the story.
        }

        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync(CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> sink)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            sink(new string(buffer, 0, read));
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
