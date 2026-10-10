using AIHarness.Prompting;

namespace AIHarness.Inspection;

public sealed record ClaudeMdAudit(bool Exists, string Path, int LineCount, IReadOnlyList<string> Sections);

/// <param name="Present">The definition file exists.</param>
/// <param name="Problems">Why the file is not a valid Claude Code subagent (see <see cref="AgentDefinition.Validate"/>); empty when valid.</param>
public sealed record AgentEntry(string Name, bool Present, IReadOnlyList<string> Problems)
{
    public bool IsValid => Present && Problems.Count == 0;
}

public sealed record AgentsAudit(bool DirectoryExists, IReadOnlyList<AgentEntry> Agents, IReadOnlyList<string> Unexpected)
{
    public bool AllPresent => DirectoryExists && Agents.All(a => a.IsValid);
}

public sealed record SpecEntry(string FileName, string Title);

public sealed record SpecsAudit(bool DirectoryExists, IReadOnlyList<SpecEntry> Specs);

public sealed record InspectionReport(string RootPath, ClaudeMdAudit ClaudeMd, AgentsAudit Agents, SpecsAudit Specs)
{
    public bool IsSuccessful =>
        ClaudeMd.Exists && Agents.AllPresent && Specs.DirectoryExists && Specs.Specs.Count > 0;
}

/// <summary>
/// Inspects the repository governance artifacts (CLAUDE.md, agents, OpenSpec specs).
/// Performs no console output so it can be reused by other front-ends.
/// </summary>
public sealed class RepositoryInspector(string rootPath)
{
    public static readonly IReadOnlyList<string> ExpectedAgents =
    [
        "lead", "architect", "developer", "tester", "code-reviewer",
        "security-reviewer", "devops", "documentation",
    ];

    public InspectionReport Inspect() =>
        new(rootPath, InspectClaudeMd(), InspectAgents(), InspectSpecs());

    private ClaudeMdAudit InspectClaudeMd()
    {
        var path = Path.Combine(rootPath, "CLAUDE.md");
        if (!File.Exists(path))
            return new ClaudeMdAudit(false, path, 0, []);

        var lines = File.ReadAllLines(path);
        var sections = lines
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l[3..].Trim())
            .ToList();
        return new ClaudeMdAudit(true, path, lines.Length, sections);
    }

    private AgentsAudit InspectAgents()
    {
        var dir = Path.Combine(rootPath, ".claude", "agents");
        if (!Directory.Exists(dir))
            return new AgentsAudit(false, ExpectedAgents.Select(Missing).ToList(), []);

        var found = Directory.GetFiles(dir, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var agents = ExpectedAgents
            .Select(a => found.Contains(a)
                ? new AgentEntry(a, true, AgentDefinition.Parse(File.ReadAllText(Path.Combine(dir, a + ".md"))).Validate(a))
                : Missing(a))
            .ToList();
        var unexpected = found.Except(ExpectedAgents, StringComparer.Ordinal).Order().ToList();
        return new AgentsAudit(true, agents, unexpected);

        static AgentEntry Missing(string name) => new(name, false, ["no existe"]);
    }

    private SpecsAudit InspectSpecs()
    {
        var dir = Path.Combine(rootPath, "openspec", "specs");
        if (!Directory.Exists(dir))
            return new SpecsAudit(false, []);

        var specs = Directory.GetFiles(dir, "*.md")
            .Order(StringComparer.Ordinal)
            .Select(f => new SpecEntry(Path.GetFileName(f), ReadTitle(f)))
            .ToList();
        return new SpecsAudit(true, specs);
    }

    private static string ReadTitle(string file)
    {
        var heading = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
        return heading is null ? "(sin título)" : heading[2..].Trim();
    }

    /// <summary>Walks up from <paramref name="start"/> until a directory containing CLAUDE.md is found.</summary>
    public static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
                return dir.FullName;
        }
        return null;
    }
}
