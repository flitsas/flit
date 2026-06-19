-- HU #10200 (DEV-ONLY) — Seed de desarrollo para Tab Operación | Feature #10116
-- ⚠️  DEV-ONLY: tenant + user fijos que el frontend de Operación usa por defecto.
--     NO usar en producción. GUIDs fijos acordados con frontend:
--       DEV_TENANT_ID = 11111111-1111-1111-1111-111111111111
--       DEV_USER_ID   = 22222222-2222-2222-2222-222222222222
-- Idempotente: ON CONFLICT DO NOTHING / WHERE NOT EXISTS en cada sentencia.
-- Re-ejecutable sin efectos secundarios.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Tenant DEV (identity.tenants) — FK destino de procedure_instances.tenant_id
--    Columnas NOT NULL reales: name, nit (UNIQUE), slug (UNIQUE, CHECK
--    '^[a-z0-9]+(-[a-z0-9]+)*$'). status default 'active' (CHECK active|inactive|
--    suspended). NO existen code/legal_name/tax_id/tenant_type/is_active.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.tenants (id, name, nit, slug, status, created_at)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'Flit Dev Tenant',
    '900000000-0',
    'flit-dev',
    'active',
    now()
)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. User DEV (identity.users) — FK destino de procedure_instances.created_by_user_id.
--    Columnas NOT NULL reales: tenant_id (uuid), email (citext UNIQUE).
--    El estado es account_state (default 'inactive', CHECK active|inactive|
--    temp_blocked|permanent_blocked) → usamos 'active'. NO existen display_name
--    ni status. El user dev referencia el tenant dev en tenant_id.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.users (id, tenant_id, email, account_state, created_at)
VALUES (
    '22222222-2222-2222-2222-222222222222',
    '11111111-1111-1111-1111-111111111111',
    'dev@flitsas.io',
    'active',
    now()
)
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Publicar 1 procedure_type con configuración completa (AC1/AC2 demo).
--    Los seeds de HU #10151 dejan todos los tipos en 'draft' SIN steps/sections/
--    form_fields. Aquí publicamos MATRICULA_NUEVA y le sembramos una configuración
--    mínima (1 step → 1 section → 3 fields) para que el dropdown (AC1) y el wizard
--    (AC2) funcionen end-to-end. Idempotente (WHERE NOT EXISTS / ON CONFLICT).
-- ─────────────────────────────────────────────────────────────────────────────

-- 3.1 Step
INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
SELECT uuidv7(), pt.id, 'DATOS_VEHICULO', 'Datos del vehículo', 1, true
FROM tramites.procedure_types pt
WHERE pt.code = 'MATRICULA_NUEVA'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_steps s
      WHERE s.procedure_type_id = pt.id AND s.code = 'DATOS_VEHICULO'
  );

-- 3.2 Section
INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout)
SELECT uuidv7(), s.id, 'IDENTIFICACION', 'Identificación del vehículo', 1, 'single'
FROM tramites.procedure_steps s
JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
WHERE pt.code = 'MATRICULA_NUEVA' AND s.code = 'DATOS_VEHICULO'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_sections sec
      WHERE sec.procedure_step_id = s.id AND sec.code = 'IDENTIFICACION'
  );

-- 3.3 Form fields (idempotente vía UNIQUE (procedure_section_id, field_key))
INSERT INTO tramites.form_fields (id, procedure_section_id, field_key, label, field_type, is_required, sort_order)
SELECT uuidv7(), sec.id, v.field_key, v.label, v.field_type, v.is_required, v.sort_order
FROM tramites.procedure_sections sec
JOIN tramites.procedure_steps s ON s.id = sec.procedure_step_id
JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
CROSS JOIN (VALUES
    ('plate',       'Placa',                'text',   true,  1::smallint),
    ('vin',         'VIN / Número de chasis','text',  true,  2::smallint),
    ('vehicle_year','Año del modelo',       'number', false, 3::smallint)
) AS v(field_key, label, field_type, is_required, sort_order)
WHERE pt.code = 'MATRICULA_NUEVA' AND s.code = 'DATOS_VEHICULO' AND sec.code = 'IDENTIFICACION'
ON CONFLICT (procedure_section_id, field_key) DO NOTHING;

-- 3.4 Publicar el tipo (solo si aún no está publicado → idempotente)
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
