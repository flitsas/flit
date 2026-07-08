# Contratos compartidos — Reportes 2.0 (HU-A/B/C/D)

> **Este documento es NORMATIVO** para los 4 agentes que desarrollan en paralelo.
> Cualquier desviación de nombres de tablas, DTOs, rutas, slugs o propiedad de archivos
> rompe la integración. Si algo no está definido aquí, sigue las convenciones del repo
> (ver `docs/contexto-funcional-flit.md`).
>
> Fecha: 2026-07-07 · Base: rama `feature/jcopete-metricas-20260607`.

---

## 0. Hechos verificados del repo (NO asumas lo contrario)

- Slugs RBAC usan **notación de punto** (`reportes.read`), NO dos puntos. Sembrados en
  `Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs` (módulos: `dashboard, tramites,
  reportes, validaciones, usuarios, rbac, improntas`).
- Estados N03 (en español, ya persistidos en `procedure_instances.status` y
  `procedure_instance_status_history.to_status`): `borrador | anulado | preparado | entregado |
  aprobado | rechazado`. `Reason` es texto libre (varchar 500). Columnas de history:
  `from_status, to_status, changed_at, changed_by, reason, metadata (jsonb)`.
- `admin.ot_api_call_logs` NO tiene columna `provider`: usa `endpoint`, `direction`,
  `http_method`, `response_code (short?)`, `duration_ms (int?)`, `called_at`, `error_message`.
- `tramites.procedure_instance_attachments` NO tiene soft-delete ni `replaced_by`.
  **"Documento reemplazado" se define como**: filas adicionales con el mismo
  (`procedure_instance_id`, `tipo`) más allá de la primera (orden por `uploaded_at`).
- Snake_case en BD es **automático** (`UseSnakeCaseNamingConvention`). Configs EF via
  `IEntityTypeConfiguration` en `Flit.Infrastructure/Persistence/Configurations/<Área>/`,
  aplicadas por `ApplyConfigurationsFromAssembly` (no se toca `FlitDbContext`).
- RLS se aplica con SQL crudo embebido: archivo en
  `Flit.Infrastructure/Persistence/Sql/Ddl/NN-nombre.sql` + patrón
  `CREATE POLICY tenant_isolation ... USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)`.
- Tests backend: xUnit v3 + FluentAssertions + NSubstitute; EF **InMemory** (sin Postgres real);
  auth-integration con `WebApplicationFactory<Program>` y `Database__AutoMigrate=false`.
- Serialización JSON de la API: camelCase (default ASP.NET). DTOs = records C# PascalCase.
- Front: Tailwind v4 (sin shadcn), `cn` en `lib/utils.ts`, Recharts ^2.15, patrón de tabs =
  `components/admin/transit-offices/OtTabBar.tsx`, estados UI = `components/admin/UiStateBoundary.tsx`,
  polling = patrón `BiometricStep.tsx:115-125` (`setInterval` + cleanup).
- Cliente HTTP analytics: `apiFetch` de `lib/api/client.ts` (query params; tenantId por query;
  fechas `YYYY-MM-DD`), descargas via `lib/api/download.ts`.
- Zona horaria de negocio: **America/Bogota** (agrupaciones por día/hora la usan:
  `occurred_at AT TIME ZONE 'America/Bogota'`).

---

## 1. Migraciones EF — regla especial para la paralelización

**NINGÚN agente ejecuta `dotnet ef migrations add`** (el ModelSnapshot colisionaría entre
worktrees). Cada HU con tablas nuevas entrega:

1. Entidad + `IEntityTypeConfiguration` (para que el modelo EF y los tests InMemory funcionen).
2. El archivo DDL en `Persistence/Sql/Ddl/` (tabla + índices + RLS + trigger de auditoría si aplica).

Las migraciones se generan **en la fase de integración** (una por HU):
- HU-A → migración `HU10621_AppUsageEvents` + DDL `30-HU10621-app-usage-events.sql`.
- HU-D → migración `HU10624_AnalyticsSchedulesAlerts` + DDL `31-HU10624-analytics-schedules-alerts.sql`.

---

## 2. Propiedad de archivos por HU (regla anti-conflictos)

Regla general: **no toques archivos de otra HU**. Archivos compartidos permitidos:

