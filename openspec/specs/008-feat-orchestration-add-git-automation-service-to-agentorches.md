# OpenSpec 008: feat(orchestration): Add Git Automation Service to AgentOrchestrator

## Issue Reference
Closes #27

- **Autor:** @aaicardi
- **Etiquetas:** `type:feature`, `area:architecture`
- **URL:** https://github.com/aaicardi/ai-engineering-platform/issues/27

## Context & Objectives
> ## Description
> Extend the AIHarness AgentOrchestrator to automatically manage Git lifecycle operations.
>
> ## Requirements
> 1. Automatically create and checkout a feature branch (feature/issue-number-slug) before invoking the agent subprocess.
> 2. Ensure working directory is clean or synchronized before creating branches.
> 3. Automatically execute dotnet test post-execution.
> 4. Prepare a GitHub Pull Request (gh pr create) upon successful execution.

## Functional Requirements
- **RF-01:** Automatically create and checkout a feature branch (feature/issue-number-slug) before invoking the agent subprocess.
- **RF-02:** Ensure working directory is clean or synchronized before creating branches.
- **RF-03:** Automatically execute dotnet test post-execution.
- **RF-04:** Prepare a GitHub Pull Request (gh pr create) upon successful execution.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Los requisitos funcionales están cubiertos por pruebas.
- [ ] El pipeline de CI/CD sigue pasando en verde.
