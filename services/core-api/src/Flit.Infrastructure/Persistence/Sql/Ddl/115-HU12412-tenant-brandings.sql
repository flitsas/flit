-- HU #12412 (Feature #12366, Épica #12237 Marca Blanca) — identidad de marca de la cabeza de red.
-- Migración: 20260916100000_E12412_TenantBrandings · ADR-0060 (Propuesto) D1.
--
-- Qué crea:
--   1. identity.trg_require_marca_blanca_head(): función de disparador COMPARTIDA (la reutilizan
--      admin.tenant_brand_logos aquí y admin.tenant_domains en el DDL 116). Fail-closed (misma
--      filosofía que ck_tenants_group_parent_by_type de #12406): la fila solo puede pertenecer a una
--      cabeza con tenant_type = 'MARCA_BLANCA' AND is_group_parent = true. Lanza ERRCODE
--      check_violation con CONSTRAINT = TG_ARGV[0] para que la aplicación lo traduzca a un error de
--      negocio identificable (AC2). NO se dispara al cambiar el tipo en identity.tenants: #12412 AC6
--      exige conservar el dato; los resolutores comprueban el tipo EN LECTURA.
--   2. admin.tenant_brandings: 1:1 con la cabeza (tenant_id UNIQUE + FK). El ADR-0060 D1 proponía
--      tenant_id como PK (excepción a A3); aquí se sigue el precedente del repo para el 1:1 por
--      tenant (admin.tenant_operational_policies, DDL 07): id uuidv7 PK + tenant_id UNIQUE, porque
--      public.trg_audit_log() lee NEW.id y sin esa columna el rastro técnico fallaría. draft jsonb (borrador editable, puede estar incompleto) y
--      published jsonb (snapshot completo al publicar), con published_version/at/by acoplados por
--      CHECK. Retiro lógico por deleted_at (la fila se conserva, AC6).
--   3. admin.tenant_brand_logos: una fila por versión del logotipo (patrón
--      admin.company_personalized_documents: versión + integridad + reemplazo que conserva la
--      anterior, AC7). Una sola versión active por cabeza (único parcial). El binario vive en storage.
--
-- Sin poblado (AC8): las tablas nacen vacías; ninguna compañía que no sea MARCA_BLANCA cambia.
-- Idempotente (IF NOT EXISTS / DROP ... IF EXISTS) y reversible (Down completo en la migración).
--
-- RLS: política tenant_isolation con app.is_superadmin / app.current_tenant_id, como el resto del
-- repo. Sin FORCE (convención vigente: la app conecta como owner). La lectura pública por dominio y la
-- herencia de la hija (#12418) las hace la app con su propio resolutor; no hay fila en la hija.
--
-- Auditoría: trg_audit_log (rastro técnico) en ambas tablas. La auditoría de negocio con old/new
-- legible (AC5) la escribe el repositorio en admin.tenant_config_audit_logs en el mismo SaveChanges
-- (patrón CompanyWriteRepository), NO un trigger.

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. Función compartida: solo una cabeza de tipo MARCA_BLANCA puede tener marca / logotipo / dominio
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION identity.trg_require_marca_blanca_head()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_type   text;
    v_parent boolean;
BEGIN
    SELECT t.tenant_type, t.is_group_parent
      INTO v_type, v_parent
      FROM identity.tenants t
     WHERE t.id = NEW.tenant_id;

    IF v_type IS DISTINCT FROM 'MARCA_BLANCA' OR v_parent IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'tenant % no es cabeza de tipo MARCA_BLANCA (tipo=%, is_group_parent=%)',
            NEW.tenant_id, v_type, v_parent
            USING ERRCODE = 'check_violation',
                  CONSTRAINT = TG_ARGV[0],
                  TABLE = TG_TABLE_NAME,
                  SCHEMA = TG_TABLE_SCHEMA;
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION identity.trg_require_marca_blanca_head() IS
    'ADR-0060 · Marca, logotipo y dominio solo se escriben sobre una cabeza de tipo MARCA_BLANCA (#12406 acopla is_group_parent al tipo). TG_ARGV[0] = nombre lógico del constraint que se reporta (ck_<tabla>_marca_blanca).';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. admin.tenant_brandings — 1:1 con la cabeza (id PK + tenant_id UNIQUE, patrón tenant_operational_policies)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.tenant_brandings (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id          uuid        NOT NULL,
    draft              jsonb       NOT NULL DEFAULT '{"schemaVersion":1}'::jsonb,
    published          jsonb       NULL,
    published_version  integer     NOT NULL DEFAULT 0,
    published_at       timestamptz NULL,
    published_by       uuid        NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         uuid        NULL,
    updated_at         timestamptz NOT NULL DEFAULT now(),
    updated_by         uuid        NULL,
    deleted_at         timestamptz NULL,
    deleted_by         uuid        NULL,
    row_version        bigint      NOT NULL DEFAULT 0,

    CONSTRAINT pk_tenant_brandings PRIMARY KEY (id),
    CONSTRAINT fk_tenant_brandings_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_tenant_brandings_tenant_id UNIQUE (tenant_id),
    CONSTRAINT ck_tenant_brandings_draft_object CHECK (jsonb_typeof(draft) = 'object'),
    CONSTRAINT ck_tenant_brandings_published_object CHECK (published IS NULL OR jsonb_typeof(published) = 'object'),
    CONSTRAINT ck_tenant_brandings_published_version CHECK (published_version >= 0),
    -- published, published_at, published_by y published_version > 0 van juntos o no van
    CONSTRAINT ck_tenant_brandings_published_consistent CHECK (
        (published IS NULL AND published_at IS NULL AND published_by IS NULL AND published_version = 0)
        OR
        (published IS NOT NULL AND published_at IS NOT NULL AND published_by IS NOT NULL AND published_version > 0)
    )
);

COMMENT ON TABLE admin.tenant_brandings IS
    'ADR-0060 (HU #12412) · Identidad de marca de una cabeza MARCA_BLANCA: borrador y versión publicada. 1:1 con identity.tenants (tenant_id UNIQUE; id uuidv7 PK por public.trg_audit_log). La hija hereda por parent_tenant_id en lectura; nunca tiene fila.';
COMMENT ON COLUMN admin.tenant_brandings.tenant_id IS
    'Cabeza de red dueña de la marca (identity.tenants.id). UNIQUE + FK: una marca por cabeza. Solo MARCA_BLANCA (tr_tenant_brandings_marca_blanca).';
COMMENT ON COLUMN admin.tenant_brandings.draft IS
    'Borrador editable (jsonb schemaVersion=1: platformName, colors{primary,secondary,onPrimary}, logoId). Puede estar incompleto; la forma la valida la aplicación (#12413).';
COMMENT ON COLUMN admin.tenant_brandings.published IS
    'Snapshot publicado (misma forma que draft, siempre completo). NULL = nunca publicada. Es lo que resuelven /public/branding, /me/branding y el tema de correo.';
COMMENT ON COLUMN admin.tenant_brandings.published_version IS
    'Se incrementa en cada publicación (0 = nunca publicada); se guarda en notification_delivery_logs.theme_version (#12428 AC5).';
COMMENT ON COLUMN admin.tenant_brandings.published_at IS
    'Instante de la última publicación (AC4 #12412).';
COMMENT ON COLUMN admin.tenant_brandings.published_by IS
    'Usuario que publicó por última vez (identity.users.id, sin FK: el rastro sobrevive al usuario). AC4 #12412.';
COMMENT ON COLUMN admin.tenant_brandings.deleted_at IS
    'Retiro lógico de la marca (la fila se conserva; AC6 #12412). NULL = vigente.';
COMMENT ON COLUMN admin.tenant_brandings.row_version IS
    'Token de concurrencia (public.trg_row_version). Publicación y cambio de tipo concurrentes se resuelven aquí (ADR-0060 D1).';

DROP TRIGGER IF EXISTS tr_tenant_brandings_marca_blanca ON admin.tenant_brandings;
CREATE TRIGGER tr_tenant_brandings_marca_blanca
    BEFORE INSERT OR UPDATE OF tenant_id, draft, published ON admin.tenant_brandings
    FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_brandings_marca_blanca');

DROP TRIGGER IF EXISTS tr_tenant_brandings_row_version ON admin.tenant_brandings;
CREATE TRIGGER tr_tenant_brandings_row_version BEFORE UPDATE ON admin.tenant_brandings
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_brandings_audit ON admin.tenant_brandings;
CREATE TRIGGER tr_tenant_brandings_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_brandings
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE admin.tenant_brandings ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brandings;
CREATE POLICY tenant_isolation ON admin.tenant_brandings
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3. admin.tenant_brand_logos — versionado del logotipo (patrón company_personalized_documents)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.tenant_brand_logos (
    id               uuid          NOT NULL DEFAULT uuidv7(),
    tenant_id        uuid          NOT NULL,
    version          integer       NOT NULL,
    status           varchar(20)   NOT NULL DEFAULT 'active',
    content_type     varchar(20)   NOT NULL,
    filename         varchar(255)  NOT NULL,
    storage_path     varchar(1000) NOT NULL,
    storage_sha256   char(64)      NOT NULL,
    size_bytes       integer       NOT NULL,
    width_px         integer       NOT NULL,
    height_px        integer       NOT NULL,
    superseded_at    timestamptz   NULL,
    superseded_by    uuid          NULL,
    created_at       timestamptz   NOT NULL DEFAULT now(),
    created_by       uuid          NULL,
    updated_at       timestamptz   NOT NULL DEFAULT now(),
    updated_by       uuid          NULL,
    deleted_at       timestamptz   NULL,
    deleted_by       uuid          NULL,
    row_version      bigint        NOT NULL DEFAULT 0,

    CONSTRAINT pk_tenant_brand_logos PRIMARY KEY (id),
    CONSTRAINT fk_tenant_brand_logos_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_tenant_brand_logos_tenant_version UNIQUE (tenant_id, version),
    CONSTRAINT ck_tenant_brand_logos_version CHECK (version > 0),
    CONSTRAINT ck_tenant_brand_logos_status CHECK (status IN ('active', 'superseded')),
    CONSTRAINT ck_tenant_brand_logos_content_type CHECK (content_type IN ('image/png', 'image/jpeg', 'image/webp')),
    CONSTRAINT ck_tenant_brand_logos_size CHECK (size_bytes > 0),
    CONSTRAINT ck_tenant_brand_logos_dims CHECK (width_px > 0 AND height_px > 0),
    CONSTRAINT ck_tenant_brand_logos_sha CHECK (storage_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_tenant_brand_logos_superseded CHECK (
        (status = 'active' AND superseded_at IS NULL) OR (status = 'superseded' AND superseded_at IS NOT NULL)
    )
);

-- Una sola versión activa (y vigente) por cabeza.
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_brand_logos_one_active
    ON admin.tenant_brand_logos (tenant_id) WHERE status = 'active' AND deleted_at IS NULL;
-- Cobertura de la FK (A9) y de la lectura del historial por cabeza.
CREATE INDEX IF NOT EXISTS ix_tenant_brand_logos_tenant_id
    ON admin.tenant_brand_logos (tenant_id);

COMMENT ON TABLE admin.tenant_brand_logos IS
    'ADR-0060 (HU #12412) · Versiones del logotipo de marca. La versión anterior se conserva con su verificación de integridad (AC7). Se sirve por GET /public/branding/logos/{id} con Cache-Control immutable; el id (uuidv7) es opaco y no identifica a la compañía.';
COMMENT ON COLUMN admin.tenant_brand_logos.tenant_id IS
    'Cabeza de red dueña del logotipo (identity.tenants.id). Solo MARCA_BLANCA (tr_tenant_brand_logos_marca_blanca).';
COMMENT ON COLUMN admin.tenant_brand_logos.version IS
    'Incremental por tenant_id, empieza en 1 y nunca se reutiliza (uq_tenant_brand_logos_tenant_version).';
COMMENT ON COLUMN admin.tenant_brand_logos.status IS
    'active (la que referencia el borrador/publicado más reciente) | superseded (reemplazada; se conserva).';
COMMENT ON COLUMN admin.tenant_brand_logos.content_type IS
    'Detectado por firma binaria (ImageContentTypeSniffer), nunca por extensión. image/png | image/jpeg | image/webp. SVG rechazado (#12413 AC1).';
COMMENT ON COLUMN admin.tenant_brand_logos.filename IS
    '@pii:low — nombre de archivo original tal como lo subió el usuario; puede llevar razón social.';
COMMENT ON COLUMN admin.tenant_brand_logos.storage_path IS
    'Ruta en IAttachmentStorage (grupo = tenant_id, tipo brand-logo). El binario nunca vive en la fila.';
COMMENT ON COLUMN admin.tenant_brand_logos.storage_sha256 IS
    'SHA-256 (hex minúscula) del binario calculado por IAttachmentStorage.SaveAsync; ETag del endpoint público.';
COMMENT ON COLUMN admin.tenant_brand_logos.size_bytes IS
    'Tamaño del binario en bytes (> 0). El máximo lo impone la aplicación (#12413, Branding:MaxBytes).';
COMMENT ON COLUMN admin.tenant_brand_logos.superseded_at IS
    'Instante en que una versión nueva la reemplazó. Obligatorio si status = superseded.';
COMMENT ON COLUMN admin.tenant_brand_logos.superseded_by IS
    'Usuario que cargó la versión que la reemplazó (identity.users.id, sin FK).';
COMMENT ON COLUMN admin.tenant_brand_logos.deleted_at IS
    'Retiro lógico. NULL = conservada (lo normal: ninguna versión se borra, AC7).';

DROP TRIGGER IF EXISTS tr_tenant_brand_logos_marca_blanca ON admin.tenant_brand_logos;
CREATE TRIGGER tr_tenant_brand_logos_marca_blanca
    BEFORE INSERT OR UPDATE OF tenant_id ON admin.tenant_brand_logos
    FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_brand_logos_marca_blanca');

DROP TRIGGER IF EXISTS tr_tenant_brand_logos_row_version ON admin.tenant_brand_logos;
CREATE TRIGGER tr_tenant_brand_logos_row_version BEFORE UPDATE ON admin.tenant_brand_logos
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_brand_logos_audit ON admin.tenant_brand_logos;
CREATE TRIGGER tr_tenant_brand_logos_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_brand_logos
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE admin.tenant_brand_logos ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brand_logos;
CREATE POLICY tenant_isolation ON admin.tenant_brand_logos
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );
