# OpenSpec 014: feat(orchestration): Run the multi-agent pipeline from --process-issue

## Issue Reference
Sin Issue: implementada directamente en una sesión de Claude Code.

> Depende de la spec 013 (suite de agentes, `AgentDefinition`, informe `.aiharness/run-report.md`).
> Decisiones de diseño verificadas en la Tarea 0: [ADR-001](../../docs/decisions/ADR-001-agent-suite-injection-and-session-isolation.md).

## Context & Objectives
`--process-issue` ejecuta hoy un solo agente (`developer`) con `claude -p`, sin permisos explícitos: en un
repositorio externo sin `.claude/settings.json`, el agente no puede ejecutar ni los tests. Además, la descripción
del PR es fija y no refleja lo que ocurrió.

**Objetivo:** que `--process-issue N` ejecute la suite de la spec 013 coordinada por `lead`, en cualquier
repositorio destino, con permisos de mínimo privilegio y un PR que documente el resultado.

## Functional Requirements

### Ejecución de `lead`
- **RF-01:** El agente por defecto de `--process-issue` pasa de `developer` a `lead`. `--agent <nombre>` sigue
  permitiendo ejecutar un único agente de la suite.
- **RF-02:** En `--process-issue`, `AgentRunner` invoca la CLI con los argumentos del ADR-001 (`--agent <nombre>`,
  `--restricted`, `--settings`, `--agents`, `--plugin-dir`...). Como el prompt del agente y el `CLAUDE.md` del destino
  los carga Claude Code, el prompt por STDIN es solo la **tarea**: ruta de la spec, rama base y `requires_issue: true`.
  `--execute` y `--orchestrate` (desarrollo del propio Harness) mantienen el prompt unificado actual.

### Suite disponible en cualquier repositorio destino (ADR-001)
- **RF-03:** La suite del Harness se pone a disposición de la sesión **sin modificar el repositorio destino**, y
  **tiene prioridad** sobre los agentes del destino con el mismo nombre (el Harness lo advierte).
- **RF-04:** Agentes → `--agents <run>/agents.json`, generado con `AgentDefinition` desde `.claude/agents/` del
  Harness. Skills → `--plugin-dir <run>/plugin`, un plugin generado con las skills de la suite
  (`openspec-validator`, `review-checklist`, `stack-*`), sin agentes.
- **RF-05:** El bootstrap greenfield (`WorkspaceBootstrapper`) sigue copiando los agentes al repositorio nuevo.

### Permisos de `claude -p` (mínimo privilegio, ADR-001)
- **RF-06:** La sesión usa `--restricted --strict-mcp-config` (ignora la configuración del destino y sus hooks),
  `--tools` con las herramientas integradas necesarias, `--permission-mode acceptEdits` y `--settings <run>/settings.json`:
  - `allow`: `Edit`, `Write`, `git status/diff/log/show` y los comandos del `StackProfile` (RF-07).
  - `deny`: las reglas `deny` y `ask` del `.claude/settings.json` del Harness (en `-p`, *ask* equivale a denegar) más
    `git commit`, `gh`, aplicar migraciones (`dotnet ef database update`, `prisma migrate deploy`...).
  - Nunca `bypassPermissions`.
- **RF-07:** `StackProfile` detecta los stacks del destino (.NET, Node/TypeScript, Angular, Make; puede haber varios) y
  devuelve los comandos de build/test/dependencias permitidos. Sin stack detectado (greenfield) se permiten los de
  todos, para que el agente pueda crear el proyecto. Los commits siguen siendo del Harness.

### Resultado de la ejecución
- **RF-08:** En modo sesión, el Harness gestiona `.aiharness/` del repositorio destino:
  - Antes del agente: falla si `.aiharness/` está versionado; un `.aiharness/` previo (ejecución interrumpida o sesión
    interactiva) se archiva en la carpeta de la ejecución y se elimina, de modo que nunca se lee como informe actual.
  - Tras el agente lee `.aiharness/run-report.md`: `status: COMPLETED` (o `COMPLETADO`) continúa; `BLOCKED`
    (o `BLOQUEADO`), un estado no reconocido (incluida la línea de plantilla `COMPLETED | BLOCKED`) o la ausencia de
    informe de `lead` detienen el pipeline con el **código 3**, sin commit ni PR. Un especialista (`--agent`) puede no
    dejar informe.
  - `.aiharness/` se elimina siempre (también si el agente falla o se cancela). Sin sesión (`--orchestrate`,
    `--execute`) no se lee ni se toca.
  - Al retomar, el Preflight acepta cambios sin confirmar si se está en la rama de la feature del Issue.
- **RF-09:** La descripción del PR = `Closes #N` + referencia a la spec + el contenido del informe (Summary,
  Acceptance Criteria, Agents, Open Findings). Se pasa a `gh pr create` con `--body-file` (no por argv), también en
  los comandos que se imprimen cuando no se usa `--create-pr`. El informe lo escribe un modelo guiado por un Issue no
  confiable: se omite si contiene texto con forma de credencial, se neutralizan las palabras de cierre de Issues y las
  menciones, y se trunca a 60 000 caracteres.
