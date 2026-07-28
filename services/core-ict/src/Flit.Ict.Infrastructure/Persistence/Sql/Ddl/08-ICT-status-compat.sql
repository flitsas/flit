-- =============================================================================
-- core-ict — Compatibilidad de la respuesta de estado v1 (clientes antiguos).
-- Añade status_process_observation al histórico y ajusta las descripciones del
-- catálogo a la forma title-case de v1 (Registrado / En Validacion / ...).
-- Idempotente.
-- =============================================================================

ALTER TABLE ict.external_integration_process_status
    ADD COLUMN IF NOT EXISTS status_process_observation varchar(500) NOT NULL DEFAULT '';

UPDATE ict.external_integration_parameter_process_status AS p
SET name_description = v.d
FROM (VALUES
    (1, 'Registrado'),
    (2, 'En Validacion'),
    (3, 'Procesado'),
    (4, 'Con Novedades'),
    (5, 'Borrador'),
    (6, 'Anulado')
) AS v(id, d)
WHERE p.id = v.id AND p.name_description <> v.d;
