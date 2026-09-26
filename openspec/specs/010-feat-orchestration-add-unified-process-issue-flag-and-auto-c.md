# OpenSpec 010: feat(orchestration): Add unified --process-issue flag and auto-commit stage

## Issue Reference
Closes #31

- **Autor:** @aaicardi
- **Etiquetas:** `type:feature`, `area:architecture`
- **URL:** https://github.com/aaicardi/ai-engineering-platform/issues/31

## Context & Objectives
> ## Description
> Unify the AI Harness pipeline into a single entrypoint (--process-issue <number>) and implement an auto-commit stage for agent modifications.
>
> ## Requirements
> 1. Add --process-issue <number> parameter to Program.cs that chains: Fetch Issue -> Create Branch -> Generate Spec -> Run Agent -> Auto-Commit -> Test -> Create PR.
> 2. Implement auto-commit in AgentOrchestrator when untracked/modified files exist after agent execution.
> 3. Automatically execute tests and handle PR creation without manual git commands.

## Functional Requirements
- **RF-01:** Add --process-issue <number> parameter to Program.cs that chains: Fetch Issue -> Create Branch -> Generate Spec -> Run Agent -> Auto-Commit -> Test -> Create PR.
- **RF-02:** Implement auto-commit in AgentOrchestrator when untracked/modified files exist after agent execution.
- **RF-03:** Automatically execute tests and handle PR creation without manual git commands.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Los requisitos funcionales están cubiertos por pruebas.
- [ ] El pipeline de CI/CD sigue pasando en verde.
