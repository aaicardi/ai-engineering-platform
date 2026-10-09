using AIHarness.GitHub;
using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class TargetWorkspaceTests : IDisposable
{
    private static readonly GitHubRepository Harness = new("aaicardi", "ai-engineering-platform");
    private static readonly GitHubRepository Shop = new("acme", "shop");

    private readonly string _temp = Directory.CreateTempSubdirectory("aiharness-ws-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string HarnessRoot => Path.Combine(_temp, "harness");

    private string WorkspacesHome => Path.Combine(_temp, "workspaces");

    private TargetWorkspace Resolve(GitHubRepository? repository, string? targetDirectory = null) =>
        TargetWorkspace.Resolve(HarnessRoot, repository, targetDirectory, Harness, WorkspacesHome);

    [Fact]
    public void Resolve_NoTarget_WorksOnTheHarnessRepository()
    {
        Assert.Equal(new TargetWorkspace(HarnessRoot, Harness, false), Resolve(null));
    }

    [Fact]
    public void Resolve_RepoIsTheHarnessItself_WorksOnTheHarnessRepository()
    {
        var workspace = Resolve(new GitHubRepository("AAicardi", "AI-Engineering-Platform"));

        Assert.Equal(HarnessRoot, workspace.RootPath);
        Assert.False(workspace.RequiresClone);
    }

    [Fact]
    public void Resolve_ExternalRepo_UsesItsWorkspaceAndClonesItOnFirstUse()
    {
        var expected = Path.Combine(WorkspacesHome, "acme", "shop");

        Assert.Equal(new TargetWorkspace(expected, Shop, true), Resolve(Shop));

        Directory.CreateDirectory(expected);
        File.WriteAllText(Path.Combine(expected, "README.md"), "");
        Assert.Equal(new TargetWorkspace(expected, Shop, false), Resolve(Shop));
    }

    [Fact]
    public void Resolve_TargetDirectory_IsUsedAsIsAndClonedOnlyWhenEmptyAndRepoIsKnown()
    {
        var target = Directory.CreateDirectory(Path.Combine(_temp, "checkout")).FullName;

        Assert.Equal(new TargetWorkspace(target, Shop, true), Resolve(Shop, target));
        Assert.Equal(new TargetWorkspace(target, null, false), Resolve(null, target));

        File.WriteAllText(Path.Combine(target, "CLAUDE.md"), "");
        Assert.False(Resolve(Shop, target).RequiresClone);
    }

    [Fact]
    public void Resolve_RelativeTargetDirectory_IsMadeAbsolute()
    {
        Assert.True(Path.IsPathFullyQualified(Resolve(null, "relative-dir").RootPath));
    }

    [Fact]
    public async Task CloneAsync_RunsGhRepoCloneFromTheCreatedParentDirectory()
    {
        var workspace = Resolve(Shop);
        var runner = new ScriptedProcessRunner();

        var exitCode = await workspace.CloneAsync(runner, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exitCode);
        var clone = Assert.Single(runner.Requests);
        Assert.Equal("gh", clone.FileName);
        Assert.Equal(["repo", "clone", "acme/shop", workspace.RootPath], clone.Arguments);
        Assert.Equal(Path.Combine(WorkspacesHome, "acme"), clone.WorkingDirectory);
        Assert.True(Directory.Exists(clone.WorkingDirectory));
    }

    [Fact]
    public async Task CloneAsync_WithoutRepository_Throws()
    {
        var workspace = new TargetWorkspace(Path.Combine(_temp, "x"), null, true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CloneAsync(new ScriptedProcessRunner(), TextWriter.Null, TextWriter.Null));
    }

    [Theory]
    [InlineData("acme", "shop", true)]
    [InlineData("ACME", "Shop", true)]
    [InlineData("acme", "shop-api", false)]
    public void GitHubRepository_Matches_IsCaseInsensitive(string owner, string name, bool expected)
    {
        Assert.Equal(expected, Shop.Matches(new GitHubRepository(owner, name)));
        Assert.False(Shop.Matches(null));
    }
}
