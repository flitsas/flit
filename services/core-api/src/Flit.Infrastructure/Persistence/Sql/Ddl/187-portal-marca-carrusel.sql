-- =============================================================================
-- Mejora Épica #14297 (sin HU) — carrusel de imágenes de la columna lateral del portal del organismo.
-- Migración: 20261107120000_PortalMarcaCarrusel.
--
-- El carrusel (hasta 6 imágenes, cada una con texto alternativo y enlace opcional) vive en el jsonb del borrador y del publicado
-- (clave "carousel": [{ imageId, alt, linkUrl }]; ausente = vacío, sin cambiar schemaVersion). Las imágenes reutilizan
-- admin.transit_office_portal_brand_images con un tipo nuevo, 'carousel': basta reemplazar el CHECK de kind (DDL 180).
-- No se edita el DDL 180 ya aplicado. Idempotente (DROP CONSTRAINT IF EXISTS + ADD).
-- =============================================================================

ALTER TABLE admin.transit_office_portal_brand_images
    DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brand_images_kind;
ALTER TABLE admin.transit_office_portal_brand_images
    ADD CONSTRAINT ck_transit_office_portal_brand_images_kind CHECK (kind IN ('logo', 'banner', 'carousel'));

COMMENT ON COLUMN admin.transit_office_portal_brand_images.kind IS
    'Tipo de imagen: logo, banner o carousel (imagen del carrusel de la columna lateral, hasta 6 por marca).';
