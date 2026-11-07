-- =============================================================================
-- HU #14401 (Feature #14400, Épica #14297) — marca de la web de persona natural.
-- Migración: 20261106120000_HU14401_PortalMarcaWeb.
--
-- La marca de la web es la misma estructura que la de un organismo (borrador, publicado, versión, imágenes), con ámbito
-- propio. En lugar de duplicar tablas, las de DDL 180 admiten UNA fila de ámbito web sin organismo (transit_office_id nulo):
--   * scope = 'organismo' | 'web' (las filas existentes quedan en 'organismo'; su comportamiento no cambia).
--   * CHECK: 'organismo' exige organismo; 'web' exige que sea nulo. Una fila no puede cruzarse de ámbito.
--   * Índice único parcial: a lo sumo UNA marca web (brandings) y una versión por tipo de imagen (brand_images).
--     La unicidad por organismo del DDL 180 no se toca (en un UNIQUE los nulos no chocan).
-- No se edita el DDL 180 ya aplicado. Idempotente.
-- =============================================================================

ALTER TABLE admin.transit_office_portal_brandings
    ADD COLUMN IF NOT EXISTS scope varchar(10) NOT NULL DEFAULT 'organismo';
ALTER TABLE admin.transit_office_portal_brandings ALTER COLUMN transit_office_id DROP NOT NULL;
ALTER TABLE admin.transit_office_portal_brandings DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brandings_scope;
ALTER TABLE admin.transit_office_portal_brandings ADD CONSTRAINT ck_transit_office_portal_brandings_scope CHECK (
    (scope = 'organismo' AND transit_office_id IS NOT NULL) OR (scope = 'web' AND transit_office_id IS NULL));
CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_portal_brandings_web
    ON admin.transit_office_portal_brandings (scope) WHERE scope = 'web';
COMMENT ON COLUMN admin.transit_office_portal_brandings.scope IS
    'Ámbito de la marca: organismo (transit_office_id obligatorio) o web (la web de persona natural, sin organismo, una sola fila).';

ALTER TABLE admin.transit_office_portal_brand_images
    ADD COLUMN IF NOT EXISTS scope varchar(10) NOT NULL DEFAULT 'organismo';
ALTER TABLE admin.transit_office_portal_brand_images ALTER COLUMN transit_office_id DROP NOT NULL;
ALTER TABLE admin.transit_office_portal_brand_images DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brand_images_scope;
ALTER TABLE admin.transit_office_portal_brand_images ADD CONSTRAINT ck_transit_office_portal_brand_images_scope CHECK (
    (scope = 'organismo' AND transit_office_id IS NOT NULL) OR (scope = 'web' AND transit_office_id IS NULL));
CREATE UNIQUE INDEX IF NOT EXISTS uq_transit_office_portal_brand_images_web_version
    ON admin.transit_office_portal_brand_images (kind, version) WHERE scope = 'web';
COMMENT ON COLUMN admin.transit_office_portal_brand_images.scope IS
    'Ámbito de la imagen: organismo o web (sin organismo).';
