-- HU #12348 / #12409 — auditoría de intentos rechazados al crear trámite
-- Migración: 20260910230000_HU12348_ProcedureRadicationGateAudit

CREATE TABLE IF NOT EXISTS tramites.procedure_radication_gate_denials (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_radication_gate_denials PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    user_id uuid,
    transit_office_id uuid,
    denial_reason varchar(80) NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_procedure_radication_gate_denials_tenant_occurred
    ON tramites.procedure_radication_gate_denials(tenant_id, occurred_at DESC);

COMMENT ON TABLE tramites.procedure_radication_gate_denials IS
    'HU #12348/#12409: intentos rechazados al crear trámite (OT no permitido, red inactiva, compañía inactiva).';

ALTER TABLE tramites.procedure_radication_gate_denials ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_radication_gate_denials;
CREATE POLICY tenant_isolation ON tramites.procedure_radication_gate_denials
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_procedure_radication_gate_denials_audit ON tramites.procedure_radication_gate_denials;
CREATE TRIGGER tr_procedure_radication_gate_denials_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_radication_gate_denials
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
