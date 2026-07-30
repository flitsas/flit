# [REPORTERÍA] Subsistema de Reportería Transaccional V2 — Feature #11076

**Fecha de planificación:** 2026-07-29
**Autor:** Jorman Copete
**Agente:** architecture-agent
**Estado:** Diseño aprobado · Listo para descomposición en HUs
**Feature ADO:** #11076
**Rama base:** `feature/AB-11076-reporteria-transaccional-v2` (base `develop` @ `5e05b6f1`)

---

## 1. Objetivo del Feature

Unificar y ampliar el subsistema de reportería de FLIT en un único módulo `reportes` con:
- Consulta interactiva avanzada de trámites (30 días default, 12 meses max)
- Exportaciones asíncronas con notificación en tiempo real (Excel, CSV, PDF institucional)
- Consolidado/volumetría, productividad, tiempos/SLA configurable y auditoría histórica
- Consultas guardadas y preferencias de dashboard personalizables
- Vista global para Admin FLIT con filtros opcionales por empresa/OT
- Eliminación completa del módulo `reportes-detallados` (big-bang, sin redirect)

---

## 2. Contexto funcional

| Capacidad | Módulo actual | Estado en V2 |
|-----------|--------------|--------------|
| Dashboard overview/KPIs | `reportes` | MANTENER y extender |
| Lista paginada de trámites | `reportes-detallados` | ABSORBER en tab `tramites` de `reportes` |
| Exportación Excel síncrona | `reportes-detallados` | REEMPLAZAR por exportación asíncrona |
| Scheduling de informes | `reportes` (Reportes 2.0) | MANTENER |
| Alertas por umbral | `reportes` (Reportes 2.0) | MANTENER |
| Auditoría de responsabilidad | — | NUEVO |
| Consultas guardadas | — | NUEVO |
| Preferencias de dashboard | — | NUEVO |
| Tiempos/SLA | — | NUEVO |
| Consolidado/volumetría | — | NUEVO (extiende overview) |

---

## 3. Decisiones de diseño aprobadas

| ID | Decisión | ADR |
|----|----------|-----|
| G1 | Exportaciones asíncronas: `export_jobs` + PG LISTEN/NOTIFY + Worker + SignalR + REST fallback | ADR-0037 |
| G2 | Eliminación big-bang de `reportes-detallados` sin redirect | ADR-0038 |
| G3 | ✅ ALTER TABLE determinista en `status_history` (aprobado 2026-07-29 — PG17 O(1), nullable/NULL, sin bifurcación de schema) | — |
| G4 | Storage reutiliza `IAttachmentStorage`/`FileManagerAttachmentStorage` via adaptador `IExportFileStorage` | ADR-0037 |
| G5 | Permisos por seed ejecutable; matriz de 15 slugs documentada para QA/Security | — |
| G6 | Backfill NULL en historial; UI muestra "Historial no disponible" | — |
| G7 | Notificación: email + badge/toast in-app vía SignalR; REST polling como fallback | ADR-0037 |

---

## 4. Arquitectura — Diagrama de componentes

```
┌─────────────────────────────────────────────────────────────────┐
│  Frontend (Next.js App Router)                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  Shell.tsx (dock)                                        │    │
│  │  └── "reportes" [único icono]                           │    │
│  │      └── Reportes.tsx                                   │    │
│  │          ├── Tab: resumen (existente)                   │    │
│  │          ├── Tab: tramites (V2, reemplaza detallados)   │    │
│  │          ├── Tab: consolidado                           │    │
│  │          ├── Tab: productividad                         │    │
│  │          ├── Tab: tiempos-sla                           │    │
│  │          ├── Tab: auditoria                             │    │
│  │          ├── Tab: programados (existente)               │    │
│  │          └── Tab: alertas (existente)                   │    │
│  │          ├── ExportController (badge/toast/download)    │    │
│  │          └── ReportFilterContext (estado global)        │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                 │
│  lib/signalr/export-jobs-client.ts ← WebSocket /hubs/export-jobs│
│  lib/api/reporting-v2.ts ← REST /api/v1/reporting/*             │
└─────────────────────────────────────────────────────────────────┘
           │ HTTPS + WebSocket (upgrade via YARP)
┌─────────────────────────────────────────────────────────────────┐
│  Flit.Gateway (YARP)                                            │
│  /api/** → core-api-cluster (REST, 30s timeout)                 │
│  /hubs/** → core-api-signalr-cluster (WS, 5min timeout)         │
│             + SessionAffinity cookie                            │
│  [BLOQUEANTE: app.UseWebSockets() antes de MapReverseProxy()]   │
└─────────────────────────────────────────────────────────────────┘
           │ HTTP interno
┌─────────────────────────────────────────────────────────────────┐
│  Flit.Api (core-api)                                            │
│  ReportingEndpoints (/api/v1/reporting/procedures*)             │
│  ExportJobsEndpoints (/api/v1/reporting/exports*)               │
│  SavedQueriesEndpoints + DashboardPreferencesEndpoints          │
│  ExportJobsHub (/hubs/export-jobs) [SignalR]                    │
│  ExportJobsChannelListener [BackgroundService: LISTEN/NOTIFY]   │
│  ExportJobsWorker [BackgroundService: FOR UPDATE SKIP LOCKED]   │
│  IExportFileStorage → FileManagerExportStorage                  │
│    └── IAttachmentStorage → FileManagerAttachmentStorage → S3   │
└─────────────────────────────────────────────────────────────────┘
           │ Npgsql
┌───────────────────────────────────┐
│  PostgreSQL                       │
│  analytics.export_jobs            │
│  analytics.saved_queries          │
│  analytics.dashboard_preferences  │
│  analytics.report_sla_config      │
│  analytics.procedure_instance_status_history (extender)         │
└───────────────────────────────────┘
```

