-- =============================================================================
-- HU #13659 (Feature #13105 F-E, Épica #12753) — marca como exigidos por la API los 8 slugs de Reportes.
-- Migración: 20261019100000_HU13659_EnforcedReportes. Enforcement: Flit.Api/Authorization/ReportesModulePermissions.cs y
-- Endpoints/Analytics (flag Security:ModulePermissionEnforcement:Enabled). Sin backfill de grants: los roles que hoy
-- reciben estos slugs (SuperAdmin por bypass, admin_tramites por el seeder) ya los tienen; reportes.detallados.* es otra HU.
-- Idempotente: reaplicarlo reescribe los mismos valores.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('reportes.read', 'true'), ('reportes.resumen.read', 'true'), ('reportes.operacion.read', 'true'),
        ('reportes.ot.read', 'true'), ('reportes.uso.read', 'true'), ('reportes.productividad.read', 'true'),
        ('reportes.consultas.read', 'true'), ('reportes.programacion.manage', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
