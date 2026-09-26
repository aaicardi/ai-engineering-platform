using System.Diagnostics;
using System.Text;
using AIHarness.Execution;

namespace AIHarness.Tests;

// Uses /bin/sh as a stand-in for the Claude CLI; CI runs on ubuntu-latest.
public sealed class ProcessRunnerTests
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();

    private Task<int> RunShAsync(string script, string? stdin = null, string? workingDirectory = null, CancellationToken cancellationToken = default) =>
        new ProcessRunner().RunAsync(
            new ProcessRequest("/bin/sh", ["-c", script], workingDirectory, stdin),
            chunk => { lock (_stdout) _stdout.Append(chunk); },
            chunk => { lock (_stderr) _stderr.Append(chunk); },
            cancellationToken);

    [Fact]
    public async Task RunAsync_CapturesStdoutStderrAndExitCode()
    {
        var exitCode = await RunShAsync("printf 'salida'; printf 'error' >&2; exit 3");

        Assert.Equal(3, exitCode);
        Assert.Equal("salida", _stdout.ToString());
        Assert.Equal("error", _stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_WritesStandardInputAndClosesIt()
    {
        var prompt = "# Prompt unificado\nlínea con acentos: ñ á\n" + new string('x', 256 * 1024);

        var exitCode = await RunShAsync("cat", stdin: prompt);

        Assert.Equal(0, exitCode);
        Assert.Equal(prompt, _stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_WithoutStandardInput_ClosesItImmediately()
    {
        var exitCode = await RunShAsync("cat; echo fin");

        Assert.Equal(0, exitCode);
        Assert.Equal("fin\n", _stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_UsesWorkingDirectory()
    {
        var dir = Directory.CreateTempSubdirectory("aiharness-tests-");
        try
        {
            await RunShAsync("pwd -P", workingDirectory: dir.FullName);

            Assert.Equal(dir.FullName, _stdout.ToString().TrimEnd());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_StreamsOutputBeforeProcessExits()
    {
        var firstChunk = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var run = new ProcessRunner().RunAsync(
            new ProcessRequest("/bin/sh", ["-c", "printf 'parcial'; sleep 30"]),
            chunk => firstChunk.TrySetResult(chunk),
            _ => { },
            cts.Token);

        // A partial line (no newline) arrives while the process is still running.
        Assert.Equal("parcial", await firstChunk.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(run.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsProcessPromptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunShAsync("sleep 30", cancellationToken: cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelled_DoesNotStartProcess()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"aiharness-{Guid.NewGuid():N}");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunShAsync($"touch '{marker}'", cancellationToken: new CancellationToken(canceled: true)));

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsExecutableNotFound()
    {
        var ex = await Assert.ThrowsAsync<ExecutableNotFoundException>(() =>
            new ProcessRunner().RunAsync(new ProcessRequest("aiharness-no-existe-xyz", []), _ => { }, _ => { }));

        Assert.Equal("aiharness-no-existe-xyz", ex.FileName);
    }
}
