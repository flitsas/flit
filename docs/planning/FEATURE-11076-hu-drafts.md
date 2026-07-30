# BORRADOR — NO CREADO EN ADO
# Descomposición HUs — Feature #11076 Reportería Transaccional V2

> **Estado:** BORRADOR — PENDIENTE APROBACIÓN HUMANA · NO CREADO EN ADO
> **Fecha:** 2026-07-29 (revisión 2)
> **Autor:** tech-lead-agent Modo B · supervisado por Jorman Copete
> **Feature ADO:** [REPORTERÍA] Subsistema de Reportería Transaccional V2 — #11076
> **Rama base:** `feature/AB-11076-reporteria-transaccional-v2` (basada en `develop` @ `5e05b6f1`)
> **ADRs referenciados:** ADR-0037, ADR-0038, ADR-0039
> **Diseño:** `docs/design/FEATURE-11076-reporteria-transaccional-v2.md`
> **Total HUs:** 16 · **Total SP:** 69 · **Sub-features cubiertos:** G1–G10

---

## ⚠️ Precondición DoR-US global

**Ninguna HU de este listado puede transicionarse a `Active` hasta que el Feature #11076 esté en estado `Active` o `Resolved` en Azure DevOps.**
Esta precondición es parte del criterio DoR-US #1 (Parent Feature Active/Resolved) y aplica a las 16 HUs sin excepción. No crear, no activar, no asignar sprint hasta confirmación del estado del Feature padre.

---

## Convención de títulos

Los títulos siguen el formato canónico `flit-crear-hu`:
```
[CAPA] – Título legible (sin detalle técnico en el título)
```
- **Capa:** `[BACKEND]` para toda la capa de datos, infraestructura y API; `[FRONTEND]` para la capa de presentación.
- HU-01 usa `[BACKEND]` con nota de agente responsable: **database-agent** (owner DDL/migración).
- HU-02 usa `[BACKEND]` con nota de agente responsable: **infra-agent** (owner gateway/config).
- El detalle técnico (nombres de clases, rutas, opciones) va en AC y notas, no en el título.

---

## Índice de HUs

| Clave | Título | Capa | SP | Dep | CFs |
|-------|--------|------|----|-----|-----|
| HU-01 | `[BACKEND] – Schema analytics V2: migración, entidades EF Core y vista v_reporting_tramites` | BACKEND (agente: database-agent) | 5 | — | G1,G3,G4,G9 |
| HU-02 | `[BACKEND] – Gateway YARP: WebSockets y SessionAffinity SignalR` | BACKEND (agente: infra-agent) | 2 | 01 | G10 |
| HU-03 | `[BACKEND] – RBAC seed V2: permisos reporting.* y depreciación detailed-report.*` | BACKEND | 3 | 01 | G5 |
| HU-04 | `[BACKEND] – ExportJobs application: puertos, RequestExport y GetDownloadUrl` | BACKEND | 5 | 01,03 | G1 |
| HU-05 | `[BACKEND] – ExportJobs infrastructure: FileManagerStorage, Worker LISTEN/NOTIFY y SignalR Hub` | BACKEND | 8 | 02,04 | G1,G7 |
| HU-06 | `[BACKEND] – ExportJobs endpoints, wiring SignalR y eliminación definitiva del legado detailed-report` | BACKEND | 3 | 03,05,07 | G1,G8 |
| HU-07 | `[BACKEND] – Reporting procedures: listado V2, detalle y auditoría` | BACKEND | 5 | 01,03 | G2,G3,G6 |
| HU-08 | `[BACKEND] – Reporting analytics: consolidado, productividad y SLA` | BACKEND | 5 | 01,03 | G2 |
| HU-09 | `[BACKEND] – Saved queries CRUD y dashboard preferences` | BACKEND | 3 | 01,03 | G4,G9 |
| HU-10 | `[FRONTEND] – Big-bang: eliminar ReportesDetallados y limpiar dock` | FRONTEND | 2 | 13 | G8 |
| HU-11 | `[FRONTEND] – Cliente V2: API client, ReportFilterContext y SignalR client` | FRONTEND | 5 | 06,07,08,09 | G1,G2,G4 |
| HU-12 | `[FRONTEND] – Reportes.tsx V2: estructura tabs y ExportController` | FRONTEND | 5 | 11 | G1,G7,G8 |
| HU-13 | `[FRONTEND] – Tab Trámites V2: tabla paginada con filtros avanzados` | FRONTEND | 5 | 12 | G2,G1 |
| HU-14 | `[FRONTEND] – Tabs Consolidado y Productividad` | FRONTEND | 5 | 12 | G2 |
| HU-15 | `[FRONTEND] – Tabs SLA y Auditoría con HistoryUnavailableBadge` | FRONTEND | 5 | 12 | G2,G3,G6 |
| HU-16 | `[FRONTEND] – Dashboard preferences UI y Saved queries panel` | FRONTEND | 3 | 11,12 | G9,G4 |

**Total: 69 SP**

---

## Sub-features de referencia (G1–G10)

| Código | Descripción |
|--------|-------------|
| G1 | Export jobs async (tabla durable + worker LISTEN/NOTIFY + FOR UPDATE SKIP LOCKED + SignalR + REST fallback) |
| G2 | Reporting queries V2 (procedures, consolidado, productivity, SLA) |
| G3 | Status history audit trail (ALTER TABLE + columnas role/org_at_time + historyAvailable flag) |
| G4 | Saved queries CRUD por usuario (privadas + is_shared en tenant) |
| G5 | RBAC seed V2 (15 slugs reporting.* + depreciación detailed-report.*) |
| G6 | "Historial no disponible" badge para registros pre-backfill (historyAvailable=false) |
| G7 | Notificaciones (email SMTP al completar job + badge in-app en ExportController) |
| G8 | Navegación big-bang: eliminar reportes-detallados sin redirect (ADR-0038) |
| G9 | Dashboard preferences: mostrar/ocultar/reordenar KPIs sin constructor libre |
| G10 | Gateway/infra: UseWebSockets + cluster SignalR + SessionAffinity (ADR-0039) |

---

## HU-01 · [BACKEND] Schema analytics V2: migración, entidades EF Core y vista v_reporting_tramites

> **Agente responsable:** database-agent (owner DDL/migración)
> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – Schema analytics V2: migración, entidades EF Core y vista v_reporting_tramites`

### Narrativa
Como **database-agent** quiero registrar formalmente la migración y los artefactos de datos de F11076 ya materializados en la rama para que exista ownership y trazabilidad ADO completa de cada decisión de schema antes de que cualquier agente backend consuma las tablas.

> **Detalle técnico (AC/notas):** Migración `20260730022248_F11076_ReportingV2.cs`; DDL 45/46; tablas `analytics.export_jobs`, `analytics.saved_queries`, `analytics.dashboard_preferences`, `analytics.report_sla_config`, `analytics.holiday_calendar`; ALTER TABLE `tramites.procedure_instance_status_history` (3 columnas nullables, PG17 O(1)); vista `analytics.v_reporting_tramites`; trigger NOTIFY; trigger pending_limit; RLS en todas las tablas analytics; umbral `Reporting:MigrationSafety:StatusHistoryRowWarningThreshold` configurable (solo Warning, no bifurca DDL).

### AC Gherkin

```gherkin
# Positivo — migración aplica en ambiente limpio
Dado que la rama tiene la migración 20260730022248_F11076_ReportingV2.cs
Cuando se ejecuta dotnet ef database update
Entonces el schema analytics existe con las 5 tablas nuevas
Y v_reporting_tramites es consultable con campos plate, vin, transit_office_name, company_name, elapsed_hours_total
Y __EFMigrationsHistory contiene "20260730022248_F11076_ReportingV2"

# Positivo — RLS aísla por tenant
Dado que app.current_tenant_id = 'tenant-A'
Cuando se hace SELECT en analytics.export_jobs
Entonces solo se ven filas de tenant-A; las de tenant-B no son visibles

# Positivo — trigger NOTIFY al INSERT
Dado que un listener escucha en 'export_jobs_channel'
Cuando se hace INSERT en analytics.export_jobs con status='pending'
Entonces pg_notify emite el jobId en el canal

