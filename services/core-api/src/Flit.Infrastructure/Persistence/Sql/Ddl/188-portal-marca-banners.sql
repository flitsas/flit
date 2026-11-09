-- =============================================================================
-- Mejora Épica #14297 (sin HU) — el «banner principal» del portal del organismo pasa de UNA imagen a una LISTA de hasta 6.
-- Migración: 20261109120000_PortalMarcaBanners.
--
-- La lista vive en el jsonb del borrador y del publicado (clave "banners": [{ imageId, alt, linkUrl }]; ausente = vacío, sin
-- cambiar schemaVersion). Las imágenes siguen siendo kind 'banner' de admin.transit_office_portal_brand_images (el CHECK no cambia).
--
-- Migración de datos (idempotente): el banner único legacy ("bannerId") de cada ORGANISMO pasa a ser el primer elemento de
-- "banners" (texto alternativo «Banner del organismo», sin enlace) y la clave "bannerId" se retira, tanto del borrador como del
-- publicado. Si el documento ya tiene "banners" no se toca la lista (solo se retira un "bannerId" residual). La marca web
-- (transit_office_id NULL) no se modifica: sus banners viven en Contenido. La versión publicada no cambia: el contenido servido es
-- el mismo banner.
-- =============================================================================

UPDATE admin.transit_office_portal_brandings
   SET draft = CASE
         WHEN jsonb_exists(draft, 'banners') THEN draft - 'bannerId'
         ELSE (draft - 'bannerId') || jsonb_build_object('banners', jsonb_build_array(
                jsonb_build_object('imageId', draft ->> 'bannerId', 'alt', 'Banner del organismo')))
       END
 WHERE transit_office_id IS NOT NULL
   AND jsonb_exists(draft, 'bannerId')
   AND jsonb_typeof(draft -> 'bannerId') = 'string';

UPDATE admin.transit_office_portal_brandings
   SET published = CASE
         WHEN jsonb_exists(published, 'banners') THEN published - 'bannerId'
         ELSE (published - 'bannerId') || jsonb_build_object('banners', jsonb_build_array(
                jsonb_build_object('imageId', published ->> 'bannerId', 'alt', 'Banner del organismo')))
       END
 WHERE transit_office_id IS NOT NULL
   AND published IS NOT NULL
   AND jsonb_exists(published, 'bannerId')
   AND jsonb_typeof(published -> 'bannerId') = 'string';
