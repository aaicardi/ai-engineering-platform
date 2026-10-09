# OpenSpec 012: feat(orchestration): Add multi-repository workspace isolation and greenfield scaffolding

## Issue Reference
Closes #39

- **Autor:** @aaicardi
- **Etiquetas:** `type:feature`, `area:architecture`
- **URL:** https://github.com/aaicardi/ai-engineering-platform/issues/39

## Context & Objectives
> ## Description
> Extend AI Harness to support multi-repository workspace switching (--target-repo / --target-dir) and greenfield project scaffolding.
>
> ## Requirements
> 1. Allow --process-issue to accept external target repositories (--repo <owner/name> or --target-dir <path>).
> 2. Set sub-process WorkingDirectory (AgentRunner) to target repo root.
> 3. Detect empty or greenfield repos and automatically bootstrap initial architecture and CLAUDE.md.
> 4. Execute target repo's Quality Gate dynamically and open PR on target repository.

## Functional Requirements
- **RF-01:** Allow --process-issue to accept external target repositories (--repo <owner/name> or --target-dir <path>).
- **RF-02:** Set sub-process WorkingDirectory (AgentRunner) to target repo root.
- **RF-03:** Detect empty or greenfield repos and automatically bootstrap initial architecture and CLAUDE.md.
- **RF-04:** Execute target repo's Quality Gate dynamically and open PR on target repository.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Los requisitos funcionales están cubiertos por pruebas.
- [ ] El pipeline de CI/CD sigue pasando en verde.
