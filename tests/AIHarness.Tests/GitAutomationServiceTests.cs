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
        Assert.Equal(["add", "--all", "--", ".", ":(exclude).aiharness"], runner.Requests[0].Arguments);
        Assert.Equal(["commit", "-m", "feat(x): it's done (closes #1)"], runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task CommitAllAsync_AddFails_DoesNotCommit()
    {
        var runner = new ScriptedProcessRunner().On("git add --all -- . :(exclude).aiharness", exitCode: 128);

        Assert.Equal(128, await Service(runner).CommitAllAsync("feat: x"));
        Assert.Equal(["git add --all -- . :(exclude).aiharness"], runner.CommandLines);
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

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task HasCommitsAsync_IsFalseOnAnUnbornHead(int exitCode, bool expected)
    {
        var runner = new ScriptedProcessRunner().On("git rev-parse --verify --quiet HEAD", exitCode: exitCode);

        Assert.Equal(expected, await Service(runner).HasCommitsAsync());
    }

    [Fact]
    public async Task SetUnbornBranchAsync_PointsHeadAtTheBranch()
    {
        var runner = new ScriptedProcessRunner();

        Assert.Equal(0, await Service(runner).SetUnbornBranchAsync("main"));
        Assert.Equal(["git symbolic-ref HEAD refs/heads/main"], runner.CommandLines);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public async Task RemoteBranchExistsAsync_MapsLsRemoteExitCode(int exitCode, bool expected)
    {
        var runner = new ScriptedProcessRunner().On("git ls-remote --exit-code --heads origin main", exitCode: exitCode);

        Assert.Equal(expected, await Service(runner).RemoteBranchExistsAsync("main"));
    }

    [Fact]
    public async Task RemoteBranchExistsAsync_RemoteUnreachable_Throws()
    {
        var runner = new ScriptedProcessRunner().On("git ls-remote --exit-code --heads origin main", exitCode: 128);

        var ex = await Assert.ThrowsAsync<GitAutomationException>(() => Service(runner).RemoteBranchExistsAsync("main"));

        Assert.Equal(128, ex.ExitCode);
    }

    [Fact]
    public void PullRequestArguments_BodyFile_ReplacesTheInlineBody()
    {
        var arguments = GitAutomationService.PullRequestArguments(
            new PullRequestRequest("main", "feature/1-x", "feat: x", "Closes #1", BodyFile: "/runs/1/pr-body.md"));

        Assert.Equal(
            ["pr", "create", "--base", "main", "--head", "feature/1-x", "--title", "feat: x", "--body-file", "/runs/1/pr-body.md"],
            arguments);
    }

    [Fact]
    public void PullRequestArguments_TargetRepository_AddsRepoFlag()
    {
        var withoutRepo = GitAutomationService.PullRequestArguments(new PullRequestRequest("main", "feature/1-x", "feat: x", "Closes #1"));
        var withRepo = GitAutomationService.PullRequestArguments(
            new PullRequestRequest("main", "feature/1-x", "feat: x", "Closes #1", new AIHarness.GitHub.GitHubRepository("acme", "shop")));

        Assert.DoesNotContain("--repo", withoutRepo);
        Assert.Equal(withoutRepo.Concat(["--repo", "acme/shop"]), withRepo);
    }
}
