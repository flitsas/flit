# ADR-0021: Fuente de datos de la analítica — agregados con refresh vs. lectura en vivo de trámites

**Fecha**: 2026-06-30
**Status**: Aceptado
**Deciders**: Juan Felipe Montoya Garcia (Líder Técnico) — aceptado 2026-06-30, Feature #10139
**Decisión aceptada**: Opción C — Lectura 100% en vivo (eliminar agregados)
**Tags**: arquitectura, backend, analitica, tramites, reporting, performance

## Contexto

El Feature #10139 (Dashboard de Trámites Operativos) entregó la analítica con una **arquitectura de datos híbrida**:

- `GET /api/v1/analytics/overview` y `GET /api/v1/analytics/productivity/top` leen **tablas agregadas materializadas** (`analytics.procedure_metrics_daily`, `analytics.user_productivity_daily`).
- `GET /api/v1/analytics/procedures` y `GET /api/v1/analytics/export/excel` leen **en vivo** desde `tramites.procedure_instances` / `procedure_types` / `identity.users`.

El "puente" que puebla los agregados es la función SQL `analytics.refresh_procedure_aggregates(tenant, from, to)` (`20-HU10240-analytics-refresh.sql`). **Solo se invoca una vez, en el seed de Development** (`21-HU10240-analytics-dev-seed.sql`). No existe `BackgroundService`, endpoint, trigger ni cron que la ejecute en runtime. Diagnóstico completo en `docs/reporte-estado-modulo-analitica.md`.

**Consecuencia:** en QA/PROD los agregados quedan vacíos → donuts, Top 5 y PDF ejecutivo salen en cero, mientras la tabla de detalle y el Excel sí muestran trámites reales (incoherencia visible donut↔detalle). Restricciones: multi-tenant con RLS (`app.current_tenant_id`); volumen esperado moderado por tenant; el equipo ya opera un patrón de hosted service (`IdentityValidationOutboxProcessor`).

## Decisión

Se adopta la **Opción C — Lectura 100% en vivo**: `GetOverviewAsync` y `GetTopProducersAsync` se reescriben para consultar directamente `tramites.*` (igual que ya hace `GetProcedureDetailsAsync`), y se **eliminan** las tablas `analytics.procedure_metrics_daily` / `analytics.user_productivity_daily` y la función `analytics.refresh_procedure_aggregates()`. Una sola fuente de verdad.

## Alternativas consideradas

### Opción A: Mantener agregados + refresh programado y on-demand

`BackgroundService` (`AnalyticsRefreshHostedService`) que, cada N minutos, recorre tenants activos e invoca `refresh_procedure_aggregates(tenant, hoy-1, hoy)` (ventana incremental), más un endpoint `POST /api/v1/analytics/refresh` para forzar recálculo.

**Pros:** lecturas de dashboard muy baratas (sobre agregados pre-calculados); protege la BD operacional en alto volumen; reutiliza el patrón de hosted service ya presente; cambios acotados al backend.
**Cons:** desfase de minutos (no real-time); un job operativo más que monitorear; no elimina la doble fuente de verdad (GAP-4 persiste como ventana de desfase entre donut y detalle).
**Esfuerzo:** M
**Riesgos:** drift entre agregados y datos vivos si el job falla; necesidad de observabilidad/alarma del refresh.

### Opción B: Vistas materializadas / triggers AFTER en trámites

Convertir los agregados en *materialized views* con refresh incremental, o poblarlos vía triggers `AFTER INSERT/UPDATE` sobre `procedure_instances` y `procedure_instance_status_history`.

**Pros:** consistencia más fuerte y cercana a tiempo real.
**Cons:** los triggers añaden latencia a la **escritura del flujo de trámites** (acoplamiento operacional); mayor complejidad de mantenimiento, RLS sobre MV y manejo de `REFRESH CONCURRENTLY`.
**Esfuerzo:** L
**Riesgos:** degradación del path crítico de trámites; bloqueos/locks en refresh de MV; difícil de razonar y testear.

