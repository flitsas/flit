-- =============================================================================
-- HU #14375 / #14376 (Feature #14374, Épica #14297) — marca del portal Persona Natural de cada organismo.
-- Migración: 20261105120000_HU14375_PortalMarcaOrganismo.
--
-- Modelo propio por organismo (transit_office_id), con borrador y publicado en jsonb, como admin.tenant_brandings
-- pero SIN reutilizar sus tablas: los disparadores de marca blanca exigen un tenant MARCA_BLANCA y aquí la marca
-- pertenece a un organismo del catálogo. Marca blanca no cambia.
--
--   1. admin.transit_office_portal_brandings — una fila por organismo. draft = borrador editable (puede estar incompleto),
--      published = snapshot completo al publicar (NULL = el portal usa la marca FLIT). Retirar deja published en NULL y
--      conserva el borrador. published_version nunca baja: sube en cada publicación y sobrevive al retiro.
--   2. admin.transit_office_portal_brand_images — una fila por versión de logo o banner (id propio por versión). Las
--      versiones anteriores se conservan: la URL pública de una imagen publicada es inmutable. El binario vive en storage.
--
-- Tablas globales sin tenant_id y sin PII (nombre y colores de marca, imágenes de marca). Sin RLS: las administra el
-- organismo o el SuperAdmin y el alcance lo impone el caso de uso. Auditoría técnica: trg_audit_log + row_version.
-- Idempotente (IF NOT EXISTS / DROP ... IF EXISTS).
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.transit_office_portal_brandings (
    id                uuid        NOT NULL DEFAULT uuidv7(),
    transit_office_id uuid        NOT NULL,
    draft             jsonb       NOT NULL DEFAULT '{"schemaVersion":1}'::jsonb,
    published         jsonb       NULL,
    published_version integer     NOT NULL DEFAULT 0,
    published_at      timestamptz NULL,
    published_by      uuid        NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid        NULL,
    updated_at        timestamptz NOT NULL DEFAULT now(),
    updated_by        uuid        NULL,
    row_version       bigint      NOT NULL DEFAULT 0,

    CONSTRAINT pk_transit_office_portal_brandings PRIMARY KEY (id),
    CONSTRAINT fk_transit_office_portal_brandings_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_transit_office_portal_brandings_office UNIQUE (transit_office_id),
    CONSTRAINT ck_transit_office_portal_brandings_draft_object CHECK (jsonb_typeof(draft) = 'object'),
    CONSTRAINT ck_transit_office_portal_brandings_published_object CHECK (published IS NULL OR jsonb_typeof(published) = 'object'),
    CONSTRAINT ck_transit_office_portal_brandings_published_version CHECK (published_version >= 0),
    -- published, published_at y published_by van juntos o no van (published_version se conserva al retirar).
    CONSTRAINT ck_transit_office_portal_brandings_published_consistent CHECK (
        (published IS NULL AND published_at IS NULL AND published_by IS NULL)
        OR
        (published IS NOT NULL AND published_at IS NOT NULL AND published_by IS NOT NULL AND published_version > 0)
    )
);

COMMENT ON TABLE admin.transit_office_portal_brandings IS
    'Marca del portal Persona Natural de un organismo (HU #14375): borrador y versión publicada. Una fila por organismo. Global sin tenant_id y sin PII.';
COMMENT ON COLUMN admin.transit_office_portal_brandings.draft IS
    'Borrador editable (jsonb schemaVersion=1: platformName, colors{primary,secondary,onPrimary}, logoId, bannerId). Nunca sale por la ruta pública.';
COMMENT ON COLUMN admin.transit_office_portal_brandings.published IS
    'Snapshot publicado (misma forma que draft, completo). NULL = nunca publicada o retirada: el portal usa la marca FLIT.';
COMMENT ON COLUMN admin.transit_office_portal_brandings.published_version IS
    'Sube en cada publicación y no baja al retirar.';
COMMENT ON COLUMN admin.transit_office_portal_brandings.published_by IS
    'Usuario que publicó por última vez (identity.users.id, sin FK: el rastro sobrevive al usuario).';

DROP TRIGGER IF EXISTS tr_transit_office_portal_brandings_row_version ON admin.transit_office_portal_brandings;
CREATE TRIGGER tr_transit_office_portal_brandings_row_version BEFORE UPDATE ON admin.transit_office_portal_brandings
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_transit_office_portal_brandings_audit ON admin.transit_office_portal_brandings;
CREATE TRIGGER tr_transit_office_portal_brandings_audit AFTER INSERT OR UPDATE OR DELETE ON admin.transit_office_portal_brandings
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

CREATE TABLE IF NOT EXISTS admin.transit_office_portal_brand_images (
    id                uuid          NOT NULL,
    transit_office_id uuid          NOT NULL,
    kind              varchar(10)   NOT NULL,
    version           integer       NOT NULL,
    content_type      varchar(20)   NOT NULL,
    storage_path      varchar(1000) NOT NULL,
    storage_sha256    char(64)      NOT NULL,
    size_bytes        integer       NOT NULL,
    created_at        timestamptz   NOT NULL DEFAULT now(),
    created_by        uuid          NULL,

    CONSTRAINT pk_transit_office_portal_brand_images PRIMARY KEY (id),
    CONSTRAINT fk_transit_office_portal_brand_images_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_transit_office_portal_brand_images_version UNIQUE (transit_office_id, kind, version),
    CONSTRAINT ck_transit_office_portal_brand_images_kind CHECK (kind IN ('logo', 'banner')),
    CONSTRAINT ck_transit_office_portal_brand_images_version CHECK (version > 0),
    CONSTRAINT ck_transit_office_portal_brand_images_content_type CHECK (content_type IN ('image/png', 'image/jpeg', 'image/webp')),
    CONSTRAINT ck_transit_office_portal_brand_images_size CHECK (size_bytes > 0),
    CONSTRAINT ck_transit_office_portal_brand_images_sha CHECK (storage_sha256 ~ '^[0-9a-f]{64}$')
);

COMMENT ON TABLE admin.transit_office_portal_brand_images IS
    'Versiones del logo y el banner del portal de un organismo (HU #14376). Inmutables: la versión anterior se conserva. El id es opaco y es el de la URL pública (solo si está publicada).';
COMMENT ON COLUMN admin.transit_office_portal_brand_images.storage_path IS
    'Ruta opaca en IAttachmentStorage. El binario nunca vive en la fila. No se guarda el nombre original del archivo.';
COMMENT ON COLUMN admin.transit_office_portal_brand_images.content_type IS
    'Detectado por firma binaria, nunca por la extensión. image/png | image/jpeg | image/webp.';

CREATE INDEX IF NOT EXISTS ix_transit_office_portal_brand_images_office
    ON admin.transit_office_portal_brand_images (transit_office_id, kind);

DROP TRIGGER IF EXISTS tr_transit_office_portal_brand_images_audit ON admin.transit_office_portal_brand_images;
CREATE TRIGGER tr_transit_office_portal_brand_images_audit AFTER INSERT OR UPDATE OR DELETE ON admin.transit_office_portal_brand_images
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
