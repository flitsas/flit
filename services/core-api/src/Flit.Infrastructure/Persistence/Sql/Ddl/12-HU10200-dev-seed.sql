-- HU #10200 (DEV-ONLY) — Seed de desarrollo para Tab Operación | Feature #10116
-- ⚠️  DEV-ONLY: tenant + user fijos que el frontend de Operación usa por defecto.
--     NO usar en producción. GUIDs fijos acordados con frontend:
--       DEV_TENANT_ID = 11111111-1111-1111-1111-111111111111
--       DEV_USER_ID   = 22222222-2222-2222-2222-222222222222
-- Idempotente: ON CONFLICT DO NOTHING / WHERE NOT EXISTS en cada sentencia.
-- Re-ejecutable sin efectos secundarios.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Tenant DEV (identity.tenants) — FK destino de procedure_instances.tenant_id.
--    Esquema (post-merge develop): code, legal_name, tax_id, tenant_type, is_active,
--    created_at, row_version (default 0). Code/tax_id distintos del tenant 'DEMO' que
--    siembra DevelopmentAuthSeeder para no colisionar con sus UNIQUE.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'FLITDEV',
    'Flit Dev Tenant',
    '900000000-0',
    'FLIT',
    true,
    now()
)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. User DEV (identity.users) — FK destino de procedure_instances.created_by_user_id.
--    Esquema (post-merge develop): email (UNIQUE), display_name, status, created_at,
--    row_version (default 0). NO existe tenant_id en users (no es tenant-scoped a nivel
--    de tabla); el vínculo tenant↔trámite vive en procedure_instances.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.users (id, email, display_name, status, created_at)
VALUES (
    '22222222-2222-2222-2222-222222222222',
    'dev@flitsas.io',
    'Usuario Dev',
    'active',
    now()
)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Pasos, secciones y campos: RETIRADOS (ADR-0050).
--
--    Este seed creaba un paso único —DATOS_VEHICULO— «para que el wizard
--    funcione end-to-end», de cuando los tipos se publicaban sin ninguna
--    parametrización. Desde ADR-0050 el recorrido lo declara el catálogo
--    (DDL 81), y como `DevelopmentAuthSeeder` re-ejecuta este script en CADA
--    arranque de Development, el paso volvía a aparecer después de que la
--    migración lo borrara: el tipo quedaba con DOS pasos en sort_order = 1 y el
--    asistente pintaba uno de más, vacío, en la primera posición.
-- ─────────────────────────────────────────────────────────────────────────────

-- 4. Publicar el tipo (solo si aún no está publicado → idempotente)
UPDATE tramites.procedure_types
SET publication_status = 'published',
    published_at = now()
WHERE code = 'MATRICULA_NUEVA'
  AND publication_status <> 'published';

-- ─────────────────────────────────────────────────────────────────────────────
-- Verificación post-seed (DEV)
-- ─────────────────────────────────────────────────────────────────────────────
-- SELECT id FROM identity.tenants WHERE id = '11111111-1111-1111-1111-111111111111';
-- SELECT id FROM identity.users   WHERE id = '22222222-2222-2222-2222-222222222222';
-- SELECT code, publication_status FROM tramites.procedure_types WHERE code = 'MATRICULA_NUEVA';
--   esperado: published
-- SELECT count(*) FROM tramites.form_fields ff
--   JOIN tramites.procedure_sections sec ON sec.id = ff.procedure_section_id
--   JOIN tramites.procedure_steps st ON st.id = sec.procedure_step_id
--   JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
--   WHERE pt.code = 'MATRICULA_NUEVA';  -- esperado: 3
