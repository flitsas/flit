-- =============================================================================
-- HU #14073 (Feature J, Épica #14021) — contenido administrable del portal de Persona Natural: preguntas frecuentes,
-- banners, enlaces (legales, contacto de ventas y video) y versiones publicadas.
-- Migración: 20261028150000_HU14073_ContenidoPortalPersonaNatural.
--
-- Modelo «borrador / publicado» (AC4):
--   * admin.persona_natural_faqs / _banners / _links son el contenido de TRABAJO (el borrador): lo que edita el
--     superadministrador. El portal NO lo lee.
--   * admin.persona_natural_content_versions guarda cada publicación como una foto inmutable (jsonb) con versión,
--     fecha y usuario: el portal lee SOLO la última versión publicada (y filtra la vigencia de los banners al leer). Las
--     versiones anteriores quedan consultables y no se editan ni se borran (trigger).
-- Los textos son texto plano (la API rechaza HTML); las imágenes de los banners se guardan en el almacenamiento de banners
-- de FLIT 2 (IBannerImageStorage, ADR-0057): aquí solo viaja la ruta opaca, el hash y el tipo. Los enlaces legales NO
-- inventan contenido: una URL nula = «sin documento oficial» y el portal conserva su aviso actual.
-- Todo nace VACÍO. Tablas GLOBALES sin tenant_id (excepción análoga a admin.banners, ADR-0058); sin RLS por la misma razón.
-- Aditivo: no altera ninguna tabla existente. Sin datos personales de ciudadanos (si quien publica pusiera uno, es
-- responsabilidad de quien publica; el formulario lo advierte).
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.persona_natural_faqs (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_faqs PRIMARY KEY (id),
    question varchar(300) NOT NULL,
    answer varchar(2000) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT ck_persona_natural_faqs_question CHECK (length(btrim(question)) > 0),
    CONSTRAINT ck_persona_natural_faqs_answer CHECK (length(btrim(answer)) > 0)
);
CREATE INDEX IF NOT EXISTS ix_persona_natural_faqs_active_order ON admin.persona_natural_faqs (is_active, sort_order);
COMMENT ON TABLE admin.persona_natural_faqs IS
    'Preguntas frecuentes del portal de Persona Natural (HU #14073): contenido de trabajo (borrador). Texto plano, sin PII.';

CREATE TABLE IF NOT EXISTS admin.persona_natural_banners (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_banners PRIMARY KEY (id),
    title varchar(200) NOT NULL,
    alt_text varchar(250) NOT NULL,
    link_url varchar(2048),
    image_path varchar(500) NOT NULL,
    image_sha256 varchar(64) NOT NULL,
    image_content_type varchar(30) NOT NULL,
    image_bytes bigint NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    valid_from timestamptz,
    valid_until timestamptz,
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT ck_persona_natural_banners_title CHECK (length(btrim(title)) > 0),
    -- Una imagen sin texto alternativo se rechaza (accesibilidad).
    CONSTRAINT ck_persona_natural_banners_alt CHECK (length(btrim(alt_text)) > 0),
    CONSTRAINT ck_persona_natural_banners_image_type CHECK (image_content_type IN ('image/png', 'image/jpeg', 'image/webp')),
    CONSTRAINT ck_persona_natural_banners_image_bytes CHECK (image_bytes > 0 AND image_bytes <= 2097152),
    CONSTRAINT ck_persona_natural_banners_vigencia CHECK (
        (valid_from IS NULL AND valid_until IS NULL) OR (valid_from IS NOT NULL AND valid_until IS NOT NULL AND valid_until > valid_from))
);
CREATE INDEX IF NOT EXISTS ix_persona_natural_banners_active_order ON admin.persona_natural_banners (is_active, sort_order);
COMMENT ON TABLE admin.persona_natural_banners IS
    'Banners del carrusel del portal de Persona Natural (HU #14073): contenido de trabajo (borrador). Imagen en el almacenamiento de banners (ADR-0057). Sin PII.';

CREATE TABLE IF NOT EXISTS admin.persona_natural_links (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_links PRIMARY KEY (id),
    code varchar(30) NOT NULL,
    url varchar(2048),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_persona_natural_links_code UNIQUE (code),
    CONSTRAINT ck_persona_natural_links_code CHECK (code IN ('POLITICA_DATOS', 'TERMINOS', 'CONTACTO_VENTAS', 'VIDEO'))
);
COMMENT ON TABLE admin.persona_natural_links IS
    'Enlaces del portal de Persona Natural (HU #14073): política de datos, términos, contacto de ventas y video. url nula = sin documento oficial. Sin PII.';

CREATE TABLE IF NOT EXISTS admin.persona_natural_content_versions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_content_versions PRIMARY KEY (id),
    version integer NOT NULL,
    content jsonb NOT NULL,
    published_at timestamptz NOT NULL DEFAULT now(),
    published_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_persona_natural_content_versions_version UNIQUE (version),
    CONSTRAINT ck_persona_natural_content_versions_version CHECK (version > 0)
);
COMMENT ON TABLE admin.persona_natural_content_versions IS
    'Versiones publicadas del contenido del portal de Persona Natural (HU #14073): foto inmutable (jsonb) con versión, fecha y usuario. El portal lee la última.';

-- Las versiones publicadas son historial: no se editan ni se borran.
CREATE OR REPLACE FUNCTION admin.trg_persona_natural_content_versions_guard() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'Una versión publicada del contenido es historial y no se edita ni se elimina.' USING ERRCODE = 'restrict_violation';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_persona_natural_content_versions_guard ON admin.persona_natural_content_versions;
CREATE TRIGGER tr_persona_natural_content_versions_guard BEFORE UPDATE OR DELETE ON admin.persona_natural_content_versions
    FOR EACH ROW EXECUTE FUNCTION admin.trg_persona_natural_content_versions_guard();

-- Auditoría y control de versión de fila del contenido de trabajo.
DROP TRIGGER IF EXISTS tr_persona_natural_faqs_row_version ON admin.persona_natural_faqs;
CREATE TRIGGER tr_persona_natural_faqs_row_version BEFORE UPDATE ON admin.persona_natural_faqs
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_faqs_audit ON admin.persona_natural_faqs;
CREATE TRIGGER tr_persona_natural_faqs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_faqs
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_persona_natural_banners_row_version ON admin.persona_natural_banners;
CREATE TRIGGER tr_persona_natural_banners_row_version BEFORE UPDATE ON admin.persona_natural_banners
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_banners_audit ON admin.persona_natural_banners;
CREATE TRIGGER tr_persona_natural_banners_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_banners
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_persona_natural_links_row_version ON admin.persona_natural_links;
CREATE TRIGGER tr_persona_natural_links_row_version BEFORE UPDATE ON admin.persona_natural_links
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_links_audit ON admin.persona_natural_links;
CREATE TRIGGER tr_persona_natural_links_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_links
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
