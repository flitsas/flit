-- =============================================================================
-- HU #13664 (Feature #13636 F-F, Épica #12753) — la API exige Ver y Crear por familia de trámite.
-- Migración: 20261019160000_HU13664_EnforcedPorFamilia.
--
-- Marcador enforced = 'true' (lo leen el seeder y PermisosEnforcedArchitectureTests) de los 6 slugs creados por el DDL 145
-- (HU #13663): el DDL 145 los dejó en 'false' porque aún no se exigían; este script se aplica junto con el código que los exige
-- (detrás de Security:ModulePermissionEnforcement:Enabled), así el marcador nunca promete una protección inexistente.
-- Sin backfill: los grants ya los hizo el DDL 145. Idempotente: UPDATE solo si cambia.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('matriculas.read', 'true'), ('matriculas.create', 'true'),
        ('traspasos.read', 'true'), ('traspasos.create', 'true'),
        ('otros-tramites.read', 'true'), ('otros-tramites.create', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
