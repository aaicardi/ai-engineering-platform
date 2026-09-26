# OpenSpec 005: AI Harness OpenSpec Spec Generator (.NET 10)

## Issue Reference
Closes #16

## Context & Objectives
Evolucionar `AIHarness` para incluir el servicio `SpecGenerator.cs`. Cuando se consulte un Issue de GitHub, el Harness podrá generar automáticamente un archivo `.md` estructurado según la plantilla de OpenSpec en `openspec/specs/`.

## Functional Requirements
- **RF-01:** Crear el servicio `SpecGenerator.cs` en el proyecto `src/AIHarness`.
- **RF-02:** Mapear el número, título, autor y descripción del Issue hacia los campos de OpenSpec (`Issue Reference`, `Context & Objectives`, `Functional Requirements`).
- **RF-03:** Añadir la bandera `--generate-spec` en la CLI (ej. `dotnet run -- --issue 16 --generate-spec`).
- **RF-04:** Guardar el archivo generado con la nomenclatura `openspec/specs/XXX-<issue-slug>.md`.

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Ejecutar la bandera `--generate-spec` junto con `--issue` crea un borrador válido en `openspec/specs/`.
- [ ] El pipeline de CI/CD sigue pasando en verde.
