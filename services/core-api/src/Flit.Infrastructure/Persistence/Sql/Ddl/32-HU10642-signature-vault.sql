-- HU #10642 — Baúl de firmas: custodia de firmas precargadas | Feature #10641 (ADR-0025, R13/R14)
-- Firma precargada del apoderado, custodiada por compañía (NIT) y AISLADA por tenant (RLS).
-- Estado explícito 'activa'/'revocada' como baja lógica (NO soft-delete-por-fecha: excepción
-- documentada en ADR-0025 §2). Vigencia [vigencia_desde, vigencia_hasta] enforced en BD + dominio.
-- El material criptográfico NUNCA vive en la fila: solo se referencia storage_path/storage_sha256
-- (artefacto en S3 vía IAttachmentStorage, cifrado en reposo — ADR-0025 §3).

CREATE TABLE admin.signature_vault (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_signature_vault PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    document_type varchar(10) NOT NULL,
    document_number varchar(20) NOT NULL,
    nit_empresa varchar(20) NOT NULL,
    full_name varchar(200) NOT NULL,
    signature_hash varchar(64) NOT NULL,
    storage_path varchar(1000) NOT NULL,
    storage_sha256 varchar(64) NOT NULL,
    estado varchar(20) NOT NULL DEFAULT 'activa'
        CONSTRAINT ck_signature_vault_estado
        CHECK (estado IN ('activa', 'revocada')),
    vigencia_desde date NOT NULL,
    vigencia_hasta date NOT NULL,
    mandate_signer_id uuid
        CONSTRAINT fk_signature_vault_mandate_signers
        REFERENCES admin.mandate_signers(id) ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT ck_signature_vault_vigencia CHECK (vigencia_hasta >= vigencia_desde),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Consulta de consumo (ADR-0025 §2): firma vigente por (tenant, NIT) filtrando por estado.
CREATE INDEX ix_signature_vault_tenant_id_nit_empresa_estado
  ON admin.signature_vault(tenant_id, nit_empresa, estado);
-- FK index (checklist A9): FK opcional al mandatario.
CREATE INDEX ix_signature_vault_mandate_signer_id
  ON admin.signature_vault(mandate_signer_id);

-- Invariante ADR-0025 §2: a lo sumo UNA firma 'activa' por (tenant, NIT, documento).
CREATE UNIQUE INDEX uq_signature_vault_activa
  ON admin.signature_vault(tenant_id, nit_empresa, document_number)
  WHERE estado = 'activa';

COMMENT ON COLUMN admin.signature_vault.document_number IS '@pii:high';
COMMENT ON COLUMN admin.signature_vault.full_name IS '@pii:medium';

-- RLS por tenant, idéntico a procedure_instances/procedure_instance_prenda: el baúl es
-- tenant-scoped (ADR-0025 §2/§3), NO usa el aislamiento por ámbito OT de mandate_signers.
ALTER TABLE admin.signature_vault ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.signature_vault
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Triggers de negocio (checklist A16): row_version (concurrencia optimista) + audit_log.
DROP TRIGGER IF EXISTS tr_signature_vault_row_version ON admin.signature_vault;
CREATE TRIGGER tr_signature_vault_row_version BEFORE UPDATE ON admin.signature_vault
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_signature_vault_audit ON admin.signature_vault;
CREATE TRIGGER tr_signature_vault_audit AFTER INSERT OR UPDATE OR DELETE ON admin.signature_vault
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
