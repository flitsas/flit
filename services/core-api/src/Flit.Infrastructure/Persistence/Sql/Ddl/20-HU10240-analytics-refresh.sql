-- HU #10240 — Analítica · Refresh de agregados desde trámites | Feature #10139
-- Función de refresh (TODOS los entornos): recalcula las tablas analytics
-- a partir de tramites.procedure_instances y procedure_instance_status_history.
-- Idempotente: upsert vía ON CONFLICT sobre los UNIQUE de #10153.
-- Semántica (decisión database-agent, sin ADR nuevo: no crea entidades):
--   · procedure_metrics_daily: agrupa por día de CREACIÓN (created_at::date),
--     categoría = procedure_types.family y estado ACTUAL de la instancia.
--   · user_productivity_daily: agrupa por usuario que ejecutó la transición
--     (status_history.changed_by) y fecha del evento (changed_at::date).
--       submitted = to_status 'submitted'
--       approved  = to_status IN ('approved_ot','completed')
--       rejected  = to_status IN ('rejected_ot','cancelled')
-- RLS: SECURITY INVOKER (default). El llamador (job/endpoint) debe tener
-- visibilidad del tenant (app.current_tenant_id) o ser owner (migración).
-- El filtro explícito por p_tenant_id acota el refresh a un solo tenant.

CREATE OR REPLACE FUNCTION analytics.refresh_procedure_aggregates(
    p_tenant_id uuid,
    p_date_from date,
    p_date_to date
) RETURNS void
LANGUAGE plpgsql
AS $refresh$
BEGIN
    IF p_tenant_id IS NULL THEN
        RAISE EXCEPTION 'p_tenant_id es obligatorio';
    END IF;
    IF p_date_from IS NULL OR p_date_to IS NULL OR p_date_from > p_date_to THEN
        RAISE EXCEPTION 'rango de fechas inválido: % .. %', p_date_from, p_date_to;
    END IF;

    -- 1) procedure_metrics_daily — conteo por día de creación, categoría y estado actual.
    INSERT INTO analytics.procedure_metrics_daily AS t
        (tenant_id, metric_date, procedure_category, status, count, refreshed_at)
    SELECT pi.tenant_id,
           pi.created_at::date,
           pt.family,
           pi.status,
           count(*)::integer,
           now()
    FROM tramites.procedure_instances pi
    JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
    WHERE pi.tenant_id = p_tenant_id
      AND pi.deleted_at IS NULL
      AND pi.created_at::date BETWEEN p_date_from AND p_date_to
    GROUP BY pi.tenant_id, pi.created_at::date, pt.family, pi.status
    ON CONFLICT ON CONSTRAINT uq_procedure_metrics_daily
    DO UPDATE SET count = EXCLUDED.count,
                  refreshed_at = EXCLUDED.refreshed_at;

    -- 2) user_productivity_daily — productividad por usuario y fecha del evento.
    INSERT INTO analytics.user_productivity_daily AS t
        (tenant_id, user_id, metric_date, submitted_count, approved_count, rejected_count, refreshed_at)
    SELECT h.tenant_id,
           h.changed_by,
           h.changed_at::date,
           count(*) FILTER (WHERE h.to_status = 'submitted')::integer,
           count(*) FILTER (WHERE h.to_status IN ('approved_ot', 'completed'))::integer,
           count(*) FILTER (WHERE h.to_status IN ('rejected_ot', 'cancelled'))::integer,
           now()
    FROM tramites.procedure_instance_status_history h
    WHERE h.tenant_id = p_tenant_id
      AND h.changed_by IS NOT NULL
      AND h.changed_at::date BETWEEN p_date_from AND p_date_to
    GROUP BY h.tenant_id, h.changed_by, h.changed_at::date
    HAVING count(*) FILTER (WHERE h.to_status = 'submitted') > 0
        OR count(*) FILTER (WHERE h.to_status IN ('approved_ot', 'completed')) > 0
        OR count(*) FILTER (WHERE h.to_status IN ('rejected_ot', 'cancelled')) > 0
    ON CONFLICT ON CONSTRAINT uq_user_productivity_daily
    DO UPDATE SET submitted_count = EXCLUDED.submitted_count,
                  approved_count = EXCLUDED.approved_count,
                  rejected_count = EXCLUDED.rejected_count,
                  refreshed_at = EXCLUDED.refreshed_at;
END;
$refresh$;

COMMENT ON FUNCTION analytics.refresh_procedure_aggregates(uuid, date, date) IS
  'HU #10240 — Recalcula procedure_metrics_daily y user_productivity_daily para un tenant y rango de fechas, desde tramites.procedure_instances y procedure_instance_status_history. Idempotente (upsert).';
