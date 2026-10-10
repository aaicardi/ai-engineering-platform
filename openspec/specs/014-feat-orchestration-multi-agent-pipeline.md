# OpenSpec 014: feat(orchestration): Run the multi-agent pipeline from --process-issue

## Issue Reference
Closes #<pendiente>

> Depende de la spec 013 (suite de agentes, `AgentDefinition`, informe `.aiharness/run-report.md`).

## Context & Objectives
`--process-issue` ejecuta hoy un solo agente (`developer`) con `claude -p`, sin permisos explícitos: en un
repositorio externo sin `.claude/settings.json`, el agente no puede ejecutar ni los tests. Además, la descripción
del PR es fija y no refleja lo que ocurrió.

**Objetivo:** que `--process-issue N` ejecute la suite de la spec 013 coordinada por `lead`, en cualquier
repositorio destino, con permisos de mínimo privilegio y un PR que documente el resultado.

## Functional Requirements

### Ejecución de `lead`
- **RF-01:** El agente por defecto de `--process-issue` pasa de `developer` a `lead`. `--agent <nombre>` sigue
  permitiendo ejecutar un único agente.
- **RF-02:** `AgentRunner` invoca `claude -p --agent <nombre>`, de modo que la sesión principal aplica el `tools` y el
  `model` del agente. El prompt por STDIN contiene CLAUDE.md + la spec, y la instrucción de la base contra la que se
  calcula el diff (`--base`).

### Suite disponible en cualquier repositorio destino
- **RF-03:** La suite del Harness (agentes y skills de la spec 013) se pone a disposición de la sesión **sin
  modificar el repositorio destino**. Un agente o skill que el destino ya define tiene prioridad (misma semántica de
  respaldo que `ContextBuilder` hoy).
- **RF-04:** Mecanismo: el Harness genera, por ejecución, un plugin temporal (`.claude-plugin/plugin.json`, `agents/`,
  `skills/`) fuera del repositorio destino y lo pasa con `--plugin-dir`. Si la Tarea 0 demuestra que los agentes de
  un plugin quedan con nombre calificado (`plugin:agente`) y eso rompe la delegación de `lead`, se usa
  `--agents <archivo.json>` (generado con `AgentDefinition`) para los agentes, y `--plugin-dir` solo para las skills.
- **RF-05:** El bootstrap greenfield (`WorkspaceBootstrapper`) sigue copiando los agentes al repositorio nuevo.

### Permisos de `claude -p` (mínimo privilegio)
- **RF-06:** `AgentRunner` pasa permisos explícitos, independientes de la configuración del repositorio destino:
  - `--permission-mode acceptEdits`
  - `--allowedTools`: `Read`, `Grep`, `Glob`, `Edit`, `Write`, `Agent`, `TodoWrite`,
    `Bash(git status:*)`, `Bash(git diff:*)`, `Bash(git log:*)`, `Bash(git show:*)`, y los comandos de build/test del
    stack detectado (`dotnet build/test/restore`, `npm test`, `npm run build`, `npm run lint`, `npx ng test`,
    `make test`).
  - `--disallowedTools`: `Bash(git push:*)`, `Bash(git commit:*)`, `Bash(git checkout:*)`, `Bash(git reset:*)`,
    `Bash(gh:*)`, `Bash(rm -rf:*)`, `Read(./.env)`, `Read(./.env.*)`, `Bash(dotnet ef database update:*)`.
  - Nunca `bypassPermissions`.
- **RF-07:** La detección del stack reutiliza `QualityGate.Detect` y se amplía con un `StackProfile` que devuelve
  los comandos permitidos. Los commits siguen siendo del Harness (etapa Commit), no del agente.

### Resultado de la ejecución
- **RF-08:** Tras la etapa Agent, el Harness lee `.aiharness/run-report.md`:
  - `status: BLOCKED` → la etapa Agent falla (código 3, nuevo), se muestran los bloqueos y no se hace commit ni PR.
    La rama queda para retomar.
  - Archivo inexistente con `--agent lead` → advertencia; se continúa con la descripción de PR por defecto.
  - El archivo se elimina antes de la etapa Commit (también lo cubre `.gitignore`).
