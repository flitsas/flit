-- =============================================================================
-- HU #12512 — auditoría de quién cambió ict.job_settings (GET/PUT SuperAdmin).
--
-- Tabla GLOBAL de plataforma (sin tenant, sin RLS), fila única id=1. Solo se
-- agrega updated_by (uuid, nullable): updated_at ya existía. Idempotente.
-- =============================================================================

ALTER TABLE ict.job_settings
    ADD COLUMN IF NOT EXISTS updated_by uuid NULL;

COMMENT ON COLUMN ict.job_settings.updated_by
    IS '@audit Usuario de plataforma que persistió el último PUT SuperAdmin (HU #12512). Null si el seed o un UPDATE SQL no identificó actor.';
