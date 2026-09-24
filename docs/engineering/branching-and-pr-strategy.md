# Strategy: Branching & Pull Requests

## 1. Estrategia de Ramas
- `main`: Rama de producción/estabilidad. No se permiten commits directos.
- `feat/...`: Nuevas funcionalidades o User Stories.
- `fix/...`: Corrección de bugs.
- `chore/...`: Mantenimiento, CI/CD, dependencias.
- `docs/...`: Documentación técnica.

## 2. Flujo de Trabajo (Level 4 Autonomy)
1. Abrir Issue en GitHub / OpenSpec.
2. Crear rama asociada.
3. Desarrollo asistido por Agentes (Claude Code).
4. Pull Request + Verificación CI.
5. **Aprobación Humana Explícita** antes de Merge.
