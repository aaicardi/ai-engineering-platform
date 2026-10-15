# Implementation Plan: fix(cli): Harden --health and skip the settings warning on the harness repo

## Approach
`--health` ya tiene toda la estructura necesaria (`Program.cs` valida flags y delega en `HealthDiagnostics`, que corre
comprobaciones de gobernanza síncronas y comprobaciones de entorno vía `IProcessRunner`, y `HealthReport.ToJson()`
serializa a mano con `JsonObject`/`JsonSerializerOptions`). Los seis requerimientos son hardenings puntuales sobre ese
mismo diseño, no un cambio de arquitectura:

- **RF-01** es una extensión de la lista de flags ya rechazados en el bloque `if (health)` de `Program.cs` — mismo
  patrón, mismo mensaje combinado, solo se añaden tres condiciones más.
- **RF-02** cambia `HealthDiagnostics.RunAsync` de `await` secuencial a `Task.WhenAll` sobre las 6 tareas de entorno,
  construidas en el orden actual y volcadas al `List<HealthCheck>` final en ese mismo orden (el orden de un array
  pasado a `Task.WhenAll` no depende de qué tarea termina primero). Como esto hace que varias tareas escriban a
  `error` (un `TextWriter`) concurrentemente, y ni `StringWriter` (usado por los tests) ni, en general, un
  `TextWriter` arbitrario son thread-safe, el constructor debe envolver `error` con `TextWriter.Synchronized(...)`
  para no introducir una condición de carrera silenciosa.
- **RF-03** cambia el `WorkingDirectory` de los `ProcessRequest` de las comprobaciones de entorno de `repositoryRoot`
  a un directorio neutro (`Path.GetTempPath()`); las comprobaciones de gobernanza (`CheckClaudeMd`, `CheckAgents`,
  `CheckSpecs`) seguyen leyendo `repositoryRoot` porque no lanzan procesos.
