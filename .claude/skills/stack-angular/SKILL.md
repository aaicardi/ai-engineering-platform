---
name: stack-angular
description: Convenciones de componentes standalone, signals, servicios, RxJS, formularios, testing y checklist de revisión para aplicaciones Angular. Úsala cuando el repositorio tenga angular.json; complementa a stack-typescript-node.
---

# Skill: Angular

Complementa a `stack-typescript-node` (TypeScript, npm). Las convenciones del repositorio y su versión de Angular prevalecen.

## Comandos
| Acción | Comando |
|---|---|
| Compilar | `npx ng build` (o `npm run build`) |
| Tests | `npx ng test --watch=false` (o `npm test`); en CI añade `--browsers=ChromeHeadless` si usa Karma |
| Lint | `npx ng lint` (si está configurado) |
| Generar | `npx ng generate component <ruta>` para respetar la configuración del proyecto |

## Convenciones
- **Componentes standalone** (salvo que el repositorio use NgModules) y `ChangeDetectionStrategy.OnPush`.
- **Signals** para el estado del componente (`signal`, `computed`); `input()`/`output()` en versiones que los soporten.
- **Control flow** nuevo (`@if`, `@for` con `track`) si la versión lo permite; si no, `*ngFor` con `trackBy`.
- **Servicios** con `providedIn: 'root'` e `inject()`; la lógica de negocio y HTTP va en servicios, no en componentes.
- **RxJS:** sin suscripciones manuales sin cancelar (`takeUntilDestroyed`, `async` pipe o `toSignal`); sin `subscribe` anidados (usa operadores).
- **Formularios:** Reactive Forms tipados con validadores.
- **HTTP:** interceptores para autenticación y errores; tipos de respuesta explícitos.
- **Seguridad:** no uses `bypassSecurityTrust*` con contenido de usuario; confía en el saneado de Angular.
- Rutas con *lazy loading* (`loadComponent`/`loadChildren`) y guards para las protegidas.

## Tests (Jasmine/Karma o Jest, el que use el repositorio)
- `TestBed` con el componente standalone en `imports`; `provideHttpClientTesting` y `HttpTestingController` para HTTP.
- Componentes: prueba el DOM renderizado y los eventos, no los métodos privados.
- Servicios: prueba sin `TestBed` cuando no hay dependencias de Angular.
- Asincronía con `fakeAsync`/`tick` o `waitForAsync`; nada de `setTimeout` real.

## Revisión
- [ ] `OnPush` y signals o `async` pipe; sin suscripciones sin cancelar.
- [ ] Lógica en servicios, componentes delgados.
- [ ] `track`/`trackBy` en listas.
- [ ] Sin `bypassSecurityTrust*` con datos externos.