---

## 5. Diseño de UI — Árbol de navegación

```
/ (raíz SPA)
└── ?m=reportes
    ├── ?tab=resumen        ← KPIs overview (existente, extendido)
    ├── ?tab=tramites       ← Listado V2 (reemplaza reportes-detallados)
    ├── ?tab=consolidado    ← Volumetría por tipo/OT/período
    ├── ?tab=productividad  ← Top radicadores por OT
    ├── ?tab=tiempos-sla    ← Tiempos vs SLA configurable
    ├── ?tab=auditoria      ← Historial de responsabilidad por trámite
    ├── ?tab=programados    ← Scheduling (existente)
    └── ?tab=alertas        ← Alertas por umbral (existente)
```

**Módulo eliminado (big-bang):**
- `?m=reportes-detallados` → comportamiento indefinido (módulo no existe en dock ni en registry)

---

## 6. Frontend — Componentes a crear/modificar

### Eliminar (big-bang)
- `frontend/components/atom/modules/ReportesDetallados.tsx`
- `frontend/lib/api/detailed-report.ts`
- Tests E2E para `m=reportes-detallados`

### Modificar
- `frontend/components/atom/Shell.tsx` — eliminar dock entry `reportes-detallados`
- `frontend/lib/nav/modules.ts` — eliminar `"reportes-detallados"` de `ALL_MODULE_IDS`
- `frontend/components/atom/modules/Reportes.tsx` — agregar tabs V2, `ReportFilterContext`, `ExportController`

### Crear
| Componente | Descripción |
|-----------|-------------|
| `frontend/lib/api/reporting-v2.ts` | API client tipado para `/api/v1/reporting/*` |
| `frontend/lib/signalr/export-jobs-client.ts` | Cliente SignalR con reconexión y REST fallback |
| `frontend/components/atom/modules/reportes/ReportFilterContext.tsx` | Contexto de filtros global con URL params sync |
| `frontend/components/atom/modules/reportes/ExportController.tsx` | Gestión de jobs + toast + badge + download |
| `frontend/components/atom/modules/reportes/HistoryUnavailableBadge.tsx` | Badge "Historial no disponible" (G6) |
| `frontend/components/atom/modules/reportes/tabs/TramitesTab.tsx` | Tab listado V2 con filtros avanzados |
| `frontend/components/atom/modules/reportes/tabs/ConsolidadoTab.tsx` | Tab volumetría |
| `frontend/components/atom/modules/reportes/tabs/ProductividadTab.tsx` | Tab productividad |
| `frontend/components/atom/modules/reportes/tabs/SlaTab.tsx` | Tab tiempos/SLA |
| `frontend/components/atom/modules/reportes/tabs/AuditoriaTab.tsx` | Tab auditoría histórica |

**Reglas de UI obligatorias:**
- 4 estados por componente: vacío, cargando (skeleton), error (banner + reintentar), lleno
- WCAG 2.1 AA: contraste 4.5:1, `aria-live="polite"` en progreso de export, labels en filtros
- `HistoryUnavailableBadge` se muestra cuando `historyAvailable === false` (nunca campo vacío)

---

## 7. Backend — Componentes a crear/modificar

