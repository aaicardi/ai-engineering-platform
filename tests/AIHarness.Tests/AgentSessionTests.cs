using System.Text.Json.Nodes;
using AIHarness.Orchestration;
using AIHarness.Prompting;

namespace AIHarness.Tests;

/// <summary>A harness repository with a minimal agent suite, skills and settings, and an empty target repository.</summary>
internal sealed class SuiteFixture : IDisposable
{
    public string Harness { get; } = Directory.CreateTempSubdirectory("aiharness-suite-").FullName;
    public string Target { get; } = Directory.CreateTempSubdirectory("aiharness-target-").FullName;
    public string RunDirectory => Path.Combine(Target + "-run", "1");

    public SuiteFixture()
    {
        var agents = Directory.CreateDirectory(Path.Combine(Harness, ".claude", "agents")).FullName;
        File.WriteAllText(Path.Combine(agents, "lead.md"),
            "---\nname: lead\ndescription: Coordina\ntools: Agent, Bash, Write\nmodel: sonnet\n---\n# Agent: Lead\ncuerpo-lead");
        File.WriteAllText(Path.Combine(agents, "developer.md"),
            "---\nname: developer\ndescription: Implementa\ntools: Read, Edit\n---\n# Agent: Developer\ncuerpo-dev");
        File.WriteAllText(Path.Combine(agents, "legacy.md"), "# Agent sin frontmatter");

        var skills = Path.Combine(Harness, ".claude", "skills");
        foreach (var skill in new[] { "stack-dotnet", "review-checklist", "openspec-propose" })
        {
            Directory.CreateDirectory(Path.Combine(skills, skill));
            File.WriteAllText(Path.Combine(skills, skill, "SKILL.md"), $"---\nname: {skill}\ndescription: x\n---\n");
        }

        File.WriteAllText(Path.Combine(Harness, ".claude", "settings.json"), """
            { "permissions": {
                "allow": ["Edit"],
                "ask": ["Edit(/.github/**)", "Bash(git checkout:*)"],
                "deny": ["Bash(git push:*)", "Edit(/.git/**)", "Read(~/.ssh/**)", "Read(//etc/shadow)"] } }
            """);
    }

    public AgentSession Prepare(string agent = "lead") => AgentSession.Prepare(Harness, Target, RunDirectory, agent);

    public void Dispose()
    {
        foreach (var dir in new[] { Harness, Target, Target + "-run" })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }
}

public sealed class AgentSessionTests : IDisposable
{
    private readonly SuiteFixture _suite = new();

    public void Dispose() => _suite.Dispose();

