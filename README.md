# AI Engineering Platform

Plataforma base para **AI-Native Software Engineering**, orientada a equipos de ingeniería con desarrollo asistido por agentes.

## Estado Actual

Golden Repository inicializado con estructura base, suite de agentes definida y configuración inicial del proyecto. Ver detalle en `openspec/specs/001-initial-platform-spec.md`.

| Tarea | Descripción | Estado |
|---|---|---|
| 1 | Definición de la suite de agentes (`.claude/agents/`) | ✅ Completada |
| 2 | Archivo de configuración base (`appsettings.json`) | ✅ Completada |
| 3 | Documentación del Golden Repository (`README.md`) | ✅ Completada |

## Stack Principal
- .NET 10
- TypeScript
- React
- Angular
- Docker

## Estructura del Repositorio
```
.
├── .claude/agents/     # Definiciones de los agentes (architect, developer, tester, ...)
├── .github/            # Plantillas de Issues, PRs y CODEOWNERS
├── docker/             # Recursos de contenedores
├── docs/               # Documentación técnica adicional
├── openspec/specs/     # Especificaciones OpenSpec del proyecto
├── tests/              # Pruebas unitarias e integración
├── appsettings.json    # Configuración base de la plataforma
└── CLAUDE.md           # Guía de gobernanza y pautas de trabajo
```

## Modelo de Agentes
El desarrollo en este repositorio se apoya en agentes especializados, definidos en `.claude/agents/`:

1. **Architect** — Requerimientos, diseño técnico, diagramas y decisiones.
2. **Developer** — Implementación de código siguiendo estándares.
3. **Tester** — Diseño y ejecución de pruebas unitarias/integración.
4. **Code-Reviewer** — Calidad, mantenibilidad y patrones de diseño.
5. **Security-Reviewer** — Análisis de vulnerabilidades y seguridad local.
6. **DevOps** — Contenedores (Docker), CI/CD y despliegue.
7. **Documentation** — README, OpenSpec, wikis y documentación técnica.

## Gobernanza (Nivel de Autonomía 4 / D)
- **Acciones automáticas:** lectura de código, análisis de arquitectura, ejecución de tests locales, creación de especificaciones OpenSpec.
- **Requieren aprobación humana explícita:** `git push` a ramas remotas, merge de Pull Requests, migraciones de base de datos, creación/modificación de secretos, despliegues en entornos.

Ver el detalle completo en [`CLAUDE.md`](./CLAUDE.md).

## Cómo Empezar
1. Ubicarse en la ruta de trabajo: `/home/aaicardi/workspace/ai-engineering-platform` (Linux WSL2 Ubuntu 24.04).
2. Revisar las especificaciones activas en `openspec/specs/`.
3. Consultar `appsettings.json` para la configuración base de la plataforma.
4. Seguir las convenciones de commits: **Conventional Commits** (`feat:`, `fix:`, `docs:`, `chore:`, `refactor:`).

## Especificaciones
Toda funcionalidad nueva debe contar con su especificación correspondiente en `openspec/specs/`.