### Nuevas entidades de dominio
| Entidad | Tabla | Descripción |
|---------|-------|-------------|
| `ExportJob` | `analytics.export_jobs` | Job asíncrono de exportación |
| `SavedQuery` | `analytics.saved_queries` | Consulta guardada con filtros |
| `DashboardPreferences` | `analytics.dashboard_preferences` | Preferencias de KPIs por usuario |
| `ReportSlaConfig` | `analytics.report_sla_config` | SLA configurable por tipo/OT |

### Application layer (crear)
- `ExportJobs/IExportFileStorage.cs` — puerto de storage
- `ExportJobs/IExportJobRepository.cs` — puerto repositorio
- `ExportJobs/Commands/RequestExportCommand.cs` + `RequestExportHandler.cs`
- `ExportJobs/Queries/GetExportJobHandler.cs` + `GetDownloadUrlHandler.cs`
- `Reporting/Queries/GetProceduresReportHandler.cs`
- `Reporting/Queries/GetAuditHistoryHandler.cs`

### Infrastructure layer (crear)
- `Storage/FileManagerExportStorage.cs` — adaptador `IExportFileStorage → IAttachmentStorage`
- `Workers/ExportJobsChannelListener.cs` — LISTEN/NOTIFY BackgroundService
- `Workers/ExportJobsWorker.cs` — procesamiento con FOR UPDATE SKIP LOCKED
- `Hubs/ExportJobsHub.cs` — SignalR Hub
- `Persistence/Repositories/ExportJobRepository.cs`

### API layer (crear)
- `Endpoints/Reporting/ReportingEndpoints.cs`
- `Endpoints/Reporting/ExportJobsEndpoints.cs`
- `Endpoints/Reporting/SavedQueriesEndpoints.cs`
- `Endpoints/Reporting/DashboardPreferencesEndpoints.cs`

### Modificar (existentes)
- `Flit.Api/Program.cs` — `AddSignalR()` + `MapHub<ExportJobsHub>("/hubs/export-jobs")`
- `Flit.Api/Endpoints/Analytics/DetailedReportEndpoints.cs` — `[Obsolete]`
- `Flit.Infrastructure/InfrastructureExtensions.cs` — registro de servicios nuevos
- `Flit.Gateway/Program.cs` — `app.UseWebSockets()` (**BLOQUEANTE**)
- `Flit.Gateway/appsettings.json` — SessionAffinity + cluster SignalR

---

## 8. Base de datos — Instrucciones para database-agent

### Paso 0 — G3: ALTER TABLE determinista (aprobado 2026-07-29)
El volumen de `status_history` no bifurca el schema. Se aplica siempre:
```sql
-- ADD COLUMN IF NOT EXISTS con DEFAULT NULL → O(1) en PG17 (sin reescritura de heap)
ALTER TABLE tramites.procedure_instance_status_history
    ADD COLUMN IF NOT EXISTS role_id_at_time           uuid        NULL,
    ADD COLUMN IF NOT EXISTS organization_id_at_time   uuid        NULL,
    ADD COLUMN IF NOT EXISTS organization_type_at_time varchar(20) NULL;
-- El umbral Reporting:MigrationSafety:StatusHistoryRowWarningThreshold (default 500 000)
-- es solo de telemetría/advertencia operativa; no modifica el DDL aplicado.
```

### Nuevas tablas (DDL de referencia completo en docs/design/FEATURE-11076-reporteria-transaccional-v2.md §7)
1. `analytics.export_jobs`
2. `analytics.saved_queries`
3. `analytics.dashboard_preferences`
4. `analytics.report_sla_config`

### Extensión status_history
Columnas: `role_id_at_time uuid NULL`, `organization_id_at_time uuid NULL`, `organization_type_at_time varchar(20) NULL`

### Vista nueva o extendida
Extender `analytics.v_procedure_detail_report` con: `plate`, `vin`, `transit_office_name`, `company_name`, `elapsed_hours_total`

### Migración EF Core
Archivo: `src/Flit.Infrastructure/Migrations/[timestamp]_F11076_ReportingV2.cs`
DDL referencia: `src/Flit.Infrastructure/Persistence/Sql/Ddl/40-F11076-reporting-v2.sql`

---

## 9. Permisos RBAC — Matriz para seed ejecutable

**Módulo seed:** `security.modules { code: "reportes-v2" }`

