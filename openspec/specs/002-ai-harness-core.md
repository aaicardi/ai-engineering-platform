# OpenSpec 002: AI Harness Core Architecture (.NET 10)

## Issue Reference
Closes #2

## Context & Objectives
Inicializar la solución base `AIHarness.sln` en .NET 10. Este componente actuará como el harness ejecutor que audita la presencia de `CLAUDE.md`, carga la suite de 7 agentes desde `.claude/agents/` y lee las especificaciones activas en `openspec/specs/`.

## Functional Requirements
- **RF-01:** Crear la solución .NET 10 y el proyecto de consola `src/AIHarness/AIHarness.csproj`.
- **RF-02:** Implementar la lectura y auditoría del archivo maestro `CLAUDE.md`.
- **RF-03:** Validar y listar los 7 agentes configurados en `.claude/agents/`.
- **RF-04:** Validar y parsear los archivos `.md` de especificaciones activas en `openspec/specs/`.
- **RF-05:** Actualizar el pipeline de GitHub Actions (`ci.yml`) para compilar la solución .NET 10 en cada PR.

## Acceptance Criteria
- [ ] La solución `AIHarness.sln` compila correctamente con .NET 10.
- [ ] La consola muestra la auditoría exitosa de `CLAUDE.md`, los 7 agentes y las especificaciones activas.
- [ ] El pipeline de CI/CD en GitHub Actions compila el proyecto en verde.
