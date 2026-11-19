-- =============================================================================
-- Mejora sin HU (Feature #14453, ADR-0082, revisión 2026-11-17) — ventana de anulación del cobro en caja POR ORGANISMO.
-- Migración: 20261117160000_VentanaAnulacionCajaPorOrganismo. Idempotente y aditiva.
--
--   admin.persona_natural_caja_settings        una fila por organismo que configuró la ventana (minutos, 0 = se radica al cobrar y
--                                              no se anula). Sin fila = valor por defecto de la aplicación (15, PortalCaja:...).
--                                              El tope configurable (60) lo valida la aplicación; la base solo impone 0..1440.
--   admin.persona_natural_caja_window_history  cambios (quién, antes, después, cuándo), como el historial de tarifas.
--
-- Un cambio solo aplica a cobros nuevos: el límite de anulación se guarda en el cobro (BFF, annulable_until). Tablas globales sin
-- tenant_id (como las de cobro del portal), sin PII.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.persona_natural_caja_settings (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_caja_settings PRIMARY KEY (id),
    transit_office_id uuid NOT NULL,
    annulment_window_minutes integer NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_persona_natural_caja_settings_office UNIQUE (transit_office_id),
    CONSTRAINT fk_persona_natural_caja_settings_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_persona_natural_caja_settings_window CHECK (annulment_window_minutes BETWEEN 0 AND 1440)
);

CREATE TABLE IF NOT EXISTS admin.persona_natural_caja_window_history (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_caja_window_history PRIMARY KEY (id),
    transit_office_id uuid NOT NULL,
    minutes_before integer NOT NULL,
    minutes_after integer NOT NULL,
    changed_by uuid NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_persona_natural_caja_window_history_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_persona_natural_caja_window_history_range CHECK (
        minutes_before BETWEEN 0 AND 1440 AND minutes_after BETWEEN 0 AND 1440)
);

CREATE INDEX IF NOT EXISTS ix_persona_natural_caja_window_history_office
    ON admin.persona_natural_caja_window_history (transit_office_id, changed_at DESC);

COMMENT ON TABLE admin.persona_natural_caja_settings IS
    'Ventana de anulación del cobro en caja por organismo, en minutos (0 = sin anulación). Sin fila = valor por defecto de la aplicación. Global sin tenant_id. Sin PII.';
COMMENT ON TABLE admin.persona_natural_caja_window_history IS
    'Historial de cambios de la ventana de anulación de la caja por organismo (quién, antes, después, cuándo). Global sin tenant_id. Sin PII.';

DROP TRIGGER IF EXISTS tr_persona_natural_caja_settings_row_version ON admin.persona_natural_caja_settings;
CREATE TRIGGER tr_persona_natural_caja_settings_row_version BEFORE UPDATE ON admin.persona_natural_caja_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_caja_settings_audit ON admin.persona_natural_caja_settings;
CREATE TRIGGER tr_persona_natural_caja_settings_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_caja_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
