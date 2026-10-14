# Implementation Plan: feat(specs): Map Issue acceptance criteria into the generated spec

## Refined Requirements

El validador OpenSpec marcó la spec como `NEEDS_REFINEMENT`: `RF-08` a `RF-13` son en realidad los escenarios
Given/When/Then de la sección "📋 Criterios de Aceptación" del Issue (mal clasificados como RF por el propio bug que
esta spec corrige), y `RF-14`/`RF-15` son la sección "📐 Consideraciones Técnicas" (stack y restricciones), no
comportamiento a implementar. Lo siguiente sustituye a `RF-08`..`RF-15` como criterios vigentes para el resto del
pipeline; `RF-01` a `RF-07` de la spec ya son requisitos funcionales válidos y se listan tal cual en *Changes*.

- RF-R1 a RF-R7: se corresponden 1 a 1 con `RF-01`..`RF-07` de la spec (lectura por secciones, AC como checklist
  `AC-XX`, Consideraciones Técnicas excluidas de RF, agrupación Dado/Cuando/Entonces, filtrado de restos de
  plantilla, Quality Gate al final, comportamiento genérico sin sección de criterios o sin encabezados). No se
  derivan requisitos nuevos: no hacía falta, la spec ya los dejó concretos.
- RF-14/RF-15 → contexto y restricciones (NO son requisitos de implementación aparte): stack .NET 10/C#/xUnit;
  cambio acotado a `src/AIHarness/Specs/SpecGenerator.cs` y `tests/AIHarness.Tests/SpecGeneratorTests.cs`; sin
  dependencias nuevas; sin tocar las secciones de la spec generada ni el listado de agentes. Ya recogidas en la
  Restricción explícita del enunciado del Architect; no generan tarea de desarrollo propia.

Criterios de aceptación verificables derivados de `RF-08`..`RF-13` (sustituyen a los 3 genéricos de la sección
`## Acceptance Criteria` de la spec para efectos de `tester` y revisores; ver tabla *Test Strategy*):

- [ ] AC-R1: Dado un Issue con una sección "Requisitos" (4 viñetas) y una sección "Criterios de Aceptación" /
      "Acceptance Criteria" (4 viñetas) — como el Issue aaicardi/aiharness-testlab#1 referenciado en la spec —,
      cuando se genera la spec, entonces `## Functional Requirements` tiene `RF-01` a `RF-04` con esas 4 viñetas y
      `## Acceptance Criteria` tiene `AC-01` a `AC-04` con esas 4 viñetas, seguidas del criterio del Quality Gate
      como último elemento y ningún otro texto genérico adicional.
- [ ] AC-R2: Dado un Issue construido con la plantilla de User Story anterior sin rellenar (viñetas vacías,
      `Criterio 1`, `Criterio 2`, `**Espec. OpenSpec:**`, `**Agentes requeridos:**`), cuando se genera la spec,
      entonces ninguno de esos textos aparece en `## Functional Requirements` ni en `## Acceptance Criteria`.
- [ ] AC-R3: Dado un criterio de aceptación escrito en tres viñetas consecutivas ("Dado...", "cuando...",
      "entonces..."), cuando se genera la spec, entonces las tres se renderizan como un único elemento
      `AC-XX` en `## Acceptance Criteria` (no como tres).
- [ ] AC-R4: Dado un Issue cuya sección de criterios usa el encabezado en inglés `## Acceptance Criteria` (en vez
      de "Criterios de Aceptación"), cuando se genera la spec, entonces sus viñetas se vuelcan como `AC-XX`.
- [ ] AC-R5: Dado un Issue sin encabezados (`#`/`##`/`###`) y con viñetas sueltas, cuando se genera la spec,
      entonces esas viñetas siguen siendo `RF-XX` y `## Acceptance Criteria` mantiene los 3 criterios genéricos
      actuales (sin cambios de comportamiento).
- [ ] AC-R6 (Quality Gate): `dotnet test` pasa sin errores sobre toda la solución, incluidos los tests nuevos y
      existentes de `SpecGeneratorTests`.

