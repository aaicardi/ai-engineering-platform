using AIHarness.Prompting;

namespace AIHarness.Tests;

public sealed class ContextBuilderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-tests-").FullName;

    public ContextBuilderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".claude", "agents"));
        Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Governance\nregla-global");
        File.WriteAllText(Path.Combine(_root, ".claude", "agents", "developer.md"), "# Agent: Developer\nregla-agente");
        File.WriteAllText(Path.Combine(_root, "openspec", "specs", "003-demo.md"), "# OpenSpec 003\nregla-spec");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ContextBuilder Builder => new(_root);

    [Theory]
    [InlineData("003-demo.md")]
    [InlineData("003-demo")]
    public void Build_MergesThreeLevelsInOrder(string specName)
    {
        var context = Builder.Build("developer", specName);

        Assert.Equal("developer", context.AgentName);
        Assert.Equal("003-demo.md", context.SpecFileName);
        var governance = context.Prompt.IndexOf("regla-global", StringComparison.Ordinal);
        var agent = context.Prompt.IndexOf("regla-agente", StringComparison.Ordinal);
        var spec = context.Prompt.IndexOf("regla-spec", StringComparison.Ordinal);
        Assert.True(governance >= 0 && governance < agent && agent < spec);
    }

    [Fact]
    public void Build_UnknownAgent_ThrowsAgentNotFoundWithAvailableAgents()
    {
        var ex = Assert.Throws<AgentNotFoundException>(() => Builder.Build("ghost", "003-demo.md"));

        Assert.Equal("ghost", ex.AgentName);
        Assert.Equal(["developer"], ex.Available);
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public void Build_UnknownSpec_ThrowsSpecNotFound()
    {
        var ex = Assert.Throws<SpecNotFoundException>(() => Builder.Build("developer", "999-missing.md"));

        Assert.Equal("999-missing.md", ex.SpecName);
        Assert.EndsWith("999-missing.md", ex.FileName);
    }

    [Fact]
    public void Build_MissingClaudeMd_ThrowsGovernanceNotFound()
    {
        File.Delete(Path.Combine(_root, "CLAUDE.md"));

        Assert.Throws<GovernanceNotFoundException>(() => Builder.Build("developer", "003-demo.md"));
    }

    [Fact]
    public void Build_MissingAgentsDirectory_ThrowsAgentNotFoundWithNoAvailable()
    {
        Directory.Delete(Path.Combine(_root, ".claude", "agents"), recursive: true);

        var ex = Assert.Throws<AgentNotFoundException>(() => Builder.Build("developer", "003-demo.md"));
        Assert.Empty(ex.Available);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../CLAUDE")]
    [InlineData("sub/developer")]
    [InlineData("..")]
    public void Build_InvalidAgentName_ThrowsArgumentException(string agentName)
    {
        Assert.ThrowsAny<ArgumentException>(() => Builder.Build(agentName, "003-demo.md"));
    }

    [Theory]
    [InlineData("../../CLAUDE.md")]
    [InlineData("..\\secret.md")]
    public void Build_InvalidSpecName_ThrowsArgumentException(string specName)
    {
        Assert.ThrowsAny<ArgumentException>(() => Builder.Build("developer", specName));
    }

    [Fact]
    public void Export_CreatesDirectoryAndWritesPrompt()
    {
        var context = Builder.Build("developer", "003-demo.md");
        var output = Path.Combine(_root, ContextBuilder.DefaultOutputRelativePath);

        Builder.Export(context, output);

        Assert.Equal(context.Prompt, File.ReadAllText(output));
    }
}
