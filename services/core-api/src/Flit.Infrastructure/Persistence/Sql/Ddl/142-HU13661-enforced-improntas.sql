-- =============================================================================
-- HU #13661 (Feature #13105 F-E, Épica #12753) — Improntas: improntas.read / improntas.generate quedan EXCLUIDOS.
-- Migración: 20261019120000_HU13661_EnforcedImprontas.
--
-- Las rutas /api/v1/admin/improntas* están bajo SuperAdminPolicy (solo SuperAdmin, que además bypassa el slug), así que
-- ningún rol entra hoy sin el slug y no hace falta backfill de grants.
-- Se marcan 'excluded': la ruta exige el slug, pero como solo entra el SuperAdmin (que lo bypassa), otorgarlo o
-- denegarlo por usuario no tendría efecto. Excluidos, no aparecen en «Permisos por módulo» ni se pueden configurar
-- por usuario (EXCLUDED). Si la ruta se abre a otros roles, se pasan a 'true' en un DDL nuevo.
-- Idempotente: reaplicarlo reescribe los mismos valores.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('improntas.read', 'excluded'), ('improntas.generate', 'excluded')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
