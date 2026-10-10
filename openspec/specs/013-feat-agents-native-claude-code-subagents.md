# OpenSpec 013: feat(agents): Native Claude Code agent suite with stack skills

## Issue Reference
Sin Issue: implementada directamente en una sesión de Claude Code (no a través de AI Harness).

## Context & Objectives
El propósito de la plataforma es que AI Harness tome **cualquier Historia de Usuario** de un Issue y complete el flujo
hasta el Pull Request con calidad de ingeniería. Hoy los 7 agentes de `.claude/agents/` son solo texto que
`ContextBuilder` inserta en el prompt: no tienen frontmatter, Claude Code no los registra como subagentes, todos
tienen los mismos permisos y describen la misión del rol, pero no cómo trabajar.

Esta spec construye la **suite de agentes** (contenido y configuración). La spec 014 la conecta al pipeline del Harness.

### Decisiones tomadas (2026-10-09)
| Decisión | Elección |
|---|---|
| Orquestación | Claude Code orquesta: un agente `lead` delega en los subagentes nativos (implementado en 014) |
| Hallazgos de revisión | Críticos/altos vuelven al developer (máx. 2 ciclos); el resto va al PR |
| Modelos | Por rol (tabla de RF-02) |
| Conocimiento de stack | Agentes agnósticos al stack + skills por stack |

## Functional Requirements

### Suite de agentes
- **RF-01:** La suite pasa a 8 agentes: los 7 actuales más **`lead`**, el coordinador que recibe la spec y
  delega en los demás. `lead` no edita código. Como un subagente no puede lanzar otros subagentes, `lead` debe ser el
  agente **principal** de la sesión (`claude --agent lead`, o `claude -p --agent lead` desde el Harness).
- **RF-02:** Cada agente tiene frontmatter YAML con `name` (igual al nombre del archivo), `description` (cuándo
  delegar en él, con disparadores concretos), `tools` y `model`:

  | Agente | tools | model | Modo |
  |---|---|---|---|
  | lead | Agent, Read, Grep, Glob, Bash, Write, TodoWrite, Skill | sonnet | Coordina; solo escribe `.aiharness/run-report.md` |
  | architect | Read, Grep, Glob, Bash, Write, Skill | sonnet | Escribe solo el plan / ADR |
  | developer | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet | Implementa |
  | tester | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet | Escribe y ejecuta tests; no corrige producción |
  | code-reviewer | Read, Grep, Glob, Bash, Skill | inherit | Solo lectura |
  | security-reviewer | Read, Grep, Glob, Bash, Skill | opus | Solo lectura |
  | devops | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet | Solo si la spec toca contenedores o CI |
  | documentation | Read, Edit, Write, Grep, Glob, Skill | haiku | Sin ejecución de comandos |

  `Skill` es necesario para que los agentes carguen las skills de RF-08 a RF-10.

- **RF-03:** El cuerpo de cada agente tiene, en este orden:
  1. `## Mission`
  2. `## Workflow` — pasos numerados y verificables.
  3. `## Inputs & Outputs` — qué recibe y qué entrega (archivo, ruta, formato).
  4. `## Boundaries` — lo que no hace, incluidas las acciones de CLAUDE.md §3 (push, merge, migraciones, secretos, despliegues).
  5. `## Definition of Done` — criterios de éxito y quality gates del rol.
  6. `## Escalation` — cuándo detenerse y reportar `BLOCKED` en lugar de suponer.
  7. `## Report Format` — estructura exacta del mensaje que devuelve a `lead`.
- **RF-04:** Los agentes son **agnósticos al stack**: el primer paso de su Workflow es leer el `CLAUDE.md` del
  repositorio destino, detectar el stack (archivos `*.sln`/`*.csproj`, `package.json`, `angular.json`, migraciones)
  y cargar la skill de stack correspondiente (RF-08). No contienen rutas ni nombres propios de AI Harness.
- **RF-05:** Las capacidades del roadmap sin soporte actual (GitHub Wiki, IaC, staging/producción, OpenTelemetry,
  OpenAPI) se conservan marcadas "cuando exista en el repositorio"; el agente no las crea por iniciativa propia.

