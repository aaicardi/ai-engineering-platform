# ADR-001: Inyección de la suite de agentes y aislamiento de la sesión `claude -p`

- **Estado:** Aceptado
- **Fecha:** 2026-10-10
- **Autor/Agente:** Architect / Jhoel

## Contexto
La spec 014 hace que `--process-issue` ejecute el agente `lead` (spec 013), que delega en los demás subagentes,
sobre **cualquier** repositorio destino, incluidos repositorios externos sin `.claude/`. Hay que decidir:

1. Cómo poner la suite (8 agentes + skills) a disposición de la sesión sin modificar el repositorio destino.
2. Cómo garantizar que los permisos de la sesión son los del Harness y no los que declare el repositorio destino
   (hallazgo SEC-5 de la revisión de la spec 013).

Se verificó con Claude Code 2.1.283 (Tarea 0 de la spec 014), en un repositorio de prueba con agentes y skills mínimos:

| Comprobación | Resultado |
|---|---|
| `claude -p --agent X` aplica el prompt, herramientas y modelo de X como agente principal | Sí |
| El agente principal puede delegar en subagentes (`Agent`) en `-p` | Sí |
| Agentes cargados con `--plugin-dir` | Se registran con prefijo: `<plugin>:<agente>` |
| Agentes cargados con `--agents <archivo.json>` | Nombre sin prefijo; `tools` y `model` se aplican; **ganan** sobre `.claude/agents/` del repositorio |
| Skills cargadas con `--plugin-dir` | Disponibles en `-p` como `<plugin>:<skill>` |
| `--disallowedTools` dentro de un subagente | Se respeta |
| `.claude/settings.json` del destino con `allow: ["Bash(*)"]`, sin `--restricted` | Ignorado solo porque el directorio no fue marcado como confiable; se aplicaría si alguien lo abre de forma interactiva y acepta el diálogo de confianza |
| `--restricted` | Ignora los archivos de configuración de usuario, proyecto y local; `--settings` sigue aplicando (`allow` probado: permitido vs. denegado); limita las herramientas de archivos al directorio de trabajo; bloquea escrituras en `.git/`; el `CLAUDE.md` del destino se sigue cargando |
| Agentes, skills y comandos propios del destino (`.claude/agents`, `.claude/skills`) bajo `--restricted` | **No se cargan**: un agente plantado no aparece y una skill con el mismo nombre que una de la suite no la sustituye (sin `--restricted`, sí: la del destino gana) |
| Reglas `Edit(/x)` en un archivo pasado con `--settings` | `/x` se resuelve respecto a la carpeta de ese archivo, no del repositorio; `./x` se resuelve respecto al directorio de trabajo |
| `--allowedTools`/`--disallowedTools` | Aceptan varios valores y consumen el argumento siguiente: el prompt debe ir por STDIN (como ya hace `AgentRunner`) |
| Comandos de solo lectura (`git log`, `echo`, `uname`) y de archivos en el directorio de trabajo con `acceptEdits` (`touch`) | Claude Code los permite sin regla explícita |

## Decisión
`AgentRunner` invoca la CLI así (prompt por STDIN):

```
claude -p --restricted --strict-mcp-config
       --tools Bash,Read,Edit,Write,Grep,Glob,Agent,Skill,TodoWrite
       --permission-mode acceptEdits
       --settings <run>/settings.json
       --agents <run>/agents.json
       --plugin-dir <run>/plugin
       --agent lead
```

- **Agentes → `--agents`**, un JSON generado en cada ejecución a partir de `.claude/agents/*.md` del Harness
  (`AgentDefinition`: `description`, `prompt` = cuerpo, `tools`, `model`). Conserva los nombres cortos en los que
  delega `lead`.
- **Precedencia: la suite del Harness gana** sobre los agentes del repositorio destino con el mismo nombre. Se cambia
  así RF-03 de la spec 014 ("el destino tiene prioridad"), por dos motivos: el contrato entre agentes (formatos de
  informe que consume `lead`) solo es consistente con la versión del Harness, y un repositorio externo no debe poder
  redefinir los agentes que ejecuta el pipeline (SEC-5). Si el destino define agentes con el mismo nombre, el
  Harness lo advierte.
- **Skills → `--plugin-dir`**, un plugin generado en cada ejecución que contiene **solo** las skills (sin agentes,
  para no duplicarlos con prefijo). Los agentes se refieren a las skills por su nombre corto y el modelo las
  resuelve con el prefijo del plugin.
- **Permisos → `--restricted` + `--settings`**, generado en cada ejecución por el Harness: `allow` con las
  herramientas de edición, `git status/diff/log/show` y los comandos de build/test del stack detectado
  (`StackProfile`); `deny` con las reglas `deny` **y** `ask` de `.claude/settings.json` del Harness (en `-p` nadie
  puede responder un *ask*) más `git commit/push`, `gh`, aplicar migraciones e instalaciones de paquetes fuera del
  repositorio o desde URLs. Las rutas `/x` de esas reglas se reescriben a `./x` para que apunten al repositorio destino.
  `StackProfile` no permite `npx` (descarga y ejecuta paquetes no instalados) ni plantillas arbitrarias de
  `dotnet new`/`npm init`; los scripts del repositorio se ejecutan con `npm run`.
- `--tools` lista las herramientas integradas porque `--restricted` elimina `Bash` si no se nombra.
- Los archivos generados (`settings.json`, `agents.json`, `plugin/`) van en
  `~/.aiharness/runs/<owner>/<repo>/<issue>/<timestamp>/`, fuera del repositorio destino; esa misma carpeta guarda
  el prompt y el informe para la trazabilidad (RF-11).

### Alternativas descartadas
- **Todo en un plugin (`--plugin-dir`) incluidos los agentes:** los agentes quedan con prefijo (`aih:developer`) y
  conviven con los del destino (`developer`): la delegación de `lead` sería ambigua.
- **Copiar la suite a `.claude/` del destino:** modifica el repositorio destino y el auto-commit la publicaría.
- **`--allowedTools`/`--disallowedTools` en lugar de `--settings`:** no evitan que se carguen los permisos del
  destino; con `--restricted` + `--settings` hay un único origen de permisos.

## Consecuencias
- **Positivas:**
  - El repositorio destino no se modifica ni puede ampliar permisos ni redefinir agentes (cierra SEC-5).
  - Las herramientas y el modelo por agente de la spec 013 se aplican también en el pipeline del Harness.
  - El ejecutor queda limitado al directorio del repositorio destino y no puede escribir en `.git/`.
- **Riesgos / Trade-offs:**
  - `--restricted` también ignora la configuración de usuario (`~/.claude/settings.json`) en estas ejecuciones.
  - Los comandos que Claude Code considera de solo lectura se ejecutan sin regla explícita; las reglas `deny` siguen
    aplicando sobre ellos.
  - `npm install <paquete>` y `dotnet add package` siguen permitidos (el agente necesita dependencias) y ejecutan
    scripts de instalación del registro; el `CLAUDE.md` del destino se sigue cargando y puede contener instrucciones.
    Ambos entran en el riesgo aceptado hasta la spec 015.
  - Dependencia de opciones de la CLI (`--restricted`, `--agents`): un cambio de comportamiento en futuras versiones
    rompería el aislamiento. Mitigación: tests de los argumentos exactos y repetir la Tarea 0 al actualizar Claude Code.
  - El código que escribe un agente y ejecuta `dotnet test`/`npm test` sigue pudiendo usar la red y las credenciales
    del entorno (riesgo aceptado en la spec 013); lo reduce RF-16 (token limitado y entorno sin credenciales del usuario).
