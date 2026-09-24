# Implementation Plan: AI Harness Core (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Validar la estructura de directorios `src/AIHarness` y `AIHarness.sln`.
   - Garantizar el desacoplamiento entre el analizador de archivos y la salida por consola.

2. **Developer Agent (`developer`):**
   - Crear el proyecto de consola con `dotnet new console -n AIHarness -o src/AIHarness --framework net10.0`.
   - Implementar la lógica de inspección en `Program.cs`.

3. **DevOps Agent (`devops`):**
   - Actualizar `.github/workflows/ci.yml` para incorporar el paso de compilación `dotnet build`.

4. **Code-Reviewer Agent (`code-reviewer`):**
   - Auditar que el código C# cumpla con las convenciones de `CLAUDE.md`.
