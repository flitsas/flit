-- =============================================================================
-- Épica #14644 (Feature #14645, HU #14648) — contingencia de proveedor RUNT.
-- Migración: 20261119210000_HU14648_RuntContingencyEnforced.
--
-- Marcador enforced = 'excluded' del slug nuevo runt_contingency.manage (afecta a TODAS las empresas: nunca se concede por usuario ni por rol de empresa;
-- solo el rol SuperAdmin lo tiene). Lo leen el seeder y PermisosEnforcedArchitectureTests. La API lo exige
-- en /api/v1/admin/platform/runt-contingency (AdminRuntContingencyEndpoints). El permiso lo siembra
-- DevelopmentAuthSeeder (SeedRuntContingencyPermissionsAsync), que lo concede solo a SuperAdmin; el seeder reaplica este script
-- al terminar para clasificar el permiso recién creado. Ningún rol de compañía lo trae. Sin backfill de grants.
-- Idempotente: UPDATE solo si cambia.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('runt_contingency.manage', 'excluded')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
