# OpenSpec 009: feat(cli): Add --version flag to AIHarness CLI

## Issue Reference
Closes #29

- **Autor:** @aaicardi
- **Etiquetas:** `type:feature`, `area:architecture`
- **URL:** https://github.com/aaicardi/ai-engineering-platform/issues/29

## Context & Objectives
> ## Description
> Add a --version flag to the AIHarness CLI to output the system version and environment info (.NET 10 runtime).
>
> ## Acceptance Criteria
> 1. Running 'AIHarness --version' outputs the version string and .NET runtime version.
> 2. Unit tests added to AIHarness.Tests verifying the --version flag output.

## Functional Requirements
- **RF-01:** Running 'AIHarness --version' outputs the version string and .NET runtime version.
- **RF-02:** Unit tests added to AIHarness.Tests verifying the --version flag output.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Los requisitos funcionales están cubiertos por pruebas.
- [ ] El pipeline de CI/CD sigue pasando en verde.
