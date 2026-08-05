-- Política de documento de prenda por compañía + Organismo de Tránsito.
-- Default de producto: el documento (inscripcion_prenda) es OBLIGATORIO.
-- Tabla DISPERSA opt-out: fila con document_optional=true = el check está activo y la prenda
-- deja de ser obligatoria para ese par (tenant, OT). Ausencia de fila = obligatorio.
-- Configurable por SuperAdmin (ficha compañía) y por ot_admin (hub OT).
-- RLS estricta por tenant; lecturas cross-tenant (OT admin / gate) usan set_config local.

CREATE TABLE IF NOT EXISTS admin.tenant_transit_office_prenda_document_policies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_transit_office_prenda_document_policies PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    -- true = check activo ⇒ documento de prenda OPCIONAL (opt-out del default obligatorio).
    document_optional boolean NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_tenant_transit_office_prenda_document_policies
        UNIQUE (tenant_id, transit_office_id)
);

CREATE INDEX IF NOT EXISTS ix_tenant_ot_prenda_doc_policies_tenant_office
  ON admin.tenant_transit_office_prenda_document_policies (tenant_id, transit_office_id);

ALTER TABLE admin.tenant_transit_office_prenda_document_policies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_transit_office_prenda_document_policies;
CREATE POLICY tenant_isolation ON admin.tenant_transit_office_prenda_document_policies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_tenant_ot_prenda_doc_policies_row_version
  ON admin.tenant_transit_office_prenda_document_policies;
CREATE TRIGGER tr_tenant_ot_prenda_doc_policies_row_version
  BEFORE UPDATE ON admin.tenant_transit_office_prenda_document_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_ot_prenda_doc_policies_audit
  ON admin.tenant_transit_office_prenda_document_policies;
CREATE TRIGGER tr_tenant_ot_prenda_doc_policies_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_prenda_document_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
