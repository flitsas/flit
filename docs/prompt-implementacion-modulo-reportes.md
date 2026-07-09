# PROMPT — Nuevo módulo de REPORTES Y MÉTRICAS (mejora del módulo Analytics actual)

> Copia todo lo que sigue como prompt para Claude Code (u otro agente), ejecutándolo desde la raíz del repo `flit`.

---

Construye la evolución del módulo de **Reportes** de FLIT 2.0: pestañas temáticas, métricas en línea, telemetría de uso, métricas del Organismo de Tránsito, comparativas, informes programados por correo y alertas por umbral. El trabajo se divide en **4 historias que deben desarrollarse EN PARALELO** con subagentes (Task tool) y contratos acordados de antemano, más una fase de integración secuencial.

## Contexto del sistema (leer antes de codificar)

- Lee `docs/contexto-funcional-flit.md` y `docs/reporte-estado-modulo-analitica.md` (ten en cuenta que este último es PRE-rework: los agregados materializados ya fueron **eliminados** en la migración `HU10430_DropAnalyticsAggregates` y hoy todo lee en vivo).
- Backend `services/core-api/src` — .NET 10, Minimal API + handlers POCO (CQRS manual, sin MediatR), un solo `FlitDbContext`, migraciones por HU, snake_case, RLS/tenant enforcement.
- **Estado actual del módulo Analytics** (base a extender, no reescribir):
  - Endpoints en `Flit.Api/Endpoints/Analytics/AnalyticsEndpoints.cs`: `/overview`, `/productivity/top`, `/procedures`, `/export/excel`, `/export/executive-pdf`, `/monthly-trend`. Policy `AdminCompany`, tenant desde JWT, SuperAdmin puede pasar `tenantId`.
  - Repositorio read-side `Flit.Infrastructure/Persistence/Repositories/AnalyticsReadRepository.cs` — SQL en vivo sobre `tramites.*`.
  - Frontend: `frontend/components/atom/modules/Reportes.tsx` (una sola vista) + `_reportes/` (CategoryDonut, ProductivityCards, ProcedureDetailPanel, DateRangeFilter, CompanySelector, ExportButtons). Recharts disponible.
- **N 03 Estados YA está implementado** (ADR-0022): estados de negocio en español (`borrador`, `anulado`, `preparado`, `entregado`, `aprobado`, `rechazado`) en `Flit.Tramites.Domain/Tramites/Estados/` (`TramiteEstado`, `TramiteStateMachine`, `ITramiteLifecycleService`), transiciones vía `TransitionProcedureInstanceCommand`, y `ProcedureInstanceStatusHistory` ya tiene `ChangedBy`, `Reason` y `Metadata`. **Úsalo como fuente para todas las métricas de estados/OT.**
- Patrones reutilizables: `BackgroundService` estilo outbox (`Flit.Infrastructure/Messaging/*Processor.cs`), `IEmailSender` (`Email/SmtpEmailSender.cs`), export Excel/PDF existente, `ot_api_call_logs` (consumo de APIs externas), `procedure_instance_events` (timeline), adjuntos con soft-delete (documentos reemplazados), validaciones biométricas y preflight snapshots.
- Frontend: wizard server-driven (`TramiteWizard.tsx`), RBAC con `usePermissions`/`PermissionGate`, módulos del dock vía `GET /security/modules`.

## Objetivo y decisiones ya tomadas

1. **Pestañas por TEMA** dentro de Reportes (cada una visible según permiso RBAC): **Resumen general · Operación/Trámites · Organismo de Tránsito · Uso del aplicativo · Productividad**.
2. **Métricas en línea por auto-refresh** (polling 30–60 s, configurable), no SSE/WebSocket.
3. **Incluir instrumentación nueva** (event log de wizard y uso de módulos) — sin esto la pestaña "Uso del aplicativo" no tiene datos.
4. Extras en alcance: **comparativas entre periodos, informes programados por correo, alertas por umbral, filtros avanzados con drill-down**.
5. UX limpia y comprensible para cualquier perfil (operador, supervisor, gerente, gestor OT, cliente): jerarquía visual clara, KPIs grandes con contexto ("¿esto es bueno o malo?" → variación vs. periodo anterior), tooltips explicativos, estados vacíos amables, responsive.