## Approach
El cambio es íntegramente interno a `SpecGenerator.Render` (y sus métodos privados estáticos); no toca la firma
pública (`Generate`, `Render(GitHubIssue, int, string?)`), ni las secciones de la spec generada, ni el listado de
agentes — solo cambia cómo se calculan las listas de `Functional Requirements` y `Acceptance Criteria` a partir del
`body`.

El diseño parte de una función de segmentación `SplitIntoSections(body)` que recorre el `body` línea a línea y
corta por cada encabezado ATX de nivel 1 a 3 (`^(#{1,3})\s+(.+?)\s*$`), produciendo una lista de secciones
`(Title, Content)` donde `Title` es `null` para el contenido anterior al primer encabezado (preámbulo). Si el
`body` no tiene ningún encabezado, el resultado es una única sección con `Title = null` y `Content = body`
completo — así RF-R7/AC-R5 (comportamiento sin encabezados) sale gratis del mismo camino de código, sin un `if`
especial.

Cada `Title` se normaliza con una función nueva `NormalizeHeading` (análoga a `Slugify`: `NormalizeHeading`
descompone con `NormalizationForm.FormD`, descarta las marcas combinantes (tildes) y cualquier carácter que no sea
letra/dígito/espacio ASCII — lo que de paso elimina los emojis — y baja a minúsculas, colapsando espacios). El
resultado se compara contra dos conjuntos de alias fijos:
- Criterios de Aceptación: `"criterios de aceptacion"`, `"acceptance criteria"`.
- Consideraciones Técnicas: `"consideraciones tecnicas"`, `"technical notes"`.

Las secciones cuyo título normalizado cae en el primer conjunto alimentan `## Acceptance Criteria`; las del
segundo conjunto se excluyen de RF y AC (siguen aflorando igual que hoy solo en el blockquote de
`## Context & Objectives`, que no cambia); el resto (incluido el preámbulo y, p. ej., "Objetivo"/"Requisitos")
alimenta `## Functional Requirements`, igual que hoy.

Antes de segmentar, se elimina del texto a analizar (no del texto citado en `Context & Objectives`, que debe seguir
mostrando el Issue tal cual) cualquier comentario HTML con `Regex.Replace(body, "<!--[\s\S]*?-->", "")`. Es
importante aplicar esto sobre una copia (`parsedBody`) usada solo para extraer RF/AC; el `body` original se sigue
citando íntegro en el blockquote, para no perder transparencia sobre el contenido real del Issue.

Extracción de viñetas: se reutiliza el patrón ya existente (`RequirementLineRegex`) para capturar el texto de cada
viñeta de nivel superior (lista o checklist). Sobre ese texto se aplica un filtro común `IsMeaningfulContent`
(nuevo, usado tanto para RF como para AC) que descarta: contenido vacío tras recortar; viñetas que son únicamente
una etiqueta en negrita (`^\*\*[^*]+\*\*:?\s*$`, cubre `**Agentes requeridos:**` y `**Espec. OpenSpec:**`); y los
textos de plantilla `Criterio 1` / `Criterio 2` (comparación insensible a mayúsculas, idealmente con el patrón
`^criterio\s+\d+$` para cubrir cualquier número, no solo 1 y 2).

Para la sección de Criterios de Aceptación hay un paso adicional antes de filtrar y numerar: agrupar líneas
consecutivas de un mismo escenario Dado/Cuando/Entonces (o Given/When/Then, con continuaciones Y/And). Se recorren
las viñetas ya extraídas (antes de aplicar `IsMeaningfulContent`, para no romper una secuencia por una viñeta vacía
intermedia) con una máquina de estados simple: una viñeta que empieza con `dado`/`given` (insensible a mayúsculas y
tildes) abre un grupo nuevo; mientras un grupo está abierto, una viñeta que empieza con
`cuando`/`when`/`entonces`/`then`/`y`/`and` se añade al grupo abierto; cualquier otra viñeta cierra el grupo
anterior (si existía) y abre uno nuevo de un solo elemento. Cada grupo se concatena en un único texto (uniendo sus
líneas con `", "`) antes de aplicar `IsMeaningfulContent` y numerar.

