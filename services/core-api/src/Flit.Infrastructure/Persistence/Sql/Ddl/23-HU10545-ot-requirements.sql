-- HU #10545 — Requisitos configurables por OT | Feature #10535 (R21/R22/R23)

CREATE TABLE admin.ot_requirements (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_requirements PRIMARY KEY (id),
    tenant_id uuid NOT NULL UNIQUE REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    requires_rnmc boolean NOT NULL DEFAULT false,
    allow_plate_preassign boolean NOT NULL DEFAULT false,
    identity_validation_enabled boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_ot_requirements_transit_office_id UNIQUE (transit_office_id)
);

-- RLS: la config de requisitos del OT es operativa (la empresa que radica la necesita para su
-- wizard y para el gate de identidad), no es dato sensible por tenant. Lectura ABIERTA; la
-- escritura queda AISLADA por tenant (solo el OT dueño modifica su fila).
ALTER TABLE admin.ot_requirements ENABLE ROW LEVEL SECURITY;
CREATE POLICY ot_requirements_read ON admin.ot_requirements
  FOR SELECT USING (true);
CREATE POLICY ot_requirements_write ON admin.ot_requirements
  FOR ALL
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
  WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_ot_requirements_row_version ON admin.ot_requirements;
CREATE TRIGGER tr_ot_requirements_row_version BEFORE UPDATE ON admin.ot_requirements
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_ot_requirements_audit ON admin.ot_requirements;
CREATE TRIGGER tr_ot_requirements_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_requirements
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
