---
name: stack-dotnet
description: Convenciones, comandos de build y test, patrones de testing y checklist de revisión para proyectos .NET / C# (incluido .NET 10, ASP.NET Core, xUnit). Úsala cuando el repositorio tenga archivos *.sln, *.slnx o *.csproj.
---

# Skill: .NET / C#

Las convenciones del repositorio (`CLAUDE.md`, `.editorconfig`, código existente) prevalecen sobre esta guía.

## Comandos
| Acción | Comando |
|---|---|
| Restaurar | `dotnet restore` |
| Compilar | `dotnet build --no-restore` |
| Tests | `dotnet test` (un proyecto: `dotnet test <ruta.csproj>`; un test: `--filter "FullyQualifiedName~Clase.Metodo"`) |
| Formato | `dotnet format --verify-no-changes` (si el repositorio lo usa) |
| Vulnerabilidades | `dotnet list package --vulnerable --include-transitive` |

Ejecuta los comandos desde la raíz, sobre la solución si existe.

## Convenciones
- **Nullable** habilitado: no silencies advertencias con `!` salvo que esté demostrado que el valor no es nulo.
- **Async** de punta a punta: `Task`/`ValueTask`, propaga `CancellationToken`, nunca `.Result` ni `.Wait()`;
  sin `async void` excepto en manejadores de eventos.
- **Inmutabilidad:** `record` para datos, `readonly`, colecciones `IReadOnlyList<T>` en las interfaces públicas.
- **Validación de argumentos:** `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`.
- **Excepciones:** tipos específicos; no captures `Exception` salvo en el borde de la aplicación (y regístrala);
  no uses excepciones para el control de flujo normal.
- **DI:** dependencias por constructor (constructores primarios en C# 12+), interfaces en los puntos de sustitución
  (procesos, red, reloj, sistema de archivos).
- **Recursos:** `using`/`await using` para `IDisposable`/`IAsyncDisposable`.
- **Configuración y secretos:** `IOptions<T>`, variables de entorno o user-secrets; nunca valores secretos en `appsettings*.json`.
- **ASP.NET Core:** DTOs separados de las entidades, validación de entrada, `ProblemDetails` para errores, autorización
  explícita (`[Authorize]` o políticas) en cada endpoint nuevo.
- **EF Core:** consultas con `AsNoTracking` para lectura, sin N+1 (`Include` o proyecciones); migraciones con
  `dotnet ef migrations add` — **nunca** `dotnet ef database update` (CLAUDE.md §3).

## Tests (xUnit por defecto; usa el framework que ya tenga el repositorio)
- Un proyecto de tests por proyecto de producción o el esquema existente; nombre `Metodo_Escenario_Resultado`.
- `[Fact]` para un caso, `[Theory]` + `[InlineData]` para variantes.
- Dobles de prueba con las interfaces del proyecto (fakes a mano o la librería que ya se use; no añadas Moq si no está).
- Archivos temporales con `Directory.CreateTempSubdirectory` y limpieza en `Dispose`.
- Nada de `Thread.Sleep`; para tiempo, inyecta `TimeProvider`.
- APIs: `WebApplicationFactory<TProgram>` para tests de integración.

## Revisión
- [ ] Sin `.Result`, `.Wait()` ni `async void`.
- [ ] `CancellationToken` propagado en I/O.
- [ ] Sin warnings nuevos de nullable.
- [ ] `IDisposable` liberado.
- [ ] Consultas SQL parametrizadas (EF o `DbParameter`); sin `FromSqlRaw` con interpolación de cadenas.
- [ ] Procesos externos con `ProcessStartInfo.ArgumentList` (no cadenas con shell).
