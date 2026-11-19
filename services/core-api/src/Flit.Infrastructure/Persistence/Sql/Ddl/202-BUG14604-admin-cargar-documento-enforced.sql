-- =============================================================================
-- Bug #14604 — «Cargar documento» del admin FLIT sobre un trámite en cualquier estado.
-- Migración: 20261118120000_BUG14604_AdminCargarDocumentoEnforced.
--
-- Marcador enforced = 'true' (lo leen el seeder y PermisosEnforcedArchitectureTests) del slug nuevo
-- AdminTramiteCargarDocumento (módulo admin-tramites-avanzado): la API lo exige en
-- GET/POST /api/v1/admin/tramites/{id}/documentos. El permiso lo siembra DevelopmentAuthSeeder
-- (SeedAdminTramiteAdvancedPermissionsAsync), que lo concede solo a SuperAdmin; el seeder reaplica este script al
-- terminar para clasificar el permiso recién creado. Ningún rol de compañía lo trae, así la regla «Solo SA» del módulo de
-- permisos por usuario (HU #13653) lo deja en «Solo SA». Sin backfill de grants. Idempotente: UPDATE solo si cambia.
-- =============================================================================
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('AdminTramiteCargarDocumento', 'true')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