Render final de `## Acceptance Criteria`:
- Si no hay sección reconocida como Criterios de Aceptación, **o** la hay pero tras agrupar y filtrar queda vacía
  (p. ej. porque solo tenía restos de plantilla), se mantiene exactamente el comportamiento actual (fallback
  genérico): la línea del Quality Gate (`` `{testCommand}` pasa sin errores. `` o el texto genérico si
  `testCommand` es `null`) seguida de las dos líneas genéricas actuales, en el mismo orden de hoy. Este es el
  camino que cubren ya los tests `Render_WithTestCommand_...` y `Render_WithoutTestCommand_...`, que no deben
  cambiar.
- Si hay al menos un criterio AC válido, se renderiza como checklist numerada —
  `- [ ] **AC-01:** <texto>`, `- [ ] **AC-02:** <texto>`, ... — seguida, como último elemento y sin numerar, de la
  línea del Quality Gate (mismo texto que en el fallback). Las dos líneas genéricas actuales
  ("Cada requerimiento funcional...", "El pipeline de CI...") **no** se incluyen en este camino: la spec (RF-06,
  RF-08) solo exige que el Quality Gate sea el último elemento después de los `AC-XX`, no que convivan con los
  genéricos.

`## Functional Requirements` usa el mismo filtro `IsMeaningfulContent` sobre las viñetas de las secciones no-AC
y no-Técnicas (sin el paso de agrupación GWT, que es exclusivo de AC), numerando `RF-01`, `RF-02`, ... en el orden
en que aparecen. Si tras filtrar no queda ninguna, se mantiene el placeholder actual
(`_Pendiente de definir por el agente architect._`).

## Changes
| Archivo | Cambio | RF |
|---|---|---|
| `src/AIHarness/Specs/SpecGenerator.cs` | Nueva función `SplitIntoSections(body)`: segmenta el body por encabezados `#`/`##`/`###`, preservando el preámbulo como sección sin título. | RF-01 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Nueva función `NormalizeHeading(title)`: quita tildes (NFD + descarte de marcas), quita emojis/puntuación (se queda solo con letras/dígitos/espacio ASCII), baja a minúsculas. Nuevos `[GeneratedRegex]` de soporte. | RF-01 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Conjuntos de alias normalizados para "Criterios de Aceptación"/"Acceptance Criteria" y "Consideraciones Técnicas"/"Technical Notes"; clasificación de cada sección en AC / excluida / RF. | RF-01, RF-02, RF-03 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `ExtractRequirements(body)` pasa a operar solo sobre las secciones clasificadas como RF (preámbulo + resto), reutilizando `RequirementLineRegex` + nuevo filtro `IsMeaningfulContent`. | RF-02, RF-03 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Nueva función `ExtractAcceptanceCriteria(body)`: toma la sección AC (si existe), agrupa líneas Dado/Cuando/Entonces/Given/When/Then (+ Y/And) consecutivas en un único criterio, aplica `IsMeaningfulContent` y numera. Devuelve lista vacía si no hay sección o queda vacía tras filtrar. | RF-02, RF-04 |
| `src/AIHarness/Specs/SpecGenerator.cs` | Nuevo filtro común `IsMeaningfulContent(text)`: descarta vacíos, viñetas que son solo una etiqueta en negrita, y los textos de plantilla `Criterio 1`/`Criterio 2`. Comentarios HTML (`<!-- -->`) se eliminan del texto a analizar antes de segmentar (no del body citado). | RF-05 |
| `src/AIHarness/Specs/SpecGenerator.cs` | `Render(...)`: construye `## Acceptance Criteria` con el checklist `AC-XX` + Quality Gate al final cuando hay criterios AC válidos; si no, conserva el bloque genérico actual sin cambios (incluye el caso sin encabezados). | RF-06, RF-07 |
| `tests/AIHarness.Tests/SpecGeneratorTests.cs` | Tests nuevos para cada Refined Requirement (ver *Test Strategy*); los 7 tests existentes (`Render_*`) deben seguir pasando sin modificación de sus asserts. | RF-01..RF-07 |

