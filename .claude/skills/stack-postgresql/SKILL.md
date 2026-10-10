---
name: stack-postgresql
description: Convenciones de esquema, migraciones (crearlas, nunca aplicarlas), índices, consultas seguras y checklist de revisión para cambios que tocan PostgreSQL. Úsala cuando un cambio añada o modifique tablas, migraciones, consultas SQL o el acceso a datos.
---

# Skill: PostgreSQL

Úsala junto a la skill del lenguaje (`stack-dotnet`, `stack-typescript-node`). Las convenciones del repositorio prevalecen.

## Regla de gobernanza
**Las migraciones se crean y se revisan; nunca se aplican** desde un agente (CLAUDE.md §3).
- Sí: `dotnet ef migrations add <Nombre>`, crear el archivo de migración de la herramienta del repositorio
  (Flyway, Knex, Prisma `migrate dev --create-only`, TypeORM `migration:generate`...).
- No: `dotnet ef database update`, `prisma migrate deploy`, `knex migrate:latest`, `psql` contra bases compartidas.
- Si una tarea requiere aplicarla, indícalo como paso manual en tu informe.

## Diseño de esquema
- Nombres en `snake_case`; tablas en plural o singular según el esquema existente.
- Clave primaria en todas las tablas (`bigint generated always as identity` o `uuid` según el repositorio).
- `timestamptz` para fechas (nunca `timestamp` sin zona), `numeric` para dinero, `text` en lugar de `varchar(n)` arbitrario.
- `NOT NULL` por defecto; claves foráneas con la acción `ON DELETE` explícita.
- Restricciones (`CHECK`, `UNIQUE`) para las reglas que deben cumplirse siempre.

## Migraciones seguras
- Una migración por cambio lógico, con su reversión (`Down`) cuando la herramienta lo permita.
- Cambios compatibles hacia atrás en tablas con datos: añadir columna nullable → rellenar → hacer `NOT NULL`, en pasos separados.
- Renombrar o eliminar columnas: en dos despliegues (dejar de usar → eliminar), nunca en uno.
- Índices en tablas grandes con `CREATE INDEX CONCURRENTLY` (fuera de transacción).
- Sin pérdida de datos sin un plan explícito en la spec.

## Consultas
- **Siempre parametrizadas**; nunca SQL construido con interpolación de cadenas.
- Índices para las columnas de `WHERE`, `JOIN` y `ORDER BY` de las consultas nuevas; justifícalos con el patrón de acceso.
- Paginación por clave (*keyset*) para listados grandes; `LIMIT` siempre en listados.
- Sin `SELECT *` en código de aplicación.
- Transacciones cortas; el nivel de aislamiento por defecto salvo motivo documentado.

## Tests
- Contra una base de datos efímera (Testcontainers, contenedor de `docker-compose` de desarrollo o la estrategia del repositorio),
  nunca contra una base compartida.
- Verifica que la migración aplica y revierte en la base efímera cuando el repositorio tenga esa infraestructura.

## Revisión
- [ ] Migración creada, no aplicada; reversible o con motivo documentado.
- [ ] Sin pérdida de datos ni bloqueos largos en tablas grandes.
- [ ] Consultas parametrizadas e indexadas.
- [ ] `timestamptz`, `NOT NULL` y claves foráneas correctos.
