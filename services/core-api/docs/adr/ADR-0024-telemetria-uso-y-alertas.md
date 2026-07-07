# ADR-0024 — Telemetría de uso e informes programados con alertas (Reportes 2.0)

- **Estado**: aceptada · 2026-07-07
- **Módulo**: Analytics (Reportes 2.0 — HU-A telemetría de uso, HU-D programación y alertas)
- **Requerimientos**: Feature Reportes 2.0; contrato normativo `docs/contratos-reportes-v2.md`
  (§4.6, §7 y §8); HU10621 (`analytics.app_usage_events`), HU10624
  (`analytics.report_schedules` / `alert_rules` / `alert_events`)

> Nota de numeración: este ADR se planificó como "ADR-0023" en el contrato de Reportes 2.0,
> pero ese número ya fue asignado a `ADR-0023-catalogo-global-roles.md` (HU #10505); se
> publica como ADR-0024 para no duplicar la serie.

## Contexto

Reportes 2.0 añade dos capacidades que no existían en el módulo de analítica:

1. **Métricas de uso del aplicativo** (pestaña "Uso"): qué módulos se abren, cómo se recorre
   el wizard de trámites (vistas, abandonos, duración por paso), horas pico. Ninguna tabla
   operacional captura esto; hace falta una fuente de eventos de uso, alimentada desde el
   frontend (batch `POST /api/v1/analytics/events`) y desde el backend
   (`UsageTelemetryMiddleware`).
2. **Informes periódicos por correo y alertas por umbral** (panel "Programación y alertas"):
   el tenant define informes daily/weekly/monthly y reglas tipo "tasa de rechazo > 25 % en
   24 h" que deben evaluarse solas y notificar por email.

Restricciones que condicionan el diseño:

- La telemetría **no puede degradar el flujo funcional**: el wizard de radicación y las
  APIs operativas son el camino crítico del negocio; perder eventos de uso es aceptable,
  añadir latencia o errores no.
- Multi-tenant con RLS (`app.current_tenant_id`) y filtro explícito por `tenant_id` en toda
  query nueva (§9 del contrato); sin PII fuera de las tablas operacionales.
- El API puede correr en **varias réplicas**: cualquier scheduler debe garantizar que un
  informe/alerta se procese exactamente una vez por ventana.
- `IEmailSender` (SMTP actual) solo soporta `HtmlBody`: **no hay adjuntos**.
- El runtime publica con `InvariantGlobalization` en algunos entornos: la resolución de la
  zona horaria de negocio (**America/Bogota**, §0 del contrato) no puede asumir ICU/IANA.
- El equipo ya opera dos patrones probados: hosted services tipo
  `IdentityValidationOutboxProcessor` y lectura analítica 100 % en vivo (ADR-0021).

## Decisión

### 1. Telemetría de uso (HU-A): tabla append-only + canal en memoria + writer por lotes

**Tabla** `analytics.app_usage_events` (§7 del contrato): `id uuidv7`, `tenant_id`,
`user_id NULL`, `event_type varchar(40)`, `module`, `step_key`, `procedure_instance_id NULL`,
`duration_ms >= 0`, `metadata jsonb DEFAULT '{}'`, `occurred_at`, `created_at`; índices por
`(tenant_id, occurred_at)`, `(tenant_id, event_type, occurred_at)` y parcial por instancia;
RLS `tenant_isolation` (patrón §0). Es **append-only**: nunca se actualiza, solo se inserta y
se purga por retención.

**Taxonomía CERRADA** de `event_type` (8 valores, `UsageEventTypes`): `module_view`,
`api_module_access`, `wizard_server_view`, `wizard_step_view`, `wizard_step_complete`,
`wizard_step_exit`, `wizard_abandon`, `wizard_complete`. El endpoint batch **descarta
silenciosamente** cualquier tipo desconocido (responde 202 contando solo lo aceptado): la
taxonomía evoluciona por código y contrato, no por datos libres.

**Escritura asíncrona en dos etapas** — la decisión central es que la telemetría **nunca
bloquea ni rompe el flujo funcional**:

- Cola en memoria `ChannelUsageEventQueue` (`System.Threading.Channels`, bounded a
  **10 000** con `BoundedChannelFullMode.DropWrite`): productor (middleware y endpoint batch)
  hace `TryWrite` no bloqueante; ante saturación los eventos nuevos **se descartan**.
- `UsageEventWriterProcessor` (`BackgroundService`, patrón `IdentityValidationOutboxProcessor`)
  drena la cola en lotes de hasta **200 eventos o cada 2 s** (lo que ocurra primero) e inserta
  con `AddRange + SaveChanges`. Si un lote falla, se pierde (no se reencola: evita lotes
  veneno) y se registra en log.
- Presupuesto del camino síncrono **< 5 ms**: el middleware solo mapea ruta→módulo, muestrea y
  encola (cero I/O); todo el camino va en `try/catch` — un fallo de telemetría jamás llega al
  caller (el endpoint batch degrada incluso errores inesperados a `202 {accepted: 0}`).

**Sin PII en `metadata`** (prohibido: nombres, documentos, emails, placas, VIN): el endpoint
solo acepta objetos JSON pequeños (≤ 2 000 chars serializado; lo demás colapsa a `{}`) y el
tenant/usuario salen SIEMPRE del JWT, nunca del body (no hay suplantación de atribución).

**Retención configurable**: `AnalyticsTelemetry:RetentionDays` (default **90**). El propio
writer ejecuta una limpieza diaria paginada (5 000 filas por página, memoria acotada) que
borra eventos con `occurred_at` fuera de la ventana. `AnalyticsTelemetry:Enabled` apaga la
captura sin desregistrar servicios.

**Muestreo de `api_module_access`**: 1 evento por **usuario + módulo + minuto** (caché en
memoria acotado a 10 000 entradas en el middleware). Da la señal "qué módulos usa quién y
cuándo" sin registrar cada request (el volumen sería proporcional al tráfico de la API).

### 2. Informes programados y alertas (HU-D): scheduler de poll con claim transaccional

**Tablas** (§8 del contrato, las tres con RLS `tenant_isolation` e índices por tenant):

- `analytics.report_schedules`: `report_type ∈ (resumen|operacion|ot|uso|productividad)`,
  `frequency ∈ (daily|weekly|monthly)`, `day_of_week 0-6` / `day_of_month 1-28` según
  frecuencia, `send_hour 0-23` (hora Bogotá), `format ∈ (excel|pdf)`, `recipients jsonb`
  (1..10 emails), `is_active`, `last_sent_at`, soft delete (`deleted_at`).
- `analytics.alert_rules`: `metric ∈ (rejection_rate_pct|stuck_count|external_api_errors|`
  `pending_identity_validations)`, `operator ∈ (gt|gte|lt|lte)`, `threshold numeric(12,2)`,
  `window_minutes 5..43200` (default 1440), `cooldown_minutes 5..10080` (default 240),
  `recipients`, `is_active`, `last_triggered_at`, soft delete.
- `analytics.alert_events`: historial de disparos (`alert_rule_id` FK ON DELETE CASCADE,
  `triggered_at`, `metric_value`, `threshold`, `notified`, `recipients`, `message`).

**Scheduler** `AnalyticsSchedulerProcessor` (`BackgroundService`): **poll cada 60 s** (sin
cron externo ni broker). En cada ciclo:

- **Informes**: evalúa los schedules activos con `ScheduleDueEvaluator` y, para cada vencido,
  abre transacción, lo **reclama con `SELECT … FOR UPDATE SKIP LOCKED`** y **sella
  `last_sent_at` ANTES de enviar** (commit incluido). Con varias réplicas, cada schedule lo
  envía exactamente una (la que gana el claim; las demás lo saltan sin bloquearse). Tradeoff
  aceptado: si el proceso muere entre el sello y el envío, ese periodo se pierde (preferible a
  duplicar correos).
- **Alertas**: por regla activa consulta su métrica (`IAlertMetricsReadRepository`) en su
  `window_minutes`; si `AlertRuleEvaluator.ShouldTrigger` (operador cumplido **y** cooldown
  vencido) registra el `alert_event` y sella `last_triggered_at` en la misma transacción, y
  después notifica por correo (best-effort: `notified` se marca tras el envío).
- Un fallo en un schedule/regla no tumba el ciclo (try/catch por elemento, log estructurado).

**Ventanas de envío** (idempotencia por periodo): un schedule "vence" si es su día/hora
**local** y `last_sent_at` no pertenece a la ventana actual — día calendario para daily,
**semana ISO** (lunes-domingo) para weekly, mes calendario para monthly. El informe cubre el
periodo **vencido** (ayer / semana ISO anterior / mes anterior). Hora de negocio
**America/Bogota** con fallback en cascada por `InvariantGlobalization`:
`America/Bogota` (IANA) → `SA Pacific Standard Time` (Windows) → huso fijo UTC-5 (Colombia no
tiene horario de verano, los tres son equivalentes).

**Evaluadores puros y testeables**: `ScheduleDueEvaluator` (vencimiento y periodo) y
`AlertRuleEvaluator` (operador + cooldown) son funciones estáticas **sin I/O**, con el reloj y
la zona horaria como parámetros → tests unitarios deterministas sin base de datos.

**Cooldown con historial**: `last_triggered_at` + `cooldown_minutes` evita el spam de una
métrica que oscila alrededor del umbral; cada disparo real queda en `alert_events`
(`GET /analytics/alert-events` paginado), de modo que el silencio del cooldown no borra la
evidencia.

**Correo HTML sin adjunto**: `IEmailSender` solo soporta `HtmlBody`, así que el informe
programado envía el **resumen HTML con los KPIs del periodo** (vía
`IAnalyticsReadRepository`), asunto `[FLIT] {name} — {periodo}`. El adjunto real
(`IProcedureExcelExporter` / `IExecutiveSummaryPdfGenerator` ya existen) queda como **mejora
futura** cuando el sender soporte attachments; `format` (excel|pdf) ya se persiste para ese
momento.

**Métricas de alerta soportadas** (SQL propio de `AlertMetricsReadRepository`, siempre GUC
RLS + `WHERE tenant_id = @tenant`):

| Métrica | Ventana | SQL (resumen) |
|---|---|---|
| `rejection_rate_pct` | `window_minutes` | `rechazados / (aprobados+rechazados) * 100` sobre transiciones de `procedure_instance_status_history` con `changed_at >= now() - window` (0 sin decididos) |
| `stuck_count` | no aplica (stuckDays = 7 fijo) | instancias en estado NO final (`aprobado`/`anulado`) sin transición hace > 7 días (o desde `created_at` si nunca transicionó) |
| `external_api_errors` | `window_minutes` | `count(*)` de `admin.ot_api_call_logs` con `response_code >= 400 OR error_message IS NOT NULL` en la ventana |
| `pending_identity_validations` | estado actual | validaciones biométricas en `enviado / en_proceso / pendiente_envio` |

**API**: CRUD `/api/v1/analytics/report-schedules` y `/api/v1/analytics/alert-rules` +
`/api/v1/analytics/alert-events` con policy `AdminCompanyPolicy`, tenant concreto obligatorio
(SuperAdmin con `?tenantId=`, sin vista global), validaciones 400 en español y 404 para ids
de otro tenant (contrato §4.7; espejo en `contracts/openapi/core-api.v1.yaml`).

## Alternativas descartadas

- **Escritura síncrona de telemetría en el request** (insert en el pipeline HTTP): añade
  latencia y una dependencia de BD al camino crítico; un incidente en `analytics.*` rompería
  la radicación. Descartada: viola la restricción principal.
- **Outbox en BD para telemetría** (patrón `identity_validation_outbox`): garantiza no perder
  eventos, pero duplica la escritura (outbox + tabla final) en el flujo síncrono, que es
  exactamente lo que se quiere evitar. La durabilidad extra no paga: la telemetría es
  best-effort por definición (perder eventos bajo saturación o durante un deploy es
  aceptable). El outbox se mantiene donde sí hay garantía de entrega que cumplir (webhooks,
  notificaciones de estado).
- **SSE/WebSocket para el panel "en línea"** (`live-overview`): infraestructura de conexiones
  persistentes (afinidad, reconexión, fan-out multi-tenant, interacción con YARP) para un
  dashboard interno cuya frescura objetivo es de segundos, no milisegundos. Se eligió
  **polling de 30–60 s** desde el frontend (patrón `BiometricStep`, pausado con
  `document.visibilityState`) contra un endpoint de una sola ronda de queries (< 300 ms).
- **Cron/broker externo para los informes** (Hangfire, Quartz, colas): un hosted service de
  poll 60 s con claim `FOR UPDATE SKIP LOCKED` cubre el requisito multi-réplica sin dependencia
  nueva, con el mismo patrón operativo que los procesadores existentes.

## Consecuencias

- (+) El camino crítico de trámites queda blindado: la telemetría es fire-and-forget con
  presupuesto < 5 ms, y cualquier fallo (BD caída, cola llena, payload inválido) se degrada a
  descarte con log.
- (+) Informes y alertas correctos en multi-réplica sin coordinación externa: claim
  transaccional + sellado previo al envío; idempotencia por ventana (día/semana ISO/mes) en
  hora Bogotá.
- (+) Núcleo de decisión (vencimiento, disparo, cooldown) en funciones puras → cobertura
  unitaria determinista; la infraestructura (SQL, correo, canal) se prueba por separado.
- (−) La telemetría puede perder eventos (DropWrite, lote fallido, réplica reiniciada): las
  métricas de uso son **aproximadas por diseño**. Aceptado y documentado en el contrato.
- (−) Si el proceso muere entre sellar `last_sent_at`/`last_triggered_at` y enviar el correo,
  ese envío se pierde hasta la siguiente ventana (informes) o queda el `alert_event` con
  `notified = false` (alertas). Preferido a duplicar notificaciones.
- (−) El informe llega como resumen HTML, sin el archivo excel/pdf prometido por `format`:
  limitación de `IEmailSender` (sin adjuntos), registrada como mejora futura.
- (−) Volumen nuevo en BD (`app_usage_events`): acotado por muestreo de `api_module_access`,
  taxonomía cerrada y retención de 90 días con purga diaria paginada.

## Relación con otros ADRs

- **ADR-0021 (analítica 100 % en vivo)**: los endpoints de métricas de Reportes 2.0
  (`ot-metrics`, `funnel`, `usage`, `live-overview`) siguen esa decisión — leen las tablas
  operacionales al vuelo, sin agregados materializados. `app_usage_events` no la contradice:
  no es un agregado sino una **fuente primaria** (hechos de uso que no existen en ninguna
  otra tabla), que también se lee en vivo.
- **ADR-0022 (estados N03)**: `procedure_instances.status` y
  `procedure_instance_status_history` con el vocabulario español
  (`borrador|anulado|preparado|entregado|aprobado|rechazado`) son la fuente de las métricas OT
  y de las métricas de alerta `rejection_rate_pct` / `stuck_count`; el historial por
  transición (RF05) es lo que hace calculables tiempos de decisión, reincidencia y funnel.
- **ADR-0023 (catálogo global de roles)**: sin relación funcional; solo explica el salto de
  numeración de este ADR.

## Referencias

- `docs/contratos-reportes-v2.md` — contrato normativo Reportes 2.0 (§4.6, §7, §8)
- `contracts/openapi/core-api.v1.yaml` — paths `/api/v1/analytics/*` de Reportes 2.0
- `Flit.Infrastructure/Telemetry/` — cola, writer, opciones y taxonomía (HU-A)
- `Flit.Api/Middleware/UsageTelemetryMiddleware.cs` — muestreo server-side (HU-A)
- `Flit.Infrastructure/Analytics/Scheduling/` — scheduler, evaluadores y SQL de alertas (HU-D)
- `Flit.Analytics.Application/Scheduling/` — DTOs, validación y handlers CRUD (HU-D)
- DDL: `Persistence/Sql/Ddl/30-HU10621-app-usage-events.sql` y
  `31-HU10624-analytics-schedules-alerts.sql`
