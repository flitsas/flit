-- =============================================================================
-- HU #14381 (Feature #14380, Epica #14297) - un organismo puede tener VARIOS dominios de portal, con uno principal.
-- Migracion: 20261106140000_HU14381_VariosDominiosPorOrganismo. Idempotente. No edita el DDL 183 (ya aplicado en copias).
--
--   1. Se quita el indice de un dominio vigente por organismo (uq_transit_office_domains_office). El host sigue siendo unico
--      en toda la plataforma (uq_transit_office_domains_host y el disparador entre las dos tablas de dominios).
--   2. is_primary: el dominio que usan el enlace, el QR y los correos. Indice unico parcial: a lo mas uno vigente por organismo.
--      El tope por organismo (Domains:PortalMaxPorOrganismo) lo impone la aplicacion.
--   3. Datos existentes: cada dominio activo vigente (hasta hoy, uno por organismo) queda como principal.
-- =============================================================================

DROP INDEX IF EXISTS admin.uq_transit_office_domains_office;

ALTER TABLE admin.transit_office_domains ADD COLUMN IF NOT EXISTS is_primary boolean NOT NULL DEFAULT false;

UPDATE admin.transit_office_domains
   SET is_primary = true
 WHERE status = 'active' AND deleted_at IS NULL AND is_primary = false
   AND NOT EXISTS (
       SELECT 1 FROM admin.transit_office_domains p
        WHERE p.transit_office_id = admin.transit_office_domains.transit_office_id AND p.is_primary AND p.deleted_at IS NULL);

CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_domains_primary
    ON admin.transit_office_domains (transit_office_id) WHERE is_primary AND deleted_at IS NULL;

COMMENT ON COLUMN admin.transit_office_domains.is_primary IS
    'HU #14381 - dominio principal del organismo (enlace, QR y correos). A lo mas uno vigente por organismo; solo un dominio activo puede serlo.';
