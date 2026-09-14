-- HU #12346 — origen de habilitación de OT (CLIENT | SYSTEM)
-- Migración: 20260910220000_HU12346_TransitGrantSource

ALTER TABLE admin.tenant_transit_office_grants
    ADD COLUMN IF NOT EXISTS source varchar(20) NOT NULL DEFAULT 'CLIENT';

ALTER TABLE admin.tenant_transit_office_grants
    DROP CONSTRAINT IF EXISTS ck_tenant_transit_office_grants_source;

ALTER TABLE admin.tenant_transit_office_grants
    ADD CONSTRAINT ck_tenant_transit_office_grants_source
    CHECK (source IN ('CLIENT', 'SYSTEM'));

COMMENT ON COLUMN admin.tenant_transit_office_grants.source IS
    'HU #12346: CLIENT = gestionada por la compañía; SYSTEM = gobernada por la plataforma/red.';
