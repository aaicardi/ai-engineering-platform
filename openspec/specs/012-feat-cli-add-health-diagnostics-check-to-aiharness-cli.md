# OpenSpec 012: feat(cli): Add --health diagnostics check to AIHarness CLI

## Issue Reference
Closes #35

- **Autor:** @aaicardi
- **Etiquetas:** `type:feature`, `area:architecture`
- **URL:** https://github.com/aaicardi/ai-engineering-platform/issues/35

## Context & Objectives
> ## Description
> Add a --health flag to the AIHarness CLI that performs a rapid health check of the platform setup (.claude folder, agents presence, openSpec specs directory) and outputs a diagnostic status report.
>
> ## Acceptance Criteria
> 1. Running 'AIHarness --health' outputs a JSON or structured diagnostic status (agents found, specs count, environment health).
> 2. Exit code is 0 if all core components are present, or 1 if critical files are missing.
> 3. Unit tests added to AIHarness.Tests verifying the --health flag logic.

## Functional Requirements
- **RF-01:** Running 'AIHarness --health' outputs a JSON or structured diagnostic status (agents found, specs count, environment health).
- **RF-02:** Exit code is 0 if all core components are present, or 1 if critical files are missing.
- **RF-03:** Unit tests added to AIHarness.Tests verifying the --health flag logic.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Los requisitos funcionales están cubiertos por pruebas.
- [ ] El pipeline de CI/CD sigue pasando en verde.
