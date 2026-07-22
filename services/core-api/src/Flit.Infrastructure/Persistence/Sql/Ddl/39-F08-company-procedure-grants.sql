-- FEATURE-08 — Habilitación de tipos de trámite por compañía (grant model)
-- Migración: 20260722100000_F08_CompanyProcedureGrants
-- Un SuperAdmin habilita, por compañía (tenant), qué tipos de trámite publicados puede usar.
-- Modelo grant: la presencia de la fila = habilitado. Sin fila = NO habilitado. El selector del
-- operador (GET /api/v1/procedure-types) solo muestra los tipos con grant para su tenant.
-- Tiene tenant_id → RLS tenant_isolation por app.current_tenant_id (SuperAdmin fija el GUC al
-- tenant destino dentro de la transacción de escritura; las lecturas van por owner-bypass + WHERE).
-- procedure_type_id se guarda como uuid sin FK (mismo criterio que transit_office_id en
-- admin.tenant_transit_office_grants): el grant no acopla el schema admin al catálogo de tramites.

CREATE TABLE IF NOT EXISTS admin.company_procedure_type_grants (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_procedure_type_grants PRIMARY KEY (id),
    tenant_id uuid NOT NULL
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_company_procedure_type_grants UNIQUE (tenant_id, procedure_type_id)
);

CREATE INDEX IF NOT EXISTS ix_company_procedure_type_grants_tenant_id
    ON admin.company_procedure_type_grants(tenant_id);

COMMENT ON TABLE admin.company_procedure_type_grants IS
'Tipos de trámite habilitados por compañía (FEATURE-08). Fila = habilitado; sin fila = no habilitado. El selector del operador filtra los tipos publicados por los grants de su tenant.';

ALTER TABLE admin.company_procedure_type_grants ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_procedure_type_grants;
CREATE POLICY tenant_isolation ON admin.company_procedure_type_grants
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_company_procedure_type_grants_audit ON admin.company_procedure_type_grants;
CREATE TRIGGER tr_company_procedure_type_grants_audit
    AFTER INSERT OR UPDATE OR DELETE ON admin.company_procedure_type_grants
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