## Contratos compartidos (definir ANTES de paralelizar)

Antes de lanzar los subagentes fija en un archivo de contratos (y en `contracts/openapi` si aplica): nombres de tablas nuevas, DTOs de cada endpoint nuevo, taxonomía de eventos de telemetría (`event_type`, `module`, `step_key`, `instance_id`, `duration_ms`), slugs de permisos RBAC por pestaña (`reportes:resumen:read`, `reportes:ot:read`, etc.), y el shape del parámetro de comparación de periodos (`compareWith=previous_period|previous_year`).

## Plan de ejecución — 4 historias EN PARALELO

### HU-A · TELEMETRÍA DE USO (instrumentación)
- Tabla `analytics.app_usage_events` (tenant_id, user_id, event_type, module, step_key, procedure_instance_id?, metadata jsonb, occurred_at; particionable por fecha, índices por tenant+fecha). Migración EF propia.
- Captura backend: middleware ligero (en `Flit.Api`) que registre acceso por módulo/ruta (ruta → módulo, sin PII sensible), y eventos de wizard emitidos desde los handlers existentes (`GET /wizard` = vista de paso; acciones = avance; instancia anulada/abandonada = derivable).
- Endpoint batch `POST /api/v1/analytics/events` para eventos del frontend (entrada/salida de paso con duración, aborto explícito). Fire-and-forget en el front, tolerante a fallos (nunca romper el wizard por telemetría).
- Métricas derivadas (queries en el read-repo, para HU-B): embudo de pasos con % de abandono por paso, tiempo promedio/mediana por paso y por trámite completo, módulos más usados por tenant/rol, documentos más reemplazados (adjuntos re-subidos), horas pico.
- RNF: escritura asíncrona/no bloqueante, retención configurable, sin degradar latencia de la API (>5 ms presupuesto).

### HU-B · BACKEND DE MÉTRICAS (nuevos endpoints read-side)
Extiende `AnalyticsReadRepository` + `Flit.Analytics.Application` con handlers y endpoints nuevos (misma policy y tenancy que los existentes):
- `GET /analytics/ot-metrics` — todo lo del Organismo de Tránsito sobre `procedure_instance_status_history` (N03): tasa de rechazo por OT/tipo/causal (`Reason`), tiempo promedio y p50/p90 de aprobación (`entregado→aprobado|rechazado`), reincidencia (`rechazado→borrador` y nº de ciclos), ranking de OTs por agilidad, trámites atascados (> N días en un estado, N parametrizable).
- `GET /analytics/funnel` — embudo de estados (borrador→preparado→entregado→aprobado) + embudo de pasos del wizard (de HU-A), con conteos y % de conversión.
- `GET /analytics/usage` — métricas de HU-A agregadas (uso por módulo, tiempo por paso, abandonos, horas pico) + consumo de APIs externas desde `ot_api_call_logs` (llamadas, errores, latencia por proveedor — costo operativo).
- `GET /analytics/live-overview` — snapshot liviano para el auto-refresh: trámites hoy por estado, radicados/aprobados/rechazados del día, atascados, validaciones de identidad pendientes, salud de integraciones (última hora). Optimizado (< 300 ms, una sola ronda de queries).
- **Comparativas**: todos los endpoints de métricas aceptan `compareWith` y devuelven el periodo actual + anterior + variación %. 
- Todos los endpoints con filtros: rango de fechas, OT, tipo de trámite, operador, estado, causal. Paginación donde aplique. Tests de integración por endpoint (incluye aislamiento multi-tenant).

