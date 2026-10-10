---
name: stack-typescript-node
description: Convenciones, comandos de build, lint y test, y checklist de revisión para proyectos TypeScript y Node.js (backend, librerías y base de los frontends). Úsala cuando el repositorio tenga package.json; combínala con stack-react o stack-angular si es un frontend.
---

# Skill: TypeScript / Node.js

Las convenciones del repositorio (`CLAUDE.md`, `tsconfig.json`, configuración de ESLint/Prettier, código existente) prevalecen.

## Comandos
Lee `package.json` → `scripts` y usa **esos** scripts. Detecta el gestor por el lockfile: `package-lock.json` → npm,
`pnpm-lock.yaml` → pnpm, `yarn.lock` → yarn.

| Acción | Comando habitual |
|---|---|
| Instalar | `npm ci` (respeta el lockfile) |
| Compilar | `npm run build` |
| Tests | `npm test` (un archivo: `npm test -- <ruta>`) |
| Lint | `npm run lint` |
| Tipos | `npx tsc --noEmit` |
| Vulnerabilidades | `npm audit --omit=dev` |

Si `npm test` es el placeholder de `npm init` (`echo "Error: no test specified" && exit 1`), el repositorio no tiene tests:
configura el framework que indique el plan (Vitest por defecto en proyectos nuevos).

## Convenciones
- `strict: true`; sin `any` (usa `unknown` y estrecha el tipo); sin `@ts-ignore` (si es inevitable, `@ts-expect-error` con motivo).
- Tipos para contratos públicos; `interface` o `type` según el estilo del repositorio.
- `async/await` con manejo de errores; ninguna promesa sin `await` o sin `.catch` (*floating promises*).
- Validación en los bordes (HTTP, colas, archivos) con el validador del repositorio (zod, class-validator...).
- Configuración desde variables de entorno validadas al arrancar; nunca secretos en el código.
- Dependencias: no añadas paquetes para lo que ofrece la librería estándar; si añades, actualiza el lockfile con el gestor del repositorio.
- Node: `node:` como prefijo para módulos nativos; sin APIs síncronas de I/O en rutas de petición.

## Tests (Vitest o Jest, el que use el repositorio)
- Archivos `*.test.ts` o `*.spec.ts` según la convención existente.
- `describe` por unidad, `it` por comportamiento, con nombres en lenguaje natural.
- Mocks de red y reloj (`vi.useFakeTimers` / `jest.useFakeTimers`); sin llamadas reales a red.
- Restaura los mocks entre tests.

## Revisión
- [ ] Sin `any` nuevos ni aserciones de tipo (`as`) injustificadas.
- [ ] Sin promesas sin manejar.
- [ ] Entradas externas validadas.
- [ ] Sin `eval`, `new Function` ni `child_process.exec` con cadenas construidas (usa `execFile`/`spawn` con argumentos).
- [ ] Lockfile actualizado si cambiaron dependencias.
