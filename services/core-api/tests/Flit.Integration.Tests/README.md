# Flit.Integration.Tests — arnés contra PostgreSQL real

> HU #12319 (Feature #12254, Épica #12235). Sin Testcontainers ni Docker: el arnés usa **un PostgreSQL 16
> alcanzable por connection string** (tu instancia local o el servicio `postgres:16-alpine` del CI).

## Qué hace

1. Resuelve una **conexión administrativa** (ver orden abajo) y cambia la base por `postgres`.
2. Crea una base **efímera** `flit_it_<yyyyMMddHHmmss>_<4hex>`, le aplica **todas** las migraciones EF con el
   `FlitDbContext` real (`Database.MigrateAsync()`) y descubre el esquema **antes de la primera prueba**.
3. **Antes de cada prueba** deja la base como recién migrada (`PostgresDatabaseFixture.ResetAsync`).
4. Al terminar la colección la borra con `DROP DATABASE … WITH (FORCE)`.

Nunca toca `flit_local` (tu base de trabajo) ni `flit_dev` (CI): solo usa el servidor para crear/borrar la efímera.

## Cómo correr en local

```bash
cd services/core-api
dotnet test tests/Flit.Integration.Tests/Flit.Integration.Tests.csproj
```

Con `ConnectionStrings:Core` en `src/Flit.Api/appsettings.Development.json` no hace falta nada más.
Duración de referencia: ~20 s de migración + ~1 s por prueba (34 pruebas ≈ 45 s).

### Resolución de la connection string administrativa (en este orden)

| # | Fuente | Uso |
|---|--------|-----|
| 1 | `FLIT_IT_PG_ADMIN` (env, connection string completa) | Apuntar a otro servidor o usuario sin tocar appsettings |
| 2 | `ConnectionStrings__Core` (env) | Es la que exporta `.github/workflows/core-api.yml` en el paso **Test** |
| 3 | `ConnectionStrings:Core` de `src/Flit.Api/appsettings.Development.json` | Desarrollo local (se localiza subiendo desde el directorio de salida) |

En 2 y 3 se sustituye `Database=` por `postgres`. La contraseña nunca se imprime (solo `Host/Port/Username/Database`).

El rol debe poder `CREATE DATABASE` y `DROP DATABASE … WITH (FORCE)` (superusuario o `CREATEDB` + dueño).

### Sin motor alcanzable

- **Fuera de CI:** todas las pruebas quedan **omitidas** (skip) con el mensaje
  `Requiere PostgreSQL alcanzable: define ConnectionStrings__Core o FLIT_IT_PG_ADMIN …`.
  Demostración: `FLIT_IT_PG_ADMIN="Host=127.0.0.1;Port=1;Username=x;Password=x;Database=postgres" dotnet test …`.
- **En CI** (`GITHUB_ACTIONS=true` o `CI=true`): el fixture **falla** con `CI sin PostgreSQL alcanzable …` — nunca un skip silencioso.

## Cómo escribir una prueba

```csharp
public sealed class MiPrueba(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]                       // o [PostgresTheory]
    public async Task Hace_algo()
    {
        await using var ctx = NewContext();          // FlitDbContext real (Npgsql + snake_case)
        await using var cn = await Fixture.OpenConnectionAsync(); // NpgsqlConnection para SQL directo
        ...
    }
}
```

- Un `FlitDbContext` **por operación**: tras un `SaveChanges` rechazado por el motor, EF conserva la entidad fallida.
- Los errores del motor llegan como `PostgresException` (SQL directo) o como `DbUpdateException.InnerException` (EF):
  afirmar sobre `SqlState`, `ConstraintName`, `MessageText`.
- Los `INSERT` que dependen de un trigger `BEFORE` sobre otra fila (p. ej. hijo → padre en `identity.tenants`) van
  en `SaveChanges` separados: EF no garantiza el orden entre filas sin navegación.

## Estrategia de reset (AC2) y lista blanca

`TRUNCATE … RESTART IDENTITY CASCADE` **solo de las tablas de trabajo que tienen filas** (un bloque `DO` del lado del
servidor decide cuáles), más `UPDATE identity.hierarchy_switches SET is_enabled = true`. Se excluyen:

- `__EFMigrationsHistory`.
- `PostgresDatabaseFixture.PreservedSeededTables` (constante documentada): catálogos sembrados por migración sin
  condición de entorno — `catalogs.vehicle_*`, `catalogs.rejection_reasons`, `tramites.procedure_types` y su
  configuración, `identity.hierarchy_switches`.

`HarnessBootstrapTests` comprueba que **lo sembrado tras migrar** = lista blanca + `KnownNonCatalogSeededTables`
(`identity.tenants` de `SeedMockCompanies`, `admin.notification_test_settings`, `audit.audit_logs`, que sí se truncan).
Una migración nueva que siembre algo hace fallar esa prueba hasta que alguien lo clasifique. El fixture además falla
al arrancar si una tabla preservada referencia por FK a una truncada (el `CASCADE` la vaciaría en silencio).

Se descartó la transacción con rollback por prueba: los datos serían invisibles a cualquier otra conexión
(`WebApplicationFactory`, repositorios con conexión propia) y cada aserción sobre un error del motor necesitaría un
savepoint. Los triggers de inmutabilidad (`tr_tenant_hierarchy_audit_immutable`) rechazan `DELETE` pero no `TRUNCATE`.

**Dev-seeds** (`12-HU10200`, `16-HU10133`, `21-HU10240`, `45-ricaurte`): están condicionados a
`ASPNETCORE_ENVIRONMENT=Development` / `FLIT_DEV_SEED`; el fixture neutraliza esas variables mientras migra para que
la efímera nazca igual en cualquier portátil y en CI. `catalogs.transit_offices` nace vacía (su seed `27-HU10659` no
está enlazado a ninguna migración): cada prueba siembra las OT que necesita.

## RLS

La conexión administrativa (superusuario / dueño) **no** está sujeta a las políticas RLS (ninguna tabla tiene
`FORCE ROW LEVEL SECURITY`), igual que el rol de aplicación en los ambientes. Este arnés no exercita RLS.

## CI

`.github/workflows/core-api.yml` levanta `postgres:16-alpine`, exporta `ConnectionStrings__Core` y corre `dotnet test`
de toda la solución: este proyecto se ejecuta automáticamente (está en `Flit.slnx`) y bloquea el merge si falla.
