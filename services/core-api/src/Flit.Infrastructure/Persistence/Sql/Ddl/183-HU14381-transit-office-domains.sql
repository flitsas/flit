-- =============================================================================
-- HU #14381 (Feature #14380, Épica #14297) — dominio propio del portal Persona Natural de cada organismo.
-- Migración: 20261106110000_HU14381_DominioPortalOrganismo. ADR-0081 (Propuesto), alternativa B.
--
-- Modelo propio por organismo (transit_office_id), con el mismo ciclo que admin.tenant_domains (pending → verified →
-- active; cualquiera → failed con motivo) pero SIN reutilizar su tabla: sus disparadores exigen una cabeza MARCA_BLANCA y
-- un dominio de marca blanca activa una red, cosa que no aplica a un portal público sin login. Marca blanca no cambia.
--
--   1. admin.transit_office_domains — un dominio VIGENTE por organismo y un host vigente en la plataforma (índices únicos
--      parciales WHERE deleted_at IS NULL: retirar conserva la fila y permite volver a registrar).
--   2. admin.trg_domain_host_unico_entre_tablas() — el host es único ENTRE las dos tablas (marca blanca y portal). Se
--      instala en ambas (aditivo en admin.tenant_domains: solo valida; no cambia ninguna de sus reglas ni disparadores).
--      Serializa con un candado consultivo por host para que dos altas concurrentes en tablas distintas no se crucen.
--   3. admin.v_active_portal_domains — lo único que resuelve un host al portal: active, vigente y de un organismo activo.
--
-- Tabla global sin tenant_id y sin PII (host y token de verificación). Sin RLS: la administran el organismo o el SuperAdmin y
-- el alcance lo impone el caso de uso. Reservados (dominio FLIT, ict.*) se rechazan en la aplicación (Domains:Reserved).
-- Idempotente y reversible (Down completo en la migración).
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.transit_office_domains (
    id                     uuid         NOT NULL DEFAULT uuidv7(),
    transit_office_id      uuid         NOT NULL,
    host                   varchar(253) NOT NULL,
    status                 varchar(20)  NOT NULL DEFAULT 'pending',
    verification_token     varchar(64)  NOT NULL,
    verified_at            timestamptz  NULL,
    activated_at           timestamptz  NULL,
    failed_at              timestamptz  NULL,
    failure_reason         varchar(500) NULL,
    certificate_issued_at  timestamptz  NULL,
    certificate_expires_at timestamptz  NULL,
    last_checked_at        timestamptz  NULL,
    next_check_at          timestamptz  NULL,
    check_attempts         integer      NOT NULL DEFAULT 0,
    grace_until            timestamptz  NULL,
    created_at             timestamptz  NOT NULL DEFAULT now(),
    created_by             uuid         NULL,
    updated_at             timestamptz  NOT NULL DEFAULT now(),
    updated_by             uuid         NULL,
    deleted_at             timestamptz  NULL,
    deleted_by             uuid         NULL,
    row_version            bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_transit_office_domains PRIMARY KEY (id),
    CONSTRAINT fk_transit_office_domains_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_transit_office_domains_status CHECK (status IN ('pending', 'verified', 'active', 'failed')),
    CONSTRAINT ck_transit_office_domains_host_lower CHECK (host = lower(host)),
    CONSTRAINT ck_transit_office_domains_host_length CHECK (char_length(host) BETWEEN 4 AND 253),
    -- Misma expresión que ck_tenant_domains_host_format (RFC 1123, TLD alfabético o punycode).
    CONSTRAINT ck_transit_office_domains_host_format CHECK (
        host ~ '^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$'
    ),
    CONSTRAINT ck_transit_office_domains_verification_token CHECK (verification_token ~ '^[A-Za-z0-9_-]{16,64}$'),
    CONSTRAINT ck_transit_office_domains_check_attempts CHECK (check_attempts >= 0),
    CONSTRAINT ck_transit_office_domains_verified_requires_verified_at CHECK (
        status NOT IN ('verified', 'active') OR verified_at IS NOT NULL
    ),
    CONSTRAINT ck_transit_office_domains_active_requires_verified CHECK (
        status <> 'active' OR (verified_at IS NOT NULL AND activated_at IS NOT NULL AND certificate_issued_at IS NOT NULL)
    ),
    CONSTRAINT ck_transit_office_domains_failed_requires_reason CHECK (
        status <> 'failed' OR (failed_at IS NOT NULL AND failure_reason IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_domains_office
    ON admin.transit_office_domains (transit_office_id) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_domains_host
    ON admin.transit_office_domains (host) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_domains_verification_token
    ON admin.transit_office_domains (verification_token);
CREATE INDEX IF NOT EXISTS ix_transit_office_domains_office
    ON admin.transit_office_domains (transit_office_id);
CREATE INDEX IF NOT EXISTS ix_transit_office_domains_host_active
    ON admin.transit_office_domains (host) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_transit_office_domains_next_check
    ON admin.transit_office_domains (next_check_at) WHERE deleted_at IS NULL AND status IN ('pending', 'failed', 'active');

COMMENT ON TABLE admin.transit_office_domains IS
    'HU #14381 · Dominio propio del portal Persona Natural de un organismo: uno vigente por organismo y host único en la plataforma (también frente a admin.tenant_domains). Solo resuelve el portal en estado active (admin.v_active_portal_domains). Sin login ni acotación de red.';
COMMENT ON COLUMN admin.transit_office_domains.host IS
    'Host RFC 1123 ya normalizado por la aplicación: minúsculas, punycode, sin esquema, puerto ni ruta.';
COMMENT ON COLUMN admin.transit_office_domains.status IS
    'pending → verified (TXT correcto) → active (certificado emitido); cualquiera → failed (failed_at + failure_reason).';
COMMENT ON COLUMN admin.transit_office_domains.verification_token IS
    '@pii:low — valor esperado en el TXT _flit-verify.<host>. Aleatorio, único, nunca se reutiliza.';
COMMENT ON COLUMN admin.transit_office_domains.failure_reason IS
    'Motivo estable del fallo: TXT_NOT_FOUND, TXT_MISMATCH, DNS_ERROR o CERT_NOT_ISSUED.';
COMMENT ON COLUMN admin.transit_office_domains.deleted_at IS
    'Retiro lógico: la fila se conserva como historia y deja de contar para los índices únicos parciales. NULL = vigente.';
COMMENT ON COLUMN admin.transit_office_domains.row_version IS
    'Token de concurrencia (public.trg_row_version).';

DROP TRIGGER IF EXISTS tr_transit_office_domains_row_version ON admin.transit_office_domains;
CREATE TRIGGER tr_transit_office_domains_row_version BEFORE UPDATE ON admin.transit_office_domains
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_transit_office_domains_audit ON admin.transit_office_domains;
CREATE TRIGGER tr_transit_office_domains_audit AFTER INSERT OR UPDATE OR DELETE ON admin.transit_office_domains
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- Unicidad del host ENTRE admin.tenant_domains y admin.transit_office_domains
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION admin.trg_domain_host_unico_entre_tablas()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    en_uso boolean;
BEGIN
    IF NEW.deleted_at IS NOT NULL THEN
        RETURN NEW;
    END IF;

    -- Dos altas del mismo host en tablas distintas se serializan aquí; la segunda ya ve la fila de la primera.
    PERFORM pg_advisory_xact_lock(hashtextextended('flit-domain-host:' || NEW.host, 0));

    IF TG_TABLE_NAME = 'tenant_domains' THEN
        en_uso := EXISTS (SELECT 1 FROM admin.transit_office_domains WHERE host = NEW.host AND deleted_at IS NULL);
    ELSE
        en_uso := EXISTS (SELECT 1 FROM admin.tenant_domains WHERE host = NEW.host AND deleted_at IS NULL);
    END IF;

    IF en_uso THEN
        RAISE EXCEPTION 'El host % ya está registrado en la plataforma', NEW.host
            USING ERRCODE = 'unique_violation', CONSTRAINT = 'uq_domain_host_entre_tablas';
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION admin.trg_domain_host_unico_entre_tablas() IS
    'HU #14381 · Un host vigente solo puede estar en admin.tenant_domains (marca blanca) o en admin.transit_office_domains (portal), nunca en las dos. Solo valida: no cambia ninguna otra regla de marca blanca.';

DROP TRIGGER IF EXISTS tr_tenant_domains_host_unico ON admin.tenant_domains;
CREATE TRIGGER tr_tenant_domains_host_unico
    BEFORE INSERT OR UPDATE OF host, deleted_at ON admin.tenant_domains
    FOR EACH ROW EXECUTE FUNCTION admin.trg_domain_host_unico_entre_tablas();

DROP TRIGGER IF EXISTS tr_transit_office_domains_host_unico ON admin.transit_office_domains;
CREATE TRIGGER tr_transit_office_domains_host_unico
    BEFORE INSERT OR UPDATE OF host, deleted_at ON admin.transit_office_domains
    FOR EACH ROW EXECUTE FUNCTION admin.trg_domain_host_unico_entre_tablas();

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- Vista: lo único que resuelve un host al portal de un organismo
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE VIEW admin.v_active_portal_domains AS
SELECT d.host,
       d.transit_office_id
  FROM admin.transit_office_domains d
  JOIN catalogs.transit_offices o ON o.id = d.transit_office_id
 WHERE d.status = 'active'
   AND d.deleted_at IS NULL
   AND o.is_active = true;

COMMENT ON VIEW admin.v_active_portal_domains IS
    'HU #14381 · Hosts que abren el portal de un organismo: active, vigentes y de un organismo activo. Un dominio pendiente, fallido o retirado no está aquí.';
