namespace AIHarness.Orchestration;

/// <summary>
/// Stacks declared at the root of a target repository and the build, test and dependency commands an agent may run
/// there without approval, as Claude Code permission rules (<c>Bash(npm test:*)</c>).
/// </summary>
/// <remarks>
/// A repository with no recognizable stack (greenfield) gets every stack's commands, so the agent can create the project.
/// Commands that apply database migrations are never listed: they need human approval (CLAUDE.md §3).
/// </remarks>
public sealed record StackProfile(IReadOnlyList<string> Stacks, IReadOnlyList<string> AllowedCommands)
{
    public const string DotNet = "dotnet";
    public const string Node = "node";
    public const string Angular = "angular";
    public const string Make = "make";

    private static readonly string[] AllStacks = [DotNet, Node, Angular, Make];

    private static readonly IReadOnlyDictionary<string, string[]> Commands = new Dictionary<string, string[]>
    {
        [DotNet] =
        [
            "dotnet restore", "dotnet build", "dotnet test", "dotnet format", "dotnet new sln", "dotnet new classlib",
            "dotnet new console", "dotnet new webapi", "dotnet new xunit", "dotnet new gitignore", "dotnet sln",
            "dotnet add", "dotnet list package", "dotnet ef migrations add",
        ],
        // `npm run` covers the repository's own scripts (build, lint, tsc...); `npx` is not allowed because it downloads
        // and runs any package that is not installed.
        [Node] = ["npm ci", "npm install", "npm init -y", "npm test", "npm run", "npm audit"],
        [Angular] = ["npm run ng", "npx ng build", "npx ng test", "npx ng lint", "npx ng generate"],
        [Make] = ["make test", "make build"],
    };

    /// <summary>Detects the stacks from the files at the root of <paramref name="rootPath"/>.</summary>
    public static StackProfile Detect(string rootPath)
    {
        var stacks = new List<string>();
        if (Directory.Exists(rootPath))
        {
            if (new[] { "*.sln", "*.slnx", "*.csproj" }.Any(pattern => Directory.EnumerateFiles(rootPath, pattern).Any()))
                stacks.Add(DotNet);
            if (File.Exists(Path.Combine(rootPath, "package.json")))
                stacks.Add(Node);
            if (File.Exists(Path.Combine(rootPath, "angular.json")))
                stacks.Add(Angular);
            if (File.Exists(Path.Combine(rootPath, "Makefile")))
                stacks.Add(Make);
        }

        var allowed = (stacks.Count == 0 ? AllStacks : (IEnumerable<string>)stacks)
            .SelectMany(stack => Commands[stack])
            .Select(command => $"Bash({command}:*)")
            .ToList();
        return new StackProfile(stacks, allowed);
    }
}
