-- Feature #11076 (G3) — Auditoría enriquecida de responsabilidad en status_history
-- ─────────────────────────────────────────────────────────────────────────────
-- Decisión aprobada (2026-07-29): SIEMPRE ALTER TABLE, sin bifurcación de schema.
--   • ADD COLUMN IF NOT EXISTS con DEFAULT NULL → operación O(1) en PG17 (no reescribe heap).
--   • El volumen actual no produce lock bloqueante; el umbral configurable en appsettings
--     es solo de advertencia/telemetría — no modifica este DDL.
--   • Backfill: registros previos quedan con NULL (comportamiento esperado: historyAvailable=false).
-- ─────────────────────────────────────────────────────────────────────────────

-- ─── UP ──────────────────────────────────────────────────────────────────────

ALTER TABLE tramites.procedure_instance_status_history
    ADD COLUMN IF NOT EXISTS role_id_at_time             uuid         NULL,
    ADD COLUMN IF NOT EXISTS organization_id_at_time     uuid         NULL,
    ADD COLUMN IF NOT EXISTS organization_type_at_time   varchar(20)  NULL;

-- A12: prefijo ck_ en constraint
ALTER TABLE tramites.procedure_instance_status_history
    DROP CONSTRAINT IF EXISTS ck_status_history_org_type;
ALTER TABLE tramites.procedure_instance_status_history
    ADD CONSTRAINT ck_status_history_org_type
        CHECK (organization_type_at_time IS NULL
               OR organization_type_at_time IN ('ot', 'empresa'));

-- A15: PII — datos de contexto de actor (no datos directos de persona)
COMMENT ON COLUMN tramites.procedure_instance_status_history.role_id_at_time IS
    'Feature #11076 (G3) — Rol del actor al momento del evento. NULL = historial no disponible (registros pre-backfill). @pii:indirect';
COMMENT ON COLUMN tramites.procedure_instance_status_history.organization_id_at_time IS
    'Feature #11076 (G3) — ID de OT o empresa del actor al momento del evento. Interpretar con organization_type_at_time. @pii:indirect';
COMMENT ON COLUMN tramites.procedure_instance_status_history.organization_type_at_time IS
    'Feature #11076 (G3) — Tipo de organización: ''ot'' | ''empresa''. NULL = historial no disponible.';
