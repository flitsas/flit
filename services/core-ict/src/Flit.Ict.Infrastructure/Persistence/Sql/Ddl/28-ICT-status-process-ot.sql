-- =============================================================================
-- core-ict — HU #14668 Bloque 1: ampliar catálogo de estados del pipeline ICT
-- con los estados del ciclo de vida OT post-borrador (códigos 7-13).
-- Los existentes (1-6) ya están en 03-ICT-pipeline.sql; este script solo
-- agrega los nuevos con ON CONFLICT DO NOTHING (idempotente).
-- name_description: MAYÚSCULAS (paridad v1); description: English Title Case.
-- =============================================================================

INSERT INTO ict.external_integration_parameter_process_status (id, description, name_description) VALUES
    (7,  'Prepared',      'PREPARADO'),
    (8,  'PreAssignment', 'PREASIGNACION'),
    (9,  'Assigned',      'ASIGNADO'),
    (10, 'Delivered',     'ENTREGADO'),
    (11, 'Approved',      'APROBADO'),
    (12, 'Rejected',      'RECHAZADO'),
    (13, 'Revoked',       'REVOCADO')
ON CONFLICT (id) DO NOTHING;
