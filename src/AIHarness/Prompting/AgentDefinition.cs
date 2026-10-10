namespace AIHarness.Prompting;

/// <summary>
/// An agent definition file: the optional YAML frontmatter Claude Code uses to register a subagent
/// (<c>name</c>, <c>description</c>, <c>tools</c>, <c>model</c>) and the Markdown body with its instructions.
/// </summary>
/// <remarks>
/// Minimal parser without a YAML dependency: only top-level <c>key: value</c> pairs are read, and <c>tools</c> may be a
/// comma-separated scalar or a block list (<c>- Read</c>). Anything else in the frontmatter is ignored.
/// </remarks>
public sealed record AgentDefinition(
    bool HasFrontmatter, string? Name, string? Description, IReadOnlyList<string> Tools, string? Model, string Body)
{
    private const string Delimiter = "---";

    /// <summary>Splits <paramref name="content"/> into frontmatter fields and body.</summary>
    /// <remarks>Frontmatter requires <c>---</c> on the first line and a closing <c>---</c>; otherwise the whole file is the body.</remarks>
    public static AgentDefinition Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var closing = lines.Length > 0 && lines[0].TrimEnd() == Delimiter
            ? Array.FindIndex(lines, 1, l => l.TrimEnd() == Delimiter)
            : -1;
        if (closing < 0)
            return new AgentDefinition(false, null, null, [], null, content);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tools = new List<string>();
        string? currentKey = null;
        foreach (var line in lines[1..closing])
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                // Block list item of the previous key.
                if (string.Equals(currentKey, "tools", StringComparison.OrdinalIgnoreCase))
                    tools.Add(Unquote(trimmed[2..].Trim()));
                continue;
            }
            var colon = line.IndexOf(':');
            if (char.IsWhiteSpace(line[0]) || colon <= 0)
                continue;

            currentKey = line[..colon].Trim();
            fields[currentKey] = Unquote(line[(colon + 1)..].Trim());
        }

        if (fields.TryGetValue("tools", out var inlineTools) && inlineTools.Length > 0)
            tools.AddRange(inlineTools.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote));

        var body = string.Join('\n', lines[(closing + 1)..]).TrimStart('\n');
        return new AgentDefinition(true, Field("name"), Field("description"), tools, Field("model"), body);

        string? Field(string key) => fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
    }

    /// <summary>Reasons why the definition cannot be registered as the Claude Code subagent <paramref name="expectedName"/>; empty when valid.</summary>
    public IReadOnlyList<string> Validate(string expectedName)
    {
        if (!HasFrontmatter)
            return ["sin frontmatter"];
        var problems = new List<string>();
        if (Name is null)
            problems.Add("falta 'name'");
        else if (!string.Equals(Name, expectedName, StringComparison.Ordinal))
            problems.Add($"'name' es '{Name}', se esperaba '{expectedName}'");
        if (Description is null)
            problems.Add("falta 'description'");
        return problems;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
            ? value[1..^1]
            : value;
}