| Archivo | Quién puede tocarlo | Cómo |
|---|---|---|
| `Flit.Api/Program.cs` | A, B, D | Máx. 2 líneas cada uno (1 DI/middleware + 1 `MapXxx`), cada línea con comentario `// Reportes2 HU-X` |
| `Flit.Infrastructure/InfrastructureExtensions.cs` | A, B, D | Líneas de registro DI contiguas al bloque analytics (74-76), comentadas `// Reportes2 HU-X` |
| `Flit.Analytics.Application/AnalyticsApplicationServiceCollectionExtensions.cs` | **solo B** | HU-D crea su propio `AnalyticsSchedulingServiceCollectionExtensions.cs` |
| `frontend/components/atom/modules/Reportes.tsx` | **solo C** | HU-D entrega su panel standalone; se monta en integración |
| `frontend/components/operacion/TramiteWizard.tsx` | **solo A** | — |
| `frontend/lib/api/types.ts` | **nadie** | tipos nuevos van en archivos nuevos |
| `contracts/openapi/core-api.v1.yaml` | **nadie** | se actualiza en integración |
| `AnalyticsReadRepository.cs` / `AnalyticsEndpoints.cs` existentes | **nadie** | los endpoints nuevos van en archivos nuevos |

### Archivos "verbatim compartidos" (HU-A y HU-B crean AMBOS el archivo, byte-idéntico, §6)
- `Flit.Analytics.Application/Abstractions/IUsageMetricsReadRepository.cs`
- `Flit.Analytics.Application/Abstractions/UsageMetricsDtos.cs`

Git los auto-fusiona si son idénticos. **Copia el contenido de §6 EXACTAMENTE.**

### Mapa completo

**HU-A (telemetría):**
- `Flit.Infrastructure/Persistence/Entities/Analytics/AppUsageEvent.cs`
- `Flit.Infrastructure/Persistence/Configurations/Analytics/AppUsageEventConfiguration.cs`
- `Flit.Infrastructure/Persistence/Sql/Ddl/30-HU10621-app-usage-events.sql`
- `Flit.Infrastructure/Telemetry/` (cola en memoria, writer BackgroundService, options, retención)
- `Flit.Infrastructure/Persistence/Repositories/UsageMetricsReadRepository.cs`
- `Flit.Api/Middleware/UsageTelemetryMiddleware.cs`
- `Flit.Api/Endpoints/Analytics/UsageEventsEndpoints.cs`
- Verbatim §6 · Front: `frontend/lib/telemetry.ts`, `frontend/hooks/useWizardTelemetry.ts`,
  edición de `TramiteWizard.tsx`
- Tests: `Flit.Infrastructure.Tests/Telemetry/*`, vitest `frontend/lib/__tests__/telemetry.test.ts`

**HU-B (métricas read-side):**
- `Flit.Analytics.Application/Abstractions/IAnalyticsMetricsReadRepository.cs` (+ DTOs propios en `Flit.Analytics.Application/Queries/Metrics/`)
- `Flit.Analytics.Application/Queries/Metrics/` (handlers: OtMetrics, Funnel, Usage, LiveOverview)
- `Flit.Infrastructure/Persistence/Repositories/AnalyticsMetricsReadRepository.cs`
- `Flit.Api/Endpoints/Analytics/AnalyticsMetricsEndpoints.cs`
- `AnalyticsApplicationServiceCollectionExtensions.cs` (agrega registros)
- Verbatim §6 · Tests: `Flit.Analytics.Application.Tests/Metrics/*`, `Flit.Admin.Tests/Analytics/*`

**HU-C (frontend pestañas):**
- `frontend/components/atom/modules/Reportes.tsx` (reestructura)
- `frontend/components/atom/modules/_reportes/**` (nuevos: TabBar, pestañas, hooks, KPICard,
  FunnelChart, Heatmap, LiveNowPanel, filtros globales…) — sin tocar `scheduling/` (HU-D)
- `frontend/lib/api/analytics-v2.ts` (cliente + tipos TS de los 4 endpoints nuevos, según §4)
- Tests vitest `frontend/__tests__/reportes-*.test.tsx` (puede editar los 3 existentes de reportes)

