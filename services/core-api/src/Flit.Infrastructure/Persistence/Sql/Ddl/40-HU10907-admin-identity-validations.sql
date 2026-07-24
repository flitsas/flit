-- HU #10907 — Validación de identidad ADMINISTRATIVA desacoplada de un trámite (ADR-0034).
-- Bloque AGNÓSTICO DEL SUJETO: se ancla a un sujeto por (subject_type, subject_ref) — hoy el
-- representante legal (HU #10900), reutilizable por el mandatario u otros sin tocar el esquema. Se
-- inicia/reenvía por correo reutilizando el proveedor externo (Kyverum), es VIGENTE 30 días desde la
-- aprobación y, al aprobar, se vincula al sujeto. TENANT-SCOPED con RLS (app.current_tenant_id), mismo
-- patrón que el baúl de firmas (32-HU10642) y el directorio de representantes (39-HU10900).
-- document_number y email son PII (Ley 1581): COMMENT @pii + RLS + auditoría (triggers estándar). El
-- secreto del webhook se guarda CIFRADO (webhook_secret_encrypted), nunca en claro.
-- DDL IDEMPOTENTE (CREATE ... IF NOT EXISTS + guardas para índices/políticas/triggers): re-aplicable.

CREATE TABLE IF NOT EXISTS admin.admin_identity_validations (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_admin_identity_validations PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    -- Sujeto anclado (agnóstico): tipo + id en su tabla. No hay FK (el bloque no acopla al sujeto).
    subject_type varchar(40) NOT NULL,
    subject_ref uuid NOT NULL,
    document_type varchar(10) NOT NULL,
    document_number varchar(20) NOT NULL,
    name varchar(200) NOT NULL,
    email varchar(200) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'enviado'
        CONSTRAINT ck_admin_identity_validations_status
        CHECK (status IN ('enviado', 'en_proceso', 'aprobado', 'rechazado', 'expirado')),
    provider varchar(20) NOT NULL DEFAULT 'kyverum',
    capture_url varchar(1000),
    kyverum_verification_id varchar(100),
    -- Secreto HMAC del webhook CIFRADO (Data Protection). NUNCA en claro.
    webhook_secret_encrypted text,
    provider_status varchar(40),
    provider_payload jsonb,
    certificate_hash varchar(200),
    validated_at timestamptz,
    valid_until timestamptz,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Consumo principal: la validación más reciente por sujeto en el tenant (reenvío + anclaje).
CREATE INDEX IF NOT EXISTS ix_admin_identity_validations_tenant_subject
  ON admin.admin_identity_validations(tenant_id, subject_type, subject_ref);
-- Reconciliación por id de verificación del proveedor.
CREATE INDEX IF NOT EXISTS ix_admin_identity_validations_verification_id
  ON admin.admin_identity_validations(kyverum_verification_id);
-- Barrido de vigencia/estado (identidades aprobadas por vencer).
CREATE INDEX IF NOT EXISTS ix_admin_identity_validations_tenant_status_valid_until
  ON admin.admin_identity_validations(tenant_id, status, valid_until);

COMMENT ON COLUMN admin.admin_identity_validations.document_number IS '@pii:high';
COMMENT ON COLUMN admin.admin_identity_validations.name IS '@pii:medium';
COMMENT ON COLUMN admin.admin_identity_validations.email IS '@pii';

ALTER TABLE admin.admin_identity_validations ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.admin_identity_validations;
CREATE POLICY tenant_isolation ON admin.admin_identity_validations
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_admin_identity_validations_row_version ON admin.admin_identity_validations;
CREATE TRIGGER tr_admin_identity_validations_row_version BEFORE UPDATE ON admin.admin_identity_validations
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_admin_identity_validations_audit ON admin.admin_identity_validations;
CREATE TRIGGER tr_admin_identity_validations_audit AFTER INSERT OR UPDATE OR DELETE ON admin.admin_identity_validations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
