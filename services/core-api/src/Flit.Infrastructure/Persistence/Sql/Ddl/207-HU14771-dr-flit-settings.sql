-- ─────────────────────────────────────────────────────────────────────────────
-- HU #14771 (Feature #14769) — Visibilidad de la UI de DR. FLIT (fila única de
-- plataforma). SuperAdmin la gestiona desde /admin/plataforma; el Shell la lee
-- con GET /api/v1/dr-flit/ui (cualquier autenticado).
--
-- SIN tenant_id ni RLS (deliberado): es global de plataforma, igual que
-- admin.notification_test_settings y admin.quipux_settings.
-- IDEMPOTENTE: CREATE IF NOT EXISTS + INSERT ... WHERE NOT EXISTS.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS admin.dr_flit_settings (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_dr_flit_settings PRIMARY KEY (id),

    -- false = FAB/panel ocultos (default de HU #14770). true = montar asistente.
    ui_enabled boolean NOT NULL DEFAULT false,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    updated_by uuid
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_dr_flit_settings_singleton
  ON admin.dr_flit_settings ((true));

DROP TRIGGER IF EXISTS tr_dr_flit_settings_row_version ON admin.dr_flit_settings;
CREATE TRIGGER tr_dr_flit_settings_row_version
  BEFORE UPDATE ON admin.dr_flit_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_dr_flit_settings_audit ON admin.dr_flit_settings;
CREATE TRIGGER tr_dr_flit_settings_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.dr_flit_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE admin.dr_flit_settings IS
  'HU #14771 (Feature #14769) — visibilidad de la UI de DR. FLIT (FAB + panel). Fila única. Global de plataforma: sin tenant_id ni RLS.';
COMMENT ON COLUMN admin.dr_flit_settings.ui_enabled IS
  'true = montar FAB/panel para usuarios autenticados; false = ocultos. Semilla false (HU #14770).';
COMMENT ON COLUMN admin.dr_flit_settings.row_version IS
  'Token de concurrencia optimista. Lo incrementa tr_dr_flit_settings_row_version.';
COMMENT ON COLUMN admin.dr_flit_settings.updated_by IS
  'SuperAdmin que dejó la configuración. NULL en la fila sembrada.';

INSERT INTO admin.dr_flit_settings (ui_enabled)
SELECT false
WHERE NOT EXISTS (SELECT 1 FROM admin.dr_flit_settings);