**HU-D (schedules + alertas):**
- `Flit.Infrastructure/Persistence/Entities/Analytics/{ReportSchedule,AlertRule,AlertEvent}.cs` + Configurations
- `Flit.Infrastructure/Persistence/Sql/Ddl/31-HU10624-analytics-schedules-alerts.sql`
- `Flit.Infrastructure/Analytics/Scheduling/` (AnalyticsSchedulerProcessor, evaluadores,
  `IAlertMetricsReadRepository` impl con SQL propio)
- `Flit.Analytics.Application/Scheduling/` (handlers CRUD, DTOs, `AnalyticsSchedulingServiceCollectionExtensions.cs`)
- `Flit.Api/Endpoints/Analytics/{ReportSchedulesEndpoints,AlertRulesEndpoints}.cs`
- Front standalone: `frontend/components/atom/modules/_reportes/scheduling/**`,
  `frontend/lib/api/analytics-scheduling.ts`
- Tests: `Flit.Analytics.Application.Tests/Scheduling/*`, `Flit.Infrastructure.Tests/Scheduling/*`,
  vitest `frontend/__tests__/reportes-scheduling.test.tsx`

---

## 3. RBAC — slugs nuevos (módulo `reportes`)

| Slug | Protege |
|---|---|
| `reportes.resumen.read` | Pestaña Resumen general |
| `reportes.operacion.read` | Pestaña Operación/Trámites |
| `reportes.ot.read` | Pestaña Organismo de Tránsito |
| `reportes.uso.read` | Pestaña Uso del aplicativo |
| `reportes.productividad.read` | Pestaña Productividad |
| `reportes.programacion.manage` | Botón/panel "Programación y alertas" (HU-D) |

Reglas front (HU-C/HU-D): pestaña visible si `isSuperAdmin || permissions.includes(slug)`;
**compatibilidad**: `reportes.read` (legado) hace visible al menos "Resumen general".
Si el usuario no tiene ninguna pestaña visible → estado vacío amable.
Backend: los GET de métricas mantienen `RequireAuthorization()` a nivel de grupo (decisión
vigente documentada en `AnalyticsEndpoints.cs`: lectura abierta a autenticados del tenant);
export y CRUD de programación exigen `AdminAuthorization.AdminCompanyPolicy`.
El seed de los slugs se hace en integración (DevelopmentAuthSeeder) — los agentes NO lo tocan.

---

## 4. Endpoints nuevos — rutas, parámetros y DTOs

Todos bajo el grupo existente `/api/v1/analytics`. Resolución de tenant idéntica a
`AnalyticsEndpoints.TryResolveEffectiveTenant` (claim `tenant_id`; SuperAdmin puede pasar
`?tenantId=`; para estos endpoints de métricas el SuperAdmin **debe** indicar `tenantId`
→ si no, 400 con detalle en español — NO hay vista global en los endpoints nuevos).
Errores: 400 `invalid_range` mismo mensaje que los existentes.

### 4.1 Filtros comunes (query, todos opcionales salvo from/to)

```
from=YYYY-MM-DD (req) · to=YYYY-MM-DD (req) · tenantId=guid (solo SuperAdmin)
transitOfficeId=guid · procedureTypeId=guid · operatorUserId=guid
status=<estado N03> · reason=<substring causal, case-insensitive>
compareWith=previous_period|previous_year (ausente = sin comparación)
stuckDays=int (default 7, rango 1..90) — solo ot-metrics y live-overview
```

`compareWith`: `previous_period` = ventana de la misma duración inmediatamente anterior a
`from`; `previous_year` = mismas fechas un año atrás. Respuesta comparada envuelve:
`{"current": <T>, "previous": <T> | null, "comparison": {"mode": "...", "from": "...", "to": "..."} | null}`.
La variación % la calcula el frontend con un único helper (§5); el backend NO manda deltas.

### 4.2 `GET /analytics/ot-metrics` → `OtMetricsResponse`

