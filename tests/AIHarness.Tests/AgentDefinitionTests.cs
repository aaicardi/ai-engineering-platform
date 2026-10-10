using AIHarness.Prompting;

namespace AIHarness.Tests;

public sealed class AgentDefinitionTests
{
    private const string Full = """
        ---
        name: code-reviewer
        description: "Reviews the diff: bugs, design, tests"
        tools: Read, Grep, Glob, Bash
        model: inherit
        ---

        # Agent: Code-Reviewer
        body
        """;

    [Fact]
    public void Parse_WithFrontmatter_SplitsFieldsAndBody()
    {
        var agent = AgentDefinition.Parse(Full);

        Assert.True(agent.HasFrontmatter);
        Assert.Equal("code-reviewer", agent.Name);
        Assert.Equal("Reviews the diff: bugs, design, tests", agent.Description);
        Assert.Equal(["Read", "Grep", "Glob", "Bash"], agent.Tools);
        Assert.Equal("inherit", agent.Model);
        Assert.Equal("# Agent: Code-Reviewer\nbody", agent.Body);
    }

    [Fact]
    public void Parse_WithoutFrontmatter_WholeContentIsBody()
    {
        const string content = "# Agent: Developer\nregla";

        var agent = AgentDefinition.Parse(content);

        Assert.False(agent.HasFrontmatter);
        Assert.Null(agent.Name);
        Assert.Empty(agent.Tools);
        Assert.Equal(content, agent.Body);
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_WholeContentIsBody()
    {
        const string content = "---\nname: developer\n# Agent: Developer";

        var agent = AgentDefinition.Parse(content);

        Assert.False(agent.HasFrontmatter);
        Assert.Equal(content, agent.Body);
    }

    [Fact]
    public void Parse_CrlfAndBlockListTools()
    {
        var agent = AgentDefinition.Parse("---\r\nname: tester\r\ndescription: Tests\r\ntools:\r\n  - Read\r\n  - Bash\r\n---\r\nbody\r\n");

        Assert.Equal("tester", agent.Name);
        Assert.Equal(["Read", "Bash"], agent.Tools);
        Assert.Equal("body\n", agent.Body);
    }

    [Fact]
    public void Parse_InlineListTools()
    {
        var agent = AgentDefinition.Parse("---\nname: x\ntools: [Read, \"Grep\"]\n---\n");

        Assert.Equal(["Read", "Grep"], agent.Tools);
    }

    [Fact]
    public void Validate_ValidDefinition_HasNoProblems() =>
        Assert.Empty(AgentDefinition.Parse(Full).Validate("code-reviewer"));

    [Fact]
    public void Validate_WithoutFrontmatter_ReportsIt() =>
        Assert.Equal(["sin frontmatter"], AgentDefinition.Parse("# Agent").Validate("developer"));

    [Fact]
    public void Validate_NameMismatchAndMissingDescription_ReportsBoth()
    {
        var problems = AgentDefinition.Parse("---\nname: dev\n---\nbody").Validate("developer");

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("'dev'"));
        Assert.Contains("falta 'description'", problems);
    }
}
