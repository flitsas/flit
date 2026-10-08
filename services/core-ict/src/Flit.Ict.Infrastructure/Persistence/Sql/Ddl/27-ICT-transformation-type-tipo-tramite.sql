-- =============================================================================
-- core-ict — Bug #13445 (D6): el catálogo de transformaciones usa el código ICT Tipo Trámite.
-- more_transaction_transaction_type[].transactionType NO es un código RUNT: es el catálogo
-- "Tipo Trámite" del contrato ICT (ver ict.external_integration_procedure_type, 12-…):
--   5 Blindaje · 6 Cambio de Carrocería · 7 Cambio de Color · 9 Conversiones de Combustible.
-- (8 Cambio de locatario, 10/11 duplicados y 1-4 matrículas/traspasos no son transformaciones.)
-- La semilla de 12-ICT-catalogs-parity.sql etiquetó 5='Cambio de color', 9='Transformación' y
-- 17='Cambio de carrocería', y la FK de 13-… rechazaba 6 y 7.
--
-- Este script:
--   * da de alta 6 y 7 (PK interna = siguiente libre; en una base sembrada por 12-… quedan 4 y 5);
--   * renombra 5 y 9 y asegura 5/6/7/9 activos;
--   * desactiva 17 (is_active=false). NO se borra: masters existentes pueden referenciarlo por FK.
-- Idempotente (corre en cada arranque vía IctSchemaBootstrapper): INSERT … WHERE NOT EXISTS y
-- UPDATE solo de filas que difieren (updated_at no se toca si ya está al día).
-- =============================================================================

INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM ict.external_integration_transformation_type), 6, 'Cambio de Carrocería'
WHERE NOT EXISTS (
    SELECT 1 FROM ict.external_integration_transformation_type WHERE id_transformation_type = 6);

INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name)
SELECT (SELECT COALESCE(MAX(id), 0) + 1 FROM ict.external_integration_transformation_type), 7, 'Cambio de Color'
WHERE NOT EXISTS (
    SELECT 1 FROM ict.external_integration_transformation_type WHERE id_transformation_type = 7);

UPDATE ict.external_integration_transformation_type AS t
SET name = v.name, is_active = true, deleted_at = NULL, updated_at = now()
FROM (VALUES
    (5, 'Blindaje'),
    (6, 'Cambio de Carrocería'),
    (7, 'Cambio de Color'),
    (9, 'Conversiones de Combustible')
) AS v (code, name)
WHERE t.id_transformation_type = v.code
  AND (t.name IS DISTINCT FROM v.name OR t.is_active IS NOT TRUE OR t.deleted_at IS NOT NULL);

UPDATE ict.external_integration_transformation_type
SET is_active = false, updated_at = now()
WHERE id_transformation_type = 17
  AND is_active;

COMMENT ON COLUMN ict.external_integration_transformation_type.id_transformation_type
    IS 'Código ICT Tipo Trámite de la transformación (5 blindaje, 6 carrocería, 7 color, 9 combustible) que viaja en more_transaction_transaction_type[].transactionType; no es código RUNT. 17 queda inactivo (Bug #13445).';
