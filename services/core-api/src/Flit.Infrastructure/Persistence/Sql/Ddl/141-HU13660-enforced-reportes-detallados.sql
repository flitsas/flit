-- =============================================================================
-- HU #13660 (Feature #13105 F-E, Épica #12753) — la API exige los permisos de Reportes detallados
-- (DetailedReportEndpoints, detrás de Security:ModulePermissionEnforcement:Enabled). Migración: 20261019110000_HU13660_EnforcedReportesDetallados.
--
-- 1. Backfill: admin_tramites (espejo de AdminCompany, que ya entra al export por policy) recibe read y export si le faltan:
--    el seeder solo los concedía al crear el permiso. Idempotente (ON CONFLICT DO NOTHING) y respeta
--    tr_role_permissions_same_product (solo roles del mismo producto que el módulo del permiso).
-- 2. Marcador enforced = 'true' para los dos slugs (lo leen el seeder y PermisosEnforcedArchitectureTests).
-- =============================================================================
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug IN ('reportes.detallados.read', 'reportes.detallados.export')
  JOIN security.modules m ON m.id = p.module_id AND m.product_code = r.product_code
 WHERE r.deleted_at IS NULL
   AND r.code = 'admin_tramites'
ON CONFLICT (role_id, permission_id) DO NOTHING;

UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('reportes.detallados.read', 'true'), ('reportes.detallados.export', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
