-- Trámites Rework — Slice 7: firma electrónica (mock) + FUR/contrato (mock, sin librería PDF).
-- Firma electrónica de la compraventa (solo traspaso_standard): comprador y vendedor firman.
-- Estados: pendiente_envio -> enviada -> firmada | rechazada | cancelada.
-- Proveedor MOCK (ver ISignatureProvider en Application; real later = ZapSign).
-- El FUR y el contrato de compraventa se almacenan como ADJUNTOS (tabla procedure_instance_attachments,
-- tipo 'fur' / 'compraventa') — sin schema extra; el generador es MOCK (sin librería PDF).

-- ============================================================================
-- procedure_instance_signatures
-- ============================================================================
CREATE TABLE tramites.procedure_instance_signatures (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_signatures PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    parte varchar(20) NOT NULL,
    doc_tipo varchar(40) NOT NULL DEFAULT 'compraventa',
    proveedor varchar(40) NOT NULL DEFAULT 'mock',
    estado varchar(20) NOT NULL DEFAULT 'pendiente_envio',
    envelope_id varchar(200),
    pdf_path varchar(1000),
    sha256 varchar(64),
    metadata jsonb,
    solicitado_at timestamptz NOT NULL DEFAULT now(),
    firmado_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

CREATE INDEX ix_procedure_instance_signatures_procedure_instance
  ON tramites.procedure_instance_signatures(procedure_instance_id);
CREATE INDEX ix_procedure_instance_signatures_tenant_id_instance
  ON tramites.procedure_instance_signatures(tenant_id, procedure_instance_id);

COMMENT ON COLUMN tramites.procedure_instance_signatures.parte IS 'comprador|vendedor';
COMMENT ON COLUMN tramites.procedure_instance_signatures.estado IS 'pendiente_envio|enviada|firmada|rechazada|cancelada';
COMMENT ON COLUMN tramites.procedure_instance_signatures.proveedor IS 'mock (real later = zapsign)';
COMMENT ON COLUMN tramites.procedure_instance_signatures.metadata IS 'metadata del proveedor (p. ej. signUrl)';

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.procedure_instance_signatures ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_signatures
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- Trigger de auditoría (public.trg_audit_log)
-- ============================================================================
DROP TRIGGER IF EXISTS tr_procedure_instance_signatures_audit ON tramites.procedure_instance_signatures;
CREATE TRIGGER tr_procedure_instance_signatures_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_signatures
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
