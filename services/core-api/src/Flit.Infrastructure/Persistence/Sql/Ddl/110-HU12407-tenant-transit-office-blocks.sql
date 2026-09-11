-- HU #12407 — Bloqueos de OT para cabeza Marca Blanca (Feature #12256, Épica #12235)
-- Migración: 20260910210000_HU12407_TenantTransitOfficeBlocks
-- Tabla admin.tenant_transit_office_blocks: exclusión de OT operables para la red MB.
-- Trigger: solo tenant_type = MARCA_BLANCA puede tener filas.

CREATE TABLE IF NOT EXISTS admin.tenant_transit_office_blocks (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_transit_office_blocks PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_tenant_transit_office_blocks UNIQUE (tenant_id, transit_office_id)
);

CREATE INDEX IF NOT EXISTS ix_tenant_transit_office_blocks_tenant_id
    ON admin.tenant_transit_office_blocks(tenant_id);

COMMENT ON TABLE admin.tenant_transit_office_blocks IS
    'HU #12407: OT bloqueados para una cabeza MARCA_BLANCA. Propagación automática a hijos vía lista efectiva.';

ALTER TABLE admin.tenant_transit_office_blocks ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_transit_office_blocks;
CREATE POLICY tenant_isolation ON admin.tenant_transit_office_blocks
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

CREATE OR REPLACE FUNCTION admin.trg_tenant_transit_office_blocks_marca_blanca() RETURNS trigger AS $$
DECLARE
    v_tenant_type text;
BEGIN
    SELECT t.tenant_type INTO v_tenant_type
      FROM identity.tenants t
     WHERE t.id = NEW.tenant_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'tenant % no existe', NEW.tenant_id
            USING ERRCODE = 'foreign_key_violation';
    END IF;

    IF v_tenant_type IS DISTINCT FROM 'MARCA_BLANCA' THEN
        RAISE EXCEPTION 'tenant %: los bloqueos de OT solo aplican a cabezas MARCA_BLANCA (tenant_type=%)', NEW.tenant_id, v_tenant_type
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_tenant_transit_office_blocks_marca_blanca ON admin.tenant_transit_office_blocks;
CREATE TRIGGER tr_tenant_transit_office_blocks_marca_blanca
    BEFORE INSERT OR UPDATE OF tenant_id ON admin.tenant_transit_office_blocks
    FOR EACH ROW EXECUTE FUNCTION admin.trg_tenant_transit_office_blocks_marca_blanca();

DROP TRIGGER IF EXISTS tr_tenant_transit_office_blocks_audit ON admin.tenant_transit_office_blocks;
CREATE TRIGGER tr_tenant_transit_office_blocks_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_blocks
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
