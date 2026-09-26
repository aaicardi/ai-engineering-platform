# OpenSpec 006: Testing & Quality Engineering Platform (.NET 10)

## Issue Reference
Closes #19

## Context & Objectives
Crear e integrar la suite de pruebas unitarias para `AIHarness` utilizando **xUnit** y **Moq** o dobles de prueba, asegurando la verificación automatizada del código en la CLI y dentro del pipeline de CI/CD en GitHub Actions.

## Functional Requirements
- **RF-01:** Crear el proyecto `tests/AIHarness.Tests/AIHarness.Tests.csproj` usando xUnit en .NET 10 y enlazarlo a `AIHarness.sln`.
- **RF-02:** Agregar referencia desde `AIHarness.Tests` hacia `src/AIHarness/AIHarness.csproj`.
- **RF-03:** Implementar pruebas unitarias para:
  - `ContextBuilder`: Verificar ensamble de prompts y manejo de archivos inexistentes.
  - `SpecGenerator`: Verificar formateo de plantillas de OpenSpec.
- **RF-04:** Actualizar `.github/workflows/ci.yml` para incluir la ejecución de `dotnet test --no-build --verbosity normal`.

## Acceptance Criteria
- [ ] La solución `AIHarness.sln` compila y ejecuta `dotnet test` con 100% de pruebas en verde.
- [ ] El pipeline de GitHub Actions ejecuta el paso de tests correctamente.
