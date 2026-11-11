-- =============================================================================
-- HU #14072 (Feature J, Épica #14021) — configuración del portal de Persona Natural: lista blanca de tipos de trámite y
-- parámetros generales.
-- Migración: 20261028140000_HU14072_ConfiguracionPortalPersonaNatural.
--
-- Qué hace (ADITIVO: no altera ninguna tabla existente; los organismos se habilitan con el grant que ya existe,
-- admin.tenant_transit_office_grants, sin DDL):
--   1. admin.persona_natural_enabled_types — lista blanca EXPLÍCITA de tipos de trámite que el superadministrador habilita
--      para el portal (ADR-0075 decisión 11): «habilitado = elegible (K2) + habilitado aquí + tarifa vigente». Nace VACÍA.
--      Una fila por tipo; is_enabled permite apagarlo sin perder el rastro.
--   2. admin.persona_natural_settings — parámetros generales del portal en UNA sola fila (singleton) con row_version
--      (control de concurrencia, AC4). Cada parámetro nulo = rige el valor predeterminado del BFF. Los datos bancarios y el
--      pago NO están aquí: viven en otra épica y este modelo no los trae.
--
-- Tablas GLOBALES sin tenant_id (excepción análoga a admin.banners, ADR-0058): las administra la plataforma. Sin RLS por la
-- misma razón. Sin datos personales de ciudadanos: el contacto de soporte es de FLIT. Auditoría: trg_audit_log + row_version.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.persona_natural_enabled_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_enabled_types PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT fk_persona_natural_enabled_types_procedure_types FOREIGN KEY (procedure_type_id)
        REFERENCES tramites.procedure_types (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_persona_natural_enabled_types_procedure_type UNIQUE (procedure_type_id)
);

COMMENT ON TABLE admin.persona_natural_enabled_types IS
    'Lista blanca de tipos de trámite habilitados para el portal de Persona Natural (HU #14072). Global sin tenant_id. Sin PII.';

DROP TRIGGER IF EXISTS tr_persona_natural_enabled_types_row_version ON admin.persona_natural_enabled_types;
CREATE TRIGGER tr_persona_natural_enabled_types_row_version BEFORE UPDATE ON admin.persona_natural_enabled_types
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_enabled_types_audit ON admin.persona_natural_enabled_types;
CREATE TRIGGER tr_persona_natural_enabled_types_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_enabled_types
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

CREATE TABLE IF NOT EXISTS admin.persona_natural_settings (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_settings PRIMARY KEY (id),
    -- Fila única: la restricción impide una segunda fila.
    singleton boolean NOT NULL DEFAULT true,
    CONSTRAINT uq_persona_natural_settings_singleton UNIQUE (singleton),
    CONSTRAINT ck_persona_natural_settings_singleton CHECK (singleton),
    -- Parámetros de uso y protección (nulo = predeterminado del BFF). Los rangos son los mismos del caso de uso.
    session_validity_minutes int,
    draft_validity_days int,
    queries_per_citizen_per_day int,
    queries_global_per_day int,
    captcha_enabled boolean,
    -- Contacto de soporte de FLIT que el portal muestra (texto de la empresa, no de ciudadanos).
    support_email varchar(254),
    support_phone varchar(30),
    support_hours varchar(120),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT ck_persona_natural_settings_session CHECK (session_validity_minutes IS NULL OR session_validity_minutes BETWEEN 15 AND 1440),
    CONSTRAINT ck_persona_natural_settings_draft CHECK (draft_validity_days IS NULL OR draft_validity_days BETWEEN 1 AND 90),
    CONSTRAINT ck_persona_natural_settings_citizen CHECK (queries_per_citizen_per_day IS NULL OR queries_per_citizen_per_day BETWEEN 1 AND 100),
    CONSTRAINT ck_persona_natural_settings_global CHECK (queries_global_per_day IS NULL OR queries_global_per_day BETWEEN 1 AND 100000)
);

COMMENT ON TABLE admin.persona_natural_settings IS
    'Parámetros generales del portal de Persona Natural (HU #14072): una sola fila, nulo = predeterminado del BFF. Sin datos bancarios ni PII.';

DROP TRIGGER IF EXISTS tr_persona_natural_settings_row_version ON admin.persona_natural_settings;
CREATE TRIGGER tr_persona_natural_settings_row_version BEFORE UPDATE ON admin.persona_natural_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_settings_audit ON admin.persona_natural_settings;
CREATE TRIGGER tr_persona_natural_settings_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- La fila única nace con todos los parámetros en su predeterminado (nulo).
INSERT INTO admin.persona_natural_settings (singleton) VALUES (true) ON CONFLICT (singleton) DO NOTHING;