# Positivo — trigger pending_limit (advisory)
Dado que un owner_user_id ya tiene 3 jobs en status pending/processing
Cuando se intenta INSERT un cuarto job con el mismo owner
Entonces el trigger lanza EXCEPTION ERRCODE=check_violation con mensaje EXPORT_LIMIT_EXCEEDED

# Negativo — ALTER no destruye datos existentes
Dado que procedure_instance_status_history tiene 100 000 filas pre-migración
Cuando se aplica la migración
Entonces las filas conservan sus valores originales
Y las 3 columnas nuevas son NULL en todos los registros pre-existentes (backfill esperado)

# Borde — umbral de telemetría
Dado que pg_class.reltuples estima 600 000 filas y el umbral es 500 000
Cuando la migración F11076 se aplica
Entonces se emite un Warning de telemetría con el conteo estimado
Y el DDL se aplica sin interrupción (warning solo informativo, no modifica el DDL)
```

### Story Points: 5
### Dependencias: —
### Riesgo: MEDIO — ALTER TABLE en PDN; mitigado PG17 O(1) ADD COLUMN IF NOT EXISTS DEFAULT NULL.

### Scope de archivos
```
services/core-api/src/Flit.Infrastructure/Migrations/20260730022248_F11076_ReportingV2.cs         [YA EXISTE]
services/core-api/src/Flit.Infrastructure/Migrations/20260730022248_F11076_ReportingV2.Designer.cs
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/45-F11076-reporting-v2.sql
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/46-F11076-status-history-audit.sql
services/core-api/src/Flit.Infrastructure/Persistence/Entities/Analytics/  (5 entidades)
services/core-api/src/Flit.Infrastructure/Persistence/Configurations/Analytics/  (5 Fluent API)
services/core-api/src/Flit.Infrastructure/Persistence/FlitDbContext.cs             [MODIFICADO]
services/core-api/src/Flit.Tramites.Domain/Entities/ProcedureInstanceStatusHistory.cs  [MODIFICADO]
services/core-api/src/Flit.Infrastructure/Persistence/Configurations/Tramites/ProcedureInstanceStatusHistoryConfiguration.cs
services/core-api/src/Flit.Infrastructure/Migrations/FlitDbContextModelSnapshot.cs
services/core-api/src/Flit.Infrastructure/Reporting/ReportingMigrationSafetyOptions.cs
services/core-api/src/Flit.Api/Program.cs              [MODIFICADO — safety warning post-migrate]
services/core-api/src/Flit.Api/appsettings.json        [MODIFICADO — umbral configurable]
```

### Sub-features cubiertos: G1, G3, G4, G9

---

## HU-02 · [BACKEND] Gateway YARP: WebSockets y SessionAffinity SignalR

> **Agente responsable:** infra-agent (owner gateway/config)
> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – Gateway YARP: WebSockets y SessionAffinity SignalR`

### Narrativa
Como **infra-agent** quiero habilitar WebSocket en YARP y configurar un cluster dedicado con `SessionAffinity Cookie` para ExportJobsHub, para que los clientes SignalR mantengan afinidad con su réplica y reciban eventos de progreso en tiempo real sin degradarse silenciosamente a long-polling.

> **Detalle técnico:** Agregar `app.UseWebSockets()` antes de `app.MapReverseProxy()` en `Flit.Gateway/Program.cs`. En `appsettings.json`: cluster `core-api-signalr-cluster` con `ActivityTimeout: 00:05:00`; ruta `signalr-route` con `SessionAffinity: { Policy: Cookie, AffinityKeyName: .Flit.SignalR.Affinity, FailurePolicy: Redistribute }`.

### AC Gherkin

```gherkin
# Positivo — handshake WebSocket completa
Dado que Flit.Gateway está corriendo con la configuración actualizada
Cuando un cliente conecta a wss://gateway/hubs/export-jobs con JWT válido
Entonces el handshake completa con HTTP 101 Switching Protocols
Y la conexión usa el cluster core-api-signalr-cluster

# Positivo — cookie de afinidad asignada
Dado que la ruta signalr-route tiene SessionAffinity habilitada
Cuando el cliente establece la primera conexión WebSocket
Entonces la respuesta incluye la cookie .Flit.SignalR.Affinity
Y solicitudes posteriores del cliente llegan a la misma réplica backend

# Negativo — regresión: sin UseWebSockets caía a long-polling
Dado que la versión anterior del gateway no tenía UseWebSockets()
Cuando el cliente intentaba conectar via WebSocket
Entonces la conexión se degradaba silenciosamente a long-polling
Y el comportamiento correcto post-HU es WebSocket real

# Borde — réplica afín caída → FailurePolicy Redistribute
Dado que la réplica afín está caída
Cuando el cliente SignalR intenta reconectarse
Entonces YARP redistribuye a otra réplica (FailurePolicy: Redistribute)
Y el cliente recupera el estado del job via GET /api/v1/reporting/exports/{id}
Y no se produce 503 sin fallback
```

### Story Points: 2
### Dependencias: HU-01
### Riesgo: ALTO — BLOQUEANTE para toda funcionalidad SignalR. Cambio es trivial (2 archivos, ~20 líneas) pero prerequisito de runtime; debe deploarse a DEV antes de que QA ejecute TC-01.

### Scope de archivos
```
services/core-api/src/Flit.Gateway/Program.cs          [MODIFICAR — app.UseWebSockets()]
services/core-api/src/Flit.Gateway/appsettings.json    [MODIFICAR — cluster + SessionAffinity]
```

### Sub-features cubiertos: G10

---

## HU-03 · [BACKEND] RBAC seed V2: permisos reporting.* y depreciación detailed-report.*

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – RBAC seed V2: permisos reporting.* y depreciación detailed-report.*`

### Narrativa
Como **backend-agent** quiero insertar los 15 slugs `reporting.*` y marcar como inactivos los slugs `detailed-report.*`, para que los endpoints V2 puedan usar `RequirePermission` y QA/Security puedan verificar la matriz RBAC contra la fuente ejecutable única.

> **Detalle técnico:** Migración seed con 15 slugs del §9 del diseño. Módulo `security.modules { code: "reportes-v2" }`. Slugs `detailed-report.read`, `detailed-report.export`, etc. → `is_active = false`. INSERT … ON CONFLICT DO NOTHING para idempotencia.

### AC Gherkin

```gherkin
# Positivo — 15 slugs reporting.* presentes
Dado que la migración seed RBAC se aplica en una BD limpia
Cuando se consulta security.permissions WHERE slug LIKE 'reporting.%'
Entonces se retornan exactamente 15 filas:
  reporting.read, reporting.detail, reporting.export, reporting.export.download,
  reporting.saved-queries.read, reporting.saved-queries.write,
  reporting.schedules.read, reporting.schedules.write,
  reporting.alerts.read, reporting.alerts.write,
  reporting.dashboard.preferences, reporting.audit,
  reporting.consolidado, reporting.productivity, reporting.global

# Positivo — módulo reportes-v2 creado
Cuando se consulta security.modules WHERE code = 'reportes-v2'
Entonces existe una fila con name='Reportería Transaccional V2' e is_active=true

# Negativo — slugs detailed-report.* inactivos
Cuando se consulta security.permissions WHERE slug LIKE 'detailed-report.%'
Entonces todas las filas retornan is_active=false
Y ningún endpoint con RequirePermission('detailed-report.*') autoriza a ningún usuario

# Borde — seed idempotente
Dado que el seed se ejecuta dos veces
Entonces no hay error por duplicado (ON CONFLICT DO NOTHING)
Y la BD tiene exactamente 15 filas reporting.* sin duplicados
```

### Story Points: 3
### Dependencias: HU-01
### Riesgo: MEDIO — Desactivar slugs detailed-report.* afecta sesiones activas; mitigado porque el cleanup de frontend (HU-10) y eliminación del endpoint (HU-06) ocurren en el mismo sprint o posterior.

### Scope de archivos
```
services/core-api/src/Flit.Infrastructure/Migrations/[timestamp]_F11076_RbacSeedV2.cs    [CREAR]
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/47-F11076-rbac-seed-v2.sql [CREAR]
```

### Sub-features cubiertos: G5

---

## HU-04 · [BACKEND] ExportJobs application: puertos, RequestExport y GetDownloadUrl

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – ExportJobs application: puertos, RequestExport y GetDownloadUrl`

