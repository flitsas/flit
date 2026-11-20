-- =============================================================================
-- HU #13823 (Feature #13819, Épica #13817) — «Reenviar validación de identidad» asignable por los administradores
-- de la compañía y del organismo de tránsito.
-- Migración: 20261022130000_HU13823_ReenviarValidacionAsignable.
--
-- Por qué: el permiso AdminTramiteReenviarValidacion (HU #12157) nació solo para SuperAdmin. La regla «Solo SA»
-- (HU #13653, ListSuperAdminOnlyAsync) marca como no otorgable todo permiso que NINGÚN administrador del tenant
-- tenga en su tope, así que el AdminCompany no podía asignarlo a sus roles ni a sus usuarios.
--
-- Qué hace: lo agrega a los dos roles administradores de producto 'tramites' (el del módulo admin-tramites-avanzado,
-- trigger tr_role_permissions_same_product):
--   admin_tramites  (COMPANY)         acompaña siempre a AdminCompany (HU #12964).
--   ot_admin        (TRANSIT_OFFICE)  administrador del organismo de tránsito.
-- AdminCompany es de producto 'plataforma' y no puede tener permisos de 'tramites'; su tope los toma de admin_tramites.
-- Solo este slug: cambiar estado, anular, consolidado y reasignar gestor siguen exclusivos de SuperAdmin.
-- Idempotente: ON CONFLICT DO NOTHING. Quien tenga sesión abierta debe volver a entrar para verlo.
-- =============================================================================

INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug = 'AdminTramiteReenviarValidacion'
  JOIN security.modules m ON m.id = p.module_id
 WHERE r.code IN ('admin_tramites', 'ot_admin')
   AND r.tenant_id IS NULL
   AND r.deleted_at IS NULL
   AND m.product_code = r.product_code
ON CONFLICT (role_id, permission_id) DO NOTHING;
