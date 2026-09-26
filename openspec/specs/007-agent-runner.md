# OpenSpec 007: AI Harness Agent Execution Orchestrator (.NET 10)

## Issue Reference
Closes #20

## Context & Objectives
Crear el servicio `AgentRunner.cs` en `AIHarness` para ejecutar la CLI de `claude` como un subproceso del sistema operativo, transmitiendo el prompt construido por `ContextBuilder` y capturando la salida y código de retorno.

## Functional Requirements
- **RF-01:** Crear el servicio `AgentRunner.cs` en `src/AIHarness/`.
- **RF-02:** Permitir lanzar la CLI de Claude mediante la bandera `--execute` en `Program.cs` (ej. `dotnet run -- --agent developer --spec 007-agent-runner.md --execute`).
- **RF-03:** Capturar la salida estándar (STDOUT) y de error (STDERR) del subproceso `claude` e imitar su streaming en la consola local.
- **RF-04:** Incluir pruebas unitarias en `AIHarness.Tests` para validar el lanzamiento e intercepción de subprocesos.

## Acceptance Criteria
- [ ] La solución .NET 10 compila y ejecuta la suite de pruebas unitarias al 100%.
- [ ] La bandera `--execute` invoca el subproceso CLI y devuelve el código de salida adecuado.
- [ ] El pipeline de CI/CD pasa en verde.