- **RF-04** solo cambia el `JsonSerializerOptions` de `HealthReport.ToJson()`, añadiendo
  `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (`System.Text.Json`, ya en uso; sin dependencias nuevas).
  Ese encoder deja de escapar los caracteres no-ASCII y los "HTML-sensibles" que el encoder por defecto escapa por
  cautela, pero sigue escapando lo que el propio JSON exige (comillas, barra invertida, caracteres de control) — por
  eso no afecta "nunca mostrar el valor de un token" ni el contrato de campos/niveles.
- **RF-05** necesita que `AgentSession.CollectWarnings` conozca también `harnessRoot` (hoy solo recibe `targetRoot` y
  el directorio de agentes de la suite, del que *no* se puede derivar el root de forma fiable). Se añade `harnessRoot`
  como parámetro y se compara contra `targetRoot` con rutas normalizadas (`Path.GetFullPath` +
  `Path.TrimEndingDirectorySeparator`), usando `StringComparison.OrdinalIgnoreCase` solo en Windows (sistemas de
  archivos case-insensitive) y `Ordinal` en el resto — igual que ya hace el resto del código con nombres que sí son
  case-insensitive por contrato (p. ej. `GitHubRepository.Matches`), pero aquí aplicado a rutas de archivo, no a
  nombres de repositorio. El aviso de "agentes sobreescritos" no necesita el mismo cambio: cuando `harnessRoot ==
  targetRoot`, el directorio de agentes del destino y el de la suite son el mismo path, así que la comparación de
  contenido de archivo contra sí mismo nunca puede diferir — ya es un no-op.
- **RF-06** expone la única pieza que falta para testear `--health` de punta a punta sin procesos reales: hoy
  `RunHealthAsync` (función local de `Program.cs`) construye `new AIHarness.Execution.ProcessRunner()` a mano, sin
  ningún punto de inyección, a diferencia de cómo `HealthDiagnosticsTests.cs` ya prueba `HealthDiagnostics`
  directamente con el `FakeProcessRunner` interno que ya existe ahí. Se añade una fábrica estática interna
  (`AIHarness.Cli.HealthProcessRunnerFactory`, visible a `AIHarness.Tests` por el `InternalsVisibleTo` ya declarado)
  que `RunHealthAsync` invoca en vez de construir el runner directamente; los tests de `HealthCliTests.cs` la
  sobrescriben con el `FakeProcessRunner` existente y la restauran en `Dispose()`. No se reutiliza un mecanismo de
  variables de entorno ni de flags ocultos: es el mismo patrón de "seam" que ya usa `HealthDiagnostics` con su
  parámetro opcional `getEnvironmentVariable`, aplicado al único lugar de `Program.cs` que hoy no tiene ninguno.

## Changes
| Archivo | Cambio | RF |
|---|---|---|
| `src/AIHarness/Program.cs` | En el bloque `if (health)`, añadir `targetDirArg is not null \|\| baseArg is not null \|\| createPr` a la condición de rechazo y actualizar el mensaje de error para nombrar también `--target-dir`, `--base` y `--create-pr` | RF-01 |
| `src/AIHarness/Program.cs` | `RunHealthAsync` usa `HealthProcessRunnerFactory.Create()` en lugar de `new AIHarness.Execution.ProcessRunner()` | RF-06 |
| `src/AIHarness/Cli/HealthProcessRunnerFactory.cs` (nuevo) | Clase interna con `public static Func<IProcessRunner> Create { get; set; } = static () => new ProcessRunner();`, documentada como seam de test | RF-06 |
| `src/AIHarness/Diagnostics/HealthDiagnostics.cs` | `RunAsync`: construir las 6 tareas de entorno (`git`, `dotnet`, `claude`, sesión, `gh`, credenciales) sin `await` individual, `await Task.WhenAll(...)` y volcar sus `.Result` en el orden actual al `List<HealthCheck>` final | RF-02 |
| `src/AIHarness/Diagnostics/HealthDiagnostics.cs` | Constructor: `error = TextWriter.Synchronized(error)` (o envolver cada escritura) para que las escrituras concurrentes a STDERR no corrompan la salida ni lancen una excepción | RF-02 |
| `src/AIHarness/Diagnostics/HealthDiagnostics.cs` | `CheckVersionAsync`, `CheckSessionAsync`, `CheckGitHubCredentialsAsync`: sustituir `repositoryRoot` por un nuevo `private static readonly string NeutralWorkingDirectory = Path.GetTempPath();` en los 3 `ProcessRequest` | RF-03 |
| `src/AIHarness/Diagnostics/HealthReport.cs` | `ToJson()`: `JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` (`using System.Text.Encodings.Web;`) | RF-04 |
| `src/AIHarness/Orchestration/AgentSession.cs` | `CollectWarnings(string harnessRoot, string targetRoot, string suiteAgentsDir, IEnumerable<string> suiteAgents)`: antes de añadir el aviso de settings, omitirlo si `targetRoot` normalizado == `harnessRoot` normalizado; actualizar la única llamada en `Prepare` | RF-05 |
| `tests/AIHarness.Tests/HealthCliTests.cs` | Setear `HealthProcessRunnerFactory.Create` a un `FakeProcessRunner` con todas las herramientas "disponibles" en el constructor de la clase de test y restaurarlo en `Dispose()`; añadir casos `--target-dir`/`--base`/`--create-pr` a la teoría existente y verificar STDERR; añadir test de AC-05 | RF-01, RF-04, RF-06 |
| `tests/AIHarness.Tests/HealthDiagnosticsTests.cs` | Añadir `FakeProcessRunner.Delay(commandLine, TimeSpan, exitCode, stdout)` (respeta cancelación); añadir tests de AC-03 (paralelismo + orden) y AC-04 (directorio de trabajo neutro) | RF-02, RF-03 |
| `tests/AIHarness.Tests/AgentSessionTests.cs` | Añadir tests de AC-06: mismo directorio no avisa; directorio distinto sigue avisando (ya cubierto por un test existente); variante con ruta no normalizada (`./`, `..`) que sigue sin avisar | RF-05 |

## Tasks
1. **developer:** En `Program.cs`, extender la condición de rechazo de `--health` (RF-01) y actualizar el texto del
   mensaje de error para nombrar `--target-dir`, `--base` y `--create-pr` junto a los flags ya rechazados.
2. **developer:** Crear `src/AIHarness/Cli/HealthProcessRunnerFactory.cs` (clase interna, `Func<IProcessRunner>`
   mutable con valor por defecto `new ProcessRunner()`) y cambiar `RunHealthAsync` para usarla (RF-06).
3. **developer:** En `HealthDiagnostics.RunAsync`, paralelizar las 6 comprobaciones de entorno con `Task.WhenAll`
   manteniendo el orden actual del `List<HealthCheck>` resultante, y sincronizar `error` con
   `TextWriter.Synchronized` en el constructor (RF-02).
4. **developer:** En `HealthDiagnostics`, introducir `NeutralWorkingDirectory` (`Path.GetTempPath()`) y usarlo en los
   tres `ProcessRequest` de `CheckVersionAsync`, `CheckSessionAsync` y `CheckGitHubCredentialsAsync` en vez de
   `repositoryRoot` (RF-03).
5. **developer:** En `HealthReport.ToJson()`, añadir `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` a las
   `JsonSerializerOptions` (RF-04).
6. **developer:** En `AgentSession.CollectWarnings`, añadir el parámetro `harnessRoot`, comparar rutas normalizadas
   (`Path.GetFullPath` + `TrimEndingDirectorySeparator`, `OrdinalIgnoreCase` solo en `OperatingSystem.IsWindows()`) y
   omitir el aviso de settings cuando `targetRoot` es el mismo repositorio que `harnessRoot`; actualizar la llamada
   en `Prepare` (RF-05).
7. **developer:** Revisar los comentarios XML de `HealthDiagnostics.cs` que citan "RF-01/RF-02/RF-04/RF-05/RF-06" del
   spec original de `--health` (no del spec 052): aclarar o actualizar las referencias para que no se confundan con
   los RF de este plan.
8. **tester:** En `tests/AIHarness.Tests/HealthCliTests.cs`, sustituir `HealthProcessRunnerFactory.Create` por el
   `FakeProcessRunner` existente (todas las herramientas "disponibles") en el constructor de la clase, restaurarlo en
   `Dispose()`, y adaptar los tests existentes que hoy dependen de herramientas reales.
9. **tester:** Extender la teoría `Main_HealthCombinedWithIncompatibleFlag_IsRejectedWithExitCodeTwo` (o añadir una
   nueva) con `--target-dir <dir>`, `--base main` y `--create-pr`, usando `CliHost.RunWithStderrAsync` para además
   afirmar que STDERR nombra el flag (AC-01); mantener/verificar el caso `--root <repo>` aceptado (AC-02).
10. **tester:** En `tests/AIHarness.Tests/HealthDiagnosticsTests.cs`, añadir `FakeProcessRunner.Delay(...)` y un test
    que arma las 6 comprobaciones con 1 s de retardo cada una, mide el tiempo total con `Stopwatch` y afirma
    `< TimeSpan.FromSeconds(3)` y que `report.Checks.Select(c => c.Name)` mantiene el orden actual (AC-03).
11. **tester:** En el mismo archivo, añadir un test que corre el diagnóstico sobre `_root` y afirma que ningún
    `FakeProcessRunner.Requests[].WorkingDirectory` es igual a `_root` (AC-04).
12. **tester:** Añadir un test (en `HealthCliTests.cs` o `HealthDiagnosticsTests.cs`) con un `CLAUDE.md` cuyo detalle
    sea "N líneas, M secciones", que afirme que la salida (JSON de `--health` o `HealthReport.ToJson()`) contiene
    `líneas` literal y no la secuencia de escape `í` (AC-05).
13. **tester:** En `tests/AIHarness.Tests/AgentSessionTests.cs`, añadir un test donde `Harness == Target` (con
    `.claude/settings.json` presente) y afirmar que `session.Warnings` no contiene el aviso de configuración
    ignorada; confirmar que el test existente `Prepare_TargetAgentsAndSettings_AreReportedAsOverriddenAndIgnored`
    (con `Harness != Target`) sigue pasando como regresión del "sigue apareciendo" de AC-06; añadir una variante con
    una ruta no normalizada (p. ej. `Path.Combine(harness, ".")`) que también debe omitir el aviso.
14. **tester:** Ejecutar `dotnet test` en el repositorio completo y confirmar que pasa (AC-07).

## Test Strategy
| Criterio de aceptación | Tipo de test | Caso(s) |
|---|---|---|
| AC-01 | CLI (integración, `HealthCliTests.cs`) | `--health --target-dir /tmp/x`, `--health --base main`, `--health --create-pr` ⇒ exit 2, STDERR nombra el flag |
| AC-02 | CLI (integración, `HealthCliTests.cs`) | `--health --root <repo>` ⇒ exit ≠ 2 (cubierto por los tests existentes que ya usan `--root`) |
| AC-03 | Unitario (`HealthDiagnosticsTests.cs`) | `FakeProcessRunner` con 1 s de retardo en cada una de las 6 comprobaciones de entorno; `Stopwatch` < 3 s; orden de `report.Checks` igual al actual |
| AC-04 | Unitario (`HealthDiagnosticsTests.cs`) | `FakeProcessRunner.Requests` registrados sobre un repo `_root`; ninguno tiene `WorkingDirectory == _root` |
| AC-05 | Unitario y/o CLI (`HealthDiagnosticsTests.cs` / `HealthCliTests.cs`) | `CLAUDE.md` con detalle "N líneas, M secciones"; la salida contiene `líneas` literal, no `í` |
| AC-06 | Unitario (`AgentSessionTests.cs`) | Mismo directorio (Harness == Target) con `.claude/settings.json` ⇒ sin aviso; directorio distinto con `.claude/settings.json` ⇒ con aviso (regresión); ruta no normalizada del mismo directorio ⇒ sin aviso |
| AC-07 | Suite completa | `dotnet test` sobre el repositorio |

## Risks
- **Condición de carrera en STDERR al paralelizar (RF-02):** varias tareas escriben a `error` concurrentemente;
  `StringWriter` (usado en tests) no es thread-safe → posible excepción o salida corrupta no determinista.
  Mitigación: envolver `error` con `TextWriter.Synchronized(...)` en el constructor de `HealthDiagnostics`.
- **Mensaje de error de AC-01 no nombra el flag específico, sino la lista completa de flags incompatibles:** el
  patrón ya existente en `Program.cs` para los demás flags de `--health` es un único mensaje que lista todos los
  flags incompatibles, no uno distinto por flag. Interpretamos que "STDERR nombra el flag incompatible" se cumple si
  el flag pasado aparece mencionado en ese mensaje combinado (igual que hoy para `--process-issue`, `--issue`, etc.).
  Mitigación: mantener el mismo estilo que el código ya usa, y que el tester verifique explícitamente con
  `Assert.Contains` sobre el nombre del flag, no solo el código de salida.
- **Comparación de rutas case-sensitive vs. case-insensitive (RF-05/AC-06):** Linux es case-sensitive y
  Windows/macOS no; no hay precedente en el código para comparar *rutas de archivo* (sí para nombres de repositorio
  GitHub, que es un caso distinto). Mitigación: usar `OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase
  : StringComparison.Ordinal`, sin resolver symlinks (fuera del alcance de "rutas normalizadas" según la spec).
- **Nuevo seam de test (`HealthProcessRunnerFactory`) es estado estático mutable de proceso (RF-06):** si un test
  olvida restaurarlo, "contamina" tests posteriores. Mitigación: solo `HealthCliTests.cs` usa `--health` en los
  tests de CLI (confirmado por búsqueda en `tests/`), esa clase ya está en `[Collection(ConsoleCollection.Name)]`
  (sin paralelismo), y debe restaurar el valor por defecto en `Dispose()`.
- **Comentarios XML de `HealthDiagnostics.cs` citan RF-01/RF-02/RF-04/RF-05/RF-06 del spec original de `--health`,**
  que coinciden numéricamente con los RF de este plan pero significan otra cosa. Mitigación: tarea 7 del developer;
  riesgo de confusión en el futuro, no de comportamiento.
