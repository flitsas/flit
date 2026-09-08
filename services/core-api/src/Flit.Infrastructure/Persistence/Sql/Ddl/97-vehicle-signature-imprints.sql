-- Auditoría de impronta manual firmada (paridad legacy public.vehicle_signature_imprints).
-- Se escribe al estampar en consolidado OT; guarda PEM efímero + hash + signature Base64.
-- Checklist: A1–A16. Soft-delete A6 (paridad legacy deleted_at).

CREATE TABLE IF NOT EXISTS tramites.vehicle_signature_imprints (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_signature_imprints PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_vehicle_signature_imprints_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_vehicle_signature_imprints_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,

    attachment_id uuid NOT NULL
        CONSTRAINT fk_vehicle_signature_imprints_attachment
        REFERENCES tramites.procedure_instance_attachments(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- Paridad legacy id_module (01 matrícula / 02 traspaso / 04 otros). En 2.0: código tipología o 'tramites'.
    module_code varchar(40) NOT NULL DEFAULT 'tramites',

    private_key text NOT NULL,
    public_key text NOT NULL,

    -- SHA-256 hex del PDF base (antes del stamp). Unique como en legacy.
    document_hash varchar(64) NOT NULL,
    signature text NOT NULL,
    signed_at timestamptz NOT NULL,

    was_signed_without_owner_signature boolean NOT NULL DEFAULT false,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,

    CONSTRAINT uq_vehicle_signature_imprints_document_hash UNIQUE (document_hash)
);

CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_tenant_instance
  ON tramites.vehicle_signature_imprints (tenant_id, procedure_instance_id, signed_at DESC);

CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_attachment
  ON tramites.vehicle_signature_imprints (attachment_id);

CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_document_hash
  ON tramites.vehicle_signature_imprints (document_hash);

COMMENT ON TABLE tramites.vehicle_signature_imprints IS
  'Auditoría de firma digital de impronta manual (paridad vehicle_signature_imprints legacy). HU #12116.';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.private_key IS
  '@pii:high — PEM RSA privado efímero del stamp. Paridad legacy; no loguear ni exponer en API.';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.public_key IS
  '@pii:medium — PEM RSA público efímero del stamp.';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.document_hash IS
  'SHA-256 hex del PDF de impronta antes de estampar (idempotencia / unique).';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.signature IS
  'Firma RSA-SHA256 PKCS#1 en Base64 (zona «Firma digital impronta»).';

ALTER TABLE tramites.vehicle_signature_imprints ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.vehicle_signature_imprints;
CREATE POLICY tenant_isolation ON tramites.vehicle_signature_imprints
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_vehicle_signature_imprints_row_version ON tramites.vehicle_signature_imprints;
CREATE TRIGGER tr_vehicle_signature_imprints_row_version BEFORE UPDATE ON tramites.vehicle_signature_imprints
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_vehicle_signature_imprints_audit ON tramites.vehicle_signature_imprints;
CREATE TRIGGER tr_vehicle_signature_imprints_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.vehicle_signature_imprints
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
