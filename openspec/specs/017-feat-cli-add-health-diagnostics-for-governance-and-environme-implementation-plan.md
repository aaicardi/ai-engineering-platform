# Implementation Plan: feat(cli): Add --health diagnostics for governance and environment

## Approach
`--health` añade una nueva capa de diagnóstico (`AIHarness.Diagnostics`) que **no reemplaza** la auditoría actual
(modo sin argumentos) ni el flujo de credenciales existente, sino que los reutiliza como colaboradores: una nueva
clase `HealthDiagnostics` orquesta (a) `RepositoryInspector.Inspect()` ya existente para los 3 chequeos de
gobernanza, y (b) seis invocaciones de proceso externo (`git --version`, `dotnet --version`, `claude --version`,
`claude auth status`, `gh --version`, y la detección de credenciales) a través de `IProcessRunner`, con un timeout
por invocación, siguiendo exactamente el patrón ya usado por `GitAutomationService`/`AgentRunner`
(constructor con `IProcessRunner`, `workingDirectory` y un `TextWriter error` para progreso — **sin** `TextWriter
output`, ver más abajo). El resultado se modela con dos records nuevos, `HealthCheck` y `HealthReport`, con un
método `HealthReport.ToJson()` que construye el JSON a mano con `JsonObject`/`JsonSerializerOptions
{ WriteIndented = true }`, igual que ya hace `AgentSession.cs` para otros documentos JSON del repositorio (en vez de
serializar el record directamente, para controlar el nombre y el valor en minúsculas de cada campo sin atributos
nuevos).

La separación STDOUT/STDERR (RF-04) se garantiza **por construcción**, no por disciplina: `HealthDiagnostics` solo
recibe un `TextWriter error` para su progreso (`[INFO] Comprobando git...`, etc.) y nunca una referencia a
`Console.Out`; es `Program.cs` quien, una vez termina `RunAsync`, hace el único `Console.WriteLine(report.ToJson())`
de todo el comando. Así ninguna ruta de código dentro de `HealthDiagnostics` puede escribir en STDOUT por accidente
— y lo mismo garantiza que el valor de un token nunca pueda llegar ahí, porque `HealthDiagnostics` nunca llama a
nada que devuelva el valor de un token (ver el diseño del chequeo de credenciales más abajo).

**Decisión de diseño explícita (nivel `critical`/`warning` por chequeo, dado que RF-03 no es exhaustivo para
`dotnet` ni `openspec/specs`):** RF-03 enumera como `critical` únicamente "CLAUDE.md, agentes, `git`, `claude` y su
sesión" (5 nombres) y deja `gh` y "credenciales de GitHub" como `warning` explícitamente. No menciona ni `dotnet` ni
`openspec/specs`. Se decide tratar ambos como `warning`, por ser la lectura más consistente con el resto del
enunciado: `dotnet` solo hace falta para correr tests/build localmente (no para que `--process-issue` ejecute el
pipeline de agentes, que depende de `claude` y `git`), y `openspec/specs` es auditado por `RepositoryInspector`
junto a `CLAUDE.md`/agentes en RF-01 pero, igual que `gh`, no es necesario para que el pipeline arranque (una spec
nueva se genera con `--generate-spec` aun si el directorio está vacío). Queda documentado aquí para que
`code-reviewer`/`tester` puedan objetarlo con un cambio de una sola constante si no están de acuerdo.

**Decisión de diseño explícita (chequeo de credenciales de GitHub, no se reutiliza `GitHubTokenProvider.Resolve()`
tal cual):** el Issue describe las fuentes como "`GH_TOKEN`, `GITHUB_TOKEN` o `gh auth status`" — nótese
`gh auth status`, no `gh auth token`. `GitHubTokenProvider.Resolve()` ya existe y hace casi lo mismo, pero su
fallback de CLI ejecuta `gh auth token` (que **imprime el valor real del token** por stdout y lo materializa en
memoria como `GitHubToken.Value`) a través de un `ProcessRunner` interno y estático (no el `IProcessRunner`
inyectable), con lo que no es testeable con el fake sin tocar el PATH real de la máquina de pruebas. Reemplazarlo
por `GitHubTokenProvider.Resolve()` violaría RF-06 (todo proceso externo vía `IProcessRunner` con timeout, para
poder probarse sin herramientas reales) y aumentaría sin necesidad la superficie donde el valor del token existe en
memoria durante un comando que solo debe *diagnosticar*, no *usar*, la credencial. Por eso `HealthDiagnostics`
implementa su propio chequeo de credenciales: reutiliza `GitHubTokenProvider.EnvironmentVariables` (la lista
`["GH_TOKEN", "GITHUB_TOKEN"]`) para no duplicar esos nombres, lee las variables de entorno directamente (nunca su
valor sale del método, solo se compara contra vacío) y, si ninguna está definida, ejecuta `gh auth status` vía
`IProcessRunner` (que no imprime el token, solo el estado de la sesión) para decidir `ok`. Esto sí reutiliza
`GitHubTokenProvider` (su lista de nombres, la única parte reutilizable sin comprometer RF-05/RF-06) y no requiere
modificarlo. No hace falta ningún cambio en `GitHubTokenProvider.cs`.

