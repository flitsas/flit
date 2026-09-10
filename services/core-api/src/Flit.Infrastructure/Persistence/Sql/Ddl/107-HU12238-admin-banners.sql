-- HU #12238 — Banners: esquema de admin.banners | Feature #12236

CREATE TABLE admin.banners (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_banners PRIMARY KEY (id),
    name varchar(200) NOT NULL,
    image_storage_path varchar(200) NOT NULL, -- opaco: id del file-manager (ADR-0057)
    image_sha256 char(64) NOT NULL,            -- ETag del endpoint de imagen (ADR-0057)
    link_url varchar(2048),
    valid_from timestamptz,
    valid_until timestamptz,
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT ck_banners_vigencia CHECK (
        (valid_from IS NULL AND valid_until IS NULL)
        OR (valid_from IS NOT NULL AND valid_until IS NOT NULL AND valid_until > valid_from)
    )
);

CREATE INDEX ix_banners_activo_vigencia
  ON admin.banners (is_active, valid_from, valid_until)
  WHERE deleted_at IS NULL;

COMMENT ON TABLE admin.banners IS
  'Tabla GLOBAL sin tenant_id: excepcion documentada en ADR-0058-banners-tabla-global-sin-tenant-excepcion. '
  'Un unico set de banners visible para todos los tenants (Feature #12236). '
  'valid_from/valid_until son NULLABLE (ambas NULL = sin vigencia programada, activacion/desactivacion '
  'manual via is_active): ajuste de nulabilidad sobre el DDL de referencia del ADR, exigido por AC2 '
  'de la HU #12238 — el DDL de referencia las declaraba NOT NULL con CHECK (valid_until > valid_from).';

-- Sin RLS: la tabla es global por diseño (ADR-0058). Triggers estandar de negocio (checklist A16) si aplican:
DROP TRIGGER IF EXISTS tr_banners_row_version ON admin.banners;
CREATE TRIGGER tr_banners_row_version BEFORE UPDATE ON admin.banners
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_banners_audit ON admin.banners;
CREATE TRIGGER tr_banners_audit AFTER INSERT OR UPDATE OR DELETE ON admin.banners
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