### Contrato de colaboración (lo ejecuta `lead`)
- **RF-06:** Flujo de `lead`:
  1. Validar la spec con la skill `openspec-validator`: `READY` → continuar; `NEEDS_REFINEMENT` (RF placeholder o
     criterios solo genéricos, típico de las specs generadas desde un Issue) → `architect` deriva requerimientos y
     criterios concretos en la sección *Refined Requirements* del plan; `NOT_READY` → `BLOCKED`.
  2. `architect` → plan en `openspec/specs/NNN-<slug>-implementation-plan.md` (convención existente) y ADR en
     `docs/decisions/` solo si hay una decisión de arquitectura nueva.
  3. `developer` → implementación según el plan, con build y tests en verde.
  4. `tester` → tests de los criterios de aceptación, casos límite y regresión; ejecuta la suite completa.
  5. `code-reviewer` y `security-reviewer` **en paralelo** sobre el diff (`git diff <base>...HEAD` + cambios sin confirmar).
  6. Hallazgos `CRITICAL`/`HIGH` → vuelven a `developer` (y a `tester` si afectan a tests); se repite el paso 5.
     Máximo **2 ciclos de corrección**.
  7. `devops` solo si el diff toca `Dockerfile`, `docker-compose*`, `.github/workflows/`; `documentation` si cambió
     comportamiento visible (CLI, API, configuración) o la spec lo pide.
  8. Escribir el informe de ejecución (RF-07).
- **RF-07:** Informe de ejecución en `.aiharness/run-report.md` del repositorio destino, con este formato:
  ```markdown
  status: COMPLETED | BLOCKED
  ## Summary
  ## Acceptance Criteria      (checklist con evidencia: test o archivo que lo cubre)
  ## Agents                   (agente → resultado en una línea)
  ## Open Findings            (severidad · archivo:línea · descripción; los MEDIUM/LOW y los no resueltos)
  ## Blockers                 (solo si status: BLOCKED)
  ```
  La escala de severidad común es `CRITICAL | HIGH | MEDIUM | LOW`; ambos revisores la usan con definición explícita.

### Skills
- **RF-08:** Skills de stack en `.claude/skills/<nombre>/SKILL.md` con frontmatter (`name`, `description` que
  indique cuándo cargarla):
  - `stack-dotnet` — .NET 10 / C#: estructura de soluciones, nullable, async, DI, xUnit, `dotnet build/test`.
  - `stack-typescript-node` — TypeScript/Node: strict mode, módulos, Jest/Vitest, `npm test`, lint.
  - `stack-react` — componentes, hooks, Testing Library.
  - `stack-angular` — standalone components, signals, servicios, `ng test`.
  - `stack-postgresql` — esquemas, migraciones (crear sí, **aplicar nunca**: CLAUDE.md §3), índices, consultas parametrizadas.

  Cada skill incluye: convenciones, cómo compilar/probar, patrones de test, anti-patrones y checklist de revisión.
- **RF-09:** Mover `.claude/skills/openspec-validator.md` a `.claude/skills/openspec-validator/SKILL.md` con
  frontmatter y criterios alineados con el formato real de `SpecGenerator`: H1 `# OpenSpec NNN: <título>`,
  `Closes #N`, requerimientos `RF-XX`, criterios de aceptación en checklist. `Task Breakdown` es recomendado.
  Veredictos `READY | NEEDS_REFINEMENT | NOT_READY`; los criterios que mencionan otro stack se ignoran sin bloquear.
- **RF-10:** Skill `review-checklist` compartida por ambos revisores: definición de la escala de severidad (RF-07)
  y checklist multidimensional (correctness, arquitectura, seguridad, performance, tests, mantenibilidad,
  estándares, dependencias, compatibilidad hacia atrás, contratos de API, cambios de base de datos).

### Compatibilidad con AI Harness y permisos interactivos
- **RF-11:** `ContextBuilder` excluye el frontmatter del agente al construir el prompt; un archivo sin frontmatter se
  inserta completo.
