using AIHarness.GitHub;
using AIHarness.Specs;

namespace AIHarness.Tests;

public sealed class SpecGeneratorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-tests-").FullName;
    private readonly string _specs;

    public SpecGeneratorTests()
    {
        _specs = Path.Combine(_root, "openspec", "specs");
        Directory.CreateDirectory(_specs);
        File.WriteAllText(Path.Combine(_specs, "004-github-api-connector.md"), "# OpenSpec 004");
        File.WriteAllText(Path.Combine(_specs, "005-openspec-generator.md"), "# OpenSpec 005");
        File.WriteAllText(Path.Combine(_specs, "005-openspec-generator-implementation-plan.md"), "# Plan 005");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static GitHubIssue Issue(string title = "Añadir exportación CSV", string? body = null, params string[] labels) =>
        new(42, title, body, "octocat", "open", labels, "https://github.com/owner/repo/issues/42");

    [Fact]
    public void Generate_WritesNextNumberedSpecInsideSpecsDirectory()
    {
        var spec = new SpecGenerator(_specs).Generate(Issue());

        Assert.Equal(6, spec.Number);
        Assert.Equal("006-anadir-exportacion-csv.md", spec.FileName);
        Assert.Equal(Path.Combine(_specs, spec.FileName), spec.FilePath);
        Assert.Equal(spec.Content, File.ReadAllText(spec.FilePath));
        // Nothing else created: no stray temp files, no new folders.
        Assert.Equal(4, Directory.GetFiles(_specs).Length);
        Assert.Empty(Directory.GetDirectories(_specs));
    }

    [Fact]
    public void Generate_SameIssueTwice_ThrowsAndKeepsOriginalFile()
    {
        var generator = new SpecGenerator(_specs);
        var first = generator.Generate(Issue());
        File.AppendAllText(first.FilePath, "edición manual");

        var ex = Assert.Throws<SpecAlreadyExistsException>(() => generator.Generate(Issue()));

        Assert.Equal(first.FilePath, ex.ExistingPath);
        Assert.EndsWith("edición manual", File.ReadAllText(first.FilePath));
        Assert.Equal(4, Directory.GetFiles(_specs).Length);
    }

    [Fact]
    public void Generate_MissingSpecsDirectory_ThrowsWithoutCreatingIt()
    {
        var missing = Path.Combine(_root, "nope", "specs");

        Assert.Throws<DirectoryNotFoundException>(() => new SpecGenerator(missing).Generate(Issue()));
        Assert.False(Directory.Exists(Path.Combine(_root, "nope")));
    }

    [Theory]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("feat: Soporte para C# & .NET 10!", "feat-soporte-para-c-net-10")]
    [InlineData("  ***  ", "issue-42")]
    [InlineData("配置", "issue-42")]
    public void Slugify_ProducesSafeFileNameSegment(string title, string expected)
    {
        var slug = SpecGenerator.Slugify(title, 42);

        Assert.Equal(expected, slug);
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);
    }

    [Fact]
    public void Slugify_TruncatesLongTitlesWithoutTrailingHyphen()
    {
        var slug = SpecGenerator.Slugify(string.Join(' ', Enumerable.Repeat("palabra", 20)), 1);

        Assert.True(slug.Length <= SpecGenerator.MaxSlugLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void NextSpecNumber_IgnoresFilesWithoutNumericPrefix()
    {
        File.WriteAllText(Path.Combine(_specs, "README.md"), "x");
        File.WriteAllText(Path.Combine(_specs, "12-short.md"), "x");

        Assert.Equal(6, SpecGenerator.NextSpecNumber(_specs));
        Assert.Equal(1, SpecGenerator.NextSpecNumber(Directory.CreateTempSubdirectory("aiharness-empty-").FullName));
    }

    [Fact]
    public void Render_MapsIssueFieldsToOpenSpecSectionsInOrder()
    {
        var content = SpecGenerator.Render(Issue(body: "Contexto.\n\n- [ ] Exportar a CSV\n* Validar columnas", labels: ["enhancement"]), 6);

        Assert.StartsWith("# OpenSpec 006: Añadir exportación CSV\n", content.ReplaceLineEndings("\n"));
        Assert.Contains("Closes #42", content);
        Assert.Contains("@octocat", content);
        Assert.Contains("`enhancement`", content);
        Assert.Contains("- **RF-01:** Exportar a CSV", content);
        Assert.Contains("- **RF-02:** Validar columnas", content);

        string[] sections = ["## Issue Reference", "## Context & Objectives", "## Functional Requirements", "## Acceptance Criteria"];
        var positions = sections.Select(s => content.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void Render_BodyHeadingsCannotInjectSections()
    {
        var content = SpecGenerator.Render(Issue(title: "Multi\nline # title", body: "## Acceptance Criteria\n# Hack"), 7);
        var lines = content.ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("# OpenSpec 007: Multi line # title", lines[0]);
        Assert.Single(lines, l => l.StartsWith("# ", StringComparison.Ordinal));
        Assert.Single(lines, l => l == "## Acceptance Criteria");
        Assert.Contains("> ## Acceptance Criteria", lines);
    }

    [Fact]
    public void Render_EmptyBody_UsesPlaceholders()
    {
        var content = SpecGenerator.Render(Issue(body: "   "), 6);

        Assert.Contains("_El Issue no incluye descripción.", content);
        Assert.Contains("- **RF-01:** _Pendiente de definir", content);
        Assert.Contains("**Etiquetas:** (ninguna)", content);
    }
}
