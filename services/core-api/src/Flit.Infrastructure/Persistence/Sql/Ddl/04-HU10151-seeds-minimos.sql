-- HU #10151 (Seeds) — Seeds mínimos motor parametrización | Feature #10116
-- Seeds de catálogos globales: no usan tenant_id (SuperAdmin, ADR-0019).
-- Sin secretos: base_url usa stubs seguros.
-- Idempotente: ON CONFLICT DO NOTHING en cada INSERT.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Aristas / Entidades (procedure_entities) — AC2: 4 aristas
--    Checklist A20: catálogo global sin tenant_id.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_entities (id, code, name, sort_order, is_active, external_refs)
VALUES
    (uuidv7(), 'VEHICLE',  'Vehículo',         1, true, '{}'),
    (uuidv7(), 'OWNER',    'Propietario',       2, true, '{}'),
    (uuidv7(), 'BUYER',    'Comprador',         3, true, '{}'),
    (uuidv7(), 'LESSEE',   'Arrendatario',      4, true, '{}')
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Fuentes externas (external_data_sources) — AC2: ≥6 fuentes
--    base_url usa stubs (sin secretos en BD).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.external_data_sources (id, code, name, base_url, auth_type, timeout_ms, is_active, external_refs)
VALUES
    (uuidv7(), 'SIMIT',        'SIMIT — Sistema Información Multas',        'https://stub.example.com/simit',        'api_key', 8000, true, '{}'),
    (uuidv7(), 'RUNT',         'RUNT — Registro Único Nacional Tránsito',   'https://stub.example.com/runt',         'api_key', 8000, true, '{}'),
    (uuidv7(), 'RNMC',         'RNMC — Registro Nacional Medidas Cautelares','https://stub.example.com/rnmc',        'api_key', 8000, true, '{}'),
    (uuidv7(), 'RESOLUCIONES', 'Resoluciones Ministerio Transporte',        'https://stub.example.com/resoluciones', 'none',    5000, true, '{}'),
    (uuidv7(), 'RUES',         'RUES — Registro Único Empresarial Social',  'https://stub.example.com/rues',         'api_key', 8000, true, '{}'),
    (uuidv7(), 'FASECOLDA',    'FASECOLDA — Federación Aseguradores',       'https://stub.example.com/fasecolda',    'api_key', 8000, true, '{}')
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Plantillas de consulta (consultation_templates) — AC3
--    Columnas: entity_scope, person_type, request_schema (Opción 1 diseño §1)
--    RUNT_VEHICLE debe tener 'plate_or_vin' en required_field_keys.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.consultation_templates (
    id, code, name,
    external_data_source_id,
    entity_scope, person_type,
    required_field_keys, request_schema,
    is_active, external_refs
)
SELECT
    uuidv7(),
    'RUNT_VEHICLE',
    'RUNT — Consulta vehículo por placa/VIN',
    ds.id,
    'vehicle', NULL,
    '["plate_or_vin"]'::jsonb,
    '{"plate_or_vin": "string"}'::jsonb,
    true,
    '{}'::jsonb
FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT'
ON CONFLICT (code) DO NOTHING;

INSERT INTO tramites.consultation_templates (
    id, code, name,
    external_data_source_id,
    entity_scope, person_type,
    required_field_keys, request_schema,
    is_active, external_refs
)
SELECT
    uuidv7(),
    'RUNT_ACTOR_NATURAL',
    'RUNT — Consulta actor natural',
    ds.id,
    'actor', 'natural',
    '["document_type", "document_number"]'::jsonb,
    '{"document_type": "string", "document_number": "string"}'::jsonb,
    true,
    '{}'::jsonb
FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT'
ON CONFLICT (code) DO NOTHING;

INSERT INTO tramites.consultation_templates (
    id, code, name,
    external_data_source_id,
    entity_scope, person_type,
    required_field_keys, request_schema,
    is_active, external_refs
)
SELECT
    uuidv7(),
    'RUES_ACTOR_JURIDICAL',
    'RUES — Consulta persona jurídica por NIT',
    ds.id,
    'actor', 'juridical',
    '["nit"]'::jsonb,
    '{"nit": "string"}'::jsonb,
    true,
    '{}'::jsonb
FROM tramites.external_data_sources ds WHERE ds.code = 'RUES'
ON CONFLICT (code) DO NOTHING;

INSERT INTO tramites.consultation_templates (
    id, code, name,
    external_data_source_id,
    entity_scope, person_type,
    required_field_keys, request_schema,
    is_active, external_refs
)
SELECT
    uuidv7(),
    'SIMIT_ACTOR',
    'SIMIT — Consulta multas por actor',
    ds.id,
    'actor', NULL,
    '["document_type", "document_number"]'::jsonb,
    '{"document_type": "string", "document_number": "string"}'::jsonb,
    true,
    '{}'::jsonb
