-- =============================================================================
-- HU #13662 (Feature #13105 F-E, Épica #12753) — la API exige tramites.create en las rutas que crean trámites.
-- Migración: 20261019130000_HU13662_EnforcedTramitesCreate.
--
-- 1) Backfill (no cortar accesos): hasta hoy tramites.create NUNCA se exigió; crea trámites cualquier usuario autenticado con
--    compañía/OT en su JWT. Todo rol activo del producto del módulo 'tramites' (catálogo global y roles por tenant, COMPANY y
--    TRANSIT_OFFICE, sin enumerar códigos) recibe el slug. Los roles de producto 'plataforma' (SuperAdmin, AdminCompany) no
--    pueden tenerlo (tr_role_permissions_same_product): SuperAdmin hace bypass y AdminCompany crea con su admin_tramites.
-- 2) Marcador enforced = 'true' (lo leen el seeder y PermisosEnforcedArchitectureTests).
-- Idempotente: ON CONFLICT DO NOTHING + UPDATE solo si cambia.
-- =============================================================================
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.permissions p
  JOIN security.modules m ON m.id = p.module_id
  JOIN security.roles r ON r.product_code = m.product_code
 WHERE p.slug = 'tramites.create'
   AND r.deleted_at IS NULL
   AND r.is_active
ON CONFLICT (role_id, permission_id) DO NOTHING;

UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('tramites.create', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
