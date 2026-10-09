using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIHarness.Orchestration;

/// <summary>Command that validates a repository before its Pull Request is prepared (e.g. <c>dotnet test</c>).</summary>
public sealed partial record QualityGate(string Executable, IReadOnlyList<string> Arguments)
{
    public static readonly QualityGate DotnetTest = new("dotnet", ["test"]);
    public static readonly QualityGate NpmTest = new("npm", ["test"]);
    public static readonly QualityGate MakeTest = new("make", ["test"]);

    // Script written by `npm init`; it always fails, so it is not a real test suite.
    private const string NpmPlaceholderTestScript = "echo \"Error: no test specified\" && exit 1";

    public override string ToString() => GitAutomationService.FormatCommand(Executable, Arguments);

    /// <summary>
    /// Detects the quality gate declared by the files at the root of <paramref name="rootPath"/>, in priority order:
    /// a .NET solution or project (<c>dotnet test</c>), a <c>test</c> script in <c>package.json</c> (<c>npm test</c>),
    /// a <c>test</c> target in the <c>Makefile</c> (<c>make test</c>).
    /// </summary>
    /// <returns>The quality gate, or <c>null</c> when the repository declares none.</returns>
    public static QualityGate? Detect(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return null;
        if (new[] { "*.sln", "*.slnx", "*.csproj" }.Any(pattern => Directory.EnumerateFiles(rootPath, pattern).Any()))
            return DotnetTest;
        if (HasNpmTestScript(Path.Combine(rootPath, "package.json")))
            return NpmTest;
        if (HasMakeTestTarget(Path.Combine(rootPath, "Makefile")))
            return MakeTest;
        return null;
    }

    private static bool HasNpmTestScript(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scripts", out var scripts)
                && scripts.ValueKind == JsonValueKind.Object
                && scripts.TryGetProperty("test", out var test)
                && test.ValueKind == JsonValueKind.String
                && test.GetString() is { Length: > 0 } script
                && script.Trim() != NpmPlaceholderTestScript;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasMakeTestTarget(string path) =>
        File.Exists(path) && MakeTestTargetRegex().IsMatch(File.ReadAllText(path));

    // `test:` or `test: deps`, but not the variable assignment `test := value`.
    [GeneratedRegex(@"^test\s*:(?!=)", RegexOptions.Multiline)]
    private static partial Regex MakeTestTargetRegex();
}
