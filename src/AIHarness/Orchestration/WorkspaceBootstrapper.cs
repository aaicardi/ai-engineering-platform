using AIHarness.Specs;

namespace AIHarness.Orchestration;

/// <summary>
/// Scaffolds the governance a repository needs to be processed by the harness when it has none yet (greenfield):
/// <c>CLAUDE.md</c>, the agent definitions and <c>openspec/specs/</c>. Existing files are never overwritten.
/// Performs no Git or console operations so it can be reused by other front-ends.
/// </summary>
public sealed class WorkspaceBootstrapper(string rootPath, string? agentTemplatesDirectory = null)
{
    public const string ClaudeMdFileName = "CLAUDE.md";

    // Files GitHub can create along with a new repository; their presence alone still means "empty".
    private static readonly string[] InitialFilePrefixes = ["README", "LICENSE", ".gitignore", ".gitattributes"];

    /// <summary><c>true</c> when the repository has no <c>CLAUDE.md</c> governance file.</summary>
    public bool IsGreenfield => !File.Exists(Path.Combine(rootPath, ClaudeMdFileName));

    /// <summary>
    /// <c>true</c> when the repository holds no project content: only <c>.git</c> and the files GitHub
    /// creates with a new repository (README, LICENSE, .gitignore, .gitattributes).
    /// </summary>
    public bool IsEmpty =>
        !Directory.Exists(rootPath)
        || Directory.EnumerateFileSystemEntries(rootPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .All(name => name == ".git" || InitialFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Creates the missing governance artifacts of a greenfield repository; does nothing otherwise.</summary>
    /// <returns>Paths of the created files, relative to the repository root.</returns>
    public IReadOnlyList<string> Bootstrap()
    {
        if (!IsGreenfield)
            return [];

        var created = new List<string>();
        var claudeMd = BuildClaudeMd(new DirectoryInfo(rootPath).Name, IsEmpty);

        Directory.CreateDirectory(Path.Combine(rootPath, SpecGenerator.DefaultSpecsRelativePath));
        File.WriteAllText(Path.Combine(rootPath, ClaudeMdFileName), claudeMd);
        created.Add(ClaudeMdFileName);

        if (agentTemplatesDirectory is not null && Directory.Exists(agentTemplatesDirectory))
        {
            var agentsDir = Directory.CreateDirectory(Path.Combine(rootPath, ".claude", "agents")).FullName;
            foreach (var template in Directory.GetFiles(agentTemplatesDirectory, "*.md").Order(StringComparer.Ordinal))
            {
                var target = Path.Combine(agentsDir, Path.GetFileName(template));
                if (File.Exists(target))
                    continue;
                File.Copy(template, target);
                created.Add(Path.GetRelativePath(rootPath, target));
            }
        }
        return created;
    }

    /// <summary>Governance for a repository named <paramref name="projectName"/>.</summary>
    /// <param name="empty">
    /// The repository has no code yet, so the first agent must also establish the initial architecture and the quality gate.
    /// </param>
    public static string BuildClaudeMd(string projectName, bool empty)
    {
        var architecture = empty
            ? "Repositorio greenfield: el primer agente que trabaje aquí debe establecer la arquitectura inicial\n"
              + "(estructura de la solución, proyecto de pruebas y un ADR en `docs/decisions/`) y dejar un Quality Gate\n"
              + "ejecutable desde la raíz (`dotnet test`, `npm test` o `make test`), que AI Harness ejecuta antes de preparar el Pull Request."
            : "Repositorio existente sin gobernanza previa: respetar su arquitectura, convenciones y Quality Gate actuales.";

        return $"""
        # {projectName} — Project Guidelines

        > Generado por AI Harness (bootstrap greenfield). Revíselo y ajústelo al proyecto.

        ## 1. Contexto del Proyecto
        Repositorio gestionado con AI Harness para desarrollo asistido por agentes.

        ## 2. Arquitectura Inicial
        {architecture}

        ## 3. Modelo de Autonomía y Gobernanza
        - **Requiere Aprobación Humana Explícita:** `git push`, merge de Pull Requests, migraciones de base de datos,
          secretos o variables de entorno sensibles y despliegues.

        ## 4. Convenciones de Commits y Código
        - Seguir **Conventional Commits**: `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`.
        - Todas las funcionalidades deben contar con su especificación correspondiente en `openspec/specs/`.

        """;
    }
}
