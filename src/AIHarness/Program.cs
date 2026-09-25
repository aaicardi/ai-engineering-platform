using AIHarness.Inspection;
using AIHarness.Prompting;

// Usage:
//   AIHarness [repositoryRoot]                                   -> auditoría del repositorio
//   AIHarness --agent <name> --spec <file> [--root <dir>] [--no-save]
//                                                                -> prompt unificado (consola + .claude/tmp/current-prompt.md)
// Without a root, it is discovered by walking up from the current directory.
string? agentArg = null, specArg = null, rootArg = null;
var save = true;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--agent" when i + 1 < args.Length: agentArg = args[++i]; break;
        case "--spec" when i + 1 < args.Length: specArg = args[++i]; break;
        case "--root" when i + 1 < args.Length: rootArg = args[++i]; break;
        case "--no-save": save = false; break;
        case var a when a.StartsWith("--", StringComparison.Ordinal):
            Console.Error.WriteLine($"[ERROR] Argumento inválido o sin valor: {a}");
            return 2;
        default: rootArg ??= args[i]; break;
    }
}

var root = rootArg is not null
    ? Path.GetFullPath(rootArg)
    : RepositoryInspector.FindRepositoryRoot(Directory.GetCurrentDirectory());

if (root is null || !Directory.Exists(root))
{
    Console.Error.WriteLine("[ERROR] No se encontró la raíz del repositorio (directorio con CLAUDE.md).");
    return 1;
}

if (agentArg is not null || specArg is not null)
{
    if (agentArg is null || specArg is null)
    {
        Console.Error.WriteLine("[ERROR] Se requieren ambos parámetros: --agent <nombre> --spec <archivo>.");
        return 2;
    }
    return BuildPrompt(new ContextBuilder(root), root, agentArg, specArg, save);
}

var report = new RepositoryInspector(root).Inspect();

Console.WriteLine("=== AI Harness — Auditoría del repositorio ===");
Console.WriteLine($"Raíz: {report.RootPath}");
Console.WriteLine();

Console.WriteLine("[1] CLAUDE.md");
if (report.ClaudeMd.Exists)
{
    Console.WriteLine($"  OK   {report.ClaudeMd.LineCount} líneas, {report.ClaudeMd.Sections.Count} secciones");
    foreach (var section in report.ClaudeMd.Sections)
        Console.WriteLine($"       - {section}");
}
else
{
    Console.WriteLine($"  FAIL No existe: {report.ClaudeMd.Path}");
}
Console.WriteLine();

var presentCount = report.Agents.Agents.Count(a => a.Present);
Console.WriteLine($"[2] Agentes (.claude/agents) — {presentCount}/{RepositoryInspector.ExpectedAgents.Count}");
if (!report.Agents.DirectoryExists)
    Console.WriteLine("  FAIL Directorio .claude/agents no encontrado");
foreach (var agent in report.Agents.Agents)
    Console.WriteLine($"  {(agent.Present ? "OK  " : "FAIL")} {agent.Name}");
foreach (var extra in report.Agents.Unexpected)
    Console.WriteLine($"  INFO {extra} (no esperado)");
Console.WriteLine();

Console.WriteLine($"[3] Especificaciones (openspec/specs) — {report.Specs.Specs.Count}");
if (!report.Specs.DirectoryExists)
    Console.WriteLine("  FAIL Directorio openspec/specs no encontrado");
else if (report.Specs.Specs.Count == 0)
    Console.WriteLine("  FAIL No hay especificaciones .md");
foreach (var spec in report.Specs.Specs)
    Console.WriteLine($"  OK   {spec.FileName} — {spec.Title}");
Console.WriteLine();

Console.WriteLine(report.IsSuccessful ? "Resultado: AUDITORÍA EXITOSA" : "Resultado: AUDITORÍA FALLIDA");
return report.IsSuccessful ? 0 : 1;

static int BuildPrompt(IContextBuilder builder, string root, string agent, string spec, bool save)
{
    PromptContext context;
    try
    {
        context = builder.Build(agent, spec);
    }
    catch (Exception ex) when (ex is ContextSourceNotFoundException or ArgumentException)
    {
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        return 1;
    }

    Console.WriteLine(context.Prompt);

    if (save)
    {
        var outputPath = Path.Combine(root, ContextBuilder.DefaultOutputRelativePath);
        builder.Export(context, outputPath);
        Console.Error.WriteLine($"[INFO] Prompt exportado a {outputPath}");
    }
    return 0;
}