### Narrativa
Como **backend-agent** quiero implementar los puertos `IExportFileStorage` e `IExportJobRepository` y los handlers para solicitar exportaciones, consultar estado y obtener URL de descarga, para que la lógica de negocio esté cubierta con tests unitarios antes de ensamblar la infraestructura.

> **Detalle técnico:** `IExportFileStorage.SaveExportAsync`, `GetDownloadUrlAsync`; `IExportJobRepository.NotifyChannelAsync`; `RequestExportHandler`: valida ≤3 jobs pending, ≤50k registros, rango ≤12 meses, INSERT + NOTIFY; `GetDownloadUrlHandler`: valida `owner_user_id = caller.sub`, TTL ≤15 min.

### AC Gherkin

```gherkin
# Positivo — solicitar export crea job y notifica canal
Dado que el usuario tiene 0 jobs pending/processing
Y el rango de fechas es ≤ 12 meses con ≤ 50 000 registros estimados
Cuando se ejecuta RequestExportCommand con format=excel, report_type=procedures
Entonces se inserta un ExportJob con status=pending y created_by=caller.sub
Y IExportJobRepository.NotifyChannelAsync('export_jobs_channel', jobId) se llama una vez
Y el handler retorna 202 Accepted con el jobId

# Positivo — GetDownloadUrl valida ownership y TTL
Dado que el job pertenece al caller y tiene status=completed con file_storage_path no nulo
Cuando se ejecuta GetDownloadUrlQuery
Entonces IExportFileStorage.GetDownloadUrlAsync retorna (url, expiresAt)
Y expiresAt ≤ now() + 15 minutos

# Negativo — límite de 3 jobs
Dado que el usuario ya tiene 3 jobs en status pending o processing
Cuando se ejecuta RequestExportCommand
Entonces el handler lanza ExportLimitExceededException (HTTP 409)
Y NO se hace INSERT ni se llama a NotifyChannelAsync

# Negativo — rango de fechas > 12 meses
Dado que el filtro tiene from=2025-01-01 y to=2026-06-01 (17 meses)
Cuando se ejecuta RequestExportCommand
Entonces el handler lanza DateRangeExceededException (HTTP 400 DATE_RANGE_TOO_WIDE)

# Negativo — GetDownloadUrl con job de otro usuario (IDOR)
Dado que jobId pertenece a owner_user_id='user-B' y el caller es 'user-A'
Cuando se ejecuta GetDownloadUrlQuery
Entonces el handler lanza ForbiddenException (HTTP 403)
Y no se llama a IExportFileStorage

# Borde — registros > 50 000
Dado que el filtro produciría 60 000 registros
Cuando se ejecuta RequestExportCommand
Entonces el handler lanza ExportRecordLimitExceededException (HTTP 422 EXPORT_LIMIT_EXCEEDED_RECORDS)
```

### Story Points: 5
### Dependencias: HU-01, HU-03
### Riesgo: BAJO — Clean Architecture pura; sin I/O externo en esta capa; tests unitarios con mocks directos.

### Scope de archivos
```
src/Flit.Analytics.Application/ExportJobs/IExportFileStorage.cs
src/Flit.Analytics.Application/ExportJobs/IExportJobRepository.cs
src/Flit.Analytics.Application/ExportJobs/Commands/RequestExportCommand.cs
src/Flit.Analytics.Application/ExportJobs/Commands/RequestExportHandler.cs
src/Flit.Analytics.Application/ExportJobs/Queries/GetExportJobQuery.cs
src/Flit.Analytics.Application/ExportJobs/Queries/GetExportJobHandler.cs
src/Flit.Analytics.Application/ExportJobs/Queries/GetDownloadUrlQuery.cs
src/Flit.Analytics.Application/ExportJobs/Queries/GetDownloadUrlHandler.cs
tests/Flit.Analytics.Application.Tests/ExportJobs/RequestExportHandlerTests.cs
tests/Flit.Analytics.Application.Tests/ExportJobs/GetDownloadUrlHandlerTests.cs
```

### Sub-features cubiertos: G1

---

## HU-05 · [BACKEND] ExportJobs infrastructure: FileManagerStorage, Worker LISTEN/NOTIFY y SignalR Hub

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – ExportJobs infrastructure: FileManagerStorage, Worker LISTEN/NOTIFY y SignalR Hub`

### Narrativa
Como **backend-agent** quiero implementar el adaptador `FileManagerExportStorage`, el listener de canal PostgreSQL `ExportJobsChannelListener`, el `ExportJobsWorker` resiliente multi-réplica y el `ExportJobsHub` SignalR, para procesar exportaciones de hasta 50k registros sin bloquear HTTP y notificar al usuario con progreso en tiempo real y email al completar.

> **Detalle técnico:** `FileManagerExportStorage` inyecta `IAttachmentStorage` con categoría `"exports"` e `IOptions<ExportFileManagerOptions>`; `ExportJobsChannelListener` usa Npgsql LISTEN dedicado + canal en memoria con timeout 30 s (fallback polling); `ExportJobsWorker` loop: SELECT FOR UPDATE SKIP LOCKED → processing → genera archivo → SaveExportAsync → completed → SignalR push + email SMTP; cron self-healing: status=failed WHERE processing AND updated_at < now() - 10 min.

### AC Gherkin

```gherkin
# Positivo — flujo happy path completo
Dado que hay un job en status=pending
Y ExportJobsChannelListener recibe NOTIFY en 'export_jobs_channel'
Cuando ExportJobsWorker ejecuta el loop
Entonces hace SELECT FOR UPDATE SKIP LOCKED y toma el job
Y actualiza status=processing, started_at=now()
Y genera el archivo, llama SaveExportAsync, obtiene storagePath opaco
Y actualiza status=completed, file_storage_path=storagePath, progress_pct=100
Y envía ExportCompleted via ExportJobsHub.Clients.User(ownerId)
Y envía email via IEmailSender con el jobId

# Positivo — fallback polling cuando NOTIFY no llega (PG restart)
Dado que PG reinicia y el canal NOTIFY se pierde
Cuando ExportJobsChannelListener espera más de 30 s
Entonces el canal interno recibe señal de timeout
Y ExportJobsWorker hace SELECT FOR UPDATE SKIP LOCKED y procesa el job pending

# Positivo — multi-réplica: solo una procesa el job
Dado que dos réplicas tienen ExportJobsWorker corriendo
Y hay un único job pending
Cuando ambas ejecutan SELECT FOR UPDATE SKIP LOCKED simultáneamente
Entonces solo una adquiere el lock y procesa el job
Y la otra retorna sin filas y vuelve a esperar

# Negativo — file-manager down: retry con backoff, luego failed
Dado que el file-manager retorna 503 en todos los reintentos
Cuando ExportJobsWorker intenta SaveExportAsync (3 intentos)
Entonces actualiza status=failed, error_message con la causa
Y envía ExportFailed via SignalR Hub al propietario

# Negativo — Worker crash mid-job: self-healing cron
Dado que un job tiene status=processing con updated_at > 10 min
Cuando el cron de self-healing se ejecuta
Entonces actualiza status=failed WHERE status='processing' AND updated_at < now()-interval '10 minutes'

