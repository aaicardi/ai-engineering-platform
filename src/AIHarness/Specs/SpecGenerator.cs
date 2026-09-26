using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AIHarness.GitHub;

namespace AIHarness.Specs;

/// <summary>A spec draft written to disk by <see cref="SpecGenerator"/>.</summary>
public sealed record GeneratedSpec(int Number, string FileName, string FilePath, string Content);

/// <summary>
/// Builds OpenSpec drafts from GitHub Issues and saves them as <c>NNN-&lt;slug&gt;.md</c> inside the specs directory.
/// Performs no console output so it can be reused by other front-ends.
/// </summary>
public sealed partial class SpecGenerator(string specsDirectory)
{
    public static readonly string DefaultSpecsRelativePath = Path.Combine("openspec", "specs");
    public const int MaxSlugLength = 60;

    /// <summary>Renders the draft and writes it without ever overwriting an existing file.</summary>
    /// <exception cref="DirectoryNotFoundException">The specs directory does not exist (it is never created here).</exception>
    /// <exception cref="SpecAlreadyExistsException">A spec with the same slug already exists.</exception>
    public GeneratedSpec Generate(GitHubIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var directory = Path.GetFullPath(specsDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"No existe el directorio de especificaciones: {directory}");

        var slug = Slugify(issue.Title, issue.Number);
        var existing = Directory.GetFiles(directory, $"*-{slug}.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .FirstOrDefault(f => SpecPrefixRegex().IsMatch(f) && f[(f.IndexOf('-') + 1)..] == slug + ".md");
        if (existing is not null)
            throw new SpecAlreadyExistsException(Path.Combine(directory, existing));

        var number = NextSpecNumber(directory);
        var fileName = $"{number:D3}-{slug}.md";
        var filePath = Path.GetFullPath(Path.Combine(directory, fileName));
        if (Path.GetDirectoryName(filePath) != directory.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException($"Ruta de salida fuera del directorio de especificaciones: {filePath}");

        var content = Render(issue, number);

        // Write to a temp file and move it into place so a failure never leaves a truncated spec,
        // and a concurrent writer can never be overwritten.
        var tempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, filePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(filePath))
        {
            throw new SpecAlreadyExistsException(filePath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        return new GeneratedSpec(number, fileName, filePath, content);
    }

    /// <summary>Next free spec number: highest <c>NNN-</c> prefix in <paramref name="directory"/> plus one.</summary>
    public static int NextSpecNumber(string directory) =>
        Directory.GetFiles(directory, "*.md")
            .Select(f => SpecPrefixRegex().Match(Path.GetFileName(f)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;

    /// <summary>ASCII, lowercase, hyphen-separated slug; falls back to <c>issue-&lt;number&gt;</c>.</summary>
    public static string Slugify(string title, int issueNumber)
    {
        var ascii = new StringBuilder();
        foreach (var c in (title ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                ascii.Append(char.ToLowerInvariant(c));
        }

        var slug = NonSlugCharsRegex().Replace(ascii.ToString(), "-").Trim('-');
        if (slug.Length > MaxSlugLength)
            slug = slug[..MaxSlugLength].TrimEnd('-');
        return slug.Length == 0 ? $"issue-{issueNumber}" : slug;
    }

    /// <summary>Renders the OpenSpec markdown for <paramref name="issue"/>.</summary>
    public static string Render(GitHubIssue issue, int specNumber)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var title = SingleLine(issue.Title);
        var body = issue.Body?.ReplaceLineEndings("\n").Trim() ?? string.Empty;
        var labels = issue.Labels.Count == 0 ? "(ninguna)" : string.Join(", ", issue.Labels.Select(l => $"`{SingleLine(l)}`"));

        var sb = new StringBuilder()
            .AppendLine($"# OpenSpec {specNumber:D3}: {(title.Length == 0 ? $"Issue #{issue.Number}" : title)}")
            .AppendLine()
            .AppendLine("## Issue Reference")
            .AppendLine($"Closes #{issue.Number}")
            .AppendLine()
            .AppendLine($"- **Autor:** @{SingleLine(issue.Author)}")
            .AppendLine($"- **Etiquetas:** {labels}")
            .AppendLine($"- **URL:** {issue.HtmlUrl}")
            .AppendLine()
            .AppendLine("## Context & Objectives");

        if (body.Length == 0)
        {
            sb.AppendLine("_El Issue no incluye descripción. Pendiente de definir por el agente architect._");
        }
        else
        {
            // Quoted so headings inside the Issue body cannot break the spec's section structure.
            foreach (var line in body.Split('\n'))
                sb.AppendLine(line.Length == 0 ? ">" : $"> {line}");
        }

        sb.AppendLine()
          .AppendLine("## Functional Requirements");
        var requirements = ExtractRequirements(body);
        if (requirements.Count == 0)
            sb.AppendLine("- **RF-01:** _Pendiente de definir por el agente architect._");
        for (var i = 0; i < requirements.Count; i++)
            sb.AppendLine($"- **RF-{i + 1:D2}:** {requirements[i]}");

        return sb.AppendLine()
          .AppendLine("## Acceptance Criteria")
          .AppendLine("- [ ] La solución .NET 10 compila sin errores.")
          .AppendLine("- [ ] Los requisitos funcionales están cubiertos por pruebas.")
          .AppendLine("- [ ] El pipeline de CI/CD sigue pasando en verde.")
          .ToString();
    }

    // Top-level list items (task lists, bullets, numbered) of the Issue body become requirements.
    private static List<string> ExtractRequirements(string body) =>
        body.Split('\n')
            .Select(l => RequirementLineRegex().Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(r => r.Length > 0)
            .ToList();

    private static string SingleLine(string? value) =>
        WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"^(\d{3})-.+\.md$")]
    private static partial Regex SpecPrefixRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugCharsRegex();

    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?(.+)$")]
    private static partial Regex RequirementLineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed class SpecAlreadyExistsException(string path)
    : IOException($"Ya existe una especificación para este Issue: {path}. No se sobrescribe; elimínela o renómbela para regenerarla.")
{
    public string ExistingPath { get; } = path;
}