No se introduce ningún ADR: el cambio sigue en su totalidad patrones ya existentes en el repositorio (servicio con
`IProcessRunner` inyectado + `TextWriter` para progreso, igual que `GitAutomationService`/`AgentRunner`; records
inmutables para el resultado, igual que `InspectionReport`/`RunReport`; JSON construido a mano con `JsonObject`,
igual que `AgentSession.cs`); no añade dependencias, almacenamiento ni un contrato público nuevo más allá de un
flag de CLI documentado. No hay decisión de arquitectura nueva que registrar.

## Changes
| Archivo | Cambio | RF |
|---|---|---|
| `src/AIHarness/Diagnostics/HealthCheck.cs` (nuevo) | `enum HealthCheckLevel { Critical, Warning }`; `record HealthCheck(string Name, HealthCheckLevel Level, bool Ok, string Detail)`. | RF-03, RF-04 |
| `src/AIHarness/Diagnostics/HealthReport.cs` (nuevo) | `record HealthReport(IReadOnlyList<HealthCheck> Checks)` con `bool Healthy => !Checks.Any(c => c.Level == HealthCheckLevel.Critical && !c.Ok)` y `string ToJson()` (construye `{"healthy":…, "checks":[{"name":…, "level":"critical"\|"warning", "ok":…, "detail":…}, …]}` con `JsonObject` + `WriteIndented = true`, nivel en minúsculas). | RF-04 |
| `src/AIHarness/Diagnostics/HealthDiagnostics.cs` (nuevo) | Servicio `HealthDiagnostics(IProcessRunner processRunner, string repositoryRoot, TextWriter error, Func<string, string?>? getEnvironmentVariable = null, TimeSpan? timeout = null)`. `Task<HealthReport> RunAsync(CancellationToken ct = default)`: 3 chequeos de gobernanza vía `new RepositoryInspector(repositoryRoot).Inspect()` (RF-01) + 6 chequeos de entorno vía `IProcessRunner` con timeout (`CancelAfter`, por defecto 5 s) (RF-02, RF-06); nunca escribe en STDOUT. | RF-01, RF-02, RF-03, RF-05, RF-06 |
| `src/AIHarness/Program.cs` | Nuevo flag `--health` en el bucle de parseo (igual patrón que `--generate-spec`/`--execute`); validación de incompatibilidad con `--process-issue`, `--issue`, `--repo`, `--agent`, `--spec`, `--generate-spec`, `--execute`, `--orchestrate` (exit 2); nuevo `static Task<int> RunHealthAsync(string root)` que construye `HealthDiagnostics` con `new ProcessRunner()` real y `Console.Error`, ejecuta `RunAsync`, imprime `report.ToJson()` con un único `Console.WriteLine` y devuelve `0`/`1` según `report.Healthy`; actualiza el comentario de uso al inicio del archivo. | RF-01..RF-06 |
| `README.md` | Nueva fila en la tabla de comandos (`### Uso`) para `aiharness --health [--root <dir>]`; mención en `### Requisitos` de que `--health` es la forma soportada de verificar las herramientas antes de `--process-issue`. | RF-01, RF-02 |
| `tests/AIHarness.Tests/HealthDiagnosticsTests.cs` (nuevo) | Unit tests de `HealthDiagnostics.RunAsync` con un `IProcessRunner` fake (reutiliza `ScriptedProcessRunner`, ya `internal` en `AgentOrchestratorTests.cs`, visible en todo el ensamblado de tests) y un `repositoryRoot` temporal (patrón de `RepositoryInspectorTests`). | RF-01..RF-06 |
| `tests/AIHarness.Tests/HealthCliTests.cs` (nuevo) | Tests end-to-end vía `CliHost.RunAsync("--health", "--root", dir)` para los escenarios cuyo resultado no depende de las herramientas reales instaladas en la máquina de CI (gobernanza incompleta, pureza de STDOUT). | RF-04 |