```jsonc
{
  "current": {
    "summary": {
      "entregados": 120, "aprobados": 90, "rechazados": 18,
      "rejectionRatePct": 16.7,            // rechazados / (aprobados+rechazados) * 100
      "avgApprovalHours": 52.4, "p50ApprovalHours": 41.0, "p90ApprovalHours": 130.0,
      "reincidencePct": 61.1,              // % de rechazados que volvieron a borrador
      "stuckCount": 7
    },
    "rejectionByOffice": [ { "transitOfficeId": "…", "transitOfficeName": "…",
        "entregados": 40, "aprobados": 30, "rechazados": 8, "rejectionRatePct": 21.1 } ],
    "rejectionByReason": [ { "reason": "Documento ilegible", "count": 9, "pct": 50.0 } ],
    "rejectionByType":   [ { "procedureTypeId": "…", "procedureTypeName": "…",
        "entregados": 60, "rechazados": 10, "rejectionRatePct": 14.3 } ],
    "approvalTimesByOffice": [ { "transitOfficeId": "…", "transitOfficeName": "…",
        "decididos": 38, "avgHours": 50.1, "p50Hours": 40.0, "p90Hours": 120.5 } ],
    "officeRanking": [ { "transitOfficeId": "…", "transitOfficeName": "…", "rank": 1,
        "p50Hours": 24.0, "rejectionRatePct": 5.0, "volumen": 40 } ],
    "reincidence": { "rechazadas": 18, "reintentadas": 11, "avgCiclos": 1.4, "maxCiclos": 3 },
    "stuck": { "totalCount": 7, "items": [ { "instanceId": "…", "referenceNumber": "…",
        "status": "entregado", "daysInStatus": 12.3, "transitOfficeName": "…",
        "procedureTypeName": "…", "createdByDisplayName": "…" } ] }   // items: top 50 por días
  },
  "previous": { /* mismo shape o null */ },
  "comparison": { "mode": "previous_period", "from": "…", "to": "…" }
}
```

Semántica (todas sobre `procedure_instance_status_history` + instancias, tenant-filtradas):
- *entregados/aprobados/rechazados*: nº de transiciones a ese `to_status` en el rango (por `changed_at`).
- *Tiempo de aprobación*: horas entre la ÚLTIMA transición a `entregado` y la siguiente
  transición a `aprobado`/`rechazado` de la misma instancia, decidida dentro del rango.
- *Reincidencia*: instancia con transición `rechazado→borrador`; ciclos = nº de veces que la
  instancia pasó por `rechazado`.
- *Ranking agilidad*: orden por `p50Hours` asc, desempate `rejectionRatePct` asc; solo OTs con ≥1 decidido.
- *Atascados (stuck)*: instancias cuyo estado actual NO es final (`aprobado`/`anulado`) y llevan
  > `stuckDays` días desde su última transición (o creación si no hay transiciones).
- El filtro `reason` aplica `ILIKE %…%` sobre `reason` de la transición a `rechazado`.

### 4.3 `GET /analytics/funnel` → `FunnelResponse`

```jsonc
{
  "current": {
    "states": [   // instancias DISTINTAS que ALCANZARON cada etapa dentro del rango (por created_at de la instancia)
      { "stage": "borrador",  "count": 200, "pctOfFirst": 100.0, "pctOfPrev": 100.0 },
      { "stage": "preparado", "count": 150, "pctOfFirst": 75.0,  "pctOfPrev": 75.0 },
      { "stage": "entregado", "count": 120, "pctOfFirst": 60.0,  "pctOfPrev": 80.0 },
      { "stage": "aprobado",  "count": 90,  "pctOfFirst": 45.0,  "pctOfPrev": 75.0 }
    ],
    "anulados": 12, "rechazadosVigentes": 18,   // rechazados cuyo estado ACTUAL es rechazado
    "wizardSteps": [ /* WizardStepMetric[] (§6) o [] si no hay telemetría */ ]
  },
  "previous": null, "comparison": null
}
```

### 4.4 `GET /analytics/usage` → `UsageResponse`

