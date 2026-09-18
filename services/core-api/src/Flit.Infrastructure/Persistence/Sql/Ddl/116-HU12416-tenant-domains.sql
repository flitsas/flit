-- HU #12416 (Feature #12368, Épica #12237 Marca Blanca) — dominio dedicado de la red como dato de la plataforma.
-- Migración: 20260916110000_E12416_TenantDomains · ADR-0060 (Propuesto) D1 (modelo) y D2 (vista para CORS).
--
-- Qué crea:
--   1. admin.tenant_domains: un dominio VIGENTE por cabeza MARCA_BLANCA y un host único en toda la
--      plataforma (índices únicos parciales WHERE deleted_at IS NULL: retirar un dominio conserva la
--      fila y permite re-registrar el mismo host o uno nuevo, AC5). host se guarda ya normalizado
--      (minúsculas, punycode para IDN, sin esquema/puerto/ruta) y el motor lo exige con dos CHECK
--      (RFC 1123: etiquetas alfanuméricas con guiones internos de 1 a 63, al menos dos etiquetas, TLD
--      alfabético o xn--, total <= 253). Estados: pending → verified → active; cualquiera → failed
--      (con motivo). verification_token es el valor que el cliente publica en TXT _flit-verify.<host>.
--      next_check_at / last_checked_at / check_attempts / grace_until y certificate_* quedan listos
--      para el ciclo de comprobación (#12425) y la señal de certificado (#12426); aquí no hay job.
--   2. Reutiliza identity.trg_require_marca_blanca_head() (DDL 115): solo una cabeza con
--      tenant_type = 'MARCA_BLANCA' AND is_group_parent = true puede tener dominio (AC2). Lanza
--      check_violation con CONSTRAINT = ck_tenant_domains_marca_blanca para que la aplicación lo
--      traduzca. Se dispara al insertar y al cambiar tenant_id u host; NO al retirar ni al cambiar el
--      estado: la fila se conserva si la cabeza deja de ser MARCA_BLANCA y deja de resolver en lectura.
--   3. admin.v_active_network_domains: lo único que resuelve red (resolutor por host y CORS del
--      Gateway, ADR-0060 D2): dominio active y vigente de una cabeza MARCA_BLANCA activa (is_active).
--      Apagar la clase o inactivar la cabeza lo saca de la vista sin tocar tenant_domains (AC5).
--
-- Reservados (dominio FLIT y portal de organismos, AC3) se rechazan en la APLICACIÓN
-- (Domains:Reserved): la base no conoce el dominio de despliegue.
-- Sin poblado (AC6): la tabla nace vacía; el dominio de FLIT y el portal OT siguen igual.
-- Idempotente (IF NOT EXISTS / DROP ... IF EXISTS / CREATE OR REPLACE) y reversible (Down completo en
-- la migración). RLS tenant_isolation como el resto del repo, sin FORCE (la app conecta como owner).
-- Auditoría: trg_audit_log (rastro técnico). El old/new legible (AC5) lo escribe el repositorio en
-- admin.tenant_config_audit_logs (EntityName=TenantDomain, FieldName ∈ {host, status}), NO un trigger.

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. admin.tenant_domains — dominio propio de la red (uno vigente por cabeza, host único global)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.tenant_domains (
    id                     uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id              uuid         NOT NULL,
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

    CONSTRAINT pk_tenant_domains PRIMARY KEY (id),
    CONSTRAINT fk_tenant_domains_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_tenant_domains_status CHECK (status IN ('pending', 'verified', 'active', 'failed')),
    -- Normalizado en la aplicación: minúsculas y punycode. El motor no acepta mayúsculas ni IDN sin codificar.
    CONSTRAINT ck_tenant_domains_host_lower CHECK (host = lower(host)),
    CONSTRAINT ck_tenant_domains_host_length CHECK (char_length(host) BETWEEN 4 AND 253),
    -- RFC 1123: etiquetas [a-z0-9] con guiones internos (1..63), >= 2 etiquetas, TLD alfabético (2..63) o
    -- punycode (xn--…). Sin esquema, puerto, ruta, comodines, punto final ni espacios.
    CONSTRAINT ck_tenant_domains_host_format CHECK (
        host ~ '^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$'
    ),
    CONSTRAINT ck_tenant_domains_verification_token CHECK (verification_token ~ '^[A-Za-z0-9_-]{16,64}$'),
    CONSTRAINT ck_tenant_domains_check_attempts CHECK (check_attempts >= 0),
    CONSTRAINT ck_tenant_domains_verified_requires_verified_at CHECK (
        status NOT IN ('verified', 'active') OR verified_at IS NOT NULL
    ),
    CONSTRAINT ck_tenant_domains_active_requires_verified CHECK (
        status <> 'active' OR (verified_at IS NOT NULL AND activated_at IS NOT NULL AND certificate_issued_at IS NOT NULL)
    ),
    CONSTRAINT ck_tenant_domains_failed_requires_reason CHECK (
        status <> 'failed' OR (failed_at IS NOT NULL AND failure_reason IS NOT NULL)
    )
);

-- Un dominio vigente por red · un host vigente en toda la plataforma (re-registro tras retiro permitido) ·
-- token no reutilizable jamás (ni tras retiro).
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_tenant_id
    ON admin.tenant_domains (tenant_id) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_host
    ON admin.tenant_domains (host) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_verification_token
    ON admin.tenant_domains (verification_token);
-- Cobertura de la FK (A9) incluyendo las filas retiradas (historial por cabeza).
CREATE INDEX IF NOT EXISTS ix_tenant_domains_tenant_id
    ON admin.tenant_domains (tenant_id);
-- Camino caliente del resolutor por host (la caché de 60 s lo cubre casi siempre).
CREATE INDEX IF NOT EXISTS ix_tenant_domains_host_active
    ON admin.tenant_domains (host) WHERE status = 'active' AND deleted_at IS NULL;
-- Claim del job de comprobación (#12425): FOR UPDATE SKIP LOCKED por next_check_at.
CREATE INDEX IF NOT EXISTS ix_tenant_domains_next_check
    ON admin.tenant_domains (next_check_at) WHERE deleted_at IS NULL AND status IN ('pending', 'failed', 'active');

COMMENT ON TABLE admin.tenant_domains IS
    'ADR-0060 (HU #12416) · Dominio propio de una red MARCA_BLANCA: uno vigente por cabeza (uq_tenant_domains_tenant_id) y host único en la plataforma (uq_tenant_domains_host), ambos WHERE deleted_at IS NULL. Solo resuelve red en estado active (ver admin.v_active_network_domains). Reservados (dominio FLIT, portal OT) se rechazan en la aplicación (Domains:Reserved).';
COMMENT ON COLUMN admin.tenant_domains.tenant_id IS
    'Cabeza de red dueña del dominio (identity.tenants.id). Solo MARCA_BLANCA (tr_tenant_domains_marca_blanca). FK RESTRICT: no se borra una cabeza con dominio.';
COMMENT ON COLUMN admin.tenant_domains.host IS
    'Nombre de host RFC 1123 ya normalizado por la aplicación: minúsculas, punycode para IDN, sin esquema, puerto ni ruta (ck_tenant_domains_host_*).';
COMMENT ON COLUMN admin.tenant_domains.status IS
    'pending → verified → active; cualquiera → failed (failed_at + failure_reason). Transiciones auditadas en admin.tenant_config_audit_logs (EntityName=TenantDomain, FieldName=status) por el repositorio.';
COMMENT ON COLUMN admin.tenant_domains.verification_token IS
    '@pii:low — valor esperado en TXT _flit-verify.<host>. Aleatorio (>= 16 chars base64url/base32), único en la plataforma, no adivinable; nunca se reutiliza.';
COMMENT ON COLUMN admin.tenant_domains.verified_at IS
    'Instante en que se comprobó la titularidad (TXT). Obligatorio en verified y active.';
COMMENT ON COLUMN admin.tenant_domains.activated_at IS
    'Instante en que el dominio empezó a resolver red. Obligatorio en active.';
COMMENT ON COLUMN admin.tenant_domains.failed_at IS
    'Instante del último fallo. Obligatorio en failed.';
COMMENT ON COLUMN admin.tenant_domains.failure_reason IS
    'Motivo legible del fallo (p.ej. TXT_NOT_FOUND, TXT_MISMATCH, CERT_NOT_ISSUED). Obligatorio en failed.';
COMMENT ON COLUMN admin.tenant_domains.certificate_issued_at IS
    'Señal de #12426 (PUT /internal/domains/{host}/certificate). Requisito para pasar a active (ck_tenant_domains_active_requires_verified).';
COMMENT ON COLUMN admin.tenant_domains.certificate_expires_at IS
    'Vencimiento del certificado emitido en el borde (#12426). Informativo para la renovación.';
COMMENT ON COLUMN admin.tenant_domains.last_checked_at IS
    'Última comprobación del TXT por el job dns-domain-verification (#12425).';
COMMENT ON COLUMN admin.tenant_domains.next_check_at IS
    'Cuándo toca la siguiente comprobación; NULL = sin comprobación programada. Claim con FOR UPDATE SKIP LOCKED (ix_tenant_domains_next_check).';
COMMENT ON COLUMN admin.tenant_domains.check_attempts IS
    'Comprobaciones consecutivas sin éxito; se reinicia al verificar.';
COMMENT ON COLUMN admin.tenant_domains.grace_until IS
    'Periodo de gracia (Domains:Verification:GracePeriod) cuando el TXT desaparece en un dominio activo; sigue resolviendo hasta esta fecha (#12425 AC4).';
COMMENT ON COLUMN admin.tenant_domains.deleted_at IS
    'Retiro lógico del dominio (AC5 #12416): la fila se conserva como historia y deja de contar para los índices únicos parciales. NULL = vigente.';
COMMENT ON COLUMN admin.tenant_domains.row_version IS
    'Token de concurrencia (public.trg_row_version). Un cambio del SuperAdmin y una transición del job concurrentes se resuelven aquí.';

DROP TRIGGER IF EXISTS tr_tenant_domains_marca_blanca ON admin.tenant_domains;
CREATE TRIGGER tr_tenant_domains_marca_blanca
    BEFORE INSERT OR UPDATE OF tenant_id, host ON admin.tenant_domains
    FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_domains_marca_blanca');

DROP TRIGGER IF EXISTS tr_tenant_domains_row_version ON admin.tenant_domains;
CREATE TRIGGER tr_tenant_domains_row_version BEFORE UPDATE ON admin.tenant_domains
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_domains_audit ON admin.tenant_domains;
CREATE TRIGGER tr_tenant_domains_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_domains
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE admin.tenant_domains ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_domains;
CREATE POLICY tenant_isolation ON admin.tenant_domains
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. admin.v_active_network_domains — lo único que resuelve red (resolutor por host, CORS del Gateway)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE VIEW admin.v_active_network_domains AS
SELECT d.host,
       d.tenant_id AS head_tenant_id
  FROM admin.tenant_domains d
  JOIN identity.tenants t ON t.id = d.tenant_id
 WHERE d.status = 'active'
   AND d.deleted_at IS NULL
   AND t.tenant_type = 'MARCA_BLANCA'
   AND t.is_group_parent = true
   AND t.is_active = true;

COMMENT ON VIEW admin.v_active_network_domains IS
    'ADR-0060 D2 (HU #12416) · Dominios que resuelven red (marca, acceso acotado, CORS): active, vigentes y de una cabeza MARCA_BLANCA activa. Apagar la clase o inactivar la cabeza los saca de aquí sin tocar tenant_domains (AC5 #12416, AC7 #12418).';
