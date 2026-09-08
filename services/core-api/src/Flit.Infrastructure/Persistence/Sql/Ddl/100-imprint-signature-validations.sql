-- HU #12148 — bitácora append-only de validaciones OT de firma digital de impronta manual.
-- Cada intento de verificación (valid / invalid / not_found cuando aplica) queda registrado.
-- Sin soft-delete ni row_version (mismo patrón append-only que notification_delivery_logs).

CREATE TABLE IF NOT EXISTS tramites.imprint_signature_validations (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_imprint_signature_validations PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_imprint_signature_validations_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    vehicle_signature_imprint_id uuid NOT NULL
        CONSTRAINT fk_imprint_signature_validations_imprint
        REFERENCES tramites.vehicle_signature_imprints(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_imprint_signature_validations_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    placa varchar(20) NOT NULL,

    validated_by uuid NOT NULL,
    validated_at timestamptz NOT NULL DEFAULT now(),

    result varchar(20) NOT NULL
        CONSTRAINT ck_imprint_signature_validations_result
        CHECK (result IN ('valid', 'invalid', 'not_found')),

    failure_reason text
);

CREATE INDEX IF NOT EXISTS ix_imprint_signature_validations_tenant_validated_at
  ON tramites.imprint_signature_validations (tenant_id, validated_at DESC);

CREATE INDEX IF NOT EXISTS ix_imprint_signature_validations_imprint
  ON tramites.imprint_signature_validations (vehicle_signature_imprint_id);

COMMENT ON TABLE tramites.imprint_signature_validations IS
  'HU #12148 — log append-only de validaciones OT del hash/firma de impronta manual firmada.';
COMMENT ON COLUMN tramites.imprint_signature_validations.placa IS
  'Snapshot de la placa al validar (normalizada Trim+Upper).';
COMMENT ON COLUMN tramites.imprint_signature_validations.result IS
  'valid | invalid | not_found — resultado criptográfico o de existencia al validar.';
COMMENT ON COLUMN tramites.imprint_signature_validations.failure_reason IS
  'Detalle cuando result <> valid (p. ej. firma RSA no coincide).';

ALTER TABLE tramites.imprint_signature_validations ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.imprint_signature_validations;
CREATE POLICY tenant_isolation ON tramites.imprint_signature_validations
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_imprint_signature_validations_audit ON tramites.imprint_signature_validations;
CREATE TRIGGER tr_imprint_signature_validations_audit AFTER INSERT ON tramites.imprint_signature_validations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
