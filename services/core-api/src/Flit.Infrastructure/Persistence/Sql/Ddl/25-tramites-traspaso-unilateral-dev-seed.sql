-- HU #10590 (DEV-ONLY) — Seed de un procedure_type de TRASPASO UNILATERAL (leasing) para demo.
-- ⚠️  DEV-ONLY: mirror del seed de TRASPASO_STANDARD (15-tramites-traspaso-dev-seed.sql). NO usar en producción.
--     Publica el code TRASPASO_UNILATERAL (family TRASPASO, compartida con el estándar). Al crear una
--     instancia por modalidad 'traspaso_unilateral' el handler resuelve este code y deriva
--     modalidad_entrada='traspaso_unilateral' + tipologia_codigo='traspaso_unilateral' (HU #10590),
--     activando el checklist unilateral en runtime SIN colapsar a traspaso_standard.
-- Idempotente: ON CONFLICT DO NOTHING / WHERE NOT EXISTS en cada sentencia. Re-ejecutable.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. procedure_type de traspaso unilateral publicado (familia TRASPASO).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs,
    publication_status, row_version
)
VALUES (
    uuidv7(),
    'TRASPASO_UNILATERAL',
    'Traspaso unilateral de leasing',
    'TRASPASO',
    'Traspaso unilateral: la compañía arrendadora transfiere la propiedad amparada en el contrato de leasing (placa-first, arrendadora + locatario).',
    true, '{}', 'draft', 0
)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Step (1 step → 1 section → fields placa-first).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
SELECT uuidv7(), pt.id, 'CONSULTA_PLACA', 'Consulta por placa', 1, true
FROM tramites.procedure_types pt
WHERE pt.code = 'TRASPASO_UNILATERAL'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_steps s
      WHERE s.procedure_type_id = pt.id AND s.code = 'CONSULTA_PLACA'
  );

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Section.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout)
SELECT uuidv7(), s.id, 'IDENTIFICACION', 'Identificación del vehículo y propietario', 1, 'single'
FROM tramites.procedure_steps s
JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
WHERE pt.code = 'TRASPASO_UNILATERAL' AND s.code = 'CONSULTA_PLACA'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_sections sec
      WHERE sec.procedure_step_id = s.id AND sec.code = 'IDENTIFICACION'
  );

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Form fields placa-first: plate + owner_document_type/number.
--    Idempotente vía UNIQUE (procedure_section_id, field_key).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.form_fields (id, procedure_section_id, field_key, label, field_type, is_required, sort_order)
SELECT uuidv7(), sec.id, v.field_key, v.label, v.field_type, v.is_required, v.sort_order
FROM tramites.procedure_sections sec
JOIN tramites.procedure_steps s ON s.id = sec.procedure_step_id
JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
CROSS JOIN (VALUES
    ('plate',                 'Placa',                       'text', true, 1::smallint),
    ('owner_document_type',   'Tipo de documento del propietario', 'text', true, 2::smallint),
    ('owner_document_number', 'Número de documento del propietario','text', true, 3::smallint)
) AS v(field_key, label, field_type, is_required, sort_order)
WHERE pt.code = 'TRASPASO_UNILATERAL' AND s.code = 'CONSULTA_PLACA' AND sec.code = 'IDENTIFICACION'
ON CONFLICT (procedure_section_id, field_key) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Publicar el tipo (solo si aún no está publicado → idempotente).
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE tramites.procedure_types
SET publication_status = 'published',
    published_at = now()
WHERE code = 'TRASPASO_UNILATERAL'
  AND publication_status <> 'published';

-- ─────────────────────────────────────────────────────────────────────────────
-- Verificación post-seed (DEV)
-- ─────────────────────────────────────────────────────────────────────────────
-- SELECT code, family, publication_status FROM tramites.procedure_types WHERE code = 'TRASPASO_UNILATERAL';
--   esperado: TRASPASO_UNILATERAL | TRASPASO | published
