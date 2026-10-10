---
name: review-checklist
description: Escala de severidad común (CRITICAL, HIGH, MEDIUM, LOW) y checklist multidimensional para revisar un cambio de código antes del Pull Request. Úsala en cualquier revisión de código o de seguridad para clasificar hallazgos de forma consistente.
---

# Skill: Review Checklist

## Escala de severidad
La severidad depende del **impacto demostrable**, no de la categoría. Ante la duda entre dos niveles, elige el menor
y explica por qué.

| Severidad | Criterio | Ejemplos |
|---|---|---|
| `CRITICAL` | Explotable, pérdida o corrupción de datos, rompe el build o la suite, o viola CLAUDE.md §3 | Inyección SQL alcanzable; secreto en el diff; migración destructiva sin respaldo; tests en rojo; el cambio implementa otra cosa que la spec |
| `HIGH` | Comportamiento incorrecto en un caso real, o un requerimiento o criterio de aceptación sin cumplir | Error no manejado en el flujo principal; condición de carrera; endpoint sin autorización; criterio de aceptación sin test; cambio incompatible de API no previsto en la spec |
| `MEDIUM` | Funciona, pero es frágil o costoso de mantener | Duplicación evitable; abstracción equivocada; caso límite sin test; consulta N+1 en un volumen moderado; error tragado con log |
| `LOW` | Mejora menor sin impacto funcional | Nombres poco claros; comentario desactualizado; simplificación posible |

`lead` corrige automáticamente `CRITICAL` y `HIGH`. `MEDIUM` y `LOW` van al Pull Request para decisión humana.

## Checklist
Recórrela sobre el cambio; marca `n/a` lo que no aplica.

1. **Correctness:** casos nulos o vacíos, límites (off-by-one), manejo de errores y excepciones, concurrencia y
   cancelación, liberación de recursos, zonas horarias y cultura, idempotencia.
2. **Cumplimiento de la spec:** cada `RF-XX` implementado; nada fuera de alcance; criterios de aceptación demostrables.
3. **Arquitectura:** respeta las capas y patrones existentes; dependencias en la dirección correcta; sin acoplamiento nuevo innecesario.
4. **Seguridad:** entradas validadas, consultas parametrizadas, sin secretos, autorización en operaciones nuevas, sin datos sensibles en logs.
5. **Rendimiento:** complejidad razonable para el volumen esperado; sin I/O en bucles; sin consultas N+1; sin bloqueos en código asíncrono.
6. **Tests:** cubren los criterios de aceptación y los casos de error; deterministas; verifican comportamiento, no implementación.
7. **Mantenibilidad:** nombres claros, funciones de una responsabilidad, sin código muerto ni duplicado, comentarios que explican el *porqué*.
8. **Estándares:** sigue las convenciones del repositorio y de la skill `stack-*`; sin warnings nuevos.
9. **Dependencias:** las nuevas están justificadas, con versión fija, mantenidas y sin vulnerabilidades conocidas.
10. **Compatibilidad hacia atrás:** contratos públicos, formatos de archivo, opciones de CLI y configuración existentes siguen funcionando, o el cambio está previsto en la spec.
11. **Contratos de API:** códigos de estado, formatos de error y esquemas coherentes con el resto de la API.
12. **Base de datos:** migraciones reversibles, sin pérdida de datos, índices para las consultas nuevas; las migraciones se crean, **no se aplican**.
13. **Observabilidad:** los errores relevantes se registran con contexto suficiente y sin datos sensibles.
14. **Documentación:** el comportamiento visible nuevo está documentado o se ha pedido a `documentation`.

## Un buen hallazgo
- **Ubicación exacta** (`archivo:línea`).
- **Escenario concreto:** entrada o estado → resultado incorrecto.
- **Corrección sugerida** que `developer` pueda aplicar sin volver a investigar.
- Sin hallazgos especulativos: si no puedes trazarlo, baja la confianza o descártalo.
