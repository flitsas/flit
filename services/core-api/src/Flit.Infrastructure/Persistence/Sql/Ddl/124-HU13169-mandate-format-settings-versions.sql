-- HU #13169 (Feature #13118 F6, Épica #13090 Mandatarios) — personalización y versiones de cada formato de contrato de
-- mandato. Migración: 20261001120000_HU13169_MandateFormatSettingsVersions.
--
-- Qué crea (schema admin, catálogo GLOBAL de plataforma: sin tenant_id, sin RLS, igual que
-- admin.transit_office_mandate_config del DDL 41; solo el Super Admin lo edita):
--   1. admin.mandate_format_settings: una fila por formato del catálogo (MandatoFormatCatalog) con el nombre visible,
--      el tipo de mandato asociado (valor por defecto de la redacción; la regla compañía×organismo de F5 sigue mandando)
--      y el número de la versión de plantilla vigente (0 = sin plantilla personalizada: se usa la redacción del
--      generador). row_version con el trigger de la plataforma para el control de concurrencia. NO hay soft delete:
--      los formatos no se crean ni se borran desde la interfaz (decisión del PO de la Feature #13118).
--   2. admin.mandate_format_versions: versiones INMUTABLES de la plantilla de un formato (cuerpo, SHA-256, autor y fecha).
--      Un trigger rechaza UPDATE y DELETE: reconstruir qué texto estuvo vigente en cada momento exige que nunca cambien.
--
-- Esta plantilla de formato NO es la plantilla propia del OT (custom_template_* de transit_office_mandate_config, retirada
-- de la interfaz por la HU #11705): es global, versionada y no la reactiva.
--
-- Seed: una fila por formato con nombre y tipo iguales a los actuales del catálogo, sin plantilla personalizada. El
-- catálogo vive en código (MandatoFormatCatalog); el CHECK solo fija el formato del código, así que un formato nuevo
-- exige agregarlo al catálogo e insertar su fila, no cambiar el DDL.
-- DDL idempotente (IF NOT EXISTS / DROP ... IF EXISTS / ON CONFLICT) y reversible (Down en la migración).

CREATE TABLE IF NOT EXISTS admin.mandate_format_settings (
    id                 uuid          NOT NULL DEFAULT uuidv7(),
    format_code        varchar(30)   NOT NULL,
    display_name       varchar(80)   NOT NULL,
    assignment_mode    varchar(20)   NOT NULL,
    current_version    integer       NOT NULL DEFAULT 0,
    row_version        bigint        NOT NULL DEFAULT 0,
    created_at         timestamptz   NOT NULL DEFAULT now(),
    created_by         uuid,
    updated_at         timestamptz,
    updated_by         uuid,

    CONSTRAINT pk_mandate_format_settings PRIMARY KEY (id),
    CONSTRAINT uq_mandate_format_settings_code UNIQUE (format_code),
    CONSTRAINT ck_mandate_format_settings_code CHECK (format_code ~ '^[a-z][a-z0-9_]{1,29}$'),
    CONSTRAINT ck_mandate_format_settings_name CHECK (char_length(btrim(display_name)) BETWEEN 1 AND 80),
    CONSTRAINT ck_mandate_format_settings_mode CHECK (assignment_mode IN ('signer', 'institutional', 'open')),
    CONSTRAINT ck_mandate_format_settings_current_version CHECK (current_version >= 0)
);

-- Dos formatos no pueden llamarse igual (sin distinguir mayúsculas ni espacios en los extremos).
CREATE UNIQUE INDEX IF NOT EXISTS uq_mandate_format_settings_display_name
    ON admin.mandate_format_settings (lower(btrim(display_name)));

DROP TRIGGER IF EXISTS tr_mandate_format_settings_row_version ON admin.mandate_format_settings;
CREATE TRIGGER tr_mandate_format_settings_row_version BEFORE UPDATE ON admin.mandate_format_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

COMMENT ON TABLE admin.mandate_format_settings IS
    'HU13169 · Personalización por formato de contrato de mandato (nombre visible, tipo asociado, versión vigente). Global, sin tenant.';
COMMENT ON COLUMN admin.mandate_format_settings.format_code IS
    'Código del catálogo MandatoFormatCatalog (auto, generico, sabaneta, bello, municipio): el mismo template_code de la configuración del organismo.';
COMMENT ON COLUMN admin.mandate_format_settings.assignment_mode IS
    'signer | institutional | open. Valor por defecto de la redacción; la regla por compañía y organismo (F5) prevalece.';
COMMENT ON COLUMN admin.mandate_format_settings.current_version IS
    'Número de la última versión publicada en admin.mandate_format_versions; 0 = sin plantilla personalizada (rige la redacción del generador).';
COMMENT ON COLUMN admin.mandate_format_settings.row_version IS
    'Token de concurrencia optimista; lo incrementa tr_mandate_format_settings_row_version en cada UPDATE.';

CREATE TABLE IF NOT EXISTS admin.mandate_format_versions (
    id                 uuid          NOT NULL DEFAULT uuidv7(),
    format_setting_id  uuid          NOT NULL,
    version_number     integer       NOT NULL,
    body               text          NOT NULL,
    body_sha256        char(64)      NOT NULL,
    created_at         timestamptz   NOT NULL DEFAULT now(),
    created_by         uuid,

    CONSTRAINT pk_mandate_format_versions PRIMARY KEY (id),
    CONSTRAINT fk_mandate_format_versions_setting FOREIGN KEY (format_setting_id)
        REFERENCES admin.mandate_format_settings (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT uq_mandate_format_versions_number UNIQUE (format_setting_id, version_number),
    CONSTRAINT ck_mandate_format_versions_number CHECK (version_number >= 1),
    -- Mismo límite que el editor de plantilla del organismo (SaveEditorBodyAsync).
    CONSTRAINT ck_mandate_format_versions_body CHECK (char_length(body) BETWEEN 1 AND 100000),
    CONSTRAINT ck_mandate_format_versions_sha256 CHECK (body_sha256 ~ '^[0-9a-f]{64}$')
);

COMMENT ON TABLE admin.mandate_format_versions IS
    'HU13169 · Versiones inmutables de la plantilla de un formato de mandato (cuerpo, SHA-256, autor, fecha). Nunca se modifican ni se eliminan.';
COMMENT ON COLUMN admin.mandate_format_versions.body_sha256 IS
    'SHA-256 hexadecimal (minúsculas) del cuerpo en UTF-8.';

-- Inmutabilidad: ni UPDATE ni DELETE (el contrato emitido debe poder reconstruirse con la versión exacta que usó).
CREATE OR REPLACE FUNCTION admin.trg_mandate_format_versions_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'admin.mandate_format_versions es inmutable: no se permite % de versiones publicadas', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS tr_mandate_format_versions_immutable ON admin.mandate_format_versions;
CREATE TRIGGER tr_mandate_format_versions_immutable BEFORE UPDATE OR DELETE ON admin.mandate_format_versions
  FOR EACH ROW EXECUTE FUNCTION admin.trg_mandate_format_versions_immutable();

-- Seed: una fila por formato del catálogo, con los valores de fábrica y sin plantilla personalizada.
INSERT INTO admin.mandate_format_settings (format_code, display_name, assignment_mode) VALUES
    ('auto',      'Automática (según el organismo)', 'signer'),
    ('generico',  'Genérico',                        'signer'),
    ('sabaneta',  'Sabaneta',                        'institutional'),
    ('bello',     'Bello',                           'signer'),
    ('municipio', 'Envigado, Funza y Medellín',      'signer')
ON CONFLICT (format_code) DO NOTHING;
