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
-- 2. Pasos, secciones y campos: RETIRADOS (ADR-0050).
--
--    Este seed creaba un paso único —CONSULTA_PLACA— «para que el wizard
--    funcione end-to-end», de cuando los tipos se publicaban sin ninguna
--    parametrización. Desde ADR-0050 el recorrido lo declara el catálogo
--    (DDL 81), y como `DevelopmentAuthSeeder` re-ejecuta este script en CADA
--    arranque de Development, el paso volvía a aparecer después de que la
--    migración lo borrara: el tipo quedaba con DOS pasos en sort_order = 1 y el
--    asistente pintaba uno de más, vacío, en la primera posición.
-- ─────────────────────────────────────────────────────────────────────────────

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Publicar el tipo (solo si aún no está publicado → idempotente).
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
