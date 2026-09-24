using AIHarness.Inspection;

// Usage: AIHarness [repositoryRoot]
// Without an argument, the root is discovered by walking up from the current directory.
var root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : RepositoryInspector.FindRepositoryRoot(Directory.GetCurrentDirectory());

if (root is null || !Directory.Exists(root))
{
    Console.Error.WriteLine("[ERROR] No se encontró la raíz del repositorio (directorio con CLAUDE.md).");
    return 1;
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