- **RF-09:** La descripción del PR = `Closes #N` + referencia a la spec + el contenido del informe (Summary,
  Acceptance Criteria, Agents, Open Findings). Se pasa a `gh pr create` con `--body-file` (no por argv), también en
  los comandos que se imprimen cuando no se usa `--create-pr`.
- **RF-10:** Nueva etapa visible en los logs y en `OrchestrationStage`: `Report`, entre `Agent` y `Commit`.

### Specs agnósticas al stack
- **RF-12:** `SpecGenerator` deja de escribir criterios de aceptación fijos de .NET: usa el `QualityGate` detectado en el
  repositorio destino ("`<comando>` pasa") o, si no hay ninguno, un criterio genérico.

### Trazabilidad
- **RF-11:** Se guarda una copia del informe y del prompt en `~/.aiharness/runs/<owner>/<repo>/<issue>/<timestamp>/`
  para auditoría (fuera del repositorio).

## Architecture & Components
- `Execution/AgentRunner.cs` — argumentos `--agent`, `--plugin-dir`/`--agents`, permisos (RF-02, RF-04, RF-06).
- Nuevo `Execution/AgentSessionOptions.cs` — agente, plugin/agents, herramientas permitidas y denegadas.
- Nuevo `Orchestration/StackProfile.cs` — comandos por stack (RF-07).
- Nuevo `Orchestration/RunReport.cs` — parseo de `status` y secciones (RF-08).
- `Orchestration/AgentOrchestrator.cs` — etapa `Report`, código 3, cuerpo del PR (RF-08 a RF-10).
- `Orchestration/GitAutomationService.cs` — `--body-file` en `PullRequestArguments`.
- `Program.cs` — agente por defecto `lead`; documentación de uso.

## Task Breakdown
0. [ ] **architect (spike):** con `claude` 2.1.x, verificar (a) que `--agent lead` en `-p` puede delegar en
   subagentes, (b) los nombres de los agentes cargados con `--plugin-dir` y la prioridad frente a los del repo,
   (c) que las skills de un plugin se cargan en `-p`, y (d) que `--allowedTools`/`--disallowedTools` se respetan en
   los subagentes. Registrar el resultado como ADR en `docs/decisions/` y ajustar RF-04 si hace falta.
1. [ ] **developer:** `AgentSessionOptions`, `StackProfile`, cambios en `AgentRunner`.
2. [ ] **developer:** `RunReport`, etapa `Report`, código 3, `--body-file`, copia de trazabilidad.
3. [ ] **tester:** argumentos exactos pasados a `claude` (vía `IProcessRunner` falso), informe COMPLETED/BLOCKED/ausente,
   cuerpo del PR, eliminación del informe antes del commit, permisos por stack.
4. [ ] **security-reviewer:** revisar las listas de RF-06 (ninguna permite push, merge, migraciones ni leer secretos).
5. [ ] **documentation:** README (pipeline multiagente, códigos de salida con el 3, trazabilidad).
6. [ ] **Validación end-to-end:** procesar un Issue real pequeño en un repositorio de prueba y revisar el PR resultante.

## Out of Scope
- Paralelizar varios Issues a la vez.
- Telemetría OpenTelemetry del pipeline (se apoyará en el informe y la carpeta `runs/`).

## Acceptance Criteria
- [ ] `--process-issue N` ejecuta `lead`, que delega en los subagentes, y el PR incluye el informe de ejecución.
- [ ] En un repositorio externo sin `.claude/`, los agentes pueden ejecutar build y tests sin aprobación interactiva.
- [ ] Ningún agente puede hacer `git push`, `git commit`, usar `gh` ni leer `.env` (verificado en el test end-to-end).
- [ ] Un informe `status: BLOCKED` detiene el pipeline con código 3, sin commit ni PR.
- [ ] El repositorio destino no queda con archivos de la suite ni con `.aiharness/` después de la ejecución.
- [ ] La suite de tests pasa al 100% y el CI está en verde.
