using System.Text;

namespace AIHarness.Prompting;

/// <summary>
/// File-system implementation of <see cref="IContextBuilder"/> rooted at the repository directory.
/// Performs no console output so it can be reused by other front-ends.
/// </summary>
public sealed class ContextBuilder(string rootPath) : IContextBuilder
{
    public static readonly string DefaultOutputRelativePath = Path.Combine(".claude", "tmp", "current-prompt.md");

    public PromptContext Build(string agentName, string specName)
    {
        ValidateName(agentName, nameof(agentName));
        ValidateName(specName, nameof(specName));

        var claudeMdPath = Path.Combine(rootPath, "CLAUDE.md");
        if (!File.Exists(claudeMdPath))
            throw new GovernanceNotFoundException(claudeMdPath);

        var agentsDir = Path.Combine(rootPath, ".claude", "agents");
        var agentPath = Path.Combine(agentsDir, agentName + ".md");
        if (!File.Exists(agentPath))
            throw new AgentNotFoundException(agentName, agentPath, ListAgents(agentsDir));

        var specFileName = specName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? specName : specName + ".md";
        var specPath = Path.Combine(rootPath, "openspec", "specs", specFileName);
        if (!File.Exists(specPath))
            throw new SpecNotFoundException(specName, specPath);

        var prompt = new StringBuilder()
            .AppendLine($"# Prompt unificado — Agente: {agentName} | Spec: {specFileName}")
            .AppendLine()
            .AppendSection("1. Gobernanza (CLAUDE.md)", File.ReadAllText(claudeMdPath))
            .AppendSection($"2. Agente ({agentName})", File.ReadAllText(agentPath))
            .AppendSection($"3. Especificación ({specFileName})", File.ReadAllText(specPath))
            .ToString();

        return new PromptContext(agentName, specFileName, prompt);
    }

    public void Export(PromptContext context, string outputPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, context.Prompt);
    }

    private static IReadOnlyList<string> ListAgents(string agentsDir) =>
        Directory.Exists(agentsDir)
            ? Directory.GetFiles(agentsDir, "*.md").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];

    // Names are resolved inside fixed directories; reject anything that could escape them.
    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre no puede estar vacío.", paramName);
        if (name.Contains("..") || name.IndexOfAny(['/', '\\']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Nombre inválido: '{name}'.", paramName);
    }
}

internal static class PromptStringBuilderExtensions
{
    public static StringBuilder AppendSection(this StringBuilder sb, string title, string body) =>
        sb.AppendLine("---")
          .AppendLine()
          .AppendLine($"## {title}")
          .AppendLine()
          .AppendLine(body.TrimEnd())
          .AppendLine();
}