## Tasks
1. developer: crear `src/AIHarness/Diagnostics/HealthCheck.cs` con `HealthCheckLevel` y el record `HealthCheck`
   descritos en *Changes*; `Level` se serializa siempre en minúsculas (`"critical"`/`"warning"`), nunca el nombre
   del enum por defecto de .NET.
2. developer: crear `src/AIHarness/Diagnostics/HealthReport.cs` con el record `HealthReport` y `ToJson()`;
   `Healthy` se calcula solo a partir de `Checks` (sin estado mutable), igual que `InspectionReport.IsSuccessful`.
3. developer: crear `src/AIHarness/Diagnostics/HealthDiagnostics.cs` con el constructor y `RunAsync` descritos en
   *Changes*. Chequeos de gobernanza (orden fijo, reutilizando `RepositoryInspector`):
   - `"CLAUDE.md"` (`Critical`): `Ok = report.ClaudeMd.Exists`; detalle `"{LineCount} líneas, {Sections.Count}
     secciones"` o `"No existe: {Path}"`.
   - `"Agentes (.claude/agents)"` (`Critical`): `Ok = report.Agents.AllPresent`; detalle
     `"{validCount}/{RepositoryInspector.ExpectedAgents.Count} válidos"`, con los nombres inválidos y su motivo
     cuando `!Ok` (mismo dato que ya expone `AgentEntry.Problems`).
   - `"Especificaciones (openspec/specs)"` (`Warning`): `Ok = report.Specs.DirectoryExists &&
     report.Specs.Specs.Count > 0`; detalle `"{count} especificaciones encontradas"` o el motivo del fallo.
4. developer: en `HealthDiagnostics`, añadir un helper privado `CheckVersionAsync(name, level, executable, args,
   ct)` que ejecuta el proceso vía `IProcessRunner` con `CancellationTokenSource.CreateLinkedTokenSource(ct)` +
   `CancelAfter(_timeout)`, captura stdout, y devuelve `HealthCheck(name, level, exitCode == 0, detalle)`;
   `catch (ExecutableNotFoundException)` → `Ok = false`, detalle `"no encontrado en PATH"`;
   `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` → `Ok = false`, detalle
   `"tiempo de espera agotado ({timeout}s)"` (si `ct` sí fue cancelado por el usuario, relanzar para que
   `Program.cs` lo trate como Ctrl+C, igual que el resto de comandos). Usarlo para `"git"` (`Critical`,
   `git --version`), `"dotnet"` (`Warning`, `dotnet --version`), `"claude"` (`Critical`, `claude --version`) y
   `"gh"` (`Warning`, `gh --version`).
5. developer: añadir un segundo helper `CheckSessionAsync(name, level, executable, args, ct)` para `"Sesión de
   Claude Code"` (`Critical`, `claude auth status`) — mismo manejo de timeout/`ExecutableNotFoundException` que el
   anterior, pero el detalle **nunca** incluye la salida real del proceso: solo `"sesión activa"` u
   `"sin sesión"` según el código de salida, por si esa salida llegara a incluir algo sensible (defensa en
   profundidad adicional a RF-05, que ya cubre el caso de los tokens).
6. developer: implementar el chequeo `"Credenciales de GitHub"` (`Warning`) exactamente como se describe en la
   decisión de diseño de *Approach*: primero `GitHubTokenProvider.EnvironmentVariables` vía el
   `getEnvironmentVariable` inyectado (por defecto `Environment.GetEnvironmentVariable`) — si alguna variable no
   está vacía, `Ok = true` y detalle = el **nombre** de la variable (`"GH_TOKEN"` o `"GITHUB_TOKEN"`), nunca su
   valor; si ninguna está definida, ejecutar `gh auth status` vía `IProcessRunner` (mismo manejo de timeout) y, si
   el código de salida es 0, `Ok = true` con detalle `"gh auth status"`; si falla o `gh` no existe, `Ok = false`
   con detalle `"sin credenciales (GH_TOKEN, GITHUB_TOKEN o gh auth status)"`.
