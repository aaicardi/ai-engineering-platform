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
        for (var i = 1; i < closing; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                // Block list item of the previous key.
                if (string.Equals(currentKey, "tools", StringComparison.OrdinalIgnoreCase))
                    tools.Add(Scalar(trimmed[2..]));
                continue;
            }
            var colon = line.IndexOf(':');
            if (char.IsWhiteSpace(line[0]) || colon <= 0)
                continue;

            currentKey = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length > 0 && value[0] is '>' or '|')
            {
                // Block scalar (`>`, `>-`, `|`, `|-`): the indented lines that follow are the value.
                var block = new List<string>();
                while (i + 1 < closing && (lines[i + 1].Trim().Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                    block.Add(lines[++i].Trim());
                fields[currentKey] = string.Join(value[0] == '>' ? " " : "\n", block.Where(l => l.Length > 0));
            }
            else
            {
                fields[currentKey] = Scalar(value);
            }
        }

        if (fields.TryGetValue("tools", out var inlineTools) && inlineTools.Length > 0)
            tools.AddRange(inlineTools.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Scalar));

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

    // A quoted scalar ends at its closing quote (anything after it, such as a comment, is dropped);
    // an unquoted one ends at an inline comment (`#` at the start or after a space).
    private static string Scalar(string value)
    {
        value = value.Trim();
        if (value.Length > 0 && value[0] is '"' or '\'')
        {
            var close = value.IndexOf(value[0], 1);
            return close > 0 ? value[1..close] : value;
        }
        if (value.StartsWith('#'))
            return string.Empty;
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment < 0 ? value : value[..comment].TrimEnd();
    }
}
