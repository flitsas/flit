# Estándar FLIT de procesos automáticos / periódicos

**Feature:** [#12122](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12122)  
**ADR:** [ADR-0059](decisions/ADR-0059-estandar-procesos-automaticos-periodicos.md) (Propuesto)  
**Fecha:** 2026-09-11

Esta guía es la receta operativa para **cualquier job futuro** (ICT, Quipux, analítica, outbox, retención). El ADR-0059 fija el *porqué*; aquí está el *cómo*. No sustituye a [ADR-0024 (telemetría / anti-cron)](../services/core-api/docs/adr/ADR-0024-telemetria-uso-y-alertas.md) ni a la migración ICT en [`docs/migracion-ict`](migracion-ict/).

## 1. Decisión en una frase

Todo proceso periódico corre **in-process** como `BackgroundService` del servicio dueño (`core-api` o `core-ict`), lee cadencia desde **una fila de configuración en PostgreSQL**, es editable por **SuperAdmin** en UI, y reclama trabajo con `FOR UPDATE SKIP LOCKED` para sobrevivir a varias réplicas. **No** se usan EventBridge, Lambda, Hangfire, Quartz ni cron del SO.

## 2. Referencias canónicas (no reinventar)

| Pieza | Dónde |
|---|---|
| Patrón Quipux (cadencia en BD, workers in-process) | HU #10710 + `QuipuxRegisterProcessor` / `QuipuxStatusPollProcessor` + `admin.quipux_settings` |
| Migración ICT v1 Lambda → v2 hosted | [`docs/migracion-ict`](migracion-ict/) (PROMPT-migracion-ict-core-ict.md §A.6) |
| Anti-cron / multi-réplica | [ADR-0024](../services/core-api/docs/adr/ADR-0024-telemetria-uso-y-alertas.md) — scheduler `AnalyticsSchedulerProcessor`, claim `SKIP LOCKED` |
| Snapshot en caliente ICT | `IctJobSettingsProvider` (`services/core-ict/.../Jobs/IctJobSettings.cs`) |
| Contrato SuperAdmin ICT | `GET`/`PUT` `/api/v1/admin/ict/job-settings` (HU #12512, OpenAPI `core-api.v1.yaml`) |
| Catálogo ICT + último run | `GET` `/api/v1/admin/ict/jobs` y `GET` `/api/v1/admin/ict/jobs/{jobKey}/runs` (HU #12513) |
| UI SuperAdmin | `/admin/jobs` (catálogo unificado, HU #12514) y `/admin/jobs/ict` (formulario, HU #12123) |

## 3. Checklist obligatorio para un job nuevo

1. **Hosted service** en el proceso del owner (`AddHostedService<>()`). Poll + `StartupDelay`. Un fallo de un ítem no tumba el ciclo (`try/catch` por unidad de trabajo).
2. **Configuración en BD** (fila singleton o tabla de schedules). Defaults en código como fallback. El worker relee en cada ciclo (o vía provider con throttle, como `IctJobSettingsProvider`, intervalo mínimo 15 s). Un `UPDATE` debe aplicar **sin redeploy**.
3. **UI SuperAdmin** en el catálogo `/admin/jobs` (o enlace a un formulario existente, p. ej. Quipux `/admin/quipux`). Cuatro estados: vacío, carga, error, lleno. Guard SuperAdmin. **Sin secretos** en catálogo ni en last-run.
4. **Claim multi-réplica:** `SELECT … FOR UPDATE SKIP LOCKED` (o equivalente de outbox) antes de procesar. No “elegir” réplica por env.
5. **Tipología visible:** cada fila declara uno o más de `BD` / `ENDPOINT_INTERNO` / `ENDPOINT_EXTERNO`.
6. **Owner explícito:** `core-api` o `core-ict` (nunca “Lambda”).
7. **Bitácora:** si el job escribe runs (`ict.job_runs`, outbox, etc.), el catálogo muestra último run **sin** `error_message` ni payloads. Si no escribe runs (p. ej. Retention ICT), `HasPipelineRuns = false`.
8. **Zona:** `America/Bogota` con fallback Windows `SA Pacific Standard Time` / UTC-5.
9. **OpenAPI** si hay endpoint de admin; policy SuperAdmin (o la policy del módulo, nunca anónima).

## 4. Prohibido

- EventBridge, Lambda programada, Hangfire, Quartz, cron de Linux/Windows, Azure Functions timer como “el” scheduler de negocio.
- Cadencia solo en `appsettings` / variables de entorno **sin** fila en BD (el env es fallback, no fuente de verdad operativa).
- Gate `IsDevelopment()` para encender/apagar un job (el compose de PDN ha usado `ASPNETCORE_ENVIRONMENT=Development`).
- Meter Confirmación RUNT (`/admin/plataforma/confirmacion-runt`) en este catálogo: es **otro flujo**. El Orchestrator ICT consulta RUNT/familia del **pre-trámite** vía proveedor; no es Confirmación RUNT.

## 5. Cómo lo hace ICT hoy

- Tabla singleton `ict.job_settings`. SuperAdmin lee/escribe con `GET`/`PUT /api/v1/admin/ict/job-settings` en **core-api** (no en el prefijo YARP `/api/v1/ict/**`).
- `IctJobSettingsProvider` cachea el snapshot; los cinco `BackgroundService` de pipeline + Retention consultan `Current` cada ciclo. Ante error de lectura se conserva el último snapshot (o defaults).
- Catálogo estático `IctJobCatalog`: `business-validation`, `external-validation`, `orchestrator`, `send-to-core-api`, `webhook-notification`, `retention`. Owner `core-ict`.
- Ventana horaria 0–23 con **start < end** (no overnight). Poll 1–3600 s, concurrencia 1–100, lote 1–5000.

## 6. Cómo lo hace Quipux hoy (patrón HU #10710)

- `admin.quipux_settings` (`enabled`, intervalos de registro y sondeo). El worker relee **en cada ciclo**.
- `QuipuxRegisterProcessor` y `QuipuxStatusPollProcessor` sustituyen las Lambdas v1 `registerDocument` / `validateStatusDocument`.
- UI de secretos y cadencia permanece en `/admin/quipux`; el catálogo `/admin/jobs` **solo enlaza**, no reexpone credenciales.

## 7. Confirmación RUNT (fuera de alcance)

`/admin/plataforma/confirmacion-runt` no es un job de este estándar. El banner del catálogo `/admin/jobs` debe dejarlo explícito para no mezclarlo con el Orchestrator ICT.