| Slug | HTTP | Route Pattern |
|------|------|---------------|
| `reporting.read` | GET | `/api/v1/reporting/procedures*` |
| `reporting.detail` | GET | `/api/v1/reporting/procedures/{id}` |
| `reporting.export` | POST/GET | `/api/v1/reporting/exports*` |
| `reporting.export.download` | GET | `/api/v1/reporting/exports/{id}/download-url` |
| `reporting.saved-queries.read` | GET | `/api/v1/reporting/saved-queries*` |
| `reporting.saved-queries.write` | POST/PUT/DELETE | `/api/v1/reporting/saved-queries*` |
| `reporting.schedules.read` | GET | `/api/v1/reporting/schedules*` |
| `reporting.schedules.write` | POST/PUT/DELETE | `/api/v1/reporting/schedules*` |
| `reporting.alerts.read` | GET | `/api/v1/reporting/alerts*` |
| `reporting.alerts.write` | POST/PUT/DELETE | `/api/v1/reporting/alerts*` |
| `reporting.dashboard.preferences` | GET/PUT | `/api/v1/reporting/preferences*` |
| `reporting.audit` | GET | `/api/v1/reporting/procedures/{id}/audit*` |
| `reporting.consolidado` | GET | `/api/v1/reporting/consolidado*` |
| `reporting.productivity` | GET | `/api/v1/reporting/productivity*` |
| `reporting.global` | GET | `/api/v1/reporting/*` (SuperAdmin + ?tenantId) |

**Slugs a eliminar del seed en PR big-bang:** todos los `detailed-report.*`

---

## 10. Límites y configuración operativa

| Parámetro | Valor | Dónde se aplica |
|-----------|-------|-----------------|
| Rango default | 30 días | `GET /reporting/procedures` |
| Rango máximo | 12 meses | Validación en handler |
| Registros por export | máx. 50 000 | `RequestExportHandler` |
| Jobs pending por usuario | máx. 3 | `RequestExportHandler` → 409 |
| TTL download URL | ≤ 15 min | `GetPresignedViewUrlAsync` |
| Retención archivo storage | 30 días | Lifecycle file-manager |
| Retención registro `export_jobs` | 30 días | Cron soft-delete |
| Timeout reset jobs processing | 10 min | Cron o self-healing worker |
| Polling fallback SignalR | 5 s | Cliente frontend |
| Polling fallback worker | 30 s | `ExportJobsChannelListener` |
| Formatos de export | excel, csv, pdf | `RequestExportRequest` |

---

## 11. ADRs generados

| ADR | Archivo | Estado |
|-----|---------|--------|
| ADR-0037 | `services/core-api/docs/adr/ADR-0037-exportaciones-asincronas-listen-notify-signalr.md` | Propuesto |
| ADR-0038 | `services/core-api/docs/adr/ADR-0038-navegacion-reportes-unificada-big-bang.md` | Propuesto |
| ADR-0039 | `services/core-api/docs/adr/ADR-0039-signalr-yarp-session-affinity.md` | Propuesto |

> Los ADRs están en estado **Propuesto**. La transición a **Aceptado** es exclusiva del Líder Técnico humano.

---

## 12. Riesgos principales y mitigaciones

| Riesgo | Severidad | Mitigación |
|--------|-----------|------------|
| `UseWebSockets()` ausente en Gateway | ALTO | Agregar en `Flit.Gateway/Program.cs` antes del primer deploy |
| `FILE_MANAGER_BASE_URL` mismatch | ALTO | Mismo `IOptions<ExportFileManagerOptions>` en DI; validar en CI |
| `Delete()` NO-OP — archivos no eliminables | MEDIO | Documentado como constraint; TTL URL ≤15 min protege acceso |
| YARP RoundRobin + SignalR reconnect | MEDIO | SessionAffinity cookie (ADR-0039) |
| Sin Redis backplane para > 2 réplicas | MEDIO | Activar Redis cuando réplicas > 2 (ADR-0039 revisión) |
| status_history sin datos históricos | BAJO-MEDIO | Backfill NULL; badge "Historial no disponible" |

---

## 13. Checklist de aprobación

- [x] Diseño técnico completo revisado y aprobado por Líder Técnico
- [x] ADR-0037 (export jobs) en estado Propuesto
- [x] ADR-0038 (big-bang) en estado Propuesto
- [x] ADR-0039 (SignalR YARP) en estado Propuesto
- [x] Contrato OpenAPI actualizado con tag "Reporting V2" y paths /api/v1/reporting/*
- [x] Matriz RBAC (15 slugs) aprobada para seed ejecutable
- [x] Eliminación big-bang sin redirect aprobada por PO y Líder Técnico
- [x] database-agent: G3 — ALTER TABLE determinista aprobado y materializado en migración `20260730022248_F11076_ReportingV2` (2026-07-29)
- [ ] infra-agent agrega `UseWebSockets()` en Gateway y valida en DEV
- [ ] tech-lead-agent descompone en HUs con AC Gherkin