```jsonc
{
  "current": {
    "moduleUsage":   [ { "module": "tramites", "events": 500, "uniqueUsers": 12 } ],
    "wizardSteps":   [ /* WizardStepMetric[] §6 */ ],
    "peakHours":     [ { "dayOfWeek": 1, "hour": 9, "events": 87 } ],  // 0=domingo … 6=sábado, hora Bogotá
    "documentReplacements": [ { "documentTipo": "cedula", "uploads": 40, "replacements": 12 } ],
    "externalApis":  [ { "endpoint": "…", "direction": "outbound", "calls": 300, "errors": 12,
                         "errorRatePct": 4.0, "avgDurationMs": 420.5, "p90DurationMs": 900.0 } ],
    "avgWizardDurationMs": 1860000.0, "medianWizardDurationMs": 1200000.0   // wizard completo; null si sin datos
  },
  "previous": null, "comparison": null
}
```
- `moduleUsage/wizardSteps/peakHours` salen de `analytics.app_usage_events` vía
  `IUsageMetricsReadRepository` (§6). `documentReplacements` de attachments (regla §0).
  `externalApis` de `admin.ot_api_call_logs` (`errors` = `response_code >= 400 OR error_message IS NOT NULL`).

### 4.5 `GET /analytics/live-overview` → `LiveOverviewResponse` (SIN compareWith, < 300 ms)

```jsonc
{
  "generatedAt": "2026-07-07T14:03:22Z",
  "today": {                                    // "hoy" en America/Bogota
    "creados": 14,
    "byStatus": [ { "status": "borrador", "count": 6 } ],   // estado ACTUAL de instancias activas (no finales) del tenant
    "entregados": 5, "aprobados": 3, "rechazados": 1        // transiciones de HOY
  },
  "stuckCount": 7,
  "pendingIdentityValidations": 3,              // biometric_validations con estado pendiente/en proceso
  "integrationsLastHour": { "calls": 25, "errors": 1, "avgDurationMs": 350.0 },
  "lastActivityAt": "2026-07-07T13:59:01Z"      // último cambio de estado o evento; null si nada
}
```
Acepta `stuckDays` y `tenantId` (SuperAdmin). Una sola ronda de queries (batch en una conexión).

### 4.6 `POST /analytics/events` (HU-A) — batch de telemetría del front

Request (auth requerida; tenant y userId SIEMPRE del JWT, nunca del body; máx 50 eventos;
payload > 50 → 400):
```jsonc
{ "events": [ {
    "eventType": "wizard_step_view",      // §7 taxonomía
    "module": "tramites",                  // opcional
    "stepKey": "comprador",                // opcional
    "procedureInstanceId": "guid|null",
    "durationMs": 12500,                   // opcional, int >= 0
    "occurredAt": "2026-07-07T13:59:01Z",  // opcional; default now() del servidor
    "metadata": { }                        // opcional; jsonb; SIN PII (no nombres/documentos/emails)
} ] }
```
Response: **202** `{ "accepted": 3 }`. Nunca 5xx por errores de persistencia (log + 202).
Eventos con `eventType` fuera de la taxonomía se descartan silenciosamente (se cuenta solo lo aceptado).

### 4.7 CRUD HU-D (policy `AdminCompanyPolicy` en TODOS; tenant del JWT; SuperAdmin con `?tenantId=`)

```
GET    /analytics/report-schedules            → { "items": ReportScheduleDto[] }
POST   /analytics/report-schedules            → 201 ReportScheduleDto
PUT    /analytics/report-schedules/{id}       → 200 ReportScheduleDto
DELETE /analytics/report-schedules/{id}       → 204
GET    /analytics/alert-rules                 → { "items": AlertRuleDto[] }
POST   /analytics/alert-rules                 → 201 AlertRuleDto
PUT    /analytics/alert-rules/{id}            → 200 AlertRuleDto
DELETE /analytics/alert-rules/{id}            → 204
GET    /analytics/alert-events?ruleId=&page=&pageSize=  → { "items": AlertEventDto[], "totalCount": n }
```

