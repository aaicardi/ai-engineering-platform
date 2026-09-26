# OpenSpec 004: AI Harness GitHub API Connector (.NET 10)

## Issue Reference
Closes #4

## Context & Objectives
Integrar el paquete `Octokit` en `AIHarness.csproj` para habilitar la comunicación nativa con la API de GitHub. El Harness podrá autenticarse usando el token local (`GH_TOKEN` / `GITHUB_TOKEN` o la sesión de `gh cli`) y consultar los detalles completos de cualquier Issue.

## Functional Requirements
- **RF-01:** Agregar el paquete NuGet `Octokit` al proyecto `src/AIHarness/AIHarness.csproj`.
- **RF-02:** Crear el servicio `GitHubConnector.cs` encargado de consultar los detalles de un Issue por número (`--issue <number>`).
- **RF-03:** Extraer título, cuerpo, autor, estado y etiquetas del Issue.
- **RF-04:** Exponer la opción en la CLI de `Program.cs` para invocar la lectura de un Issue (ej. `dotnet run -- --issue 4`).

## Acceptance Criteria
- [ ] La solución .NET 10 compila y descarga la dependencia `Octokit` correctamente.
- [ ] Pasar el parámetro `--issue <number>` consulta la API de GitHub y muestra en consola la información estructurada del Issue.
- [ ] El pipeline de CI/CD sigue pasando en verde.