### Opción C: Lectura 100% en vivo (eliminar agregados) — recomendada

Reescribir `GetOverviewAsync` y `GetTopProducersAsync` para consultar directamente `tramites.*` (igual que ya hace `GetProcedureDetailsAsync`), y **eliminar** las tablas `analytics.*` y la función de refresh.

**Pros:** una sola fuente de verdad → elimina GAP-1 y GAP-4 de raíz; sin job ni infraestructura de ETL; coherencia total donut↔detalle; menos superficie de mantenimiento.
**Cons:** mayor costo por consulta en dashboards (agregación on-the-fly); requiere índices adecuados (`created_at`, `tenant_id`, `status`, join a `procedure_types`); en alto volumen puede necesitar caché corta.
**Esfuerzo:** M
**Riesgos:** consultas costosas si el volumen por tenant crece mucho sin índices/caché; carga sobre la BD operacional.

## Tradeoff aceptado

Se elige **C** sobre A y B porque:
1. **Una sola fuente de verdad**: elimina de raíz GAP-1 (refresh inexistente) y GAP-4 (incoherencia donut↔detalle), en vez de acotarlos.
2. **Menor superficie operativa**: sin hosted service ni ETL que monitorear (descarta A); sin acoplar la escritura del flujo de trámites a triggers/MV (descarta B).
3. **Consistencia con lo ya entregado**: `/procedures` y el export Excel ya leen en vivo sin problemas; unificar el resto reduce la deuda conceptual.
4. **Volumen esperado moderado por tenant**: el coste de agregación on-the-fly es asumible con índices adecuados; si el volumen crece, se introduce caché corta como mitigación incremental (no requiere reintroducir agregados).

Se acepta el coste de mayor trabajo por consulta en los dashboards a cambio de correctitud y simplicidad operativa.

## Consecuencias

### Lo que se gana
- Los reportes (donuts, Top 5, PDF) reflejarán la operación real del módulo de trámites.
- Se elimina (C) o se acota (A) la incoherencia donut↔detalle.

### Lo que se pierde
- **Opción A**: se mantiene infraestructura de agregados + un job a operar.
- **Opción C**: se pierde el pre-cálculo; se paga el coste de agregación en cada consulta (mitigable con índices/caché).

### Cambios operacionales
- **A**: configurar intervalo del refresh; alarma si el job falla; runbook de refresh manual.
- **C**: migración que elimina tablas/función `analytics.*`; revisar índices en `tramites.procedure_instances`.
- Independiente de la opción: la productividad fiel requiere `changed_by` en la radicación y completar el embudo de `status_history` (ver HUs hermanas).

## ADRs relacionados
- ADR-0020 — Capa multi-proveedor de consultas externas (trámites runtime)
- ADR-0018 — Modelo de datos fase-1 FLIT Evolution

## Notas para agentes
- **Backend Agent**: si A → crear `AnalyticsRefreshHostedService` (patrón `IdentityValidationOutboxProcessor`) + endpoint `POST /analytics/refresh`. Si C → reescribir `AnalyticsReadRepository.GetOverviewAsync`/`GetTopProducersAsync` a `tramites.*` y retirar agregados + refresh.
- **Frontend Agent**: sin cambios de contrato; los DTOs de `/overview` y `/productivity/top` se mantienen.
- **QA Agent**: verificar coherencia donut↔detalle con datos reales; en A, probar el desfase del refresh.
- **Security Agent**: preservar RLS (`app.current_tenant_id`) en cualquier consulta nueva; el endpoint de refresh (A) requiere policy SuperAdmin/AdminCompany.
- **Infra Agent**: en A, monitoreo/alarma del hosted service; en C, validar plan de índices.

## Referencias
- `docs/reporte-estado-modulo-analitica.md` — diagnóstico y brechas (GAP-1..GAP-7)
- Feature #10139; HU #10153, #10240, #10243