    private static JsonNode Json(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    private static List<string> Rules(JsonNode settings, string key) =>
        settings["permissions"]![key]!.AsArray().Select(r => r!.GetValue<string>()).ToList();

    [Fact]
    public void CliArguments_IsolateTheSessionAndPointAtTheRunFiles()
    {
        var session = _suite.Prepare();

        Assert.Equal(
            [
                "-p", "--restricted", "--strict-mcp-config",
                "--tools", "Bash,Read,Edit,Write,Grep,Glob,Agent,Skill,TodoWrite",
                "--permission-mode", "acceptEdits",
                "--settings", session.SettingsPath,
                "--agents", session.AgentsPath,
                "--plugin-dir", session.PluginDirectory,
                "--agent", "lead",
            ],
            session.CliArguments);
        Assert.DoesNotContain("bypassPermissions", session.CliArguments);
        Assert.StartsWith(_suite.RunDirectory, session.SettingsPath);
    }

    [Fact]
    public void Prepare_AgentsJson_HasValidAgentsWithShortNamesBodyToolsAndModel()
    {
        var agents = Json(_suite.Prepare().AgentsPath).AsObject();

        Assert.Equal(["developer", "lead"], agents.Select(a => a.Key).Order());
        var lead = agents["lead"]!;
        Assert.Equal("Coordina", lead["description"]!.GetValue<string>());
        Assert.Equal("# Agent: Lead\ncuerpo-lead", lead["prompt"]!.GetValue<string>());
        Assert.Equal(["Agent", "Bash", "Write"], lead["tools"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal("sonnet", lead["model"]!.GetValue<string>());
        Assert.Null(agents["developer"]!["model"]);
    }

    [Fact]
    public void Prepare_UnknownAgent_Throws()
    {
        var ex = Assert.Throws<AgentNotFoundException>(() => _suite.Prepare("legacy"));

        Assert.Equal(["developer", "lead"], ex.Available);
    }

    [Fact]
    public void Prepare_Settings_AllowStackCommandsAndDenyHarnessRulesAnchoredToTheTarget()
    {
        File.WriteAllText(Path.Combine(_suite.Target, "App.sln"), "");

        var settings = Json(_suite.Prepare().SettingsPath);

        var allow = Rules(settings, "allow");
        Assert.Contains("Bash(git diff:*)", allow);
        Assert.Contains("Bash(dotnet test:*)", allow);
        Assert.DoesNotContain("Bash(npm test:*)", allow);

        var deny = Rules(settings, "deny");
        // `ask` cannot be answered in -p, so it becomes a denial; `/path` is re-anchored to the working directory.
        Assert.Contains("Edit(./.github/**)", deny);
        Assert.Contains("Bash(git checkout:*)", deny);
        Assert.Contains("Edit(./.git/**)", deny);
        Assert.Contains("Read(~/.ssh/**)", deny);
        Assert.Contains("Read(//etc/shadow)", deny);
        Assert.Contains("Bash(git commit:*)", deny);
        Assert.Contains("Bash(gh:*)", deny);
        Assert.Contains("Bash(dotnet ef database update:*)", deny);
        Assert.DoesNotContain(deny, r => r.Contains("(/.", StringComparison.Ordinal));
    }

    [Fact]
    public void Rules_NeverMixWildcardsWithTheTrailingPrefixSyntax()
    {
        // Claude Code reads `Bash(x *y:*)` as the literal prefix `x *y`: the rule would never match.
        File.WriteAllText(Path.Combine(_suite.Target, "package.json"), "{}");
        var settings = Json(_suite.Prepare().SettingsPath);

        var rules = Rules(settings, "allow").Concat(Rules(settings, "deny")).Where(r => r.StartsWith("Bash(", StringComparison.Ordinal));
        Assert.All(rules, r => Assert.False(r.EndsWith(":*)", StringComparison.Ordinal) && r[..^3].Contains('*'), r));
    }

    [Theory]
    [InlineData("Edit(/.claude/**)", "Edit(./.claude/**)")]
    [InlineData("Read(//abs/path)", "Read(//abs/path)")]
    [InlineData("Read(~/.ssh/**)", "Read(~/.ssh/**)")]
    [InlineData("Bash(git diff /*)", "Bash(git diff /*)")]
    public void AnchorToWorkingDirectory_RewritesOnlySettingsRelativePaths(string rule, string expected) =>
        Assert.Equal(expected, AgentSession.AnchorToWorkingDirectory(rule));

    [Fact]
    public void Prepare_Plugin_HoldsOnlySuiteSkillsAndNoAgents()
    {
        var plugin = _suite.Prepare().PluginDirectory;

        Assert.True(File.Exists(Path.Combine(plugin, ".claude-plugin", "plugin.json")));
        Assert.Equal(["review-checklist", "stack-dotnet"],
            Directory.GetDirectories(Path.Combine(plugin, "skills")).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(Path.Combine(plugin, "agents")));
    }

    [Fact]
    public void Prepare_DoesNotModifyTheTargetRepository()
    {
        _suite.Prepare();

        Assert.Empty(Directory.EnumerateFileSystemEntries(_suite.Target));
    }

    [Fact]
    public void Prepare_TargetAgentsAndSettings_AreReportedAsOverriddenAndIgnored()
    {
        var targetClaude = Directory.CreateDirectory(Path.Combine(_suite.Target, ".claude", "agents")).FullName;
        File.WriteAllText(Path.Combine(targetClaude, "developer.md"), "# propio");
        File.WriteAllText(Path.Combine(_suite.Target, ".claude", "settings.json"), "{\"permissions\":{\"allow\":[\"Bash(*)\"]}}");

        var session = _suite.Prepare();

        Assert.Contains(session.Warnings, w => w.Contains("developer") && w.Contains("versión del Harness"));
        Assert.Contains(session.Warnings, w => w.Contains("settings"));
        Assert.DoesNotContain("Bash(*)", Rules(Json(session.SettingsPath), "allow"));
    }
}
