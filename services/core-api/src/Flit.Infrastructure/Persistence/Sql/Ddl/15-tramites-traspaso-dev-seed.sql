-- Slice 4b (DEV-ONLY) — Seed de un procedure_type de TRASPASO para demo de modalidad placa-first.
-- ⚠️  DEV-ONLY: mirror del seed de MATRICULA_NUEVA (12-HU10200-dev-seed.sql). NO usar en producción.
--     Demuestra la modalidad traspaso (familia TRASPASO → modalidad_entrada='traspaso',
--     tipologia_codigo='traspaso_standard') derivada al crear la instancia (Slice 4b).
-- Idempotente: ON CONFLICT DO NOTHING / WHERE NOT EXISTS en cada sentencia. Re-ejecutable.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. procedure_type de traspaso publicado (familia TRASPASO).
--    Si los seeds de HU #10151 lo crearon en draft (no aplica a este code nuevo),
--    el ON CONFLICT (code) no lo pisa; la publicación se hace en el paso 5.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs,
    publication_status, row_version
)
VALUES (
    uuidv7(),
    'TRASPASO_STANDARD',
    'Traspaso',
    'TRASPASO',
    'Traspaso de propiedad entre particulares (placa-first, vendedor + comprador).',
    true, '{}', 'draft', 0
)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Step (1 step → 1 section → fields placa-first).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
SELECT uuidv7(), pt.id, 'CONSULTA_PLACA', 'Consulta por placa', 1, true
FROM tramites.procedure_types pt
WHERE pt.code = 'TRASPASO_STANDARD'
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
WHERE pt.code = 'TRASPASO_STANDARD' AND s.code = 'CONSULTA_PLACA'
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
WHERE pt.code = 'TRASPASO_STANDARD' AND s.code = 'CONSULTA_PLACA' AND sec.code = 'IDENTIFICACION'
ON CONFLICT (procedure_section_id, field_key) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Publicar el tipo (solo si aún no está publicado → idempotente).
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE tramites.procedure_types
SET publication_status = 'published',
    published_at = now()
WHERE code = 'TRASPASO_STANDARD'
  AND publication_status <> 'published';

-- ─────────────────────────────────────────────────────────────────────────────
-- Verificación post-seed (DEV)
-- ─────────────────────────────────────────────────────────────────────────────
-- SELECT code, family, publication_status FROM tramites.procedure_types WHERE code = 'TRASPASO_STANDARD';
--   esperado: TRASPASO_STANDARD | TRASPASO | published
-- SELECT count(*) FROM tramites.form_fields ff
--   JOIN tramites.procedure_sections sec ON sec.id = ff.procedure_section_id
--   JOIN tramites.procedure_steps st ON st.id = sec.procedure_step_id
--   JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
--   WHERE pt.code = 'TRASPASO_STANDARD';  -- esperado: 3
