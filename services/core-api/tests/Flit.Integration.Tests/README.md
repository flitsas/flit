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

## Cobertura #12322 — paridad y fuga entre clientes

> HU #12322 (Feature #12254). Carpeta `Tenancy/`: `HierarchyScenario` (P cabeza de grupo, hijos C1 y C2,
> ajeno X, aislado S, organismo O sobre la OT `Ot1` con grant solo hacia C1; datos simétricos por cliente),
> `CoveredQueries` (inventario + runner), `LeakAssert` (aserción que nombra id y tenant de cada fila ajena),
> `TenantLeakTests` (AC2/AC3/AC4 + ADR-0057), `TenantParityTests` (AC1/AC5 + snapshot de DTOs) y
> `CoveredQueriesInventoryTests` (esta tabla ≡ el inventario del código).

Cada consulta se ejecuta contra el repositorio **real** sobre la base efímera como C1, P, X y S (toda fila
debe ser del lector; C2 es simétrico a C1 y actúa como dueño de las filas que C1 no debe ver) y, si tiene modo global, como SuperAdmin (`null`: debe ver a los cinco).

| Id | Consulta | Parámetro de alcance | Fuga | Paridad S | SuperAdmin |
|----|----------|----------------------|------|-----------|------------|
| Q01 | `ProcedureInstanceRepository.ListWithSummaryGraphAsync` | `Guid? tenantId` | ✔ | ✔ (≡ `TenantScope.Single`) | ✔ (≡ `TenantScope.All`) |
| Q02 | `ProcedureInstanceRepository.ListWithSummaryGraphFilteredAsync` | `Guid? tenantId` | ✔ | ✔ | ✔ |
| Q03 | `ProcedureInstanceRepository.CountByStatusFilteredAsync` | `Guid? tenantId` | ✔ (agregado) | ✔ | ✔ |
| Q04 | `ProcedureInstanceRepository.GetFilterOptionsAsync` (`Companias`) | `Guid? tenantId` | ✔ | ✔ | ✔ |
| Q05 | `ProcedureInstanceRepository.GetByIdWithDetailsAsync` (detalle) | `Guid tenantId` | ✔ (+ `Hermanos_no_ven_el_detalle_del_otro`) | ✔ | — |
| Q06 | `ProcedureInstanceRepository.CountByTenantAndYearAsync` | `Guid tenantId` | ✔ (agregado) | ✔ | — |
| Q07 | `ProcedureInstanceRepository.GetStatusHistoryPageAsync` (historial de estados del detalle) | `Guid tenantId` | ✔ (trámite ajeno ⇒ `null`) | ✔ | — |
| Q08 | Historial por placa: `ListProcedureInstancesFilteredHandler` con el request de `PlateHistoryScope.BuildRequest` | `Guid? TenantId` | ✔ | ✔ | ✔ |
| Q09 | `CompanyReadRepository.ListAsync` (listado de compañías) | — (solo SuperAdmin) | n/a | DTO | ✔ |
| Q10 | `CompanyReadRepository.GetByIdAsync` | `Guid tenantId` | ✔ | ✔ | ✔ (ficha de cualquier cliente) |
| Q11 | `NotificationDeliveryLogRepository.ListByTenantAsync` | `Guid tenantId` | ✔ | ✔ | — |
| Q12 | `CompanyDocumentParamRepository.ListByTenantAsync` | `Guid tenantId` | ✔ | ✔ | — |
| Q13 | `CompanyAgreementRepository.ListActiveOfficeIdsAsync` | `Guid companyTenantId` | ✔ (contenido exacto) | ✔ | — |
| Q14 | `AdminAuditLogRepository.ListPagedAsync` | `Guid? filter.TenantId` | ✔ | ✔ | ✔ |
| Q15 | `InvitationRepository.ListPendingByTenantAsync` | `Guid tenantId` | ✔ | ✔ | — |
| Q16 | `AnalyticsReadRepository.GetOverviewAsync` | `Guid? tenantId` | ✔ (agregado) | ✔ | ✔ |
| Q17 | `AnalyticsReadRepository.GetTopProducersAsync` | `Guid? tenantId` | ✔ | ✔ | ✔ |
| Q18 | `AnalyticsReadRepository.GetMonthlyTrendAsync` | `Guid? tenantId` | ✔ (agregado) | ✔ | ✔ |
| Q19 | `AnalyticsReadRepository.GetProcedureDetailsAsync` | `Guid tenantId` | ✔ | ✔ | — |
| Q20 | `AnalyticsMetricsReadRepository.GetLiveOverviewAsync` | `Guid tenantId` | ✔ (agregado) | ✔ | — |
| Q21 | `AnalyticsMetricsReadRepository.GetFunnelAsync` | `MetricsFilter.TenantId` | ✔ (agregado) | ✔ | — |
| Q22 | `DetailedReportReadRepository.GetProceduresAsync` (vista `analytics.v_procedure_detail_report`) | `DetailedReportFilter.TenantId` | ✔ | ✔ | — |
| Q23 | `CompanyQueryRepository.ExecuteAsync` / `ExecuteForSuperAdminAsync` | `Guid tenantId` / todos | ✔ | ✔ | ✔ |
| Q24 | `OtClientProcedureRepository.ListAsync` (bandeja OT por grants) | `Guid otTenantId` | ✔ (solo C1 con grant) | — | — |
| Q25 | `OtMetricsReadRepository.ListClientCompaniesAsync` (grants) | `Guid otTenantId` | ✔ | — | — |
| Q26 | `DbTenantScopeResolver.ResolveAsync` | `Guid tenantId` | ✔ (C1→Single; P→Group / Single con interruptor apagado; X→Single; inexistente→fail-closed) | — | — |
| Q27 | `TenantScopeQueryableExtensions.WhereTenantInScope` (ruta nueva) | `TenantScope` | ✔ (Single, Group, inexistente ⇒ 0 filas) | ✔ (≡ Q01) | ✔ (≡ `null`) |
| Q28 | `ProcedureInstanceRepository.ListWithSummaryGraphFilteredAsync` (`TenantScope`, red — HU #12358, `NetworkProceduresReadTests`) | `TenantScope` | ✔ (Group(P) nunca X/S; Single(C1) nunca C2; vacío ⇒ 0; desvínculo ⇒ sin C1) | ✔ (Single(S) ≡ `Guid?` S) | — |
| Q29 | `ProcedureInstanceRepository.CountByStatusFilteredAsync` (`TenantScope`, red — HU #12358) | `TenantScope` | ✔ (vacío ⇒ 0) | — | — |
| Q30 | `ProcedureInstanceRepository.GetByIdWithDetailsAsync` (`TenantScope`, detalle de red — HU #12358) | `TenantScope` | ✔ (X ⇒ not_found; vacío ⇒ null; desvínculo ⇒ not_found) | ✔ (≡ detalle propio del hijo) | — |
| Q31 | `ProcedureInstanceRepository.ListTenantIdsWithMatchesAsync` (`TenantScope`, hijos alcanzados por las estadísticas — HU #12361, `NetworkAccessAuditTests`) | `TenantScope` | ✔ (Group(P) ⇒ P, C1, C2; acotado a C2 ⇒ solo C2) | — | — |
| Q32 | `NetworkAccessAuditReader.SearchAsync` (auditoría del acceso consolidado: consulta del hijo y del SuperAdmin — HU #12361) | `NetworkAccessAuditQuery.TenantId` | ✔ (C1 solo ve accesos que lo alcanzaron; C2 y X no ven los de C1; sobrevive al desvínculo) | — | ✔ (`TenantId = null` ⇒ toda la plataforma, paginado y acotado a 200) |
| Q33 | `AnalyticsNetworkReadRepository.GetNetworkOverviewAsync` (red, `= ANY(@tenants)` `uuid[]` — HU #12359, `NetworkAnalyticsTests`) | `IReadOnlySet<Guid> tenantIds` | ✔ (Group(P) = P+C1+C2 exacto, nunca X/S; {C1} = solo C1; vacío ⇒ 0 sin ir a la base) | ✔ ({S} ≡ `Guid?` S) | — (sin modo global por diseño) |
| Q34 | `AnalyticsNetworkReadRepository.GetNetworkTopProducersAsync` (red, `uuid[]` — HU #12359) | `IReadOnlySet<Guid> tenantIds` | ✔ (Group(P) = gestores de P, C1, C2; {C1} = solo C1; vacío ⇒ 0) | ✔ ({S} ≡ `Guid?` S) | — |
| Q35 | `AnalyticsNetworkReadRepository.GetNetworkMonthlyTrendAsync` (red, `uuid[]` — HU #12359) | `IReadOnlySet<Guid> tenantIds` | ✔ (Group(P) = suma por año/mes/categoría, un punto por clave; {C1} = solo C1; vacío ⇒ 0) | ✔ ({S} ≡ `Guid?` S) | — |
| Q36 | `DetailedReportNetworkReadRepository.GetNetworkProceduresAsync` (reporte de red sobre `analytics.v_procedure_detail_report`, `= ANY(@tenants)` `uuid[]` — HU #12360, `NetworkReportsTests`) | `NetworkDetailedReportFilter.TenantIds` | ✔ (Group(P) = filas de P+C1+C2 con su cliente, nunca X/S; {C1} = solo C1 con totales solo de C1; vacío ⇒ 0 sin ir a la base; el filtro nunca amplía) | ✔ ({C1} ≡ `DetailedReportFilter` C1; {S} ≡ ruta vieja S) | — (sin modo global por diseño) |
| Q37 | `DetailedReportNetworkReadRepository.ExportNetworkProceduresAsync` (exportación de red, mismo predicado que Q36 — HU #12360) | `NetworkDetailedReportFilter.TenantIds` | ✔ (exportado ≡ listado con el mismo filtro resuelto, ids y conteo; sin X/S; vacío ⇒ solo cabecera; sin documentos ni enlaces) | — | — |
| Q38 | `NetworkAttachmentsHandler.ListAsync` (documentos de la red: dueño por `ProcedureInstanceOwnerLookup` + `CanRead`, luego `ListAttachmentsHandler` con el tenant del dueño — HU #12410, `NetworkAttachmentsTests`) | `TenantScope` | ✔ (MARCA_BLANCA(P) lee C1/C2, nunca X; X/inexistente ⇒ `not_found` idéntico; Single ⇒ `network_scope_required`; CONCESION ⇒ `network_documents_disabled` salvo interruptor; sin URL ni `storagePath`) | ✔ (rutas viejas de S intactas) | — (sin modo global por diseño) |
| Q39 | `NetworkAttachmentsHandler.DownloadAsync` (descarga proxeada por transmisión con el tenant del dueño — HU #12410) | `TenantScope` | ✔ (bytes del binario de C1; documento inexistente / binario perdido / trámite ajeno ⇒ el mismo `not_found`; auditoría exactamente una fila por acceso, sobrevive al desvínculo) | ✔ (`DownloadAttachmentHandler` S ≡ hoy) | — |
| Q40 | `ResolvePublicBrandingHandler.HandleAsync` (`GET /public/branding`, dominio → `BrandIdentity` — HU #12429, `MarcaBlanca/BrandParityTests`, `BrandingFallbackTests`, `CrossNetworkIsolationTests`) | `bool isNetworkDomain, Guid? headTenantId` | ✔ (A nunca ve la marca de B ni viceversa; sin red ⇒ FLIT byte a byte; fallo de BD/lookup ⇒ FLIT, nunca 500) | — | — |
| Q41 | `DbEmailThemeResolver.ResolveAsync` (tema de correo por clase del tenant — HU #12429, `MarcaBlanca/BrandParityTests`, `EmailThemeByClassTests`, `BrandingFallbackTests`) | `Guid? tenantId` | ✔ (hija de MARCA_BLANCA ⇒ tema de su red; hija de Concesión / sin red ⇒ FLIT; fallo de BD ⇒ FLIT sin propagar) | ✔ (Concesión/sin red ≡ `EmailTheme.Flit` de antes de la épica) | — |
| Q42 | `POST /api/v1/auth/login` (`LoginHandler`, anti-enumeración por dominio, HTTP real con `WebApplicationFactory<Program>` + `X-Flit-Domain` — HU #12429, `MarcaBlanca/LoginAntiEnumerationTests`, `CrossNetworkIsolationTests`) | sello `X-Flit-Domain` + email/password | ✔ (credencial incorrecta, usuario de otra red y correo inexistente ⇒ mismo cuerpo/código/tiempo; usuario de A nunca entra por B ni directo por FLIT) | — | — |
| Q43 | `POST /api/v1/auth/forgot-password` + `GET /public/branding` negativos (`ForgotPasswordHandler`/`ResolvePublicBrandingHandler`, HTTP real — HU #12429, `MarcaBlanca/RecoveryAndBrandingAntiEnumerationTests`) | sello `X-Flit-Domain` + email | ✔ (usuario de la red, de otra red y correo inexistente ⇒ misma respuesta 202; dominio inexistente/verificado-no-activo/cabeza inactiva ⇒ mismo cuerpo FLIT y código 200) | — | — |
| Q44 | `MarcaBlancaHeadCompanyAuthorizationHandler` (policy `MarcaBlancaHeadCompany` de `/company/branding*` y `/company/domain*` — HU #12429, endurecimiento del hecho 88, `Flit.Admin.Tests/Authorization/MarcaBlancaHeadCompanyAuthorizationHandlerTests`) | `ClaimsPrincipal` + `ICompanyHierarchyRepository.GetHierarchyInfoAsync` | ✔ (Concesión con hijas: 403 en marca/dominio, sigue 200 en `/company/children`; MARCA_BLANCA: 200) | — | — |
| Q45 | `ProcedureInstanceRepository.ListBiometricValidationsGroupedByPersonAsync` (grilla por persona de Validación de Identidad, SQL crudo con `= ANY(uuid[])` y la persona = compañía + documento — HU #12706/#12708, `Tramites/IdentityValidationScopeTests`) | `TenantScope` | ✔ (`Single(B)` solo B; red `Group(A,[B])` lee A y B, nunca C; la misma cédula en A y B son dos filas con su historial) | ✔ (la firma por `Guid` ≡ `Single`, mismas filas) | ✔ (`All`: las tres compañías con su `TenantId`) |
| Q46 | `ProcedureInstanceRepository.CountBiometricPersonsByEstadoAsync` (KPIs de la grilla, mismo CTE que Q45) | `TenantScope` | ✔ (`Single(B)` = 1 persona; red = 3) | ✔ | ✔ (`All` suma las personas de cada compañía) |
| Q47 | `ProcedureInstanceRepository.ListBiometricValidationsByTenantAsync` (listado plano: Dashboard y submódulo) | `TenantScope` | ✔ (`Single(C)` solo C) | ✔ (firma por `Guid` sin cambios para alertas) | ✔ (`All`: 5 validaciones de 3 compañías) |
| Q48 | `ProcedureInstanceRepository.CountBiometricValidationsByEstadoAsync` (KPIs del listado plano) | `TenantScope` | ✔ | ✔ | ✔ (`All` = 5) |
| Q49 | `IdentityValidationOutboxRepository.ListStuckAsync` (atascadas: outbox de completado + cola de envío) | `TenantScope` | ✔ (`Single(A)` solo A) | ✔ (firma por `Guid` ≡ `Single`) | ✔ (`All`: A y C con su `TenantId`) |

**Demostración de que la suite detecta fugas:** `TenantLeakTests.El_helper_de_fuga_detecta_filas_ajenas` ejecuta
`WhereTenantInScope(TenantScope.All)` sobre el escenario (10 filas de 5 clientes) y afirma que `LeakAssert` lanza
`LeakDetectedException` nombrando las 8 filas ajenas a C1 con su id y su tenant.

**Snapshot de contratos (AC1):** `TenantParityTests.Los_DTOs_de_respuesta_no_incorporan_campos_nuevos` fija por
reflexión las propiedades públicas de 18 DTOs de respuesta de las consultas cubiertas; `ParentTenantId` /
`IsGroupParent` solo existen en la entidad `Tenant`.

### No cubiertas (y por qué)

| Consulta | Motivo |
|----------|--------|
| `ImprontaRepository.ListAsync` | Vista global cross-tenant **por diseño** (ADR-0022, exclusiva SuperAdmin); el filtro no recibe tenant. |
| `GET /api/v1/security/users` (listado de usuarios) | LINQ inline en `SecurityEndpoints.cs` sin repositorio: solo ejercitable con `WebApplicationFactory` (Flit.Admin.Tests). |
| `OtMetricsReadRepository.GetOperationalPanelAsync` / `GetPerformanceAsync` / `GetReportAsync` / … | Mismo `OtTenantScope` por grants que Q25 (`ListClientCompaniesAsync`); métricas de organismo, no de cliente. |
| `OtQueryRepository.*`, `OtClientProcedureRepository.GetByIdAsync` y acciones | Mismo `ExecuteOtScopedAsync` + `BuildAccessibleQuery` que Q24. |
| `MandateConfigAdminService`, `OtProfileRepository.GetByTenantAsync`, `OtRequirementsRepository.GetByTenantAsync` | Configuración por tenant con `FirstOrDefault(t.TenantId == tenantId)`: sin listado que pueda fugar; se cubren por `Flit.Infrastructure.Tests` (InMemory). |
| Filtros HTTP (`CompanyOwnTenantFilter`, `TenantEnforcementMiddleware`) | Capa Flit.Api: cubiertos por `Flit.Admin.Tests` (`TenantEnforcementMiddlewareScopeTests`, `RequestTenantResolverTests`). |

## Cobertura #12406 — clase de la cabeza como tipo de compañía y padre conservado en el trámite

Suites en `Hierarchy/` (todas heredan de `PostgresTestBase`; el escenario `P` de `HierarchyScenario` nace
con `tenant_type = CONCESION`, porque desde el DDL 109 `ck_tenants_group_parent_by_type` acopla
`is_group_parent` al tipo y `TenantSeed.New` lo respeta por defecto):

| Suite | Qué fuerza el motor / el producto |
|-------|-----------------------------------|
| `HeadTenantTypeConstraintsTests` | Catálogo de cinco tipos (`ck_tenants_tenant_type`), acoplamiento `is_group_parent = tipo es de cabeza` sin corrección silenciosa, rama (d) del trigger (clase inmutable con hijos vigentes) y vigencia de (a)(b)(c). |
| `HeadTenantTypeCompanyHandlersTests` | `CreateCompanyHandler` / `UpdateCompanyHandler` reales: alta con `CONCESION` / `MARCA_BLANCA` nace marcada cabeza; cambio de tipo sin hijos aceptado y auditado (`admin.tenant_config_audit_logs`, old/new); con hijos → 422 explícito y nada escrito; paridad de un cliente `RENTING`; el alcance de la cabeza expone su clase. |
| `ParentSnapshotTests` | `parent_tenant_id_at_creation` escrito por los dos puntos de creación (handler de trámites y repositorio administrativo), inmutable por EF (`AfterSaveBehavior.Throw`), por `db.Update` y por SQL directo (trigger); el desvínculo o cambio de cabeza no lo altera; paridad del cliente aislado; barrido estático de asignaciones. |
| `HeadTenantTypeMigrationTests` | Migración `20260910140000_HU12406_HeadTenantTypesAndParentSnapshot`: Down/Up sin poblado, DDL idempotente, precondición (cabeza marcada sin tipo de cabeza) y Down con cabeza declarada fallan completos sin dejar nada a medias. |
