# Implementation Plan: AI Harness GitHub API Connector (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Diseñar la interfaz del servicio `IGitHubConnector` y definir el manejo de autenticación.

2. **Developer Agent (`developer`):**
   - Agregar el paquete NuGet `Octokit`.
   - Implementar `GitHubConnector.cs` y actualizar `Program.cs` para soportar la bandera `--issue <number>`.

3. **Security-Reviewer Agent (`security-reviewer`):**
   - Verificar que no se expongan tokens de acceso en el código ni en los logs de la consola.
