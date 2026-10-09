using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class WorkspaceBootstrapperTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-boot-").FullName;
    private readonly string _templates = Directory.CreateTempSubdirectory("aiharness-agents-").FullName;

    public WorkspaceBootstrapperTests()
    {
        File.WriteAllText(Path.Combine(_templates, "architect.md"), "# Agent: Architect\n");
        File.WriteAllText(Path.Combine(_templates, "developer.md"), "# Agent: Developer\n");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_templates, recursive: true);
    }

    private WorkspaceBootstrapper Bootstrapper => new(_root, _templates);

    private string ClaudeMd => File.ReadAllText(Path.Combine(_root, "CLAUDE.md"));

    [Fact]
    public void Bootstrap_EmptyRepository_ScaffoldsGovernanceAgentsAndSpecsAndAsksForTheInitialArchitecture()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "README.md"), "# shop\n");
        Assert.True(Bootstrapper.IsGreenfield);
        Assert.True(Bootstrapper.IsEmpty);

        var created = Bootstrapper.Bootstrap();

        Assert.Equal(
            ["CLAUDE.md", Path.Combine(".claude", "agents", "architect.md"), Path.Combine(".claude", "agents", "developer.md")],
            created);
        Assert.StartsWith($"# {Path.GetFileName(_root)} — Project Guidelines", ClaudeMd);
        Assert.Contains("Repositorio greenfield", ClaudeMd);
        Assert.Contains("Quality Gate", ClaudeMd);
        Assert.Contains("openspec/specs/", ClaudeMd);
        Assert.True(Directory.Exists(Path.Combine(_root, "openspec", "specs")));
        Assert.Equal("# Agent: Developer\n", File.ReadAllText(Path.Combine(_root, ".claude", "agents", "developer.md")));
        Assert.False(Bootstrapper.IsGreenfield);
    }

    [Fact]
    public void Bootstrap_RepositoryWithCodeButNoGovernance_KeepsItsArchitecture()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Assert.False(Bootstrapper.IsEmpty);

        Bootstrapper.Bootstrap();

        Assert.Contains("Repositorio existente sin gobernanza previa", ClaudeMd);
        Assert.DoesNotContain("Repositorio greenfield", ClaudeMd);
    }

    [Fact]
    public void Bootstrap_ExistingAgentDefinitions_AreNotOverwritten()
    {
        var agents = Directory.CreateDirectory(Path.Combine(_root, ".claude", "agents")).FullName;
        File.WriteAllText(Path.Combine(agents, "developer.md"), "# Custom developer\n");

        var created = Bootstrapper.Bootstrap();

        Assert.DoesNotContain(Path.Combine(".claude", "agents", "developer.md"), created);
        Assert.Equal("# Custom developer\n", File.ReadAllText(Path.Combine(agents, "developer.md")));
    }

    [Fact]
    public void Bootstrap_GovernedRepository_DoesNothing()
    {
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Existing\n");

        Assert.Empty(Bootstrapper.Bootstrap());
        Assert.Equal("# Existing\n", ClaudeMd);
        Assert.False(Directory.Exists(Path.Combine(_root, ".claude")));
    }

    [Fact]
    public void Bootstrap_WithoutTemplates_OnlyScaffoldsGovernance()
    {
        Assert.Equal(["CLAUDE.md"], new WorkspaceBootstrapper(_root).Bootstrap());
        Assert.False(Directory.Exists(Path.Combine(_root, ".claude")));
    }
}