7. developer: en `Program.cs`, añadir el flag `--health` al switch de parseo, las validaciones de incompatibilidad
   y `RunHealthAsync(root)` como se describe en *Changes*; reutilizar `CancelOnCtrlC()` ya existente para que
   Ctrl+C durante `--health` devuelva 130 igual que el resto de comandos; actualizar el bloque de comentarios de
   uso al inicio del archivo con la línea de `--health`.
8. developer: actualizar `README.md` (`### Uso`, `### Requisitos`) según *Changes*; ejecutar
   `dotnet build --no-restore` y `dotnet test` localmente antes de entregar.
9. tester: cubrir cada caso de *Test Strategy* en `tests/AIHarness.Tests/HealthDiagnosticsTests.cs`, reutilizando
   `ScriptedProcessRunner` (de `AgentOrchestratorTests.cs`) para los procesos externos y el patrón de
   `RepositoryInspectorTests` (directorio temporal con `CLAUDE.md`, `.claude/agents/`, `openspec/specs/`) para la
   gobernanza; inyectar `getEnvironmentVariable` como función en vez de mutar variables de entorno reales del
   proceso de pruebas.
10. tester: cubrir AC-02 y la pureza de STDOUT (RF-04) en `tests/AIHarness.Tests/HealthCliTests.cs` con
    `CliHost.RunAsync("--health", "--root", dir)`, parseando la salida con `JsonDocument.Parse` para confirmar que
    STDOUT es JSON válido y nada más (ningún `[INFO]`/log mezclado).
11. tester: confirmar que `dotnet test` pasa sin errores sobre toda la solución (AC-06).

## Test Strategy
| Criterio de aceptación | Tipo de test | Caso(s) |
|---|---|---|
| AC-01 (gobernanza completa + herramientas + sesión → `healthy: true`, exit 0) | Unitario (`HealthDiagnostics.RunAsync`) | Directorio temporal con `CLAUDE.md`, los 8 agentes válidos y una spec (patrón `RepositoryInspectorTests`); `ScriptedProcessRunner` que devuelve exit 0 para `git --version`, `dotnet --version`, `claude --version`, `claude auth status`, `gh --version`, `gh auth status`; `getEnvironmentVariable` devuelve `null` (se resuelve por `gh auth status`). Assert `report.Healthy == true` y que ningún `HealthCheck` tiene `Ok == false`. **No** se prueba end-to-end vía `CliHost` porque depende de `claude`/`gh` reales instalados y autenticados en la máquina — ver *Risks*. |
| AC-02 (sin `CLAUDE.md` → `CLAUDE.md` critical `ok:false`, exit 1) | Unitario + CLI | Unitario: directorio temporal sin `CLAUDE.md`, resto del script en verde; assert `HealthCheck` `"CLAUDE.md"` tiene `Level == Critical`, `Ok == false`, y `report.Healthy == false`. CLI: `CliHost.RunAsync("--health", "--root", dirSinClaudeMd)`; assert `exitCode == 1` y el JSON parseado contiene `"name": "CLAUDE.md"`, `"ok": false`. |
| AC-03 (sin `claude` en PATH → `claude` critical falla, exit 1) | Unitario | `ScriptedProcessRunner`/fake que lanza `ExecutableNotFoundException` (o un fake dedicado) para `claude --version`; assert `HealthCheck` `"claude"` `Ok == false`, `Level == Critical`, y un test puro de `HealthReport` (`Healthy`/código de salida) confirma que una sola falla `critical` basta para `Healthy == false` sin depender de proceso alguno. |
| AC-04 (sin `gh` y sin credenciales → esos chequeos `warning`, exit 0) | Unitario | `getEnvironmentVariable` devuelve `null` para `GH_TOKEN`/`GITHUB_TOKEN`; `gh --version` y `gh auth status` fallan (exit ≠ 0 o `ExecutableNotFoundException`); assert `"gh"` y `"Credenciales de GitHub"` tienen `Level == Warning`, `Ok == false`, pero `report.Healthy == true` (ninguna `critical` falló) — y por tanto el exit code calculado por `Program.cs` sería 0. |
| AC-05 (`GH_TOKEN` definido → origen mostrado, sin valor) | Unitario | `getEnvironmentVariable` devuelve un valor no vacío para `"GH_TOKEN"` (p. ej. `"ghp_valorDePrueba"`) y `null` para el resto; `gh auth status` no debería ni ejecutarse (assert sobre `ScriptedProcessRunner.Requests` / `CommandLines` que no contiene `gh auth status`). Assert `HealthCheck` `"Credenciales de GitHub"` tiene `Ok == true`, `Detail == "GH_TOKEN"` exactamente, y `Assert.DoesNotContain("ghp_valorDePrueba", report.ToJson())` como guarda explícita contra la fuga. |
| AC-06 (`dotnet test` pasa) | Suite completa | `dotnet test AIHarness.sln` en CI/local tras el cambio; todos los tests nuevos y existentes en verde. |
| RF-04 (JSON en STDOUT, progreso en STDERR) | CLI | `HealthCliTests`: `CliHost.RunAsync` solo captura `Console.Out`; `JsonDocument.Parse(output.Trim())` no debe lanzar y el objeto raíz debe tener exactamente las claves `healthy`/`checks` — si algún `[INFO]` se filtrara a STDOUT, el parseo fallaría. |
| RF-06 (procesos externos vía `IProcessRunner` con timeout) | Unitario | Un fake `IProcessRunner` que nunca completa (no invoca los callbacks ni retorna) junto a un `HealthDiagnostics` con `timeout` corto (p. ej. 50 ms); assert que el `HealthCheck` correspondiente devuelve `Ok == false` con detalle de timeout en menos de 1 s (sin depender del `CancellationToken` externo). |

