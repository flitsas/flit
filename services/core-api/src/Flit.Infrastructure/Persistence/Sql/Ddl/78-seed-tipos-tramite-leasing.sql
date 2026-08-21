-- Siembra de tipos de trámite leasing / locatario.
-- Migración: 20260821120000_SeedTiposTramiteLeasing
--
-- Idempotente por code (uq_procedure_types_code). La primera ejecución inserta; si el código
-- ya existe no pisa name/family/description (mismo criterio que 69-vehicle-service-types-catalog-seed).
-- No usa BEGIN/COMMIT propios (EF envuelve la migración).

SET LOCAL row_security = off;

INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs, publication_status, published_at, version, gate_profile,
    created_at, row_version
)
VALUES
    (uuidv7(), 'MATRICULA_LEASING', 'Matrícula Leasing', 'MATRICULAS',
     'Matrícula con locatario.',
     true, '{}'::jsonb, 'published', now(), 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'TRASPASO_UNILATERAL', 'Traspaso Unilateral', 'TRASPASO',
     'Traspaso unilateral a locatario.',
     true, '{}'::jsonb, 'published', now(), 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'Traspaso con Transferencia de Dominio', 'TRASPASO',
     'Traspaso de un locatario a otro.',
     true, '{}'::jsonb, 'published', now(), 1, '{}'::jsonb, now(), 0)
ON CONFLICT (code) DO NOTHING;
