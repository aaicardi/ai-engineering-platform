# Implementation Plan: Testing & Quality Engineering Platform (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Diseñar la estructura de carpetas de pruebas `tests/AIHarness.Tests/`.

2. **Tester Agent (`tester`):**
   - Crear el proyecto de pruebas `xUnit` e implementar las clases de test unitario (`ContextBuilderTests.cs`, `SpecGeneratorTests.cs`).

3. **DevOps Agent (`devops`):**
   - Actualizar `.github/workflows/ci.yml` agregando el paso `dotnet test`.
