using AIHarness.Inspection;

namespace AIHarness.Tests;

public sealed class RepositoryInspectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-inspector-").FullName;
    private readonly string _agents;

    public RepositoryInspectorTests()
    {
        _agents = Directory.CreateDirectory(Path.Combine(_root, ".claude", "agents")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "openspec", "specs"));
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Governance\n## 1. Contexto\n");
        File.WriteAllText(Path.Combine(_root, "openspec", "specs", "001-demo.md"), "# OpenSpec 001: Demo\n");
        foreach (var name in RepositoryInspector.ExpectedAgents)
            WriteAgent(name, $"---\nname: {name}\ndescription: Agente {name}\n---\n# Agent\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteAgent(string name, string content) => File.WriteAllText(Path.Combine(_agents, name + ".md"), content);

    private InspectionReport Inspect() => new RepositoryInspector(_root).Inspect();

    private AgentEntry Agent(string name) => Inspect().Agents.Agents.Single(a => a.Name == name);

    [Fact]
    public void Inspect_AllAgentsValid_Succeeds()
    {
        var report = Inspect();

        Assert.True(report.IsSuccessful);
        Assert.All(report.Agents.Agents, a => Assert.True(a.IsValid));
        Assert.Contains("lead", report.Agents.Agents.Select(a => a.Name));
    }

    [Fact]
    public void Inspect_AgentWithoutFrontmatter_FailsWithReason()
    {
        WriteAgent("developer", "# Agent: Developer\n");

        var developer = Agent("developer");

        Assert.True(developer.Present);
        Assert.False(developer.IsValid);
        Assert.Equal(["sin frontmatter"], developer.Problems);
        Assert.False(Inspect().IsSuccessful);
    }

    [Fact]
    public void Inspect_NameMismatch_FailsWithReason()
    {
        WriteAgent("tester", "---\nname: qa\ndescription: Tests\n---\n");

        Assert.Contains(Agent("tester").Problems, p => p.Contains("'qa'"));
    }

    [Fact]
    public void Inspect_SymlinkedAgent_FailsWithReason()
    {
        var target = Path.Combine(_root, "elsewhere.md");
        File.WriteAllText(target, "---\nname: lead\ndescription: x\n---\n");
        File.Delete(Path.Combine(_agents, "lead.md"));
        File.CreateSymbolicLink(Path.Combine(_agents, "lead.md"), target);

        var lead = Agent("lead");

        Assert.False(lead.IsValid);
        Assert.Contains(lead.Problems, p => p.Contains("enlace simbólico"));
    }

    [Fact]
    public void Inspect_MissingAgent_Fails()
    {
        File.Delete(Path.Combine(_agents, "lead.md"));

        var lead = Agent("lead");

        Assert.False(lead.Present);
        Assert.False(Inspect().IsSuccessful);
    }

    [Fact]
    public void Inspect_ExtraAgent_IsReportedAsUnexpectedWithoutFailing()
    {
        WriteAgent("data-engineer", "---\nname: data-engineer\ndescription: x\n---\n");

        var report = Inspect();

        Assert.Equal(["data-engineer"], report.Agents.Unexpected);
        Assert.True(report.IsSuccessful);
    }
}
