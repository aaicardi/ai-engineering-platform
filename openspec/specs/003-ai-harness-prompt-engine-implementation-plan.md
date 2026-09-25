# Implementation Plan: AI Harness Agent Prompt Engine (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Diseñar la interfaz del servicio de contexto (`IContextBuilder`).

2. **Developer Agent (`developer`):**
   - Implementar `ContextBuilder.cs` y actualizar `Program.cs` para soportar argumentos de entrada en la CLI.
   - Implementar la exportación del prompt unificado a `.claude/tmp/current-prompt.md`.

3. **Tester Agent (`tester`):**
   - Validar el manejo de errores en caso de que el agente o la especificación indicada no existan.