```jsonc
// ReportScheduleDto
{ "id": "…", "name": "Informe semanal OT", "reportType": "ot",   // resumen|operacion|ot|uso|productividad
  "frequency": "weekly",                                          // daily|weekly|monthly
  "dayOfWeek": 1, "dayOfMonth": null,                             // weekly→dayOfWeek 0-6; monthly→dayOfMonth 1-28
  "sendHour": 7,                                                  // 0-23 hora Bogotá
  "format": "pdf",                                                // excel|pdf
  "recipients": ["gerencia@empresa.co"],                          // 1..10 emails válidos
  "isActive": true, "lastSentAt": null }

// AlertRuleDto
{ "id": "…", "name": "Rechazo OT alto",
  "metric": "rejection_rate_pct",   // rejection_rate_pct|stuck_count|external_api_errors|pending_identity_validations
  "operator": "gt",                 // gt|gte|lt|lte
  "threshold": 25.0,
  "windowMinutes": 1440,            // ventana de evaluación de la métrica
  "cooldownMinutes": 240,           // no re-disparar dentro del cooldown (default 240)
  "recipients": ["…"], "isActive": true, "lastTriggeredAt": null }

// AlertEventDto
{ "id": "…", "alertRuleId": "…", "ruleName": "…", "triggeredAt": "…",
  "metricValue": 31.2, "threshold": 25.0, "notified": true, "message": "…" }
```
Validaciones → 400 con detalle en español. `PUT`/`DELETE` de otro tenant → 404.

---

## 5. Contrato TypeScript (HU-C crea `frontend/lib/api/analytics-v2.ts`)

Tipos espejo camelCase de §4 (`OtMetricsResponse`, `FunnelResponse`, `UsageResponse`,
`LiveOverviewResponse`, `Compared<T> = { current: T; previous: T | null; comparison: {…} | null }`)
y funciones:
```ts
fetchOtMetrics(params: MetricsParams, signal?): Promise<OtMetricsResponse>
fetchFunnel(params: MetricsParams, signal?): Promise<FunnelResponse>
fetchUsageMetrics(params: MetricsParams, signal?): Promise<UsageResponse>
fetchLiveOverview(params: LiveOverviewParams, signal?): Promise<LiveOverviewResponse>
// MetricsParams = { from, to, tenantId?, transitOfficeId?, procedureTypeId?, operatorUserId?,
//                   status?, reason?, compareWith?, stuckDays? }
// helper compartido de variación: variationPct(current, previous): number | null
//   (null si previous null/0; redondeo a 1 decimal; usado por TODOS los KPIs)
```
HU-D crea el equivalente `analytics-scheduling.ts` con los DTOs de §4.7.

---

## 6. Archivos VERBATIM compartidos (HU-A y HU-B: crear byte-idéntico)

### `services/core-api/src/Flit.Analytics.Application/Abstractions/UsageMetricsDtos.cs`

```csharp
namespace Flit.Analytics.Application.Abstractions;

/// <summary>Métrica agregada de un paso del wizard (telemetría HU-A, Reportes 2.0).</summary>
public sealed record WizardStepMetricDto(
    string StepKey,
    int Views,
    int Completions,
    double AbandonmentPct,
    double? AvgDurationMs,
    double? MedianDurationMs);

/// <summary>Uso agregado de un módulo del aplicativo.</summary>
public sealed record ModuleUsageDto(string Module, int Events, int UniqueUsers);

/// <summary>Celda del heatmap de horas pico (hora America/Bogota).</summary>
public sealed record PeakHourDto(int DayOfWeek, int Hour, int Events);

/// <summary>Duración total del wizard por instancia completada.</summary>
public sealed record WizardDurationDto(double? AvgDurationMs, double? MedianDurationMs);
```

### `services/core-api/src/Flit.Analytics.Application/Abstractions/IUsageMetricsReadRepository.cs`

