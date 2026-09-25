# OpenSpec 003: AI Harness Agent Prompt Engine (.NET 10)

## Issue Reference
Closes #3

## Context & Objectives
Evolucionar la solución `AIHarness.sln` para incluir un motor en C# (`ContextBuilder`) capaz de combinar:
1. `CLAUDE.md` (Gobernanza y directrices globales)
2. `.claude/agents/<agent-name>.md` (Instrucciones específicas del agente)
3. `openspec/specs/<spec-name>.md` (Especificación funcional activa)

El output será un prompt unificado estructurado para la ejecución del agente.

## Functional Requirements
- **RF-01:** Crear el servicio `ContextBuilder` en el proyecto `AIHarness`.
- **RF-02:** Permitir seleccionar un agente por nombre (ej. `architect`, `developer`, `tester`).
- **RF-03:** Unificar los 3 niveles de contexto (`CLAUDE.md` + Agent + OpenSpec) en una cadena/archivo formateado.
- **RF-04:** Exponer la opción de CLI en `Program.cs` para invocar el ensamble de contexto por comando (ej. `dotnet run -- --agent developer --spec 003-ai-harness-prompt-engine.md`).

## Acceptance Criteria
- [ ] La solución .NET 10 compila sin errores.
- [ ] Pasar los parámetros de CLI genera el prompt unificado en la consola y lo guarda opcionalmente en `.claude/tmp/current-prompt.md`.
- [ ] El pipeline de CI/CD sigue pasando en verde.
