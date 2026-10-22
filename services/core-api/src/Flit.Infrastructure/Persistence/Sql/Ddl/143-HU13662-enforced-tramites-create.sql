-- =============================================================================
-- HU #13662 (Feature #13105 F-E, Épica #12753) — la API exige tramites.create en las rutas que crean trámites.
-- Migración: 20261019130000_HU13662_EnforcedTramitesCreate.
--
-- Sin backfill (decisión del 2026-10-19, Épica #12753): la UI ya exigía tramites.create para mostrar «Nuevo trámite» y el
-- asistente, así que los roles que crean hoy ya lo tienen y lo conservan; dárselo a todo rol de Trámites habría autorizado
-- a crear a roles de solo lectura al encender Security:ModulePermissionEnforcement:Enabled. El DDL 145 copia este grant a
-- los Crear por familia.
-- Marcador enforced = 'true' (lo leen el seeder y PermisosEnforcedArchitectureTests). Idempotente: UPDATE solo si cambia.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('tramites.create', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