```csharp
namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// Lectura agregada de <c>analytics.app_usage_events</c> (telemetría HU-A, Reportes 2.0).
/// Implementada en Flit.Infrastructure (HU-A); consumida por los handlers de métricas (HU-B).
/// Todas las consultas filtran por tenant (RLS + WHERE explícito).
/// </summary>
public interface IUsageMetricsReadRepository
{
    Task<IReadOnlyList<WizardStepMetricDto>> GetWizardStepMetricsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<ModuleUsageDto>> GetModuleUsageAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<PeakHourDto>> GetPeakHoursAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<WizardDurationDto> GetWizardDurationAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

Semántica que la implementación (HU-A) debe cumplir:
- `Views` = eventos `wizard_step_view`; `Completions` = `wizard_step_complete`;
  `AbandonmentPct` = `(1 - completions/views) * 100` (0 si views=0).
- Duraciones desde `duration_ms` de `wizard_step_complete` (por paso) y `wizard_complete` (total).
- `GetModuleUsageAsync` cuenta `module_view` + `api_module_access` agrupado por `module`.
- `GetPeakHoursAsync` sobre TODOS los eventos, hora Bogotá.

---

## 7. Taxonomía de eventos de telemetría (`analytics.app_usage_events`)

### Tabla

```sql
CREATE TABLE analytics.app_usage_events (
    id                     uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id              uuid NOT NULL,
    user_id                uuid NULL,
    event_type             varchar(40)  NOT NULL,
    module                 varchar(40)  NULL,
    step_key               varchar(40)  NULL,
    procedure_instance_id  uuid NULL,
    duration_ms            integer NULL CHECK (duration_ms IS NULL OR duration_ms >= 0),
    metadata               jsonb NOT NULL DEFAULT '{}',
    occurred_at            timestamptz NOT NULL,
    created_at             timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_app_usage_events_tenant_occurred ON analytics.app_usage_events (tenant_id, occurred_at);
CREATE INDEX ix_app_usage_events_tenant_type_occurred ON analytics.app_usage_events (tenant_id, event_type, occurred_at);
CREATE INDEX ix_app_usage_events_instance ON analytics.app_usage_events (procedure_instance_id) WHERE procedure_instance_id IS NOT NULL;
-- + RLS tenant_isolation (patrón §0)
```

### `event_type` (cerrado; el endpoint batch descarta desconocidos)

| event_type | Emisor | module | step_key | duration_ms | Significado |
|---|---|---|---|---|---|
| `module_view` | front | ✔ | — | — | El usuario abrió un módulo del dock |
| `api_module_access` | middleware BE | ✔ | — | — | Request autenticado a un módulo de la API (muestreado: 1 por usuario+módulo+minuto) |
| `wizard_server_view` | middleware BE | `tramites` | — | — | `GET /instances/{id}/wizard` (vista server-driven) |
| `wizard_step_view` | front | `tramites` | ✔ | — | Entrada a un paso del wizard |
| `wizard_step_complete` | front | `tramites` | ✔ | ✔ | Paso completado (avanzó) — duración de permanencia |
| `wizard_step_exit` | front | `tramites` | ✔ | ✔ | Salió del paso sin completarlo (retroceso/salto) |
| `wizard_abandon` | front | `tramites` | ✔ (último) | — | Salida/cancelación explícita del wizard sin radicar |
| `wizard_complete` | front | `tramites` | — | ✔ (total) | Trámite radicado desde el wizard |

Mapeo ruta→módulo del middleware: `/api/v1/tramites/*→tramites`, `/api/v1/analytics/*→reportes`,
`/api/v1/security/*→usuarios`, `/api/v1/admin/*→admin`, `/biometric-validations*→validaciones`.
Sin PII en `metadata` (prohibido: nombres, documentos, emails, placas, VIN).

---

## 8. Tablas HU-D

```sql
CREATE TABLE analytics.report_schedules (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id     uuid NOT NULL,
    name          varchar(120) NOT NULL,
    report_type   varchar(20)  NOT NULL CHECK (report_type IN ('resumen','operacion','ot','uso','productividad')),
    frequency     varchar(10)  NOT NULL CHECK (frequency IN ('daily','weekly','monthly')),
    day_of_week   smallint NULL CHECK (day_of_week BETWEEN 0 AND 6),
    day_of_month  smallint NULL CHECK (day_of_month BETWEEN 1 AND 28),
    send_hour     smallint NOT NULL DEFAULT 7 CHECK (send_hour BETWEEN 0 AND 23),
    format        varchar(10)  NOT NULL CHECK (format IN ('excel','pdf')),
    recipients    jsonb NOT NULL DEFAULT '[]',
    is_active     boolean NOT NULL DEFAULT true,
    last_sent_at  timestamptz NULL,
    created_by    uuid NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NULL,
    deleted_at    timestamptz NULL
);
CREATE TABLE analytics.alert_rules (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id          uuid NOT NULL,
    name               varchar(120) NOT NULL,
    metric             varchar(40) NOT NULL CHECK (metric IN ('rejection_rate_pct','stuck_count','external_api_errors','pending_identity_validations')),
    operator           varchar(4)  NOT NULL CHECK (operator IN ('gt','gte','lt','lte')),
    threshold          numeric(12,2) NOT NULL,
    window_minutes     integer NOT NULL DEFAULT 1440 CHECK (window_minutes BETWEEN 5 AND 43200),
    cooldown_minutes   integer NOT NULL DEFAULT 240  CHECK (cooldown_minutes BETWEEN 5 AND 10080),
    recipients         jsonb NOT NULL DEFAULT '[]',
    is_active          boolean NOT NULL DEFAULT true,
    last_triggered_at  timestamptz NULL,
    created_by         uuid NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NULL,
    deleted_at         timestamptz NULL
);
CREATE TABLE analytics.alert_events (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id      uuid NOT NULL,
    alert_rule_id  uuid NOT NULL REFERENCES analytics.alert_rules(id) ON DELETE CASCADE,
    triggered_at   timestamptz NOT NULL DEFAULT now(),
    metric_value   numeric(12,2) NOT NULL,
    threshold      numeric(12,2) NOT NULL,
    notified       boolean NOT NULL DEFAULT false,
    recipients     jsonb NOT NULL DEFAULT '[]',
    message        text NULL
);
-- índices por tenant + RLS tenant_isolation en las tres (patrón §0)
```

Scheduler (`AnalyticsSchedulerProcessor : BackgroundService`, patrón `IdentityValidationOutboxProcessor`):
- Poll cada 60 s (constante). Hora local **America/Bogota** para `send_hour`/`day_of_*`.
- Schedule "vence" si: activo, es su día/hora local, y `last_sent_at` no es de la ventana actual
  (día para daily, semana ISO para weekly, mes para monthly). Marca `last_sent_at` ANTES de enviar
  (dentro de transacción con `FOR UPDATE SKIP LOCKED`) para tolerar multi-réplica.
- Informe: reutiliza `IProcedureExcelExporter` (excel) / `IExecutiveSummaryPdfGenerator` +
  `IAnalyticsReadRepository` (pdf) con la ventana del periodo vencido; adjunto NO soportado por
  `IEmailSender` (solo HtmlBody) → **el correo lleva el resumen en HTML con los KPIs del periodo**
  y deja el archivo como mejora futura (documentar en ADR). Asunto: `[FLIT] {name} — {periodo}`.
- Alertas: por regla activa, evalúa la métrica en su `window_minutes` (SQL propio de HU-D);
  dispara si el operador se cumple Y `last_triggered_at` es NULL o anterior al cooldown; registra
  `alert_events`, actualiza `last_triggered_at` y envía email a `recipients`.
  Métricas: `rejection_rate_pct` (como §4.2 en la ventana), `stuck_count` (stuckDays=7 fijo),
  `external_api_errors` (count errores en ventana), `pending_identity_validations` (estado actual).

---

## 9. Convenciones transversales

- Mensajes de negocio y validación **en español**; código/identificadores en inglés
  (salvo vocabulario N03 ya en español).
- Toda query nueva: filtro explícito `tenant_id = @tenant` + GUC RLS via patrón
  `ExecuteWithTenantAsync` de `AnalyticsReadRepository` (replicar en repos nuevos).
- Percentiles en SQL: `percentile_cont(0.5) WITHIN GROUP (ORDER BY …)`.
- La telemetría NUNCA lanza al caller: try/catch + log; cola en memoria con
  `System.Threading.Channels` (capacidad 10 000, `BoundedChannelFullMode.DropWrite`).
- Front: textos en español, tooltips "cómo se calcula" en cada métrica (title/aria), skeletons
  con `UiStateBoundary`, `data-testid` estables para tests.
- Auto-refresh HU-C: intervalo configurable 30–60 s (default 45 s), `document.visibilityState`
  pausa el polling, indicador "actualizado hace Xs" y botón pausa.

## 10. Flujo git de cada agente (worktree)

1. Estás en un worktree aislado. Crea tu rama ANTES de tocar nada:
   `git switch -c feature/reportes2-hu-<a|b|c|d>`.
2. Al terminar (build + tests de TU alcance en verde): `git add -A` y UN commit:
   `Feature Reportes2 - HU-<X> <título corto>`.
3. NO hagas merge, NO toques otras ramas, NO edites migraciones existentes ni el ModelSnapshot.
