using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIHarness.Prompting;

namespace AIHarness.Orchestration;

/// <summary>
/// A <c>claude -p</c> session that runs the harness's agent suite on a target repository without modifying it and
/// isolated from its Claude Code configuration (ADR-001). <see cref="Prepare"/> writes, in a run directory outside the
/// target repository, the permissions (<c>settings.json</c>), the agents (<c>agents.json</c>) and a plugin holding the
/// suite's skills; <see cref="CliArguments"/> points the CLI at them.
/// </summary>
public sealed partial class AgentSession
{
    public const string SettingsFileName = "settings.json";
    public const string AgentsFileName = "agents.json";
    public const string PluginDirectoryName = "plugin";
    public const string PluginName = "aiharness";

    /// <summary>Built-in tools the session may use; <c>--restricted</c> removes Bash unless it is listed.</summary>
    public static readonly IReadOnlyList<string> BuiltInTools =
        ["Bash", "Read", "Edit", "Write", "Grep", "Glob", "Agent", "Skill", "TodoWrite"];

    /// <summary>Read-only git commands every agent may run (the harness's own <c>deny</c> rules still narrow them).</summary>
    public static readonly IReadOnlyList<string> BaseAllowedRules =
        ["Edit", "Write", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)"];

    /// <summary>
    /// Denied on top of the harness's <c>.claude/settings.json</c>: the harness owns commits, pushes and the Pull Request,
    /// and migrations are applied only with human approval (CLAUDE.md §3).
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysDeniedRules =
    [
        "Bash(git commit:*)", "Bash(git push:*)", "Bash(gh:*)", "Bash(rm -rf:*)",
        "Bash(dotnet ef database update:*)", "Bash(prisma migrate deploy:*)", "Bash(npx prisma migrate deploy:*)",
        "Bash(knex migrate:*)", "Bash(npx knex migrate:*)",
    ];

    private AgentSession(string agentName, string runDirectory, StackProfile stack, IReadOnlyList<string> warnings)
    {
        AgentName = agentName;
        RunDirectory = runDirectory;
        Stack = stack;
        Warnings = warnings;
    }

    public string AgentName { get; }

    /// <summary>Directory holding the session files, outside the target repository.</summary>
    public string RunDirectory { get; }

    public StackProfile Stack { get; }

    /// <summary>Notes for the operator, e.g. target-repository agents or settings that the session ignores.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public string SettingsPath => Path.Combine(RunDirectory, SettingsFileName);

    public string AgentsPath => Path.Combine(RunDirectory, AgentsFileName);

    public string PluginDirectory => Path.Combine(RunDirectory, PluginDirectoryName);

    /// <summary>Arguments for the Claude Code CLI; the prompt goes to STDIN.</summary>
    public IReadOnlyList<string> CliArguments =>
    [
        "-p", "--restricted", "--strict-mcp-config",
        "--tools", string.Join(',', BuiltInTools),
        "--permission-mode", "acceptEdits",
        "--settings", SettingsPath,
        "--agents", AgentsPath,
        "--plugin-dir", PluginDirectory,
        "--agent", AgentName,
    ];

    /// <summary>Skills of the suite: the spec validator, the review checklist and the stack skills.</summary>
    public static bool IsSuiteSkill(string name) =>
        name is "openspec-validator" or "review-checklist" || name.StartsWith("stack-", StringComparison.Ordinal);

    /// <summary>Writes the session files into <paramref name="runDirectory"/> (created if needed).</summary>
    /// <param name="harnessRoot">Harness repository: source of <c>.claude/agents</c>, <c>.claude/skills</c> and <c>.claude/settings.json</c>.</param>
    /// <param name="targetRoot">Repository the agent works on; its stack decides the allowed build commands.</param>
    /// <exception cref="AgentNotFoundException"><paramref name="agentName"/> is not a valid agent of the suite.</exception>
    /// <exception cref="InvalidDataException">A suite file is a symbolic link, too large, or the settings are not valid JSON.</exception>
    public static AgentSession Prepare(string harnessRoot, string targetRoot, string runDirectory, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var claudeDir = Path.Combine(harnessRoot, ".claude");
        var agents = LoadAgents(Path.Combine(claudeDir, "agents"));
        if (!agents.ContainsKey(agentName))
            throw new AgentNotFoundException(agentName, Path.Combine(claudeDir, "agents", agentName + ".md"), agents.Keys.Order().ToList());

        Directory.CreateDirectory(runDirectory);
        var stack = StackProfile.Detect(targetRoot);
        var session = new AgentSession(agentName, runDirectory, stack, CollectWarnings(targetRoot, agents.Keys));

        File.WriteAllText(session.AgentsPath, AgentsJson(agents));
        File.WriteAllText(session.SettingsPath, SettingsJson(Path.Combine(claudeDir, "settings.json"), stack));
        WritePlugin(Path.Combine(claudeDir, "skills"), session.PluginDirectory);
        return session;
    }

