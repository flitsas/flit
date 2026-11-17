-- =============================================================================
-- core-ict — Catálogo de transformaciones: vuelve al catálogo B «Tipo de transformación ICT»
-- (confirmado por FLIT 1). Corrige la decisión D6 del Bug #13445 (27-…), que había adoptado el
-- catálogo A de tipo de trámite (5 blindaje, 6 carrocería, 7 color).
--
-- more_transaction_transaction_type[].transactionType usa el catálogo B:
--   5 = Cambio de Color · 9 = Conversión de Combustible (Transformación) · 17 = Cambio de Carrocería.
-- El blindaje NO es una transformación de la lista: se declara con el trámite principal (5) y
-- armor_level_number_id.
--
-- Este script (reemplaza el efecto de 27-…, que ahora es un no-op para no deshacer esto en cada arranque):
--   * da de alta 5, 9 y 17 si faltan (PK interna = siguiente libre);
--   * renombra 5, 9 y 17 y los deja activos (deleted_at NULL);
--   * desactiva 6 y 7 (is_active=false). NO se borran: masters ya registrados pueden referenciarlos por FK.
-- Idempotente (corre en cada arranque vía IctSchemaBootstrapper): INSERT … WHERE NOT EXISTS
-- (+ ON CONFLICT DO NOTHING: dos arranques simultáneos que calculen el mismo MAX(id)+1 no fallan) y
-- UPDATE solo de filas que difieren (updated_at no se toca si ya está al día).
-- =============================================================================

INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM ict.external_integration_transformation_type), 5, 'Cambio de Color'
WHERE NOT EXISTS (
    SELECT 1 FROM ict.external_integration_transformation_type WHERE id_transformation_type = 5)
ON CONFLICT DO NOTHING;

INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM ict.external_integration_transformation_type), 9, 'Conversión de Combustible'
WHERE NOT EXISTS (
    SELECT 1 FROM ict.external_integration_transformation_type WHERE id_transformation_type = 9)
ON CONFLICT DO NOTHING;

INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM ict.external_integration_transformation_type), 17, 'Cambio de Carrocería'
WHERE NOT EXISTS (
    SELECT 1 FROM ict.external_integration_transformation_type WHERE id_transformation_type = 17)
ON CONFLICT DO NOTHING;

UPDATE ict.external_integration_transformation_type AS t
SET name = v.name, is_active = true, deleted_at = NULL, updated_at = now()
FROM (VALUES
    (5, 'Cambio de Color'),
    (9, 'Conversión de Combustible'),
    (17, 'Cambio de Carrocería')
) AS v (code, name)
WHERE t.id_transformation_type = v.code
  AND (t.name IS DISTINCT FROM v.name OR t.is_active IS NOT TRUE OR t.deleted_at IS NOT NULL);

UPDATE ict.external_integration_transformation_type
SET is_active = false, updated_at = now()
WHERE id_transformation_type IN (6, 7)
  AND is_active;

COMMENT ON COLUMN ict.external_integration_transformation_type.id_transformation_type
    IS 'Código del catálogo B «Tipo de transformación ICT» que viaja en more_transaction_transaction_type[].transactionType: 5 cambio de color, 9 conversión de combustible, 17 cambio de carrocería. 6 y 7 quedan inactivos (catálogo A de tipo de trámite, descartado). El blindaje no es una transformación.';
