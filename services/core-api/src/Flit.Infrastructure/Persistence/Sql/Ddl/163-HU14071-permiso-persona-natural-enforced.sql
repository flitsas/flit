-- =============================================================================
-- HU #14071 (Feature J, Épica #14021) — permiso de la administración del portal de Persona Natural en Plataforma.
-- Migración: 20261028130000_HU14071_PermisoPersonaNatural.
--
-- Qué hace:
--   1. Módulo RBAC 'persona-natural-admin' (producto 'plataforma') y permiso 'platform.persona_natural.manage'.
--   2. Marcador de enforcement (DDL 139): los endpoints de /api/v1/admin/plataforma/persona-natural lo exigen, así que nace
--      en 'true'.
--   3. Se concede ÚNICAMENTE al rol SuperAdmin (mismo patrón del DDL 151): ningún rol de compañía lo trae, así la regla
--      «Solo SA» del módulo de permisos por usuario lo deja en «Solo SA». Los endpoints exigen además el ROL SuperAdmin.
-- Aditiva e idempotente: IF NOT EXISTS / NOT EXISTS / ON CONFLICT DO NOTHING. No toca tablas existentes.
-- =============================================================================

INSERT INTO security.modules (code, name, description, sort_order, is_active, product_code)
SELECT 'persona-natural-admin', 'Portal Persona Natural (administración)',
       'Tarifas, configuración y contenido del portal de Persona Natural, exclusivos del Super Admin FLIT',
       (SELECT COALESCE(MAX(sort_order), 0) FROM security.modules)::int + 1,
       true, 'plataforma'
 WHERE NOT EXISTS (SELECT 1 FROM security.modules m WHERE m.code = 'persona-natural-admin');

INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
SELECT m.id, 'platform.persona_natural.manage', 'Administrar el portal de Persona Natural', 'GET',
       '/api/v1/admin/plataforma/persona-natural/tarifas', true
  FROM security.modules m
 WHERE m.code = 'persona-natural-admin'
   AND NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = 'platform.persona_natural.manage');

-- Marcador de enforcement (DDL 139).
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES ('platform.persona_natural.manage', 'true')) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;

-- Solo el rol SuperAdmin.
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug = 'platform.persona_natural.manage'
 WHERE r.code = 'SuperAdmin'
   AND r.deleted_at IS NULL
ON CONFLICT (role_id, permission_id) DO NOTHING;