- **RF-12:** La auditoría (`RepositoryInspector`) espera los 8 agentes y valida su frontmatter (presente, `name`
  igual al archivo, `description` no vacía); si no es válido → `FAIL` con el motivo.
- **RF-13:** `.claude/settings.json` (sesiones interactivas en este repositorio):
  - Eliminar los nombres inválidos `FileEdit` y `FileWrite`.
  - `allow`: `Edit`, `Write`, `Bash(dotnet build:*)`, `Bash(dotnet test:*)`, `Bash(dotnet restore:*)`,
    `Bash(git status:*)`, `Bash(git diff:*)`, `Bash(git log:*)`, `Bash(git show:*)`.
  - `deny`: `Bash(git push:*)`, `Bash(gh pr merge:*)`, `Read(./.env)`, `Read(./.env.*)`.
- **RF-14:** Añadir `.aiharness/` a `.gitignore` (ningún informe se versiona).

## Architecture & Components
- **Contenido:** `.claude/agents/*.md` (8), `.claude/skills/{stack-*,openspec-validator,review-checklist}/SKILL.md`.
- **Configuración:** `.claude/settings.json`, `.gitignore`.
- **Código:**
  - Nuevo `src/AIHarness/Prompting/AgentDefinition.cs`: separa frontmatter y cuerpo; expone `Name`, `Description`,
    `Tools`, `Model`, `Body`. Parser mínimo sin dependencias nuevas (también lo usa la spec 014).
  - `ContextBuilder.cs` (RF-11), `RepositoryInspector.cs` (RF-12).

## Task Breakdown
1. [ ] **developer:** `AgentDefinition` + `ContextBuilder` + `RepositoryInspector`.
2. [ ] **tester:** tests de `AgentDefinition` (con/sin frontmatter, sin cierre, CRLF, `:` en valores, lista `tools`),
   `ContextBuilder` (prompt sin frontmatter) y `RepositoryInspector` (8 agentes, motivos de FAIL).
3. [ ] **architect + documentation:** redactar los 8 agentes (RF-01 a RF-07).
4. [ ] **documentation:** skills de stack, `openspec-validator` y `review-checklist` (RF-08 a RF-10).
5. [ ] **devops:** `settings.json` y `.gitignore` (RF-13, RF-14).
6. [ ] **code-reviewer / security-reviewer:** revisar el diff, en especial permisos y límites de cada agente.

## Out of Scope (spec 014)
- `SpecGenerator` escribe criterios de aceptación fijos de .NET ("La solución .NET 10 compila") aunque el repositorio
  destino sea de otro stack. En esta spec lo mitiga `openspec-validator`; la corrección va en 014.
- Ejecutar `lead` desde el Harness, inyectar la suite en repositorios externos, permisos de `claude -p`, leer el
  informe y llevarlo al cuerpo del PR, fallar ante `status: BLOCKED`.
- Unificar las convenciones de OpenSpec (`openspec/changes/` vs `openspec/specs/NNN-*.md`).
- Retirar credenciales de desarrollo de `.claude/mcp.json` y `docker-compose.yml` (requiere aprobación humana).

## Acceptance Criteria
- [ ] Los 8 agentes aparecen como subagentes en una sesión de Claude Code abierta en el repositorio.
- [ ] `code-reviewer` y `security-reviewer` no pueden editar archivos.
- [ ] En una sesión `claude --agent lead`, pedir "implementa la spec NNN" ejecuta el flujo de RF-06 y
      produce `.aiharness/run-report.md` con el formato de RF-07.
- [ ] Las skills `stack-*`, `openspec-validator` y `review-checklist` aparecen como disponibles.
- [ ] El prompt de `--agent developer --spec <spec> --no-save` no contiene frontmatter.
- [ ] La auditoría reporta `AUDITORÍA EXITOSA`, y `FAIL` si un agente pierde su frontmatter.
- [ ] La suite de tests pasa al 100% y el CI está en verde.
