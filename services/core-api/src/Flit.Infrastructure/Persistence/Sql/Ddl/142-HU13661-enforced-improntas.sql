-- =============================================================================
-- HU #13661 (Feature #13105 F-E, Épica #12753) — Improntas: la API exige improntas.read / improntas.generate.
-- Migración: 20261019120000_HU13661_EnforcedImprontas.
--
-- Las rutas /api/v1/admin/improntas* están bajo SuperAdminPolicy (solo SuperAdmin, que además bypassa el slug), así que
-- ningún rol entra hoy sin el slug y no hace falta backfill de grants.
-- Marca los 2 slugs como 'true' (la exigencia va detrás de Security:ModulePermissionEnforcement:Enabled).
-- Idempotente: reaplicarlo reescribe los mismos valores.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('improntas.read', 'true'), ('improntas.generate', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
