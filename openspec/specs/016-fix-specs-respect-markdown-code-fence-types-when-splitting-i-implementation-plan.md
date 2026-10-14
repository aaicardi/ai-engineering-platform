# Implementation Plan: fix(specs): Respect Markdown code fence types when splitting Issue sections

## Approach
`SplitIntoSections` (`src/AIHarness/Specs/SpecGenerator.cs:226-257`) walks the Issue body line by line and toggles a
single `inCodeFence` boolean whenever a line matches `CodeFenceRegex` (```` ^\s*(?:```|~~~) ````, `SpecGenerator.cs:356-357`).
That toggle does not record *which* fence character opened the block nor how long the opening run was, so it closes
on the first line that merely looks like a fence, regardless of type or length — exactly the two failure modes
CommonMark forbids and that RF-01/AC-01/AC-02 describe:
- A ```` ``` ```` block containing a bare `~~~` line: the `~~~` flips `inCodeFence` back to `false` even though its
  character (`~`) does not match the opener's (`` ` ``), so the next real line is read with headings/bullets active
  again (AC-01).
- A ```` ```` ```` (4-backtick) block containing a bare ```` ``` ```` (3-backtick) line: the shorter run still matches
  `CodeFenceRegex` and flips the toggle, closing the block early even though CommonMark requires a closing run of
  length ≥ the opener's (AC-02).

Beyond headings, `ExtractBullets` (`SpecGenerator.cs:278-283`) has the same blind spot one level down: it scans every
line of a section's `content` for `RequirementLineRegex` with **no fence awareness at all** — `SplitIntoSections`
only ever used its `inCodeFence` flag to skip the heading check (`SpecGenerator.cs:242`), never to mark lines as "not
a bullet either". So a bulleted line (e.g. `- [ ] Criterio 1`) that happens to live inside a fenced code sample would
still be read as a requirement/criterion bullet today. RF-02 explicitly requires fence-awareness for *both*
headings and bullets, so fixing only the heading toggle would leave RF-02 half-satisfied for bullets.

The design fixes this with a single fence-aware preprocessing pass, mirrored on the existing "strip HTML comments
before RF/AC extraction, but never touch the quoted blockquote" pattern already in `Render` (`SpecGenerator.cs:134-135`:
`var parsedBody = HtmlCommentRegex().Replace(body, "")`). A new static method, `MaskFencedCodeBlocks(string body)`,
walks the body once, tracks the currently-open fence's character and run length (CommonMark: a fence closes only on
a line whose run is the *same character* and *length ≥* the opener's), and blanks out (replaces with an empty line,
preserving the line count so nothing downstream shifts) every line that is genuine content *inside* an open fence —
never the delimiter lines themselves, which don't look like headings or bullets anyway. Both `SplitIntoSections`
(heading detection) and `ExtractBullets` (bullet detection) then run, unmodified in their matching logic, over text
that already has no fenced content left to misclassify — so the existing ad hoc `inCodeFence` toggle in
`SplitIntoSections` becomes dead weight and is removed, and `ExtractBullets` needs no change at all. This keeps the
fence logic in exactly one place instead of duplicating a second (correct) state machine next to the heading split.

`Render` gains one line: `parsedBody` becomes `MaskFencedCodeBlocks(HtmlCommentRegex().Replace(body, ""))` instead of
just the comment-stripped body (`SpecGenerator.cs:135`). The quoted blockquote in `Context & Objectives`
(`SpecGenerator.cs:122-132`) keeps using the raw, unmodified `body`, so the Issue is still shown verbatim there —
masking only ever touches the copy used for RF/AC extraction, consistent with how comment-stripping already works.

For RF-03/AC-04, tracing `ExtractAcceptanceCriteria` (`SpecGenerator.cs:202-219`) against the exact Issue scenario
shows **no production change is needed**: a section containing only `- [ ] Criterio 1` and `- [ ] Criterio 2`
produces the bullets `"Criterio 1"` and `"Criterio 2"`; `GroupScenarioBullets` emits them as two independent
one-element groups (neither starts with a Dado/Given/Cuando/.../Y/And keyword); `IsMeaningfulContent`
(`SpecGenerator.cs:330-336`) already discards both via `CriterioPlaceholderRegex` (`^criterio\s+\d+$`,
case-insensitive, `SpecGenerator.cs:368-369`); the resulting `criteria` list is empty; `Render`'s
`acceptanceCriteria.Count == 0` branch (`SpecGenerator.cs:151-157`) already emits the generic fallback. This matches
the Issue's own framing ("...que el caso de una sección de criterios vacía **esté cubierto por tests**..." —
Context & Objectives, 📝 Requisitos, 3rd bullet): the behavior already exists from PR #45; what is missing is a
dedicated regression test locking it in, which is why RF-03 has zero rows in *Changes* for production code but one
row for tests, and why it is still mapped to AC-04 in *Test Strategy*.

## Changes
| Archivo | Cambio | RF |
|---|---|---|
| `src/AIHarness/Specs/SpecGenerator.cs` | Nuevo método estático `MaskFencedCodeBlocks(string body)`: recorre `body` línea a línea, rastrea el carácter y la longitud de la valla abierta (si alguna), y sustituye por una línea vacía cada línea de contenido literal dentro de una valla abierta (nunca las líneas delimitadoras de apertura/cierre). Una valla se cierra solo con una línea del mismo carácter y longitud ≥ la de apertura; cualquier otra coincidencia parcial queda como contenido enmascarado. | RF-01, RF-02 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Nuevo regex `FenceDelimiterRegex` (`^\s*(`{3,}\|~{3,})`, con grupo de captura) que sustituye a `CodeFenceRegex`; usado únicamente por `MaskFencedCodeBlocks`. | RF-01 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `Render`: `parsedBody` pasa de `HtmlCommentRegex().Replace(body, "")` a `MaskFencedCodeBlocks(HtmlCommentRegex().Replace(body, ""))` (línea 135); el `body` citado en el blockquote de `Context & Objectives` no cambia. | RF-01, RF-02 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `SplitIntoSections`: se elimina el campo `inCodeFence` y la rama que usaba `CodeFenceRegex`/`IsMatch` (líneas 231-244 actuales); vuelve a ser un split simple por `HeadingRegex`, porque el texto que recibe ya llega sin contenido de vallas gracias al enmascarado previo. Se actualiza el comentario XML del método para reflejar que el enmascarado ocurre upstream. | RF-01, RF-02 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `ExtractBullets`: sin cambios de lógica; queda documentado en comentario que su fence-safety ahora depende del enmascarado hecho antes de `SplitIntoSections`/`ExtractBullets`, no de un chequeo propio. | RF-02 |
| `tests/AIHarness.Tests/SpecGeneratorTests.cs` | Tests nuevos para AC-01, AC-02, AC-03, AC-04 (regresión) y un caso adicional de RF-02 para viñetas dentro de una valla (ver *Tasks*/*Test Strategy*). | RF-01, RF-02, RF-03 |

No se modifican `IsMeaningfulContent`, `CriterioPlaceholderRegex`, `GroupScenarioBullets`, `ExtractRequirements` ni
`ExtractAcceptanceCriteria`: ya implementan el fallback que RF-03 pide.

## Tasks
1. developer: añadir `FenceDelimiterRegex` (`[GeneratedRegex(@"^\s*(`{3,}|~{3,})")]`) a la zona de regex existentes
   (junto a las otras `[GeneratedRegex]` de `SpecGenerator.cs:341-372`), reemplazando `CodeFenceRegex`.
2. developer: implementar `MaskFencedCodeBlocks(string body)` en `SpecGenerator.cs` como método privado estático,
   junto a `SplitIntoSections`/`NormalizeHeading`. Firma: `private static string MaskFencedCodeBlocks(string body)`.
   Lógica (pseudocódigo):
   ```csharp
   var lines = body.Split('\n');
   char? fenceChar = null;
   var fenceLength = 0;

   for (var i = 0; i < lines.Length; i++)
   {
       var fence = FenceDelimiterRegex().Match(lines[i]);

       if (fenceChar is null)
       {
           if (fence.Success)
           {
               fenceChar = fence.Groups[1].Value[0];
               fenceLength = fence.Groups[1].Value.Length;
           }
           continue; // not inside a fence: line stays untouched
       }

       if (fence.Success && fence.Groups[1].Value[0] == fenceChar && fence.Groups[1].Value.Length >= fenceLength)
       {
           fenceChar = null;
           fenceLength = 0;
           continue; // closing delimiter: same type, length >= opener's
       }

       lines[i] = string.Empty; // literal code content: never a heading nor a bullet downstream
   }

   return string.Join('\n', lines);
   ```
   Añadir un comentario XML/`//` citando la regla de CommonMark igual que hace la spec (RF-01) para que quede
   explícito por qué se compara carácter Y longitud, no solo si "parece" una valla.
3. developer: en `Render`, cambiar la línea `var parsedBody = HtmlCommentRegex().Replace(body, "");`
   (`SpecGenerator.cs:135`) a `var parsedBody = MaskFencedCodeBlocks(HtmlCommentRegex().Replace(body, ""));`. No
   tocar el bloque que construye el blockquote (líneas 122-132), que sigue usando `body` sin enmascarar.
4. developer: simplificar `SplitIntoSections` quitando `inCodeFence` y la rama `if (CodeFenceRegex().IsMatch(line))`
   (`SpecGenerator.cs:231-244`); el método vuelve a ser: por cada línea, si matchea `HeadingRegex` abre una nueva
   sección, si no, se añade a `currentLines`. Actualizar el comentario XML del método (líneas 221-225) para decir
   que el enmascarado de vallas ocurre antes de llamar a esta función, no dentro de ella.
5. developer: correr `dotnet build --no-restore` y `dotnet test` localmente; confirmar que los 257 tests existentes
   (línea base verificada antes de este plan) siguen en verde sin tocar sus asserts.
6. tester: cubrir AC-01..AC-04 y el caso adicional de bullets-en-valla con `[Fact]` nuevos en
   `tests/AIHarness.Tests/SpecGeneratorTests.cs`, siguiendo el estilo `Render_<Escenario>_<Resultado>` ya usado en
   el archivo (ver *Test Strategy* para los casos concretos). No introducir dependencias nuevas (ni Moq).
7. tester: confirmar con un test dedicado que el camino "valla con tipo/longitud coincidentes" (AC-03) no es una
   regresión silenciosa del comportamiento actual (ya pasaba con el toggle simple) y sigue pasando tras el cambio.

## Test Strategy
| Criterio de aceptación | Tipo de test | Caso(s) |
|---|---|---|
| **AC-01:** Dado un Issue con un bloque abierto con tres acentos graves que contiene una línea `~~~` y después una línea `## Criterios de Aceptación`, cuando se genera la spec, entonces esa línea se trata como contenido del bloque y no como un encabezado. | Unitario (`Render`) | Body: `"## Requisitos\n- Primero\n\`\`\`\n~~~\n## Criterios de Aceptación\n\`\`\`\n- Segundo"`. Assert: `RF-01` = "Primero" y `RF-02` = "Segundo" (ambos bajo "Requisitos"); no aparece ningún `AC-01`/`AC-XX` derivado de la falsa cabecera; `## Acceptance Criteria` queda con los 3 criterios genéricos (no hay sección AC real). |
| **AC-02:** Dado un Issue con un bloque abierto con cuatro acentos graves que contiene una línea de tres acentos graves, cuando se genera la spec, entonces el bloque no se cierra en esa línea. | Unitario (`Render`) | Body: `"## Requisitos\n\`\`\`\`\n\`\`\`\n## No es un encabezado\n\`\`\`\`\n- Requisito real"`. Assert: `RF-01` = "Requisito real" (única viñeta, bajo "Requisitos"); no existe ninguna sección ni RF/AC derivados de "No es un encabezado" (la línea de 3 backticks no cerró el bloque, así que esa línea sigue enmascarada como contenido). |
| **AC-03:** Dado un Issue con un bloque abierto con `~~~` y cerrado con `~~~`, cuando se genera la spec, entonces las secciones posteriores al bloque se clasifican correctamente. | Unitario (`Render`), regresión | Body: `"## Requisitos\n- Uno\n~~~\nalgo de codigo\n~~~\n\n## Criterios de Aceptación\n- Dos"`. Assert: `RF-01` = "Uno"; `AC-01` = "Dos" (la cabecera real después del cierre correcto sí se reconoce como sección AC). |
| **AC-04:** Dado un Issue cuya sección "Criterios de Aceptación" solo contiene `- [ ] Criterio 1` y `- [ ] Criterio 2`, cuando se genera la spec, entonces sus criterios de aceptación son los genéricos y no aparece ningún criterio AC-XX. | Unitario (`Render`), regresión (sin cambio de producción) | Body: `"## Criterios de Aceptación\n- [ ] Criterio 1\n- [ ] Criterio 2"`. Assert: el contenido no contiene `"AC-01"` ni ninguna variante `AC-0`; `## Acceptance Criteria` es exactamente el bloque genérico de 3 líneas (quality gate + "Cada requerimiento funcional..." + "El pipeline de CI..."), igual que `Render_BodyWithoutHeadings_KeepsBulletsAsRequirementsAndGenericCriteria`. |
| **AC-05:** `dotnet test` pasa. | Suite completa | `dotnet test` tras los cambios: los 257 tests existentes (línea base) más los nuevos de esta tabla, todos en verde. |
| Adicional (RF-02, no numerado como AC pero exigido literalmente por el RF): una viñeta dentro de una valla de código no se lee como requisito/criterio. | Unitario (`Render`) | Body: `"## Requisitos\n- Primero\n\`\`\`\n- [ ] Esto parece una viñeta pero es código\n\`\`\`\n- Segundo"`. Assert: solo "Primero" y "Segundo" aparecen como `RF-XX`; el texto "Esto parece una viñeta pero es código" no aparece en `## Functional Requirements` ni en `## Acceptance Criteria` (puede seguir citado en el blockquote, que no cambia). |

## Risks
- **`FenceDelimiterRegex` hace match por prefijo, no de línea completa** (p. ej. una línea de cierre con texto
  colgando después de las vallas, `` ``` extra `` , se trataría igual que un cierre válido, aunque CommonMark exige
  que la línea de cierre contenga *solo* el carácter de valla más espacios) → esta es la misma simplificación que ya
  tenía `CodeFenceRegex` hoy (prefijo, no ancla `$`), así que no es una regresión; ninguno de los AC-01..AC-05 ejercita
  ese caso. Mitigación: documentarlo aquí explícitamente para que developer/tester no lo tomen como un bug nuevo
  introducido por este cambio, y dejarlo fuera de alcance salvo que el `code-reviewer` lo marque como hallazgo.
- **Indentación de hasta 3 espacios antes de una valla** (`\s*` en el regex acepta cualquier cantidad de espacios,
  no solo 0-3 como exige CommonMark estrictamente para que siga siendo una valla y no un bloque indentado) → mismo
  comportamiento que el regex original; los Issues de GitHub rara vez usan indentación de 4+ espacios antes de una
  valla de código, y ningún AC lo cubre. Mitigación: fuera de alcance, mismo nivel de fidelidad que antes del cambio.
- **Eliminar `inCodeFence`/`CodeFenceRegex` de `SplitIntoSections` es un cambio estructural, no solo aditivo** → si
  el enmascarado (`MaskFencedCodeBlocks`) tuviera un defecto y dejara pasar contenido de una valla sin blanquear,
  `SplitIntoSections` ya no tendría ninguna red de seguridad propia. Mitigación: el test de AC-01/AC-02/AC-03 (y el
  test adicional de bullets-en-valla) ejercitan exactamente los casos límite de CommonMark que `MaskFencedCodeBlocks`
  debe cubrir; si alguno falla, es evidencia directa de un defecto en el enmascarado, no un efecto colateral oculto
  en otro método.
- **Orden de aplicación: enmascarado después de quitar comentarios HTML** (`HtmlCommentRegex` no es fence-aware, así
  que un comentario HTML dentro de una valla de código podría eliminarse igualmente hoy, antes y después de este
  cambio) → defecto preexistente, no introducido ni agravado por este plan; RF-01/RF-02/RF-03 no mencionan
  comentarios HTML dentro de vallas, así que queda fuera de alcance explícitamente.
