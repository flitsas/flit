-- FEATURE 02 — Fuente de la consulta de comparendos por compañía.
-- Columna aditiva en admin.tenant_operational_policies: internal (módulo de comparendos con
-- fuente base) | external (consulta en línea al SIMIT). Default 'external'. Se persiste y
-- audita en esta feature; su USO en el flujo del trámite llega en FEATURE 05.
-- Idempotente (IF NOT EXISTS + guard de constraint) para re-aplicación segura.

ALTER TABLE admin.tenant_operational_policies
    ADD COLUMN IF NOT EXISTS fines_query_source text NOT NULL DEFAULT 'external';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tenant_operational_policies_fines_query_source'
          AND conrelid = 'admin.tenant_operational_policies'::regclass
    ) THEN
        ALTER TABLE admin.tenant_operational_policies
            ADD CONSTRAINT ck_tenant_operational_policies_fines_query_source
            CHECK (fines_query_source IN ('internal', 'external'));
    END IF;
END $$;