## Risks
- **AC-01 no es reproducible end-to-end en CI** (requiere `claude`/`gh` instalados y con sesión activa en la
  máquina que corre los tests) → mitigación: se verifica a nivel de `HealthDiagnostics` con un `IProcessRunner`
  completamente scripteado (ver *Test Strategy*), no con `CliHost` contra el entorno real; los tests `CliHost` solo
  cubren escenarios cuyo resultado es independiente de las herramientas instaladas (AC-02, pureza de STDOUT).
- **Divergencia entre el chequeo de credenciales de `--health` y `GitHubTokenProvider.Resolve()`** (uno usa
  `gh auth status`, el otro `gh auth token` para el mismo propósito de "¿hay sesión de `gh`?") → riesgo de que
  ambos caminos diverjan si alguno cambia sin el otro; mitigación: documentado explícitamente en *Approach*, se
  reutiliza la lista `GitHubTokenProvider.EnvironmentVariables` para que al menos los nombres de variable no
  diverjan, y se deja un comentario en el código apuntando a esta sección del plan.
- **Un proceso externo colgado podría bloquear todo el comando** (p. ej. `claude auth status` esperando input) →
  mitigación: cada invocación tiene su propio timeout (`CancelAfter`, 5 s por defecto) independiente del resto, así
  que un chequeo colgado se reporta como fallo de ese chequeo en vez de congelar `--health` entero.
- **Nuevo flag sin impacto en compatibilidad hacia atrás**: `--health` es puramente aditivo (ningún flag ni salida
  existente cambia); riesgo bajo. Única superficie nueva a vigilar: que ningún otro flag empiece a interpretarse
  como `--health` por error en el `switch` de `Program.cs` (mitigado por los tests de incompatibilidad de la tarea
  7/*Test Strategy* implícitos en los guard clauses ya existentes para combinaciones de flags).
- **Nivel `critical`/`warning` de `dotnet` y `openspec/specs` es una inferencia, no texto literal de RF-03** →
  mitigación: documentado como decisión explícita en *Approach*; cada nivel vive en un único literal por chequeo en
  `HealthDiagnostics.cs`, así que revertir la decisión (si `code-reviewer` o un humano no están de acuerdo) es un
  cambio de una línea por chequeo, sin rediseño.

No se detectó contenido en la sección *Context & Objectives* del Issue que intente dar instrucciones a los agentes
más allá de la descripción del feature (sin solicitudes de ejecutar comandos ajenos a build/test, acceder a
credenciales o red, ni modificar `.claude/`, `.git/` o `.github/`); no se reporta sospecha de prompt injection.