## Tasks
1. developer: implementar `NormalizeHeading` y los `[GeneratedRegex]` de soporte (heading ATX nivel 1-3, etiqueta
   en negrita, `criterio\s+\d+`, palabra inicial Dado/Cuando/Entonces/Given/When/Then/Y/And) en
   `src/AIHarness/Specs/SpecGenerator.cs`, junto a los regex existentes, siguiendo el mismo estilo
   (`partial` + `[GeneratedRegex]`).
2. developer: implementar `SplitIntoSections` y la clasificación de secciones (AC / excluida-técnica / RF) usando
   los alias normalizados fijos listados en *Approach*.
3. developer: reescribir `ExtractRequirements` para operar solo sobre las secciones RF (preámbulo incluido) y
   aplicar `IsMeaningfulContent`; mantener la firma `List<string> ExtractRequirements(string body)` para no tocar
   más superficie de la necesaria.
4. developer: implementar `ExtractAcceptanceCriteria(string body)` con el paso de agrupación Dado/Cuando/Entonces
   (máquina de estados descrita en *Approach*) y el filtro `IsMeaningfulContent`, devolviendo `List<string>` (vacía
   si no aplica).
5. developer: en `Render`, eliminar los comentarios HTML del texto usado para `SplitIntoSections`/extracción (no
   del `body` citado en `Context & Objectives`) y construir `## Acceptance Criteria` con la lógica de dos caminos
   (checklist `AC-XX` + Quality Gate al final, o fallback genérico sin cambios) descrita en *Approach*.
6. developer: ejecutar `dotnet build --no-restore` y `dotnet test` localmente antes de entregar; confirmar que los
   7 tests `Render_*` y `Generate_*`/`Slugify_*`/`NextSpecNumber_*` existentes siguen pasando sin tocar sus asserts.
7. tester: cubrir cada criterio de *Test Strategy* con un `[Fact]`/`[Theory]` nuevo en
   `tests/AIHarness.Tests/SpecGeneratorTests.cs`, siguiendo el estilo `Render_<Escenario>_<Resultado>` ya usado en
   el archivo; no introducir Moq ni otras dependencias.
8. tester: añadir al menos un caso con comentarios HTML multilínea (`<!-- ... -->` abarcando varias líneas,
   incluida una que contenga una viñeta) para confirmar que ese contenido no aparece ni en RF ni en AC, pero el
   resto del body sí se sigue citando en `Context & Objectives`.
9. tester: añadir un caso con encabezado de nivel 3 (`### Criterios de Aceptación`) para confirmar que RF-01 ("#",
   "##", "###") se cumple en los tres niveles, y un caso con `####` para confirmar que un encabezado de nivel 4 NO
   se trata como encabezado (sus viñetas, si las tiene la sección que lo contiene, siguen las reglas de la sección
   padre).

