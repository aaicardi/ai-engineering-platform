---
name: openspec-validator
description: Valida si una especificación de openspec/specs/NNN-*.md está lista para implementarse (estructura, requerimientos RF-XX, criterios de aceptación verificables) y devuelve READY, NEEDS_REFINEMENT o NOT_READY. Úsala antes de planificar o implementar cualquier spec, en especial las generadas automáticamente desde un Issue.
---

# Skill: OpenSpec Validator

Evalúa una spec antes de que se planifique o implemente. Las specs que genera AI Harness desde un Issue
(`SpecGenerator`) tienen esta estructura:

```markdown
# OpenSpec NNN: <título>
## Issue Reference        → "Closes #N"
## Context & Objectives   → cuerpo del Issue, citado con ">"
## Functional Requirements → "- **RF-01:** ..."
## Acceptance Criteria    → checklist "- [ ] ..."
```
Las specs escritas a mano pueden añadir `## Architecture & Components`, `## Task Breakdown` (recomendados) u `## Out of Scope`.

## 1. Estructura
| Comprobación | Si falla |
|---|---|
| H1 `# OpenSpec NNN: <título>` y nombre de archivo `NNN-<slug>.md` | NOT_READY |
| `Closes #N` (o `Fixes`/`Resolves`) — obligatorio cuando la ejecuta AI Harness | NOT_READY |
| Sección de requerimientos con al menos un `RF-XX` | NOT_READY |
| Sección de criterios de aceptación con al menos un elemento | NOT_READY |

## 2. Contenido
Revisa cada requerimiento y criterio:

- **Placeholder:** un `RF-XX` con el texto *"Pendiente de definir por el agente architect"* → NEEDS_REFINEMENT.
- **Criterios genéricos:** si todos los criterios son genéricos (compila, "requisitos cubiertos por pruebas", CI en verde)
  y ninguno describe comportamiento observable → NEEDS_REFINEMENT.
- **Criterios de otro stack:** un criterio que menciona un stack distinto del repositorio (p. ej. ".NET" en un proyecto
  Node) → se ignora ese criterio y se anota; no bloquea.
- **Verificable:** cada criterio debe poder comprobarse con un test o una verificación concreta.
  "Debe ser rápido" no lo es; "responde en menos de 200 ms con 1 000 registros" sí.
- **Contradicciones:** dos requerimientos incompatibles entre sí, o con el `CLAUDE.md` → NOT_READY.
- **Acciones de CLAUDE.md §3:** si el requerimiento solo se cumple con una acción que requiere aprobación humana
  (aplicar migraciones, desplegar, crear secretos), se anota como paso manual; no bloquea por sí mismo.

## 3. ¿Se puede refinar?
Con NEEDS_REFINEMENT, comprueba si el *Context & Objectives* tiene información suficiente para derivar requerimientos y
criterios concretos (quién, qué comportamiento, qué resultado). Si no la tiene (por ejemplo, el Issue solo tiene título),
el veredicto pasa a NOT_READY.

## Veredictos
| Veredicto | Significado | Qué hace `lead` |
|---|---|---|
| `READY` | Implementable tal cual | Continúa |
| `NEEDS_REFINEMENT` | Implementable, pero `architect` debe derivar RF y criterios concretos del contexto | Pide a `architect` la sección *Refined Requirements* en el plan |
| `NOT_READY` | Falta información o hay contradicciones | `status: BLOCKED` con las preguntas |

## Formato de respuesta
```markdown
verdict: READY | NEEDS_REFINEMENT | NOT_READY
spec: <ruta>
structure: <OK | problemas>
issues:
- <RF-XX o criterio> — <problema>
ignored_criteria: <criterios de otro stack | ninguno>
manual_steps: <acciones de CLAUDE.md §3 | ninguna>
questions: <preguntas para una persona, solo si NOT_READY>
```