### HU-C · FRONTEND — REPORTES CON PESTAÑAS
Reestructura `Reportes.tsx` en un layout de pestañas (conserva y recoloca lo existente, no lo dupliques):
- **Resumen general**: KPIs grandes con variación vs. periodo anterior (↑↓ y color), tendencia mensual (línea), donuts por categoría, panel "ahora mismo" alimentado por `/live-overview` con **auto-refresh 30–60 s** (visible: "actualizado hace Xs", pausable).
- **Operación/Trámites**: embudo de estados (funnel chart), trámites atascados (tabla accionable → link al trámite), distribución por tipo/OT, comparativas.
- **Organismo de Tránsito**: tasas de rechazo (barras por OT y por causal), tiempos de aprobación (boxplot o barras p50/p90), ranking de OTs, reincidencia.
- **Uso del aplicativo**: embudo de pasos del wizard con % de abandono, tiempo por paso, módulos más usados, documentos más reemplazados, consumo/errores de APIs externas, horas pico (heatmap).
- **Productividad**: top productores existente + detalle por operador (radicados, aprobados, tiempo promedio), comparativa entre operadores.
- Transversal: filtros globales persistentes entre pestañas (fecha, OT, tipo, operador) + filtros específicos por pestaña; **drill-down**: clic en cualquier gráfica abre el panel de detalle filtrado (`ProcedureDetailPanel` reutilizado); export Excel/PDF por pestaña; visibilidad de pestañas por permiso RBAC (`PermissionGate`); loading skeletons, estados vacíos con explicación, tooltips "cómo se calcula esta métrica"; Recharts; responsive; textos en español.
- Tests: vitest para lógica de filtros/formatos y render por pestaña según permisos.

### HU-D · INFORMES PROGRAMADOS + ALERTAS POR UMBRAL
- Tablas `analytics.report_schedules` (tenant, tipo de informe, periodicidad diaria/semanal/mensual, destinatarios, formato excel/pdf, activo) y `analytics.alert_rules` (tenant, métrica, operador, umbral, ventana, destinatarios, activo) + `analytics.alert_events` (historial de disparos, para evitar spam — cooldown).
- `BackgroundService` (sigue el patrón de `Flit.Infrastructure/Messaging/*Processor.cs`) que: (a) según cron simple evalúa schedules y envía el informe (reutiliza los generadores de export Excel/PDF existentes) vía `IEmailSender`; (b) evalúa reglas de alerta (ej. tasa de rechazo OT > X%, trámites atascados > N días, errores de API externa > Y en 1 h) y notifica por correo.
- Endpoints CRUD `/analytics/report-schedules` y `/analytics/alert-rules` (policy `AdminCompany`) + UI de administración dentro de Reportes (modal/subsección "Programación y alertas").
- Tests: evaluación de reglas (dispara/no dispara/cooldown) y generación de schedule.

### Fase de integración (secuencial, tras las 4 HU)
1. Merge; wiring en `Program.cs`/DI; registrar permisos RBAC nuevos (seed) y módulo en el dock si aplica.
2. `dotnet build` + suite backend completa + `vitest` en verde; revisar contratos OpenAPI actualizados en `contracts/`.
3. Verificación end-to-end: crear trámites de prueba recorriendo estados N03 (con causales de rechazo) → validar que cada pestaña muestra datos coherentes entre sí (mismo total en donut, funnel y detalle), que el drill-down filtra bien, que el auto-refresh actualiza el live-overview, que un schedule envía correo (ConsoleEmailSender en dev) y que una regla de alerta dispara y respeta cooldown.
4. Verificación de rendimiento: `/live-overview` < 300 ms y pestañas < 1.5 s con volumen de seed.

## Reglas
- Convenciones del repo: snake_case en BD, una migración por HU, Minimal API + handlers POCO, sin MediatR, español en UI y mensajes de negocio, aislamiento multi-tenant en TODA query nueva (tenant del JWT; SuperAdmin cross-tenant).
- No rompas los 6 endpoints de analytics existentes ni la vista actual: evoluciónalos dentro de las pestañas.
- La telemetría jamás debe afectar el flujo funcional (fire-and-forget, try/catch, sin PII sensible en metadata).
- Cero errores de compilación y cero tests rotos al finalizar. Documenta el diseño de telemetría y el modelo de alertas en un ADR nuevo.
