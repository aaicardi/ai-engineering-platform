# AI Engineering Platform

Plataforma base para **AI-Native Software Engineering**, orientada a equipos de ingeniería con desarrollo asistido por agentes.

El repositorio cumple dos funciones:

1. **Golden Repository** — gobernanza, suite de agentes, plantillas y especificaciones OpenSpec que sirven de referencia para otros proyectos.
2. **AI Harness** — CLI en .NET 10 (`src/AIHarness`) que lleva un Issue de GitHub hasta un Pull Request: genera la especificación, ejecuta un agente de Claude Code, valida con el Quality Gate del repositorio y prepara el PR.

## Stack Principal
- .NET 10
- TypeScript
- React
- Angular
- Docker

## Estructura del Repositorio
```
.
├── .claude/
│   ├── agents/            # Subagentes de Claude Code (lead, architect, developer, tester, ...)
│   ├── commands/opsx/     # Comandos del flujo OpenSpec (propose, apply, archive, explore)
│   └── skills/            # Skills: OpenSpec, validación de specs, revisión y stacks
├── .github/               # Plantillas de Issues y PRs, CODEOWNERS y workflow de CI
├── docs/                  # Documentación de ingeniería y plantilla de ADR
├── openspec/specs/        # Especificaciones OpenSpec (una por funcionalidad)
├── src/AIHarness/         # CLI AI Harness
├── tests/AIHarness.Tests/ # Pruebas unitarias (xUnit)
├── AIHarness.sln          # Solución .NET
├── appsettings.json       # Configuración descriptiva de la plataforma
├── Dockerfile             # Imagen base (placeholder)
├── docker-compose.yml     # PostgreSQL de desarrollo
└── CLAUDE.md              # Guía de gobernanza y pautas de trabajo
```

## AI Harness

### Pipeline
```
Issue de GitHub → spec OpenSpec → rama feature/<issue>-<slug> → agente (claude -p) → commit → Quality Gate → Pull Request
```

El orquestador (`AgentOrchestrator`) recorre las etapas `Preflight → Bootstrap → Branch → Spec → Agent → Commit → Tests → PullRequest` y se detiene en la primera que falla, indicando la etapa en el error.

- **Preflight:** exige un árbol de trabajo sin cambios en archivos versionados.
- **Branch:** crea o reutiliza `feature/<issue>-<slug>`, de modo que una ejecución interrumpida puede reanudarse.
- **Spec:** genera `openspec/specs/NNN-<slug>.md` desde el Issue o reutiliza la existente (nunca sobrescribe).
- **Agent:** envía el prompt unificado (definición del agente + spec + CLAUDE.md) por STDIN a `claude -p`. No se omiten permisos: aplica la configuración de Claude Code del repositorio.
- **Commit:** confirma los cambios del agente con el título de la spec (`<tipo>: ... (closes #N)`).
- **Tests (Quality Gate):** detecta y ejecuta, en este orden, `dotnet test` (`*.sln`, `*.slnx` o `*.csproj` en la raíz), `npm test` (script `test` real en `package.json`) o `make test`.
- **PullRequest:** sin `--create-pr` solo imprime los comandos `git push` y `gh pr create` para que una persona los ejecute; con `--create-pr` los ejecuta.

