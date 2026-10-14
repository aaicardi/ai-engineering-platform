# Implementation Plan: fix(specs): Number Issue specs by Issue number and avoid a duplicate Quality Gate criterion

## Approach
Today `SpecGenerator.Generate` (`src/AIHarness/Specs/SpecGenerator.cs:27-69`) computes the spec number by calling
`NextSpecNumber(directory)` — the highest existing `NNN-` prefix plus one — and only rejects a *re-generation of the
same Issue* by globbing `*-{slug}.md` and checking that the matched file's suffix equals `{slug}.md`
(`SpecGenerator.cs:36-41`). Two generators pointed at the same directory for two different Issues therefore race on
the same "next free number" and can both compute the same value (the exact failure mode `#47`/`#35` hit). `RF-01`
removes that computation entirely for Issue-sourced specs: the spec number *is* `issue.Number`. Since two different
GitHub Issues never share a number, two concurrent `Generate` calls for different Issues can never collide on the
file name anymore — `AC-02` falls out of this change for free, with no locking or retry logic needed.

`RF-02` only requires `SpecPrefixRegex` (`SpecGenerator.cs:375`, `^(\d{3})-.+\.md$`) to accept 3 **or more** digits
instead of exactly 3, because `{number:D3}` (used both for the title, `SpecGenerator.cs:111`, and the file name,
`SpecGenerator.cs:44`) already prints 4+-digit numbers in full — `D3` means "at least 3 digits", never "truncate to
3" — so Issue `#1234` already renders as `1234-...md` / `# OpenSpec 1234:` today; only the *recognition* regex needs
widening. The Issue's "equivalente en Orchestration" is `SpecMetadata.SpecPrefixRegex` (`SpecMetadata.cs:52`,
`^\d+-`), which already accepts one or more digits (a superset of "3 or more"); it is confirmed correct by
inspection and by `SpecMetadataTests` — no change needed there. `RepositoryInspector.InspectSpecs`
(`RepositoryInspector.cs:92-103`) lists every `*.md` file with no digit-prefix filter at all, so it already
"recognizes" any file regardless of prefix length; `AC-03`'s `RepositoryInspector` clause needs a regression test,
not a code change (same pattern as spec 016's AC-04).

`RF-03` replaces the current *slug-based* duplicate check with a *number-based* conflict check, because the file
name is now fully determined by `issue.Number` (the slug can no longer by itself tell us whether a number is "free").
The directory is scanned once for every `*.md` file whose name matches the (widened) `SpecPrefixRegex` and whose
captured number equals `issue.Number`. Two outcomes:
- **No match** → free to write `{number:D3}-{slug}.md`.
- **A match whose file name is byte-for-byte equal to the computed `{number:D3}-{slug}.md`** → same Issue, same
  title/slug as before: keep today's `SpecAlreadyExistsException` (`SpecGenerator.cs:409-413`), unchanged message and
  behavior (per RF-03's second sentence).
- **A match whose file name differs** (same number, different slug) → a *new* file is not written; `Generate` throws
  a new `SpecNumberConflictException` that names the existing file, mirroring `SpecAlreadyExistsException`'s shape
  (an `IOException` subclass with an `ExistingPath` property) but with a message describing a number collision, not
  a repeat generation.

  The exact-name check must run **before** picking "any match" as the conflict, not instead of scanning every match:
  the fixture already used by `SpecGeneratorTests` seeds `005-openspec-generator.md` **and**
  `005-openspec-generator-implementation-plan.md` side by side — both match prefix `005`. If a future Issue is
  numbered `5` and regenerated, a naive "first file with that number" check could non-deterministically pick the
  `-implementation-plan.md` file (depending on `Directory.GetFiles` enumeration order) and wrongly report a number
  conflict instead of the correct `SpecAlreadyExistsException` for the real duplicate. The design therefore first
  looks for an exact file-name match among *all* same-number files, and only falls back to "name the first
  same-number file" when no exact match exists.

`RF-05` is independent of the numbering change and lives entirely in `Render`. `ExtractAcceptanceCriteria` already
produces the exact bullet strings that become `AC-XX` lines (`SpecGenerator.cs:148`); the fix only needs to check,
in the branch that already has a non-empty `acceptanceCriteria` list (`SpecGenerator.cs:159-164`), whether any of
those bullets already mentions `testCommand` (case-insensitive substring match — the Issue's own wording wraps it in
backticks, e.g. `` `dotnet test` pasa.``, so a substring check on the unwrapped command text is enough) before
appending `qualityGateLine`. When there is no recognized Acceptance Criteria section at all
(`acceptanceCriteria.Count == 0`, `SpecGenerator.cs:152-157`), the Quality Gate line is always the generic fallback
and is always appended — untouched, matching `AC-06`'s framing ("Issue... no mencionan el comando de test... se
sigue agregando").

`RF-04` requires no code change: `NextSpecNumber` stays public, keeps scanning every `SpecPrefixRegex`-matching file
and returning `max + 1`, and is simply no longer called from `Generate`. Existing specs `001`-`017` are never
touched (no renaming logic exists or is added). One accepted side effect, not a defect: because `NextSpecNumber`
now also counts any Issue-numbered file with 4+ digits that may exist in the directory (widened regex), a
hand-written spec created after a very-high-numbered Issue spec would jump straight past it (e.g. next hand-written
number after an Issue `#1234` spec is `1235`, not `018`). RF-04 only asks that `NextSpecNumber` "sigue disponible"
for hand-written specs, not that its numbering stay independent of Issue numbers sharing the same directory; this is
called out as a risk below, not fixed, since no RF/AC asks for it.

No ADR is needed: this is a bug fix to an existing algorithm (the number source and a recognition regex) inside a
single existing class, with no new pattern, dependency, storage, or public API/contract being introduced — the
Issue's own "Consideraciones Técnicas" frames it as `type:bug`, and the fix stays entirely inside
`SpecGenerator.cs` plus documentation, as the Issue's own restriction requires.

## Changes
| Archivo | Cambio | RF |
|---|---|---|
| `src/AIHarness/Specs/SpecGenerator.cs` | `Generate`: el número deja de calcularse con `NextSpecNumber(directory)` y pasa a ser `issue.Number`. Se calcula `fileName`/`filePath` con ese número antes del chequeo de duplicados. | RF-01 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `Generate`: el chequeo de duplicado por slug (líneas 36-41 actuales, glob `*-{slug}.md`) se reemplaza por un chequeo por número: se listan los `*.md` cuyo nombre matchea `SpecPrefixRegex` y cuyo número capturado == `issue.Number`; si alguno coincide exactamente con el `fileName` calculado, se lanza `SpecAlreadyExistsException` (igual que hoy); si no coincide ninguno exactamente pero existe algún otro archivo con ese número, se lanza la nueva `SpecNumberConflictException` nombrando ese archivo. | RF-01, RF-03 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Nueva excepción `SpecNumberConflictException(string existingPath, int number)` (subclase de `IOException`, al estilo de `SpecAlreadyExistsException`), con `ExistingPath`/`Number` como propiedades y un mensaje que nombra el archivo existente y aclara que no se sobrescribe ni renumera. | RF-03 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `SpecPrefixRegex`: `^(\d{3})-.+\.md$` → `^(\d{3,})-.+\.md$` (3 o más dígitos). | RF-02 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `Render`: en la rama `acceptanceCriteria.Count > 0` (línea 159-164 actual), antes de añadir `qualityGateLine` se comprueba si `testCommand` no es nulo/vacío y alguna línea ya extraída lo menciona (`Contains`, `StringComparison.OrdinalIgnoreCase`); si es así, no se añade esa línea. La rama `Count == 0` no cambia. | RF-05 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Comentarios XML que describen la convención como "`NNN-<slug>.md`" (resumen de la clase, línea 12; doc de `NextSpecNumber`, línea 71) se actualizan para distinguir specs de Issue (número = Issue) de specs manuales (número = `NextSpecNumber`). Cosmético, sin efecto en tests. | RF-01, RF-04 |
| `src/AIHarness/Program.cs` | Comentario de ayuda en línea 24 (`-> además genera openspec/specs/NNN-<slug>.md`) se actualiza para aclarar que `NNN` es el número del Issue. Cosmético. | RF-01 |
| `README.md` | Se documenta la convención de nombres junto a la descripción actual de la etapa **Spec** (línea ~47): el número es el del Issue (3+ dígitos, sin recalcular "el siguiente libre"); las specs escritas a mano siguen una numeración secuencial propia vía `NextSpecNumber`; un número de Issue ya usado por otra spec falla sin sobrescribir ni renumerar. | RF-01, RF-02, RF-03, RF-04 |
| `tests/AIHarness.Tests/SpecGeneratorTests.cs` | Se actualizan `Generate_WritesNextNumberedSpecInsideSpecsDirectory` y `Generate_SameIssueTwice_ThrowsAndKeepsOriginalFile` (asumían numeración secuencial `6`) para reflejar que el número generado es el del Issue (`42` → `042-...md`). Se renombran/ajustan sus asserts en consecuencia. | RF-01 |
| `tests/AIHarness.Tests/SpecGeneratorTests.cs` | Tests nuevos para AC-01 a AC-07 y el caso de colisión con un archivo `-implementation-plan.md` del mismo número (ver *Tasks*/*Test Strategy*). | RF-01, RF-02, RF-03, RF-05 |

No se modifica `src/AIHarness/Orchestration/SpecMetadata.cs` (su `SpecPrefixRegex` ya acepta `\d+-`) ni
`src/AIHarness/Inspection/RepositoryInspector.cs` (no filtra por prefijo numérico); ambos quedan confirmados por
inspección y por un test de regresión nuevo, no por un cambio de producción.

## Tasks
1. developer: en `SpecGenerator.cs`, cambiar `Generate` para que `number = issue.Number` (eliminar la llamada a
   `NextSpecNumber(directory)` en este método; el método en sí no se toca) y mover el cálculo de `slug`/`fileName`/
   `filePath` antes del chequeo de duplicados.
2. developer: reemplazar el bloque de detección de duplicado (glob `*-{slug}.md` + comparación de sufijo,
   `SpecGenerator.cs:36-41` actuales) por el chequeo basado en número descrito en *Approach*: listar `*.md`,
   filtrar por `SpecPrefixRegex().Match(f)` exitoso con `int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) ==
   number`, buscar primero una coincidencia exacta de nombre (→ `SpecAlreadyExistsException`) y, si no la hay, usar
   el primer archivo restante con ese número (→ `SpecNumberConflictException`).
3. developer: añadir `SpecNumberConflictException` junto a `SpecAlreadyExistsException` (final del archivo), con la
   forma `public sealed class SpecNumberConflictException(string existingPath, int number) : IOException($"Ya
   existe una especificación con el número {number:D3}: {existingPath}. No se sobrescribe ni se renumera; elimínela,
   renómbrela, o confirme que el número de Issue es el correcto antes de regenerar.") { public string ExistingPath {
   get; } = existingPath; public int Number { get; } = number; }`.
4. developer: cambiar `SpecPrefixRegex` (`SpecGenerator.cs:375`) de `@"^(\d{3})-.+\.md$"` a `@"^(\d{3,})-.+\.md$"`.
   No tocar `SpecMetadata.SpecPrefixRegex` (`^\d+-`, ya correcto).
5. developer: en `Render` (`SpecGenerator.cs:159-164`), antes de `sb.AppendLine(qualityGateLine)` en la rama con
   criterios extraídos, añadir el chequeo `!string.IsNullOrWhiteSpace(testCommand) &&
   acceptanceCriteria.Any(c => c.Contains(testCommand, StringComparison.OrdinalIgnoreCase))`; omitir la línea cuando
   sea verdadero. No tocar la rama `acceptanceCriteria.Count == 0`.
6. developer: actualizar los comentarios XML señalados en *Changes* (clase, `NextSpecNumber`) y el comentario de
   `Program.cs:24`; actualizar la sección **Spec** del README (línea ~47) con la convención descrita en *Changes*.
7. developer: correr `dotnet build --no-restore` y `dotnet test` (línea base verificada antes de este plan: 292
   tests en verde) tras los cambios anteriores, antes de pasarlo a tester.
8. tester: actualizar `Generate_WritesNextNumberedSpecInsideSpecsDirectory` (nombre y asserts: `Issue()` usa número
   `42`, así que ahora debe esperar `spec.Number == 42`, `FileName == "042-anadir-exportacion-csv.md"`) y
   `Generate_SameIssueTwice_ThrowsAndKeepsOriginalFile` (ya no depende de un número secuencial; sigue esperando
   `SpecAlreadyExistsException`, solo el nombre de archivo esperado cambia a `042-...md`).
9. tester: cubrir AC-01 a AC-07 con `[Fact]`/`[Theory]` nuevos en `SpecGeneratorTests.cs`, siguiendo el estilo
   `Generate_<Escenario>_<Resultado>` / `Render_<Escenario>_<Resultado>` ya usado (ver *Test Strategy*).
10. tester: añadir un test dedicado a la colisión de orden de enumeración descrita en *Approach*: directorio con
    `005-openspec-generator.md` y `005-openspec-generator-implementation-plan.md` (como ya seedea el constructor de
    `SpecGeneratorTests`), generar un Issue número `5` dos veces — la primera debe lanzar `SpecNumberConflictException`
    (ninguno de los dos archivos existentes es el que ese Issue generaría) nombrando uno de los dos archivos
    existentes; ningún archivo nuevo debe crearse.
11. tester: añadir un test de regresión para `RepositoryInspector.InspectSpecs` con un archivo `1234-algo.md` en el
    directorio de specs, confirmando que aparece en `SpecsAudit.Specs` sin cambio de producción (cierra la cláusula
    de `RepositoryInspector` en AC-03 sin tocar `RepositoryInspector.cs`).
12. tester: confirmar que `SpecMetadataTests.FromIssue_MatchesTheMetadataOfTheGeneratedSpec` (usa Issue número 31)
    sigue pasando sin cambios — es la prueba end-to-end de que `SpecMetadata.FromIssue`/`Parse` y el `Generate`
    rediseñado producen el mismo número/slug para el mismo Issue.
13. documentation (si no lo cubre developer en la tarea 6): revisar que el README quede consistente con la tabla de
    *Especificaciones* existente (001-017 no se renumeran) y con la nueva convención.

## Test Strategy
| Criterio de aceptación | Tipo de test | Caso(s) |
|---|---|---|
| **AC-01:** Issue #52, título "fix(specs): Algo", directorio con `001-*`...`017-*`, al generar se crea `052-fix-specs-algo.md` con encabezado `# OpenSpec 052:`. | Unitario (`Generate`) | `GitHubIssue(52, "fix(specs): Algo", ...)` sobre un directorio temporal con archivos `001-*.md`..`017-*.md` de relleno (o el fixture ya existente, que no usa esos números). Assert: `spec.Number == 52`, `spec.FileName == "052-fix-specs-algo.md"`, `spec.Content` empieza con `"# OpenSpec 052: fix(specs): Algo"`. |
| **AC-02:** Dos generadores sobre el mismo directorio, Issues #52 y #53, en cualquier orden, crean `052-*`/`053-*` sin colisión. | Unitario (`Generate`), dos variantes de orden | Dos instancias de `SpecGenerator` sobre el mismo directorio temporal; `Generate(Issue(52))` y `Generate(Issue(53))` en el orden A→B y B→A (dos `[Fact]` o un `[Theory]` con el orden como parámetro). Assert: ambas llamadas devuelven sin excepción; existen `052-*.md` y `053-*.md`; no hay archivo `006-*`/`NextSpecNumber`-based. |
| **AC-03:** Issue #1234 genera un archivo que empieza con `1234-`; `SpecMetadata`/`RepositoryInspector` lo reconocen. | Unitario (`Generate`, `SpecMetadata.Parse`, `RepositoryInspector.InspectSpecs`) | `Generate(Issue(1234, ...))`: assert `spec.FileName.StartsWith("1234-")`. `SpecMetadata.Parse(spec.FileName, spec.Content)`: assert `IssueNumber == 1234`. Regresión: `RepositoryInspector` sobre un directorio con `1234-algo.md` lo lista en `SpecsAudit.Specs` (sin cambio de producción, solo bloquea la regresión). |
| **AC-04:** Directorio con `052-otro-slug.md`; generar Issue #52 con otro título falla nombrando `052-otro-slug.md`; el archivo queda intacto. | Unitario (`Generate`) | Crear `052-otro-slug.md` con contenido de control; `Generate(Issue(52, "Otro título distinto"))`. Assert: `Assert.Throws<SpecNumberConflictException>`; `ex.ExistingPath` termina en `052-otro-slug.md`; el contenido de `052-otro-slug.md` no cambió; no se creó ningún archivo `052-<nuevo-slug>.md`. |
| **AC-04 (orden de enumeración):** colisión de número con un archivo `-implementation-plan.md` del mismo prefijo. | Unitario (`Generate`), caso límite | Directorio con `005-openspec-generator.md` y `005-openspec-generator-implementation-plan.md` (fixture ya existente); `Generate(Issue(5, "Algo distinto"))`. Assert: `SpecNumberConflictException` (no `SpecAlreadyExistsException`, porque ningún archivo existente es exactamente `005-algo-distinto.md`); ninguno de los dos archivos existentes cambia; no se crea un tercer archivo. |
| **AC-05:** Acceptance Criteria del Issue ya incluye `` - [ ] `dotnet test` pasa. `` y `testCommand = "dotnet test"`: la sección generada tiene una sola línea que menciona `dotnet test`. | Unitario (`Render`) | `Render(Issue(body: "## Criterios de Aceptación\n- [ ] `dotnet test` pasa."), 18, "dotnet test")`. Assert: `content.Split("dotnet test").Length - 1 == 1` dentro de la sección `## Acceptance Criteria`; esa única línea es la `AC-01` extraída del Issue, no una línea de Quality Gate añadida. |
| **AC-06:** Acceptance Criteria del Issue no menciona el comando de test, `testCommand` configurado: se sigue agregando la línea de Quality Gate. | Unitario (`Render`), regresión | Ya cubierto por `Render_AcceptanceCriteriaSection_WithTestCommand_PutsQualityGateLast` (RF-06 del plan anterior); se añade un assert explícito de que la línea `` - [ ] `npm test` pasa sin errores. `` es la única mención del comando, confirmando que RF-05 no rompe este camino. |
| **AC-07 / `dotnet test` pasa.** | Suite completa | `dotnet test` tras los cambios: los 292 tests existentes (línea base) más los nuevos de esta tabla, todos en verde. |

## Risks
- **`NextSpecNumber` puede saltar numeraciones altas si convive con specs de Issue de 4+ dígitos** (ver *Approach*,
  RF-04) → no es un defecto nuevo exigido a corregir por ningún RF/AC; se documenta en el README (tarea 6) para que
  quien escriba una spec a mano sepa que el próximo número libre puede no ser consecutivo a `017` si ya existen
  specs de Issues muy numerados en el mismo directorio.
- **Cambiar el chequeo de duplicado de "por slug" a "por número" es un cambio de comportamiento, no solo aditivo**
  → si el orden de enumeración de `Directory.GetFiles` devuelve primero un archivo "hermano" (p. ej. un
  `-implementation-plan.md` del mismo número) en vez del archivo exacto, se podría reportar `SpecNumberConflictException`
  en vez de `SpecAlreadyExistsException` para una regeneración legítima del mismo Issue. Mitigación: el diseño busca
  primero la coincidencia exacta de nombre entre *todos* los archivos con ese número antes de elegir cualquier otro
  como "el conflicto" (ver *Approach* y tarea 10, que lo ejercita explícitamente).
- **El chequeo de `RF-05` usa `Contains` de texto plano, no un parser de Markdown** → un Issue que mencione el
  comando de test con una variación textual distinta (p. ej. con flags adicionales, `dotnet test --no-build`) no
  sería detectado como "ya cubierto" y el generador añadiría la línea de Quality Gate igualmente, duplicando el
  criterio en ese caso específico. Mitigación: fuera de alcance — RF-05 y su único AC (`AC-05`) solo exigen detectar
  una mención literal del comando configurado; una coincidencia más laxa (regex, tokenización) no está pedida y
  añadiría superficie de falsos positivos (p. ej. un comando que aparece dentro de una frase no relacionada con
  Quality Gate).
- **Compatibilidad hacia atrás de specs 001-017** → el cambio no toca `NextSpecNumber` ni renombra nada; las specs
  existentes seguidas de Issues ya cerrados conservan su nombre. Mitigación: ningún cambio de producción recorre ni
  reescribe archivos existentes; los tests de regresión (tarea 11, 12) lo confirman sin depender de contenido nuevo.