# Borde — SignalR desconectado: push no lanza excepción
Dado que el cliente está desconectado al completar el job
Cuando SignalR Hub intenta enviar ExportCompleted
Entonces no se lanza excepción (SignalR descarta silenciosamente)
Y el email SMTP se envía de todas formas
Y el cliente recupera el estado via GET /api/v1/reporting/exports/{id}
```

### Story Points: 8
### Dependencias: HU-02, HU-04
### Riesgo: ALTO — path crítico de complejidad; security-agent debe revisar IDOR y SQL parameterizado antes del merge.

### Scope de archivos
```
src/Flit.Infrastructure/Storage/FileManagerExportStorage.cs
src/Flit.Infrastructure/Workers/ExportJobsChannelListener.cs
src/Flit.Infrastructure/Workers/ExportJobsWorker.cs
src/Flit.Infrastructure/Hubs/ExportJobsHub.cs
src/Flit.Infrastructure/Persistence/Repositories/ExportJobRepository.cs
src/Flit.Infrastructure/InfrastructureExtensions.cs    [MODIFICAR — registrar IExportFileStorage, workers, hub]
tests/Flit.Infrastructure.Tests/Workers/ExportJobsWorkerTests.cs
```

### Sub-features cubiertos: G1, G7

---

## HU-06 · [BACKEND] ExportJobs endpoints, wiring SignalR y eliminación definitiva del legado detailed-report

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.
> **Nota:** Esta HU elimina completamente `/api/v1/detailed-report/*` y sus tests. Requiere que HU-07 (endpoint V2 `/reporting/procedures`) esté listo antes, para que el endpoint de reemplazo exista en el mismo sprint de deploy.

### Título exacto (ADO)
`[BACKEND] – ExportJobs endpoints, wiring SignalR y eliminación definitiva del legado detailed-report`

### Narrativa
Como **backend-agent** quiero exponer los 4 endpoints de export jobs, registrar `AddSignalR()` y `MapHub<ExportJobsHub>` en Program.cs, y eliminar completamente `DetailedReportEndpoints.cs` junto con sus tests y cualquier registro de DI legado, para que la API quede limpia del módulo `detailed-report` una vez el reemplazo V2 (`/reporting/procedures`) esté disponible en el mismo deploy.

> **Detalle técnico:** POST/GET `/api/v1/reporting/exports`, GET `/api/v1/reporting/exports/{id}`, GET `/api/v1/reporting/exports/{id}/download-url`; `builder.Services.AddSignalR()`; `app.MapHub<ExportJobsHub>("/hubs/export-jobs")`; DELETE completo de `DetailedReportEndpoints.cs` y sus tests; limpiar cualquier registro de DI o referencia a `IDetailedReportRepository` si existe.

### AC Gherkin

```gherkin
# Positivo — POST /reporting/exports retorna 202
Dado que el usuario tiene permiso reporting.export y 0 jobs activos
Cuando hace POST /api/v1/reporting/exports con body { format:"excel", reportType:"procedures", filters:{} }
Entonces recibe HTTP 202 con { jobId: uuid, status:"pending" }
Y el hub /hubs/export-jobs acepta WebSocket upgrade

# Positivo — GET /reporting/exports lista jobs del usuario
Dado que el usuario tiene 2 jobs completados
Cuando hace GET /api/v1/reporting/exports
Entonces recibe HTTP 200 con array de 2 jobs (solo del caller, tenant + owner isolation)

# Positivo — GET /reporting/exports/{id}/download-url retorna URL temporal
Dado que el job está completed y pertenece al usuario
Cuando hace GET /api/v1/reporting/exports/{jobId}/download-url
Entonces recibe HTTP 200 con { downloadUrl, expiresAt } donde expiresAt ≤ now() + 15 min

# Positivo — endpoint legado eliminado retorna 404
Dado que DetailedReportEndpoints.cs ha sido eliminado del repositorio
Cuando se hace GET /api/v1/detailed-report/anything
Entonces el servidor retorna HTTP 404 (endpoint no existe, no 200 ni 403)
Y no existe ninguna ruta registrada con el prefijo /api/v1/detailed-report/*

# Negativo — usuario sin permiso reporting.export → 403
Dado que el usuario no tiene reporting.export
Cuando hace POST /api/v1/reporting/exports
Entonces recibe HTTP 403

# Negativo — download-url de job ajeno → 403 (IDOR check, no 404)
Dado que jobId pertenece a otro usuario del mismo tenant
Cuando hace GET /api/v1/reporting/exports/{jobId}/download-url
Entonces recibe HTTP 403 (no 404 — no revelar existencia del job)

# Borde — compilación sin referencias al módulo eliminado
Dado que DetailedReportEndpoints.cs y sus tests son eliminados en este PR
Cuando se ejecuta dotnet build en la solución
Entonces no hay errores de compilación por referencias a DetailedReportEndpoints
Y el pipeline CI pasa sin suites rotas de detailed-report
```

### Story Points: 3
### Dependencias: HU-03, HU-05, HU-07
### Riesgo: BAJO — Surface mínima; handlers probados en HU-04/05; la eliminación es menor en tamaño. Dependencia en HU-07 garantiza que /reporting/procedures existe antes de borrar el legado.

### Scope de archivos
```
src/Flit.Api/Endpoints/Reporting/ExportJobsEndpoints.cs                [CREAR]
src/Flit.Api/Program.cs                                                 [MODIFICAR — AddSignalR() + MapHub + registrar ExportJobsEndpoints]
src/Flit.Api/Endpoints/Analytics/DetailedReportEndpoints.cs             [ELIMINAR]
tests/Flit.Api.Tests/Analytics/DetailedReportEndpointsTests.cs          [ELIMINAR — si existe]
```

### Sub-features cubiertos: G1, G8

---

## HU-07 · [BACKEND] Reporting procedures: listado V2, detalle y auditoría

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – Reporting procedures: listado V2, detalle y auditoría`

### Narrativa
Como **backend-agent** quiero implementar el query builder parametrizado para el listado paginado V2, el endpoint de detalle y el endpoint de auditoría con la señal `historyAvailable`, para que el frontend pueda renderizar los tabs Trámites y Auditoría con enforcement correcto de tenant y sin posibilidad de SQL injection en los parámetros de filtro.

> **Detalle técnico:** GET `/api/v1/reporting/procedures` — 11 filtros, `sortBy` mapeado a switch exhaustivo (anti-SQLi), paginación 50/pág máx 200, rango máx 12 meses, `TryResolveEffectiveTenant` (SuperAdmin + ?tenantId vs tenant regular → 403); GET `/api/v1/reporting/procedures/{id}`; GET `/api/v1/reporting/procedures/{id}/audit` — `historyAvailable: role_id_at_time IS NOT NULL en al menos una fila`.

### AC Gherkin

```gherkin
# Positivo — listado con rango default 30 días
Dado que el usuario tiene reporting.read y no pasa parámetros de fecha
Cuando hace GET /api/v1/reporting/procedures
Entonces recibe HTTP 200 con items del último período de 30 días
Y pageSize=50, page=1 y totalCount presente

# Positivo — SuperAdmin con ?tenantId= filtra por esa empresa
Dado que el caller tiene reporting.global y rol SuperAdmin
Cuando hace GET /api/v1/reporting/procedures?tenantId=uuid-tenant-A
Entonces recibe solo trámites de tenant-A

# Positivo — auditoría con historyAvailable=true
Dado que el trámite tiene filas status_history con role_id_at_time NOT NULL
Cuando hace GET /api/v1/reporting/procedures/{id}/audit
Entonces recibe historyAvailable:true y cada evento incluye roleName y organizationName

# Positivo — sortBy mapeado a columna concreta (anti-SQLi)
Dado que el caller envía sortBy=elapsed_hours
Cuando el handler construye ORDER BY
Entonces usa la columna elapsed_hours_total de v_reporting_tramites (switch exhaustivo)
Y no concatena el parámetro directamente en el SQL

# Negativo — tenant regular con ?tenantId= ajeno → 403
Dado que el caller es AdminCompany de tenant-A
Cuando hace GET /api/v1/reporting/procedures?tenantId=uuid-tenant-B
Entonces recibe HTTP 403

# Negativo — rango > 12 meses → 400
Dado que from=2024-01-01 y to=2026-01-01 (24 meses)
Cuando hace GET /api/v1/reporting/procedures?from=2024-01-01&to=2026-01-01
Entonces recibe HTTP 400 con code DATE_RANGE_TOO_WIDE

# Borde — auditoría con historyAvailable=false (pre-backfill)
Dado que todas las filas status_history del trámite tienen role_id_at_time IS NULL
Cuando hace GET /api/v1/reporting/procedures/{id}/audit
Entonces recibe historyAvailable:false y NO se lanza error 500

# Borde — sortBy fuera de enum → 400 (sin query dinámico)
Dado que el caller envía sortBy=DROP_TABLE
Cuando el handler valida el parámetro en el switch
Entonces recibe HTTP 400 sin ejecutar ninguna query
```

### Story Points: 5
### Dependencias: HU-01, HU-03
### Riesgo: MEDIO — sortBy switch exhaustivo crítico para anti-SQLi; tenant enforcement con dos niveles.

### Scope de archivos
```
src/Flit.Analytics.Application/Reporting/Queries/GetProceduresReportQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetProceduresReportHandler.cs
src/Flit.Analytics.Application/Reporting/Queries/GetProcedureDetailQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetProcedureDetailHandler.cs
src/Flit.Analytics.Application/Reporting/Queries/GetAuditHistoryQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetAuditHistoryHandler.cs
src/Flit.Api/Endpoints/Reporting/ReportingEndpoints.cs    [CREAR — paths /procedures*]
tests/Flit.Analytics.Application.Tests/Reporting/GetProceduresReportHandlerTests.cs
tests/Flit.Analytics.Application.Tests/Reporting/GetAuditHistoryHandlerTests.cs
```

### Sub-features cubiertos: G2, G3, G6

---

## HU-08 · [BACKEND] Reporting analytics: consolidado, productividad y SLA

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – Reporting analytics: consolidado, productividad y SLA`

### Narrativa
Como **backend-agent** quiero implementar los handlers de volumetría consolidada, productividad por actor/OT y tiempos vs SLA configurable con fallback jerárquico, para que AdminCompany y SuperAdmin vean métricas de rendimiento y cumplimiento en sus respectivos tabs.

> **Detalle técnico:** GET `/api/v1/reporting/consolidado` (permiso reporting.consolidado); GET `/api/v1/reporting/productivity` (permiso reporting.productivity); GET `/api/v1/reporting/sla` (permiso reporting.read); lookup SLA jerárquico: (tenant+OT+tipo) → (tenant+tipo) → (tenant global) → default `slaConfigured:false`.

### AC Gherkin

```gherkin
# Positivo — consolidado retorna volumetría por tipo
Dado que el usuario tiene reporting.consolidado
Cuando hace GET /api/v1/reporting/consolidado?from=2026-01-01&to=2026-06-30
Entonces recibe HTTP 200 con filas por tipo de trámite y totales del tenant del caller

# Positivo — SLA lookup OT específico
Dado que existe report_sla_config para tenant-A, OT-1, tipo 'traslado', sla_hours=48
Cuando GetSlaHandler busca el SLA para OT-1 tipo 'traslado'
Entonces usa sla_hours=48 y calcula compliance % de trámites con elapsed_hours_total ≤ 48

# Positivo — SLA fallback a global del tenant
Dado que no existe config para el tipo específico pero sí una config global con sla_hours=72
Cuando GetSlaHandler busca SLA para un tipo sin config OT específica
Entonces usa sla_hours=72 (fallback global del tenant)

# Negativo — sin permiso reporting.consolidado → 403
Dado que el caller tiene solo reporting.read
Cuando hace GET /api/v1/reporting/consolidado
Entonces recibe HTTP 403

# Negativo — sin permiso reporting.productivity → 403
Dado que el caller es un Radicador
Cuando hace GET /api/v1/reporting/productivity
Entonces recibe HTTP 403

# Borde — SLA sin ninguna config del tenant
Dado que no existe ninguna report_sla_config para el tenant
Cuando GetSlaHandler procesa los trámites
Entonces el response incluye slaConfigured:false sin lanzar excepción
```

### Story Points: 5
### Dependencias: HU-01, HU-03
### Riesgo: MEDIO — Cálculo SLA con holiday_calendar y calendar_type; lookup jerárquico con 3 niveles de fallback.

### Scope de archivos
```
src/Flit.Analytics.Application/Reporting/Queries/GetConsolidadoQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetConsolidadoHandler.cs
src/Flit.Analytics.Application/Reporting/Queries/GetProductivityQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetProductivityHandler.cs
src/Flit.Analytics.Application/Reporting/Queries/GetSlaQuery.cs
src/Flit.Analytics.Application/Reporting/Queries/GetSlaHandler.cs
src/Flit.Api/Endpoints/Reporting/ReportingEndpoints.cs    [MODIFICAR — agregar consolidado, productivity, sla]
tests/Flit.Analytics.Application.Tests/Reporting/GetSlaHandlerTests.cs
```

### Sub-features cubiertos: G2

---

## HU-09 · [BACKEND] Saved queries CRUD y dashboard preferences

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[BACKEND] – Saved queries CRUD y dashboard preferences`

### Narrativa
Como **backend-agent** quiero implementar el CRUD de consultas guardadas con visibilidad `is_shared` en el tenant y el upsert de preferencias de dashboard por usuario, para que cada usuario gestione sus filtros frecuentes y personalice los KPIs visibles sin afectar a otros.

> **Detalle técnico:** GET/POST/PUT/DELETE `/api/v1/reporting/saved-queries` (permisos saved-queries.read y saved-queries.write); GET/PUT `/api/v1/reporting/preferences` (permiso reporting.dashboard.preferences); upsert por (tenant_id, user_id); DELETE valida ownership; is_shared=true expone la query a otros usuarios del mismo tenant.

### AC Gherkin

```gherkin
# Positivo — crear saved query privada
Dado que el usuario tiene reporting.saved-queries.write
Cuando hace POST /api/v1/reporting/saved-queries con { name:"Q1", filtersJson:{}, isShared:false }
Entonces recibe HTTP 201 con el id creado y la query es visible solo para ese usuario

# Positivo — is_shared=true visible para otros del mismo tenant
Dado que usuario-A crea una query con is_shared=true en tenant-A
Cuando usuario-B del mismo tenant-A hace GET /api/v1/reporting/saved-queries
Entonces la query compartida de usuario-A aparece en el listado

# Positivo — upsert dashboard preferences
Dado que el usuario no tiene preferences previas
Cuando hace PUT /api/v1/reporting/preferences con { visibleKpis:["totalTramites"], kpiOrder:[...] }
Entonces recibe HTTP 200 y se crea la fila
Cuando hace PUT de nuevo con otra configuración
Entonces la fila se actualiza (upsert sin duplicado)

# Negativo — DELETE de query ajena → 403/404
Dado que queryId pertenece a usuario-B y el caller es usuario-A
Cuando hace DELETE /api/v1/reporting/saved-queries/{queryId}
Entonces recibe HTTP 403 o 404

# Borde — query de otro tenant invisible (RLS)
Dado que usuario-A en tenant-A tiene una query is_shared=true
Cuando usuario-C en tenant-B hace GET /api/v1/reporting/saved-queries
Entonces la query de tenant-A no aparece (RLS)
```

### Story Points: 3
### Dependencias: HU-01, HU-03
### Riesgo: BAJO — CRUD estándar. Riesgo residual en visibilidad is_shared cross-user dentro del tenant.

### Scope de archivos
```
src/Flit.Analytics.Application/SavedQueries/     [CREAR — 4 handlers CRUD]
src/Flit.Analytics.Application/DashboardPreferences/  [CREAR — 2 handlers GET + upsert]
src/Flit.Api/Endpoints/Reporting/SavedQueriesEndpoints.cs         [CREAR]
src/Flit.Api/Endpoints/Reporting/DashboardPreferencesEndpoints.cs [CREAR]
```

### Sub-features cubiertos: G4, G9

---

## HU-10 · [FRONTEND] Big-bang: eliminar ReportesDetallados y limpiar dock

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.
> **Nota de timing:** Esta HU se ejecuta **después de HU-13** (Tab Trámites V2 lista), garantizando que el reemplazo funcional exista antes de eliminar el módulo legado. El sprint debe incluir release notes sobre bookmarks `?m=reportes-detallados` rotos (ADR-0038).

### Título exacto (ADO)
`[FRONTEND] – Big-bang: eliminar ReportesDetallados y limpiar dock`

### Narrativa
Como **frontend-agent** quiero eliminar `ReportesDetallados.tsx`, `detailed-report.ts` y la entrada del dock, sin redirect en `page.tsx`, para que el codebase quede limpio del módulo legado una vez el tab Trámites V2 esté disponible como reemplazo funcional.

> **Detalle técnico:** DELETE `ReportesDetallados.tsx` + `detailed-report.ts`; MODIFICAR `Shell.tsx` (eliminar entry `moduleId:'reportes-detallados'`); MODIFICAR `modules.ts` (eliminar de `ALL_MODULE_IDS`); NO modificar `page.tsx` (sin redirect — ADR-0038 aprobado); DELETE tests E2E de reportes-detallados.

### AC Gherkin

```gherkin
# Positivo — dock no muestra reportes-detallados
Dado que la aplicación carga con usuario autenticado
Cuando se renderiza Shell.tsx
Entonces no existe ningún icono con moduleId='reportes-detallados'
Y solo hay un icono 'reportes' en el dominio de reportería

# Positivo — TypeScript compila sin referencias al módulo eliminado
Dado que los archivos del módulo legado son eliminados
Cuando se ejecuta npx tsc --noEmit
Entonces no hay errores de import no resuelto por esos archivos

# Negativo — ?m=reportes-detallados no carga componente eliminado
Dado que un usuario navega a ?m=reportes-detallados
Cuando el router procesa el parámetro
Entonces el componente ReportesDetallados no existe en el árbol
Y la aplicación no lanza error de componente no encontrado
Y NO hay redirect automático (big-bang sin redirect — ADR-0038)

# Borde — tests E2E del módulo eliminado no bloquean CI
Dado que los tests E2E de reportes-detallados son eliminados en este PR
Cuando se ejecuta el pipeline CI
Entonces las suites no reportan fallo por archivos eliminados
```

### Story Points: 2
### Dependencias: HU-13
### Riesgo: MEDIO — Bookmarks `?m=reportes-detallados` dejan de funcionar. Mitigado: release notes en sprint + Tab Trámites V2 (HU-13) ya disponible como reemplazo.

### Scope de archivos
```
frontend/components/atom/modules/ReportesDetallados.tsx    [ELIMINAR]
frontend/lib/api/detailed-report.ts                        [ELIMINAR]
frontend/components/atom/Shell.tsx                         [MODIFICAR — eliminar dock entry]
frontend/lib/nav/modules.ts                                [MODIFICAR — eliminar de ALL_MODULE_IDS]
frontend/__tests__/reportes-detallados/                    [ELIMINAR]
```

### Sub-features cubiertos: G8

---

## HU-11 · [FRONTEND] Cliente V2: API client, ReportFilterContext y SignalR client

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Cliente V2: API client, ReportFilterContext y SignalR client`

### Narrativa
Como **frontend-agent** quiero implementar el API client tipado para todos los endpoints `/api/v1/reporting/*`, el `ReportFilterContext` con estado persistido en URL params y el cliente SignalR con reconexión automática y fallback a polling REST cada 5 s, para que todos los tabs de reportes tengan una fuente de datos robusta independientemente del estado de conectividad WebSocket.

> **Detalle técnico:** `reporting-v2.ts` — wrapper tipado de todos los endpoints del OpenAPI Reporting V2; `ReportFilterContext` — React context con estado en URL params (from, to, dateType, status, procedureType, tenantId, OT, search, sortBy, sortOrder, page, pageSize); `export-jobs-client.ts` — conexión SignalR con reconexión automática, `onProgress`/`onCompleted`/`onFailed` callbacks, fallback GET /exports/{id} cada 5 s si hub.state !== 'Connected'.

### AC Gherkin

```gherkin
# Positivo — ReportFilterContext persiste filtros en URL
Dado que el usuario aplica status=en_proceso y dateFrom=2026-01-01
Cuando navega a otra pestaña del browser y vuelve con el mismo URL
Entonces ReportFilterContext restaura los filtros desde los URL params
Y la pestaña activa muestra datos filtrados sin re-aplicar

# Positivo — SignalR client recibe ExportProgress
Dado que export-jobs-client.ts está conectado al hub
Cuando el worker emite ExportProgress { jobId, progressPct:60 }
Entonces el callback onProgress es llamado con progressPct=60
Y la UI actualiza el indicador sin polling REST

# Positivo — fallback polling cuando hub desconectado
Dado que hub.state === 'Disconnected'
Cuando se solicita una exportación y el cliente espera el resultado
Entonces export-jobs-client.ts hace GET /api/v1/reporting/exports/{jobId} cada 5 s
Y al detectar status='completed' notifica al caller

# Negativo — JWT expirado: hub reconecta con nuevo token
Dado que el JWT expira mientras el hub está conectado
Cuando la conexión se cierra por autenticación
Entonces export-jobs-client.ts invoca el proveedor de token y reconecta automáticamente

# Borde — múltiples tabs del mismo usuario reciben el evento
Dado que el usuario tiene el módulo abierto en 2 tabs
Cuando el hub emite ExportCompleted
Entonces ambas tabs reciben el evento (hub agrupa por userId)
```

### Story Points: 5
### Dependencias: HU-06, HU-07, HU-08, HU-09
### Riesgo: MEDIO — Reconexión automática SignalR en entornos con firewall; fallback polling debe activarse correctamente.

### Scope de archivos
```
frontend/lib/api/reporting-v2.ts
frontend/lib/signalr/export-jobs-client.ts
frontend/components/atom/modules/reportes/ReportFilterContext.tsx
frontend/__tests__/reportes/export-jobs-client.test.ts
frontend/__tests__/reportes/ReportFilterContext.test.tsx
```

### Sub-features cubiertos: G1, G2, G4

---

## HU-12 · [FRONTEND] Reportes.tsx V2: estructura tabs y ExportController

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Reportes.tsx V2: estructura tabs y ExportController`

### Narrativa
Como **frontend-agent** quiero extender `Reportes.tsx` con la estructura de 8 tabs V2 y el `ExportController` con badge/toast/progress/download, para que el módulo reportes sea el único punto de entrada a toda la reportería y el usuario vea el estado de sus exports en tiempo real desde cualquier tab.

> **Detalle técnico:** 8 tabs en orden: resumen, trámites, consolidado, productividad, tiempos-sla, auditoría, programados, alertas; `ExportController` — badge con aria-label, toast de progreso (aria-live="polite"), toast ExportCompleted con botón "Descargar" que llama `/exports/{id}/download-url`; tabs programados y alertas reutilizan lógica existente de AnalyticsEndpoints; 4 estados UI obligatorios en ExportController.

### AC Gherkin

```gherkin
# Positivo — 8 tabs en orden correcto
Dado que el usuario abre el módulo reportes
Cuando Reportes.tsx monta
Entonces se renderizan 8 tabs: resumen, trámites, consolidado, productividad, tiempos-sla, auditoría, programados, alertas
Y la tab activa por defecto es 'resumen'
Y la navegación por teclado entre tabs funciona con Arrow keys (WCAG 2.1 AA)

# Positivo — ExportController badge con conteo de pendientes
Dado que el usuario tiene 2 jobs en status processing
Cuando ExportController carga
Entonces el badge muestra "2" con aria-label="2 exportaciones en progreso"

# Positivo — toast ExportCompleted con link de descarga
Dado que el hub emite ExportCompleted
Cuando ExportController recibe el evento
Entonces aparece un toast con "Exportación lista" y botón "Descargar"
Y al hacer clic llama GET /api/v1/reporting/exports/{id}/download-url e inicia descarga

# Positivo — progreso porcentual con aria-live
Dado que el hub emite ExportProgress con progressPct=40
Cuando ExportController actualiza la UI
Entonces la barra muestra 40% y el elemento tiene aria-live="polite"

# Negativo — estado vacío (sin jobs)
Dado que el usuario no tiene export jobs
Cuando ExportController carga
Entonces muestra estado vacío con "Sin exportaciones recientes" (no error, no spinner infinito)

# Borde — toast de error cuando job falla
Dado que el hub emite ExportFailed
Cuando ExportController recibe el evento
Entonces muestra toast de error con el mensaje del job
Y el badge no cuenta los jobs failed en el contador de pendientes
```

### Story Points: 5
### Dependencias: HU-11
### Riesgo: MEDIO — Extender Reportes.tsx sin romper tabs existentes (resumen, programados, alertas). aria-live crítico para WCAG 2.1 AA.

### Scope de archivos
```
frontend/components/atom/modules/Reportes.tsx                              [MODIFICAR — estructura tabs V2 + ExportController + ReportFilterContext]
frontend/components/atom/modules/reportes/ExportController.tsx             [CREAR]
frontend/components/atom/modules/reportes/HistoryUnavailableBadge.tsx      [CREAR]
frontend/__tests__/reportes/ExportController.test.tsx                      [CREAR]
```

### Sub-features cubiertos: G1, G7, G8

---

## HU-13 · [FRONTEND] Tab Trámites V2: tabla paginada con filtros avanzados

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Tab Trámites V2: tabla paginada con filtros avanzados`

### Narrativa
Como **Radicador u Operador** quiero ver la lista de trámites con 11 filtros avanzados (estado, tipo, OT, rango de fechas hasta 12 meses, búsqueda por placa/VIN/documento), paginación de 50/pág, sorting y exportación asíncrona del conjunto filtrado, para analizar el historial sin el límite del exportador legado.

> **Detalle técnico:** Filtros activos como chips; sincronización con `ReportFilterContext` y URL params; validación de rango ≤12 meses en UI (sin enviar al backend); botón "Exportar" → POST `/reporting/exports` con filtros del contexto → ExportController; 4 estados UI obligatorios.

### AC Gherkin

```gherkin
# Positivo — tabla carga con rango default 30 días
Dado que el usuario navega al tab 'trámites'
Cuando TramitesTab monta
Entonces hace GET /api/v1/reporting/procedures con from=hoy-30d y to=hoy
Y renderiza la tabla con ≤50 filas con skeleton loader durante la carga

# Positivo — filtros actualizan URL y recarga tabla
Dado que el usuario selecciona status=en_proceso y procedureType=traslado
Cuando aplica los filtros
Entonces la URL se actualiza y la tabla se recarga
Y los filtros activos son visibles como chips encima de la tabla

# Positivo — exportar conjunto filtrado
Dado que el usuario tiene reporting.export y el filtro tiene < 50 000 registros
Cuando hace clic en "Exportar" y selecciona Excel
Entonces POST /api/v1/reporting/exports con los filtros del ReportFilterContext
Y ExportController recibe el jobId y muestra progreso (no bloquea UI)

# Negativo — estado vacío (0 resultados)
Dado que el filtro retorna items=[] y totalCount=0
Cuando la respuesta llega
Entonces el tab muestra estado vacío con "Sin datos para el período seleccionado" con icono ilustrativo

# Negativo — estado error en fallo de red
Dado que GET /api/v1/reporting/procedures retorna 500
Cuando el componente recibe el error
Entonces muestra banner de error con código HTTP y botón "Reintentar"

# Borde — rango > 12 meses bloqueado en UI
Dado que el usuario intenta seleccionar from=2024-01-01 y to=2026-01-01 (24 meses)
Cuando el date picker valida el rango
Entonces muestra "Rango máximo 12 meses" inline y NO envía la query al backend
```

### Story Points: 5
### Dependencias: HU-12
### Riesgo: BAJO — Patrón de tabla paginada ya existe en TramitesTable.tsx. Riesgo en sincronización de filtros con ReportFilterContext.

### Scope de archivos
```
frontend/components/atom/modules/reportes/tabs/TramitesTab.tsx    [CREAR]
frontend/__tests__/reportes/TramitesTab.test.tsx                  [CREAR]
```

### Sub-features cubiertos: G2, G1

---

## HU-14 · [FRONTEND] Tabs Consolidado y Productividad

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Tabs Consolidado y Productividad`

### Narrativa
Como **AdminCompany o SuperAdmin** quiero ver la volumetría de trámites por tipo/OT en el tab Consolidado y el ranking de productividad por actor/OT en el tab Productividad, con los 4 estados UI y exportación asíncrona, para hacer seguimiento del desempeño organizacional.

### AC Gherkin

```gherkin
# Positivo — consolidado muestra volumetría
Dado que el usuario tiene reporting.consolidado
Cuando navega al tab 'consolidado'
Entonces GET /api/v1/reporting/consolidado retorna datos
Y la tabla/gráfica muestra filas por tipo de trámite con totales y % de participación
Y el tab muestra los 4 estados: vacío / cargando / error / lleno

# Positivo — productividad muestra top radicadores
Dado que el usuario tiene reporting.productivity
Cuando navega al tab 'productividad'
Entonces GET /api/v1/reporting/productivity retorna datos
Y la tabla muestra actores ordenados por volumen de trámites con columnas OT, tipo de actor y conteos

# Positivo — exportar consolidado
Dado que el usuario hace clic en "Exportar" en el tab consolidado
Cuando selecciona CSV
Entonces POST /api/v1/reporting/exports con reportType='consolidado' y filtros del contexto
Y ExportController muestra el job en progreso

# Negativo — sin permiso reporting.productivity → estado sin permiso
Dado que el caller es un Radicador
Cuando navega al tab 'productividad'
Entonces el tab muestra "No tienes permiso para ver este reporte" (no error 500)

# Borde — sin datos en la OT/período seleccionados
Dado que la respuesta llega con data vacía
Entonces ambos tabs muestran estado vacío con mensaje contextual
```

### Story Points: 5
### Dependencias: HU-12
### Riesgo: BAJO — Render de tablas y gráficas de resumen; sin lógica compleja de filtros.

### Scope de archivos
```
frontend/components/atom/modules/reportes/tabs/ConsolidadoTab.tsx     [CREAR]
frontend/components/atom/modules/reportes/tabs/ProductividadTab.tsx   [CREAR]
frontend/__tests__/reportes/ConsolidadoTab.test.tsx                   [CREAR]
```

### Sub-features cubiertos: G2

---

## HU-15 · [FRONTEND] Tabs SLA y Auditoría con HistoryUnavailableBadge

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Tabs SLA y Auditoría con HistoryUnavailableBadge`

### Narrativa
Como **AdminCompany o SuperAdmin** quiero ver el cumplimiento de SLA por tipo/OT en el tab SLA y el historial completo de responsables en el tab Auditoría, con el badge "Historial no disponible" cuando `historyAvailable=false` (registros pre-backfill), para tener trazabilidad sin datos engañosos en campos vacíos.

> **Detalle técnico:** `HistoryUnavailableBadge` se muestra cuando `historyAvailable===false` — no campos vacíos ni guiones; tab SLA con indicador visual diferenciado de cumplimiento vs incumplimiento; SuperAdmin puede ver auditoría global con selector de empresa activo; campos PII solo se muestran con permiso reporting.audit.

### AC Gherkin

```gherkin
# Positivo — SLA muestra compliance porcentual
Dado que el usuario tiene reporting.read y existe config SLA del tenant
Cuando navega al tab 'tiempos-sla'
Entonces GET /api/v1/reporting/sla retorna datos
Y la tabla muestra tiempo promedio, SLA configurado y % cumplimiento por tipo
Y trámites dentro del SLA se destacan visualmente diferente a los incumplidos

# Positivo — auditoría muestra timeline con responsables
Dado que el usuario tiene reporting.audit y el trámite tiene historyAvailable=true
Cuando navega al tab 'auditoría' y selecciona el trámite
Entonces GET /api/v1/reporting/procedures/{id}/audit retorna eventos
Y cada evento muestra fecha, estado anterior→nuevo, usuario, rol y organización al momento

# Positivo — HistoryUnavailableBadge cuando historyAvailable=false
Dado que el trámite tiene role_id_at_time IS NULL en todas las filas (pre-backfill)
Y la respuesta incluye historyAvailable:false
Cuando AuditoriaTab renderiza el detalle
Entonces HistoryUnavailableBadge se muestra con "Historial no disponible para este trámite"
Y NO se muestran campos vacíos ni guiones en lugar de roles

# Negativo — sin permiso reporting.audit → mensaje de sin permiso
Dado que el caller tiene solo reporting.read
Cuando navega al tab 'auditoría'
Entonces el tab muestra "No tienes permiso para ver el historial de auditoría"
Y NO se llama a /procedures/{id}/audit

# Borde — SLA sin configuración del tenant
Dado que slaConfigured=false en la respuesta
Cuando el tab SLA renderiza
Entonces muestra banner "Sin configuración de SLA. Configure los objetivos en Ajustes."
Y muestra tiempos promedio sin columna de compliance

# Borde — SuperAdmin ve auditoría global con selector empresa
Dado que el caller es SuperAdmin con reporting.global
Cuando navega al tab 'auditoría' sin tenantId
Entonces el componente muestra selector de empresa activo para filtrar
```

### Story Points: 5
### Dependencias: HU-12
### Riesgo: MEDIO — HistoryUnavailableBadge crítico para no confundir al usuario. PII solo visible con reporting.audit (verificar en security-agent).

### Scope de archivos
```
frontend/components/atom/modules/reportes/tabs/SlaTab.tsx          [CREAR]
frontend/components/atom/modules/reportes/tabs/AuditoriaTab.tsx    [CREAR]
frontend/__tests__/reportes/AuditoriaTab.test.tsx                  [CREAR]
frontend/__tests__/reportes/SlaTab.test.tsx                        [CREAR]
```

### Sub-features cubiertos: G2, G3, G6

---

## HU-16 · [FRONTEND] Dashboard preferences UI y Saved queries panel

> **Precondición DoR-US:** Feature #11076 en estado Active o Resolved.

### Título exacto (ADO)
`[FRONTEND] – Dashboard preferences UI y Saved queries panel`

### Narrativa
Como **usuario autenticado** quiero personalizar los KPIs visibles en el tab Resumen (mostrar/ocultar/reordenar con DnD accesible por teclado) y gestionar mis consultas guardadas desde cualquier tab, para adaptar la reportería a mi flujo sin afectar a otros usuarios.

### AC Gherkin

```gherkin
# Positivo — ocultar KPI y persistir preferencia
Dado que el usuario abre el panel de preferencias
Cuando desactiva el toggle del KPI 'tramitesRechazados'
Entonces PUT /api/v1/reporting/preferences actualiza config_json
Y el KPI desaparece del tab resumen y sigue oculto tras recargar la página

# Positivo — saved query aplica filtros al contexto
Dado que el usuario tiene una saved query con { status:'en_proceso', procedureType:'traslado' }
Cuando hace clic en "Aplicar"
Entonces ReportFilterContext se actualiza con esos filtros
Y la URL se actualiza y el tab activo recarga

# Positivo — guardar filtros activos como nueva query
Dado que el usuario tiene filtros activos en ReportFilterContext
Cuando hace clic en "Guardar consulta actual" con nombre "Q-OT1"
Entonces POST /api/v1/reporting/saved-queries con los filtros y name='Q-OT1'
Y la nueva consulta aparece en el panel

# Negativo — DnD accesible por teclado (WCAG 2.1 AA)
Dado que el usuario usa solo teclado para reordenar KPIs
Cuando usa Space para seleccionar y Arrow keys para mover
Entonces el orden actualiza visualmente
Y aria-live anuncia el nuevo orden al lector de pantalla
Y PUT /api/v1/reporting/preferences se envía al confirmar

# Borde — límite de saved queries en UI
Dado que el usuario tiene 20 saved queries
Cuando intenta crear la número 21
Entonces la UI muestra "Límite de consultas guardadas alcanzado" sin llamar al backend
```

### Story Points: 3
### Dependencias: HU-11, HU-12
### Riesgo: BAJO — Personalización sin impacto en datos. Riesgo en DnD accesibilidad con teclado (WCAG).

### Scope de archivos
```
frontend/components/atom/modules/reportes/DashboardPreferencesPanel.tsx    [CREAR]
frontend/components/atom/modules/reportes/SavedQueriesPanel.tsx            [CREAR]
frontend/__tests__/reportes/SavedQueriesPanel.test.tsx                     [CREAR]
```

### Sub-features cubiertos: G9, G4

---

## Matriz de cobertura CF (G1–G10) → HU

| Sub-feature | Descripción | HUs |
|------------|-------------|-----|
| **G1** | Export jobs async (tabla + worker + LISTEN/NOTIFY + SignalR + REST fallback) | HU-01, HU-04, HU-05, HU-06, HU-11, HU-12, HU-13 |
| **G2** | Reporting queries V2 (procedures, consolidado, productivity, SLA) | HU-07, HU-08, HU-11, HU-13, HU-14, HU-15 |
| **G3** | Status history audit trail (ALTER TABLE + historyAvailable flag) | HU-01, HU-07, HU-15 |
| **G4** | Saved queries CRUD | HU-01, HU-09, HU-11, HU-16 |
| **G5** | RBAC seed V2 (15 slugs reporting.* + depreciación detailed-report.*) | HU-03 |
| **G6** | "Historial no disponible" badge (backfill NULL) | HU-07, HU-15 |
| **G7** | Notificaciones (email SMTP + badge in-app) | HU-05, HU-12 |
| **G8** | Navegación big-bang (eliminar reportes-detallados sin redirect) | HU-06 (elimina endpoint BE), HU-10 (elimina FE) |
| **G9** | Dashboard preferences (show/hide/reorder KPIs) | HU-01, HU-09, HU-16 |
| **G10** | Gateway/infra UseWebSockets + SessionAffinity | HU-02 |

---

## Orden topológico de implementación (corregido)

```
Layer 0 ─────────────────────────────────────────────────────────────────────
  HU-01 [DATABASE/db-agent]   Schema analytics V2 + migración       (base absoluta)

Layer 1 — paralelos entre sí tras HU-01 ────────────────────────────────────
  HU-02 [BACKEND/infra-agent] Gateway UseWebSockets + SessionAffinity   ← BLOQUEANTE SignalR
  HU-03 [BACKEND]             RBAC seed V2

Layer 2 — paralelos entre sí tras HU-01 + HU-03 ────────────────────────────
  HU-04 [BACKEND]    ExportJobs application layer    (dep: 01,03)
  HU-07 [BACKEND]    Reporting procedures + audit    (dep: 01,03)
  HU-08 [BACKEND]    Reporting consolidado + prod + SLA  (dep: 01,03)
  HU-09 [BACKEND]    Saved queries + dashboard prefs (dep: 01,03)

Layer 3 ─────────────────────────────────────────────────────────────────────
  HU-05 [BACKEND]    ExportJobs infrastructure       (dep: 02,04)

Layer 4 — HU-06 espera HU-05 y HU-07 ───────────────────────────────────────
  HU-06 [BACKEND]    ExportJobs endpoints + eliminar legado BE  (dep: 03,05,07)
  ← HU-07 debe estar lista antes de HU-06 para que /reporting/procedures
    reemplace a /detailed-report/* en el mismo sprint de deploy

Layer 5 ─────────────────────────────────────────────────────────────────────
  HU-11 [FRONTEND]   API client + ReportFilterContext + SignalR client  (dep: 06,07,08,09)

Layer 6 ─────────────────────────────────────────────────────────────────────
  HU-12 [FRONTEND]   Reportes.tsx V2 + ExportController  (dep: 11)
  ← NO depende de HU-10; el shell V2 puede construirse sin eliminar el legado

Layer 7 — paralelos entre sí tras HU-12 ────────────────────────────────────
  HU-13 [FRONTEND]   Tab Trámites V2            (dep: 12)
  HU-14 [FRONTEND]   Tabs Consolidado + Productividad  (dep: 12)
  HU-15 [FRONTEND]   Tabs SLA + Auditoría       (dep: 12)
  HU-16 [FRONTEND]   Dashboard prefs + Saved queries  (dep: 11,12)

Layer 8 — solo tras HU-13 (reemplazo funcional listo) ──────────────────────
  HU-10 [FRONTEND]   Big-bang: eliminar legado FE      (dep: 13)
  ← Tab Trámites V2 debe estar disponible antes de eliminar ReportesDetallados
```

### Camino crítico
```
HU-01 → HU-03 → HU-07 → HU-06 → HU-11 → HU-12 → HU-13 → HU-10
```

---

## Alertas de gate antes de crear en ADO

1. **HU-01** — database-agent ejecuta `db-schema-validator` (checklist A1–A20) antes de activar.
2. **HU-02** — BLOQUEANTE: deploarse a DEV antes de que QA ejecute TC-01 (happy path SignalR).
3. **HU-05** — Path crítico de complejidad; security-agent revisa IDOR y SQL parameterizado antes del merge.
4. **HU-06** — Elimina `DetailedReportEndpoints.cs` definitivamente; requiere HU-07 en el mismo sprint.
5. **HU-12 + HU-10** — No van en el mismo sprint obligatoriamente; HU-12 puede ir antes. HU-10 va cuando HU-13 esté lista.
6. **Sprint HU-10** — Release notes de comunicación a usuarios (bookmarks `?m=reportes-detallados` rotos — ADR-0038).
7. **Todas las HUs** — Feature #11076 debe estar `Active` o `Resolved` antes de activar cualquier HU hija (DoR-US criterio Parent Feature).

---

*BORRADOR — NO CREADO EN ADO · tech-lead-agent Modo B (revisión 2) · 2026-07-29*