### Requisitos
- .NET SDK 10
- `git`
- [Claude Code CLI](https://docs.claude.com/en/docs/claude-code) (`claude`) para `--execute`, `--orchestrate` y `--process-issue`
- [GitHub CLI](https://cli.github.com/) (`gh`) para `--create-pr` y para clonar repositorios externos
- Credenciales de GitHub: `GH_TOKEN`, `GITHUB_TOKEN` o una sesión de `gh auth login` (sin ellas el acceso es anónimo y con límite de tasa reducido). Solo se muestra el origen del token, nunca su valor.

### Compilación y pruebas
```bash
dotnet build AIHarness.sln
dotnet test AIHarness.sln
```

### Uso
Desde la raíz del repositorio (o con `--root <dir>`; sin él, la raíz se descubre subiendo desde el directorio actual hasta encontrar `CLAUDE.md`):

```bash
alias aiharness='dotnet run --project src/AIHarness --'
```

| Comando | Descripción |
|---|---|
| `aiharness` | Auditoría del repositorio: CLAUDE.md, agentes esperados y specs |
| `aiharness --agent <nombre> --spec <archivo>` | Construye el prompt unificado, lo imprime y lo guarda en `.claude/tmp/current-prompt.md` (`--no-save` para no guardarlo) |
| `aiharness --agent <nombre> --spec <archivo> --execute` | Ejecuta el prompt con `claude -p` y devuelve su código de salida |
| `aiharness --agent <nombre> --spec <archivo> --orchestrate [--base <rama>] [--create-pr]` | Ciclo Git completo para una spec existente |
| `aiharness --issue <n> [--repo <owner/name>]` | Muestra un Issue de GitHub (repo por defecto: remote `origin`) |
| `aiharness --issue <n> --generate-spec` | Además genera la spec en `openspec/specs/` |
| `aiharness --process-issue <n> [--agent <nombre>] [--repo <owner/name>] [--target-dir <dir>] [--base <rama>] [--create-pr]` | Pipeline completo desde el Issue (agente `developer` por defecto) |
| `aiharness --version` | Versión de AI Harness y del runtime .NET |
| `aiharness --status` | Versión, runtime, sistema operativo y uptime |

Ejemplo — procesar el Issue #42 de este repositorio y dejar el PR preparado para revisión humana:
```bash
aiharness --process-issue 42
```

### Repositorios externos y greenfield
`--process-issue` puede trabajar sobre otro repositorio:

- `--target-dir <dir>`: usa esa copia local (debe ser un clon del mismo repositorio que el Issue). Si no existe o está vacía y se indica `--repo`, se clona ahí.
- `--repo <owner/name>` sin `--target-dir`: usa `~/.aiharness/workspaces/<owner>/<name>` y lo clona si no existe (`gh repo clone`).

Las definiciones de agentes siempre se toman de este repositorio. Si el repositorio destino no tiene `CLAUDE.md`, se genera la gobernanza inicial (CLAUDE.md, `.claude/agents/` y `openspec/specs/`); si además no tiene commits, ese bootstrap se convierte en el primer commit de la rama base.

### Códigos de salida
| Código | Significado |
|---|---|
| `0` | Éxito |
| `1` | Fallo de la operación (auditoría, Git, API de GitHub, agente, Quality Gate...) |
| `2` | Uso incorrecto (argumento inválido o combinación no permitida) |
| `127` | Ejecutable no encontrado (`claude`, `git`, `gh`...) |
| `130` | Cancelado por el usuario (Ctrl+C) |

Si el agente o el Quality Gate terminan con un código distinto de cero, ese código se propaga.

## Modelo de Agentes
La suite de `.claude/agents/` son **subagentes nativos de Claude Code**: cada uno declara en su frontmatter las
herramientas y el modelo que usa, y en su cuerpo un procedimiento (Workflow), entradas y salidas, límites,
Definition of Done, cuándo escalar a un humano y el formato de su informe.

| Agente | Rol | Herramientas | Modelo |
|---|---|---|---|
| **lead** | Coordina la implementación de una spec delegando en los demás; no edita código | Agent, Read, Grep, Glob, Bash, Write*, TodoWrite, Skill | sonnet |
| **architect** | Plan de implementación y ADRs | Read, Grep, Glob, Bash, Write, Skill | sonnet |
| **developer** | Implementación y corrección de hallazgos | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet |
| **tester** | Tests de los criterios de aceptación, casos límite y regresión | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet |
| **code-reviewer** | Revisión del diff (solo lectura) | Read, Grep, Glob, Bash, Skill | inherit |
| **security-reviewer** | Revisión de seguridad del diff (solo lectura) | Read, Grep, Glob, Bash, Skill | opus |
| **devops** | Docker, Compose y GitHub Actions; no despliega | Read, Edit, Write, Grep, Glob, Bash, Skill | sonnet |
| **documentation** | README y `docs/` alineados con el código | Read, Edit, Write, Grep, Glob, Skill | haiku |

\* `lead` solo escribe su informe `.aiharness/run-report.md` (ignorado por git).

### Flujo de `lead`
```
openspec-validator → architect (plan) → developer → tester → devops (si aplica)
  → code-reviewer ∥ security-reviewer → CRITICAL/HIGH vuelven a developer y se revisa de nuevo (máx. 2 ciclos)
  → documentation (si aplica) → verificación final → .aiharness/run-report.md
```
Si la spec no es implementable o se necesita una acción que requiere aprobación humana, `lead` termina con
`status: BLOCKED` y las preguntas a resolver.

### Uso
- **Flujo completo:** `claude --agent lead` y pedir, por ejemplo, *"implementa openspec/specs/013-....md"*.
  `lead` debe ser el agente **principal** de la sesión: un subagente no puede delegar en otros subagentes.
- **Un especialista:** desde cualquier sesión, *"usa el agente code-reviewer para revisar los cambios respecto a main"*.

### Skills
Los agentes son independientes del stack: detectan el del repositorio y cargan la skill correspondiente de `.claude/skills/`.

| Skill | Uso |
|---|---|
| `openspec-validator` | ¿La spec está lista? `READY`, `NEEDS_REFINEMENT` o `NOT_READY` |
| `review-checklist` | Escala de severidad común y checklist de revisión |
| `stack-dotnet`, `stack-typescript-node`, `stack-react`, `stack-angular`, `stack-postgresql` | Convenciones, comandos, testing y revisión por stack |

### Permisos interactivos
[`.claude/settings.json`](.claude/settings.json):
- **Permitido:** editar, `dotnet restore/build/test` y `git status/diff/log/show`.
- **Pregunta antes** (en `claude -p` queda denegado): editar `.claude/`, `.github/`, `CLAUDE.md`, `.mcp.json` y archivos
  de build compartidos (`Directory.Build.*`, `*.targets`, `NuGet.config`); `git checkout/switch/reset/stash/clean/restore`
  y opciones globales de git (`git -C`, `git -c`, `git --…`).
- **Denegado:** `git push`, `gh pr merge`, `gh auth token`, `git diff` sobre rutas fuera del repositorio o con
  `--no-index`, `git … --output`, editar `.git/` y la configuración global de git, leer `.env*`, `~/.ssh` y `~/.config/gh`.

Estas reglas solo ven el comando que escribe el agente, no lo que ejecuta un proceso hijo (por ejemplo, un test).
La garantía real de CLAUDE.md §3 está en GitHub: `main` protegida sin bypass y credenciales limitadas para el Harness.
Además, todos los agentes tratan el contenido de los Issues como datos no confiables.

## Gobernanza (Nivel de Autonomía 4 / D)
- **Acciones automáticas:** lectura de código, análisis de arquitectura, ejecución de tests locales, creación de especificaciones OpenSpec.
- **Requieren aprobación humana explícita:** `git push` a ramas remotas, merge de Pull Requests, migraciones de base de datos, creación/modificación de secretos, despliegues en entornos.

AI Harness aplica esta política: el push y la creación del PR solo ocurren con `--create-pr`, y el merge queda siempre en manos humanas. Ver el detalle completo en [`CLAUDE.md`](./CLAUDE.md).

## CI
El workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) se ejecuta en cada push y PR a `main`: valida la estructura del repositorio y `appsettings.json`, compila y prueba la solución, y ejecuta la auditoría de AI Harness.

## Cómo Empezar
1. Ubicarse en la ruta de trabajo: `/home/aaicardi/workspace/ai-engineering-platform` (Linux WSL2 Ubuntu 24.04).
2. Compilar y probar: `dotnet test AIHarness.sln`.
3. Ejecutar la auditoría: `dotnet run --project src/AIHarness`.
4. Revisar las especificaciones activas en `openspec/specs/` y el proceso de trabajo en [`docs/engineering/`](docs/engineering/).
5. Seguir las convenciones de commits: **Conventional Commits** (`feat:`, `fix:`, `docs:`, `chore:`, `refactor:`).

## Especificaciones
Toda funcionalidad nueva debe contar con su especificación correspondiente en `openspec/specs/`.

| Spec | Funcionalidad |
|---|---|
| 001 | Configuración inicial de la plataforma |
| 002 | Núcleo de AI Harness (auditoría del repositorio) |
| 003 | Motor de prompts de agentes |
| 004 | Conector de la API de GitHub |
| 005, 006 | Generador de OpenSpec desde Issues |
| 006 | Plataforma de testing y calidad |
| 007 | Ejecución de agentes (`--execute`) |
| 008 | Automatización Git en el orquestador (`--orchestrate`) |
| 009 | Flag `--version` |
| 010 | Pipeline unificado `--process-issue` y commit automático |
| 011 | Uptime en `--status` |
| 012 | Workspaces multi-repositorio y bootstrap greenfield |
| 013 | Suite de subagentes nativos y skills de stack |
| 014 | Pipeline multiagente en `--process-issue` (pendiente) |
