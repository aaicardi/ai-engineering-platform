using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class GitAutomationServiceTests
{
    private static GitAutomationService Service(ScriptedProcessRunner runner, TextWriter? output = null) =>
        new(runner, "/repo", output ?? TextWriter.Null, TextWriter.Null);

    [Theory]
    [InlineData("", true)]
    [InlineData(" M file.cs\n", false)]
    [InlineData("A  new.cs\n", false)]
    public async Task IsWorkingTreeCleanAsync_ReflectsTrackedChangesOnly(string porcelain, bool expected)
    {
        var runner = new ScriptedProcessRunner().On("git status --porcelain --untracked-files=no", stdout: porcelain);

        Assert.Equal(expected, await Service(runner).IsWorkingTreeCleanAsync());
    }

    [Fact]
    public async Task IsWorkingTreeCleanAsync_GitFails_ThrowsWithCommandAndExitCode()
    {
        var runner = new ScriptedProcessRunner().On("git status --porcelain --untracked-files=no", exitCode: 128);

        var ex = await Assert.ThrowsAsync<GitAutomationException>(() => Service(runner).IsWorkingTreeCleanAsync());

        Assert.Equal(128, ex.ExitCode);
        Assert.Contains("git status", ex.Message);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" M file.cs\n", true)]
    [InlineData("?? new.cs\n", true)]
    public async Task HasUncommittedChangesAsync_IncludesUntrackedFiles(string porcelain, bool expected)
    {
        var runner = new ScriptedProcessRunner().On("git status --porcelain", stdout: porcelain);

        Assert.Equal(expected, await Service(runner).HasUncommittedChangesAsync());
    }

    [Fact]
    public async Task CommitAllAsync_StagesEverythingThenCommitsWithTheMessage()
    {
        var runner = new ScriptedProcessRunner();

        var exitCode = await Service(runner).CommitAllAsync("feat(x): it's done (closes #1)");

        Assert.Equal(0, exitCode);
        Assert.Equal(["add", "--all"], runner.Requests[0].Arguments);
        Assert.Equal(["commit", "-m", "feat(x): it's done (closes #1)"], runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task CommitAllAsync_AddFails_DoesNotCommit()
    {
        var runner = new ScriptedProcessRunner().On("git add --all", exitCode: 128);

        Assert.Equal(128, await Service(runner).CommitAllAsync("feat: x"));
        Assert.Equal(["git add --all"], runner.CommandLines);
    }

    [Theory]
    [InlineData("main\n", "main")]
    [InlineData("HEAD\n", null)]
    public async Task GetCurrentBranchAsync_ReturnsNullOnDetachedHead(string stdout, string? expected)
    {
        var runner = new ScriptedProcessRunner().On("git rev-parse --abbrev-ref HEAD", stdout: stdout);

        Assert.Equal(expected, await Service(runner).GetCurrentBranchAsync());
    }

    [Fact]
    public async Task CheckoutFeatureBranchAsync_RelaysGitOutput()
    {
        var runner = new ScriptedProcessRunner()
            .On("git rev-parse --verify --quiet refs/heads/feature/1-x", exitCode: 1)
            .On("git checkout -b feature/1-x", stdout: "Switched to a new branch\n");
        var output = new StringWriter();

        var exitCode = await Service(runner, output).CheckoutFeatureBranchAsync("feature/1-x");

        Assert.Equal(0, exitCode);
        Assert.Equal("Switched to a new branch\n", output.ToString());
    }

    [Fact]
    public async Task CountCommitsAheadAsync_ParsesRevListCount()
    {
        var runner = new ScriptedProcessRunner().On("git rev-list --count main..HEAD", stdout: "4\n");

        Assert.Equal(4, await Service(runner).CountCommitsAheadAsync("main"));
    }

    [Fact]
    public void FormatCommand_QuotesArgumentsThatNeedIt()
    {
        var command = GitAutomationService.FormatCommand("gh", ["pr", "create", "--title", "feat(x): it's done", "--head", "feature/1-x"]);

        Assert.Equal("gh pr create --title 'feat(x): it'\\''s done' --head feature/1-x", command);
    }
}
