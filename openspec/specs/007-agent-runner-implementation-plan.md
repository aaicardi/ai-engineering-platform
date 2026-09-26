# Implementation Plan: Agent Execution Orchestrator (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Diseñar la abstracción de `ProcessRunner` para facilitar el mocking y las pruebas unitarias sin invocar ejecutables reales.

2. **Developer Agent (`developer`):**
   - Implementar `AgentRunner.cs` usando `System.Diagnostics.Process` y actualizar `Program.cs` para soportar `--execute`.

3. **Tester Agent (`tester`):**
   - Implementar las pruebas unitarias correspondientes en `AIHarness.Tests`.
