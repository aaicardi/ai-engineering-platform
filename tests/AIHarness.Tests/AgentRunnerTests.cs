using AIHarness.Execution;
using AIHarness.Prompting;

namespace AIHarness.Tests;

public sealed class AgentRunnerTests
{
    private static readonly PromptContext Context = new("developer", "007-agent-runner.md", "# Prompt unificado\ncontenido");

    private sealed class FakeProcessRunner(int exitCode = 0, string[]? stdout = null, string[]? stderr = null) : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<int> RunAsync(ProcessRequest request, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken = default)
        {
            Request = request;
            Token = cancellationToken;
            foreach (var chunk in stdout ?? []) onStandardOutput(chunk);
            foreach (var chunk in stderr ?? []) onStandardError(chunk);
            return Task.FromResult(exitCode);
        }
    }

    [Fact]
    public async Task RunAsync_LaunchesClaudeCliWithPromptOnStandardInput()
    {
        var fake = new FakeProcessRunner();

        await new AgentRunner(fake, "/repo", TextWriter.Null, TextWriter.Null).RunAsync(Context);

        Assert.NotNull(fake.Request);
        Assert.Equal("claude", fake.Request.FileName);
        Assert.Equal(["-p"], fake.Request.Arguments);
        Assert.Equal("/repo", fake.Request.WorkingDirectory);
        Assert.Equal(Context.Prompt, fake.Request.StandardInput);
    }

    [Fact]
    public async Task RunAsync_CustomExecutable_IsUsed()
    {
        var fake = new FakeProcessRunner();

        await new AgentRunner(fake, "/repo", TextWriter.Null, TextWriter.Null, "/opt/claude/bin/claude").RunAsync(Context);

        Assert.Equal("/opt/claude/bin/claude", fake.Request?.FileName);
    }

    [Fact]
    public async Task RunAsync_RelaysStdoutAndStderrChunksToTheirWriters()
    {
        var fake = new FakeProcessRunner(stdout: ["Hola ", "mundo\n"], stderr: ["aviso\n"]);
        var output = new StringWriter();
        var error = new StringWriter();

        await new AgentRunner(fake, "/repo", output, error).RunAsync(Context);

        Assert.Equal("Hola mundo\n", output.ToString());
        Assert.Equal("aviso\n", error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public async Task RunAsync_ReturnsProcessExitCode(int exitCode)
    {
        var result = await new AgentRunner(new FakeProcessRunner(exitCode), "/repo", TextWriter.Null, TextWriter.Null).RunAsync(Context);

        Assert.Equal(exitCode, result);
    }

    [Fact]
    public async Task RunAsync_ForwardsCancellationToken()
    {
        var fake = new FakeProcessRunner();
        using var cts = new CancellationTokenSource();

        await new AgentRunner(fake, "/repo", TextWriter.Null, TextWriter.Null).RunAsync(Context, cts.Token);

        Assert.Equal(cts.Token, fake.Token);
    }

    [Fact]
    public async Task RunAsync_NullContext_Throws()
    {
        var runner = new AgentRunner(new FakeProcessRunner(), "/repo", TextWriter.Null, TextWriter.Null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!));
    }
}