    private static SortedDictionary<string, AgentDefinition> LoadAgents(string agentsDir)
    {
        var agents = new SortedDictionary<string, AgentDefinition>(StringComparer.Ordinal);
        if (!Directory.Exists(agentsDir))
            return agents;
        foreach (var file in Directory.GetFiles(agentsDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var definition = AgentDefinition.Parse(SafeFile.ReadText(file));
            // Only registrable subagents (valid frontmatter) are part of the suite.
            if (definition.Validate(name).Count == 0)
                agents[name] = definition;
        }
        return agents;
    }

    private static List<string> CollectWarnings(string targetRoot, IEnumerable<string> suiteAgents)
    {
        var warnings = new List<string>();
        var targetAgentsDir = Path.Combine(targetRoot, ".claude", "agents");
        var overridden = suiteAgents.Where(a => File.Exists(Path.Combine(targetAgentsDir, a + ".md"))).ToList();
        if (overridden.Count > 0)
            warnings.Add($"El repositorio destino define los agentes {string.Join(", ", overridden)}; se usa la versión del Harness.");

        var targetClaudeDir = Path.Combine(targetRoot, ".claude");
        if (Directory.Exists(targetClaudeDir) && Directory.EnumerateFiles(targetClaudeDir, "settings*.json").Any())
            warnings.Add("Se ignora la configuración de Claude Code del repositorio destino (.claude/settings*.json).");
        return warnings;
    }

    // { "<name>": { "description", "prompt", "tools"?, "model"? } } — the format of `claude --agents`.
    private static string AgentsJson(SortedDictionary<string, AgentDefinition> agents)
    {
        var root = new JsonObject();
        foreach (var (name, agent) in agents)
        {
            var entry = new JsonObject { ["description"] = agent.Description, ["prompt"] = agent.Body };
            if (agent.Tools.Count > 0)
                entry["tools"] = new JsonArray(agent.Tools.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
            if (agent.Model is not null)
                entry["model"] = agent.Model;
            root[name] = entry;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SettingsJson(string harnessSettingsPath, StackProfile stack)
    {
        // In `claude -p` nobody can answer an `ask`, so the harness's `ask` rules are denials too.
        var denied = new List<string>();
        if (File.Exists(harnessSettingsPath))
        {
            try
            {
                var permissions = JsonNode.Parse(SafeFile.ReadText(harnessSettingsPath))?["permissions"];
                foreach (var key in new[] { "deny", "ask" })
                    denied.AddRange(permissions?[key]?.AsArray().Select(r => r?.GetValue<string>()).OfType<string>() ?? []);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new InvalidDataException($"'{harnessSettingsPath}' no es una configuración válida: {ex.Message}", ex);
            }
        }
        denied.AddRange(AlwaysDeniedRules);

        var permissionsNode = new JsonObject
        {
            ["allow"] = ToArray(BaseAllowedRules.Concat(stack.AllowedCommands)),
            ["deny"] = ToArray(denied.Select(AnchorToWorkingDirectory).Distinct(StringComparer.Ordinal)),
        };
        return new JsonObject { ["permissions"] = permissionsNode }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        static JsonArray ToArray(IEnumerable<string> rules) => new(rules.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
    }

    /// <summary>
    /// In a settings file passed with <c>--settings</c>, a rule path starting with a single <c>/</c> is relative to that
    /// file's directory, not to the repository (verified with Claude Code 2.1.283); <c>./</c> anchors it to the working
    /// directory, which is the target repository.
    /// </summary>
    public static string AnchorToWorkingDirectory(string rule) => SettingsRelativePathRegex().Replace(rule, "$1(./");

    private static void WritePlugin(string skillsDir, string pluginDir)
    {
        var manifestDir = Directory.CreateDirectory(Path.Combine(pluginDir, ".claude-plugin")).FullName;
        File.WriteAllText(Path.Combine(manifestDir, "plugin.json"), new JsonObject
        {
            ["name"] = PluginName,
            ["description"] = "AI Harness agent suite skills",
        }.ToJsonString());

        if (!Directory.Exists(skillsDir))
            return;
        foreach (var skillDir in Directory.GetDirectories(skillsDir).Where(d => IsSuiteSkill(Path.GetFileName(d))))
        {
            if (!File.Exists(Path.Combine(skillDir, "SKILL.md")))
                continue;
            var target = Path.Combine(pluginDir, "skills", Path.GetFileName(skillDir));
            foreach (var file in Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(skillDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, SafeFile.ReadText(file));
            }
        }
    }

    // `Tool(/path…)` but not `Tool(//absolute…)`.
    [GeneratedRegex(@"^(\w+)\(/(?!/)")]
    private static partial Regex SettingsRelativePathRegex();
}