## Test Strategy
| Criterio de aceptación | Tipo de test | Caso(s) |
|---|---|---|
| AC-R1 (Issue con 4 RF + 4 AC + Quality Gate) | Unitario (`Render`) | Fixture sintética con sección "Requisitos" (4 viñetas) y sección "Criterios de Aceptación" (4 viñetas); assert `RF-01`..`RF-04`, `AC-01`..`AC-04`, línea de Quality Gate última y sin las 2 líneas genéricas. |
| AC-R2 (restos de plantilla descartados) | Unitario (`Render`) | Body con viñetas vacías, `Criterio 1`, `Criterio 2`, `**Espec. OpenSpec:**`, `**Agentes requeridos:**` en secciones RF y AC; assert que ninguno de esos textos aparece en el contenido generado. |
| AC-R3 (agrupación Dado/Cuando/Entonces) | Unitario (`ExtractAcceptanceCriteria` vía `Render`) | Sección AC con 3 viñetas "Dado...", "cuando...", "entonces..." consecutivas; assert que `## Acceptance Criteria` tiene un único `AC-XX` con las tres fusionadas (contar ocurrencias de `AC-0` en el contenido). |
| AC-R4 (encabezado en inglés) | Unitario (`Render`) | Body con `## Acceptance Criteria` (inglés) y viñetas; assert que se vuelcan como `AC-XX`, no como `RF-XX`. |
| AC-R5 (sin encabezados → comportamiento actual) | Unitario (`Render`) | Body sin ningún `#`/`##`/`###`, solo viñetas; assert que son `RF-XX` y que `## Acceptance Criteria` tiene exactamente los 3 criterios genéricos actuales (mismo contenido que hoy). |
| AC-R6 (Quality Gate) | Suite completa | `dotnet test` en CI/local tras los cambios; todos los tests (nuevos y existentes) en verde. |
| RF-03 (Consideraciones Técnicas visibles pero no RF) | Unitario (`Render`) | Body con sección "Consideraciones Técnicas" con viñetas; assert que esas viñetas aparecen en el blockquote de `Context & Objectives` y no en `## Functional Requirements`. |
| RF-05 (comentarios HTML descartados) | Unitario (`Render`) | Body con `<!-- nota interna: - Esto no es un requisito -->` dentro de una sección RF o AC; assert que el contenido del comentario no aparece como RF ni AC, pero si el comentario completo se cita tal cual en el blockquote (transparencia del Issue original). |

## Risks
- **Cambio de forma de `## Acceptance Criteria` cuando hay sección AC reconocida** (se pierden las dos líneas
  genéricas "Cada requerimiento funcional..." / "El pipeline de CI...") → es lo que pide explícitamente RF-06/RF-08
  ("seguidos del criterio del Quality Gate", sin mencionar las otras dos); mitigación: documentarlo en el PR y
  cubrirlo con AC-R1 para que no sea un efecto colateral silencioso, y dejar sin cambios el camino sin sección AC
  (RF-07/AC-R5), que es el que usan hoy las specs ya generadas sin esta sección.
- **La palabra de continuación "Y"/"And" es muy corta y ambigua** (muchas frases en español empiezan con "Y" sin
  ser continuación de un Dado/Cuando/Entonces) → riesgo de fusionar viñetas no relacionadas si aparecen justo
  después de un "Dado" abierto; mitigación: la máquina de estados solo fusiona mientras hay un grupo "Dado"
  explícitamente abierto (nunca fusiona viñetas sueltas que no siguen a un "Dado"), y se añade un test con una
  viñeta normal que empieza con "Y" fuera de un grupo Dado para confirmar que no se fusiona por accidente.
  Si el `tester` encuentra un falso positivo real al escribir los tests, debe reportarlo antes de cerrar la tarea
  en vez de forzar el `[Fact]` a pasar debilitando el assert.
- **Fixture para AC-R1 no es el Issue real de `aaicardi/aiharness-testlab#1`** (no hay acceso de red/credenciales
  para leerlo, y no hay un fixture existente en el repo) → se usa una fixture sintética equivalente en estructura
  (4 RF + 4 AC); mitigación: si existe texto disponible del Issue real (p. ej. porque el usuario lo pega), usarlo
  tal cual; si no, la fixture sintética basta para verificar el comportamiento descrito en RF-08, que es lo que la
  spec pide probar, no el contenido literal de ese Issue externo.
- **Regresión en los 10 tests existentes de `SpecGeneratorTests`** (en particular los dos que fijan el formato
  actual sin encabezados, `Render_WithTestCommand_...` y `Render_WithoutTestCommand_...`) → mitigación: el diseño
  hace que "sin sección AC reconocida" sea exactamente el mismo camino de código que hoy (ningún `if` nuevo cambia
  ese bloque); correr la suite completa antes de entregar (tarea developer 6) es la verificación directa.