FROM tramites.external_data_sources ds WHERE ds.code = 'SIMIT'
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Familias de trámite (procedure_types) — AC2: ≥3 familias, ≥2 tipos draft
--    publication_status = 'draft' para todos los seeds (AC2).
--    published_at / published_by: NULL (borrador, no publicado).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs,
    publication_status, row_version
)
VALUES
    -- Canónicos operativos (wizard)
    (uuidv7(), 'MATRICULA_NUEVA',           'Matrícula inicial',           'MATRICULAS', 'Matrícula inicial de vehículo.', true, '{}', 'draft', 0),
    (uuidv7(), 'TRASPASO_STANDARD',         'Traspaso',                    'TRASPASO',   'Traspaso de propiedad.', true, '{}', 'draft', 0),
    -- Activos de catálogo
    (uuidv7(), 'CAMBIO_LOCATARIO',          'Cambio de locatario',         'OTROS',      'Cambio de locatario (leasing).', true, '{}', 'draft', 0),
    (uuidv7(), 'CAMBIO_CARROCERIA',         'Cambio de carrocería',        'OTROS',      'Cambio de carrocería.', true, '{}', 'draft', 0),
    (uuidv7(), 'BLINDAJE',                  'Blindaje',                    'OTROS',      'Blindaje de vehículo.', true, '{}', 'draft', 0),
    (uuidv7(), 'CAMBIO_COLOR',              'Cambio de color',             'OTROS',      'Cambio de color.', true, '{}', 'draft', 0),
    (uuidv7(), 'DUPLICADO_PLACA',           'Duplicado de placa',          'OTROS',      'Duplicado de placa.', true, '{}', 'draft', 0),
    (uuidv7(), 'DUPLICADO_TARJETA',         'Duplicado de tarjeta',        'OTROS',      'Duplicado de tarjeta de propiedad.', true, '{}', 'draft', 0),
    (uuidv7(), 'LEVANTAMIENTO_PRENDA',      'Levantar prenda',             'OTROS',      'Levantamiento de prenda.', true, '{}', 'draft', 0),
    (uuidv7(), 'PRENDA_INSCRIPCION',        'Inscribir prenda',            'OTROS',      'Inscripción de prenda.', true, '{}', 'draft', 0),
    (uuidv7(), 'RADICADO_CUENTA',           'Radicado de cuenta',          'OTROS',      'Radicado de cuenta.', true, '{}', 'draft', 0),
    (uuidv7(), 'CONVERSION_COMBUSTIBLE',    'Conversiones de combustible', 'OTROS',      'Conversiones de combustible.', true, '{}', 'draft', 0),
    (uuidv7(), 'TRASLADO_CUENTA',           'Traslado de cuenta',          'OTROS',      'Traslado de cuenta.', true, '{}', 'draft', 0),
    (uuidv7(), 'CANCELACION_MATRICULA',     'Cancelación de matrícula',    'MATRICULAS', 'Cancelación de matrícula.', true, '{}', 'draft', 0),
    -- Inactivos
    (uuidv7(), 'REGRABAR_MOTOR_CHASIS',     'Regrabar motor, chasis',      'OTROS',      'Regrabación de motor y/o chasis.', false, '{}', 'draft', 0),
    (uuidv7(), 'REMATRICULA',               'Rematrícula',                 'MATRICULAS', 'Rematrícula de vehículo.', false, '{}', 'draft', 0),
    (uuidv7(), 'LEVANTAR_INSCRIBIR_PRENDA', 'Levantar e inscribir prenda', 'OTROS',      'Levantamiento e inscripción de prenda.', false, '{}', 'draft', 0),
    (uuidv7(), 'CAMBIO_ACREEDOR',           'Cambio acreedor',             'OTROS',      'Cambio de acreedor prendario.', false, '{}', 'draft', 0)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- Queries de verificación post-seed
-- ─────────────────────────────────────────────────────────────────────────────
-- SELECT COUNT(*) FROM tramites.procedure_entities;             -- esperado: 4
-- SELECT COUNT(*) FROM tramites.external_data_sources;         -- esperado: 6
-- SELECT COUNT(*) FROM tramites.consultation_templates;        -- esperado: 4
-- SELECT COUNT(*) FROM tramites.procedure_types;               -- esperado: 5 (≥2 draft)
-- SELECT COUNT(DISTINCT family) FROM tramites.procedure_types;  -- esperado: 3
-- SELECT required_field_keys FROM tramites.consultation_templates WHERE code = 'RUNT_VEHICLE';
--   esperado: ["plate_or_vin"]
-- SELECT column_name FROM information_schema.columns
--   WHERE table_schema='tramites' AND table_name='form_fields'
--   AND column_name IN ('is_locked','lock_reason','consultation_template_id');
--   esperado: 3 filas (AC1)
