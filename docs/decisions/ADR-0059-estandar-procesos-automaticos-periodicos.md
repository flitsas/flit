# ADR-0059: Estándar de procesos automáticos / periódicos (BackgroundService + config en BD)

**Fecha**: 2026-09-11  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, core-api / core-ict  
**Tags**: arquitectura, backend, frontend, jobs, BackgroundService, SuperAdmin, Feature-12122

**No reutiliza ADR-0055** (`ADR-0055-captura-dual-hecho-prenda-levantar-inscribir.md`, prenda). El siguiente número libre tras ADR-0058 (banners) es **0059**.

## Contexto

FLIT 1.0 ejecutaba integraciones periódicas como Lambdas + EventBridge (ICT: cinco jobs programados; Quipux: `registerDocument` / `validateStatusDocument`). En 2.0 el runtime es un servicio ASP.NET Core en VPS/k3s, a menudo con **varias réplicas**.

Ya hay tres implementaciones que convergieron al mismo patrón, pero no había un ADR ni una guía de repo que lo dejara obligatorio para el siguiente job:

- **Quipux** (HU #10710): `QuipuxRegisterProcessor` / `QuipuxStatusPollProcessor`, cadencia en `admin.quipux_settings`, UI `/admin/quipux`.
- **Analítica / anti-cron** ([ADR-0024](../../services/core-api/docs/adr/ADR-0024-telemetria-uso-y-alertas.md)): `AnalyticsSchedulerProcessor` con poll 60 s y `FOR UPDATE SKIP LOCKED`; descarta Hangfire/Quartz/colas externas.
- **ICT** ([`docs/migracion-ict`](../migracion-ict/)): cinco `BackgroundService` + Retention; snapshot vía `IctJobSettingsProvider` sobre `ict.job_settings`.

El Feature [#12122](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12122) cierra el hueco operativo: SuperAdmin ve y ajusta cadencia ICT (`GET`/`PUT /api/v1/admin/ict/job-settings`, HU #12512) y un catálogo unificado `/admin/jobs` (HU #12513–#12514). Sin un ADR, el siguiente módulo podría reintroducir cron externo o cadencia solo en `appsettings`.

Restricciones: multi-réplica; zona `America/Bogota`; SuperAdmin sin secretos en el catálogo; Confirmación RUNT **no** es este catálogo; contrato ICT vive en **core-api** (`/api/v1/admin/ict/**`), no en YARP `/api/v1/ict/**`.

## Decisión

Adoptar como estándar de plataforma: **todo job periódico es un `BackgroundService` in-process del servicio dueño, con configuración en PostgreSQL, UI SuperAdmin y claim `FOR UPDATE SKIP LOCKED`**. Receta operativa en [`docs/estandar-jobs-periodicos-flit.md`](../estandar-jobs-periodicos-flit.md).

## Alternativas consideradas

### Opción 1: Estándar in-process (BackgroundService + BD + UI) — elegida

**Pros:**
- Reutiliza Quipux HU #10710, ADR-0024 y `IctJobSettingsProvider` sin runtime nuevo.
- Cadencia cambia en caliente (UPDATE en BD; ICT con throttle 15 s).
- Multi-réplica resuelto con el mismo claim que outbox/analítica.
- SuperAdmin opera desde `/admin/jobs` sin tocar GitOps para un poll.

**Cons:**
- Cada servicio dueño registra sus hosted services; no hay un “job engine” único.
- Un proceso saturado comparte CPU con HTTP del mismo host.

**Esfuerzo:** S (documentar + catálogo; el runtime ya existe)  
**Riesgos:** Un job pesado puede competir con el API; se mitiga con lotes/concurrencia en BD (clamps ICT 1–5000 / 1–100).

### Opción 2: Scheduler externo (Hangfire, Quartz, EventBridge, Lambda, cron SO)

**Pros:**
- Aislamiento de proceso; dashboards de terceros; “lo conocemos de v1”.

**Cons:**
- Contradice ADR-0024 (anti-cron) y la migración ICT (`docs/migracion-ict`).
- Credenciales, red y fallos de un runtime más; duplicados si no hay claim en BD.
- Cadencia fuera de la UI SuperAdmin o en dos sitios.

**Esfuerzo:** L  
**Riesgos:** Drift v1/v2, secretos en Lambdas, doble ejecución en redeploy.

### Opción 3: Motor genérico de jobs compartido (nuevo microservicio scheduler)

**Pros:**
- Un solo catálogo de runtime; escala independiente del API.

**Cons:**
- Servicio, contrato, HA y operación nuevos para un problema ya resuelto in-process.
- Atrasa el Feature #12122 (formulario ICT + catálogo) sin ganar el recorte pedido.

**Esfuerzo:** L  
**Riesgos:** Over-engineering; dos dueños (scheduler vs dominio) para un poll.

## Tradeoff aceptado

Se elige la **Opción 1** porque el patrón ya corre en producción (Quipux, analítica, ICT). El Feature #12122 no inventa un orquestador: expone lo que ya existe y lo deja escrito. Un motor genérico (Opción 3) queda fuera de alcance; si algún día se justifica, este ADR se supersede con número nuevo — **nunca reutilizando 0055**.

## Consecuencias

### Lo que se gana

- Receta única para el siguiente job: hosted + fila BD + UI + `SKIP LOCKED`.
- SuperAdmin ve ICT y Quipux en `/admin/jobs` sin mezclar Confirmación RUNT.
- Contrato real documentado: `GET`/`PUT /api/v1/admin/ict/job-settings`.

### Lo que se pierde

- Libertad de meter un cron “rápido” en GitOps/Lambda para un piloto.
- Un dashboard Hangfire de fábrica.

### Cambios operacionales

- Jobs nuevos entran al catálogo SuperAdmin (estático + last run) o como enlace a su formulario.
- Cambiar poll/lote/concurrencia ICT: UI `/admin/jobs/ict` o PUT; `IctJobSettingsProvider` aplica en el siguiente ciclo.
- Quipux: secretos siguen en `/admin/quipux`; el catálogo solo enlaza.

## ADRs relacionados

- [ADR-0024](../../services/core-api/docs/adr/ADR-0024-telemetria-uso-y-alertas.md) — telemetría / informes; descarta cron externo (Hangfire, Quartz, colas).
- [ADR-0055](../../services/core-api/docs/adr/ADR-0055-captura-dual-hecho-prenda-levantar-inscribir.md) — **otro tema** (prenda); no supersede ni reutiliza número.
- [ADR-0047](ADR-0047-gate-navegacion-dock-igual-url.md) — gate del dock SuperAdmin (`/admin/jobs`).

## Notas para agentes

- **Backend Agent**: nuevo job = `BackgroundService` + tabla/settings + OpenAPI admin si aplica. No Hangfire. ICT: no colocar estos endpoints bajo `/api/v1/ict` (YARP). Usar `IctJobSettingsProvider` como referencia de snapshot.
- **Frontend Agent**: catálogo `/admin/jobs`, formulario dueño, 4 estados, SuperAdmin, tipología BD/ENDPOINT_INTERNO/ENDPOINT_EXTERNO, banner RUNT vs Confirmación RUNT.
- **QA Agent**: TCs de SuperAdmin vs 403; clamps; last run sin secretos; Retention sin bitácora.
- **Security Agent**: catálogo y last-run sin secretos, tokens ni `error_message` con PII.
- **Infra Agent**: no programar EventBridge/cron para estos jobs; el proceso del servicio es el scheduler.

## Referencias externas

- Feature #12122; HUs #12512, #12513, #12123, #12514, #12515.
- HU #10710 (parametrización / patrón Quipux).
- [`docs/migracion-ict`](../migracion-ict/).
- [`docs/estandar-jobs-periodicos-flit.md`](../estandar-jobs-periodicos-flit.md).
- OpenAPI: `contracts/openapi/core-api.v1.yaml` (`/api/v1/admin/ict/job-settings`).
