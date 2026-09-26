# Implementation Plan: AI Harness OpenSpec Spec Generator (.NET 10)

## Agent Sequence & Responsibilities

1. **Architect Agent (`architect`):**
   - Diseñar la plantilla de salida de `SpecGenerator.cs` para mantener la sintaxis oficial de OpenSpec.

2. **Developer Agent (`developer`):**
   - Implementar `SpecGenerator.cs` y actualizar `Program.cs` para soportar `--generate-spec`.

3. **Tester Agent (`tester`):**
   - Validar que el archivo `.md` se escriba correctamente en `openspec/specs/` sin sobrescribir archivos existentes sin advertencia.
