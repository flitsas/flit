-- FEATURE-08 / Fase 2b — Parte B2: CREATE tramites.procedure_type_snapshots
-- Migración: 20260721100200_F08_ProcedureTypeSnapshots
-- CFD-01/AC#5: snapshot liviano del tipo al crear instancia de trámite.
-- FK a procedure_instances verificada: tramites.procedure_instances(id) confirmado.
-- Tiene tenant_id → RLS tenant_isolation.
-- Inmutable (INSERT-only): sin updated_at/by ni deleted_at/by.

CREATE TABLE IF NOT EXISTS tramites.procedure_type_snapshots (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_type_snapshots PRIMARY KEY (id),
    tenant_id uuid NOT NULL
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL
        REFERENCES tramites.procedure_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    type_version integer NOT NULL,
    snapshot jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_procedure_type_snapshots_instance
        UNIQUE (procedure_instance_id)
);

CREATE INDEX IF NOT EXISTS ix_procedure_type_snapshots_tenant_id
    ON tramites.procedure_type_snapshots(tenant_id);
CREATE INDEX IF NOT EXISTS ix_procedure_type_snapshots_procedure_type_id
    ON tramites.procedure_type_snapshots(procedure_type_id);

COMMENT ON TABLE tramites.procedure_type_snapshots IS
'Snapshot liviano del tipo de trámite al crear instancia (CFD-01/AC#5). Inmutable post-INSERT. Captura: gate_profile + conformationRules + stepSectionTypes (sin form_fields).';
COMMENT ON COLUMN tramites.procedure_type_snapshots.snapshot IS
'Snapshot mínimo. Esquema: { "code", "name", "family", "version", "gateProfile": {...}, "conformationRules": [{entityCode, validationProfile}], "stepSectionTypes": [{stepCode, sectionTypes: []}] }';

ALTER TABLE tramites.procedure_type_snapshots ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_type_snapshots;
CREATE POLICY tenant_isolation ON tramites.procedure_type_snapshots
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_procedure_type_snapshots_audit ON tramites.procedure_type_snapshots;
CREATE TRIGGER tr_procedure_type_snapshots_audit
    AFTER INSERT ON tramites.procedure_type_snapshots
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