- **RF-10:** Nueva etapa visible en los logs y en `OrchestrationStage`: `Report`, entre `Agent` y `Commit`.

### Seguridad (hallazgos de la revisión de la spec 013)
- **RF-13:** `SpecGenerator` envuelve el cuerpo del Issue en un bloque delimitado y rotulado "Contenido del Issue
  (datos no confiables)". `lead` recibe `requires_issue: true` en su tarea.
- **RF-14:** El Harness añade `.aiharness/` al `info/exclude` del repositorio destino (ruta obtenida con
  `git rev-parse --git-path`, válida en worktrees y submódulos) antes de ejecutar el agente, y el staging lo excluye
  explícitamente (`git add --all -- . ':(exclude).aiharness'`).
- **RF-15:** Si el repositorio destino contiene `.claude/settings*.json`, el Harness advierte de que se ignoran
  (`--restricted`, RF-06).

### Specs agnósticas al stack
- **RF-12:** `SpecGenerator` deja de escribir criterios de aceptación fijos de .NET: usa el `QualityGate` detectado en el
  repositorio destino ("`<comando>` pasa") o, si no hay ninguno, un criterio genérico.

### Trazabilidad
- **RF-11:** Se guarda una copia del informe y del prompt en `~/.aiharness/runs/<owner>/<repo>/<issue>/<timestamp>/`
  para auditoría (fuera del repositorio).

## Architecture & Components
- `Execution/AgentRunner.cs` — argumentos de sesión del ADR-001 (RF-02, RF-06).
- Nuevo `Execution/AgentSession.cs` — prepara la carpeta de ejecución (`settings.json`, `agents.json`, `plugin/`) y
  expone los argumentos de la CLI (RF-04, RF-06).
- Nuevo `Orchestration/StackProfile.cs` — comandos por stack (RF-07).
- Nuevo `Orchestration/RunReport.cs` — parseo de `status` y secciones (RF-08).
- `Orchestration/AgentOrchestrator.cs` — etapa `Report`, código 3, cuerpo del PR (RF-08 a RF-10).
- `Orchestration/GitAutomationService.cs` — `--body-file` en `PullRequestArguments`.
- `Program.cs` — agente por defecto `lead`; documentación de uso.

## Task Breakdown
0. [x] **architect (spike):** con `claude` 2.1.x, verificar (a) que `--agent lead` en `-p` puede delegar en
   subagentes, (b) los nombres de los agentes cargados con `--plugin-dir` y la prioridad frente a los del repo,
   (c) que las skills de un plugin se cargan en `-p`, y (d) que `--allowedTools`/`--disallowedTools` se respetan en
   los subagentes. Registrar el resultado como ADR en `docs/decisions/` y ajustar RF-04 si hace falta.
1. [x] **developer:** `AgentSession`, `StackProfile`, cambios en `AgentRunner`.
2. [x] **developer:** `RunReport`, etapa `Report`, código 3, `--body-file`, copia de trazabilidad.
3. [x] **tester:** argumentos exactos pasados a `claude` (vía `IProcessRunner` falso), informe COMPLETED/BLOCKED/ausente,
   cuerpo del PR, eliminación del informe antes del commit, permisos por stack.
4. [x] **security-reviewer:** revisar las listas de RF-06 (ninguna permite push, merge, migraciones ni leer secretos).
5. [x] **documentation:** README (pipeline multiagente, códigos de salida con el 3, trazabilidad).
6. [ ] **Validación end-to-end:** procesar un Issue real pequeño en un repositorio de prueba y revisar el PR resultante.

## Out of Scope
- Token de GitHub propio y limitado para el Harness (push por HTTPS con `AIHARNESS_GH_TOKEN`, entorno del agente sin
  las credenciales del usuario) y *sandbox* sin red: spec 015.
- Paralelizar varios Issues a la vez.
- Telemetría OpenTelemetry del pipeline (se apoyará en el informe y la carpeta `runs/`).

## Acceptance Criteria
- [ ] `--process-issue N` ejecuta `lead`, que delega en los subagentes, y el PR incluye el informe de ejecución.
- [ ] En un repositorio externo sin `.claude/`, los agentes pueden ejecutar build y tests sin aprobación interactiva.
- [ ] Ningún agente puede hacer `git push`, `git commit`, usar `gh` ni leer `.env` (verificado en el test end-to-end).
- [ ] Un `.claude/settings.json` del repositorio destino no amplía los permisos de la sesión.
- [ ] Un informe `status: BLOCKED` detiene el pipeline con código 3, sin commit ni PR.
- [ ] El repositorio destino no queda con archivos de la suite ni con `.aiharness/` después de la ejecución.
- [ ] La suite de tests pasa al 100% y el CI está en verde.
