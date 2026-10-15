-- =============================================================================
-- HU #13580 (Feature #13579, Épica #13271) — Reportes Detallados deja de ser un módulo y pasa a ser una
-- pestaña del módulo Reportes.
--
-- 1. Los permisos `reportes.detallados.read` y `reportes.detallados.export` pasan al módulo `reportes` con el
--    mismo slug. Los grants (security.role_permissions) apuntan al id del permiso, que no cambia: ningún rol
--    pierde acceso, y quien solo tenía Reportes Detallados pasa a ver el módulo `reportes` (los módulos
--    accesibles se derivan de los permisos concedidos).
-- 2. El módulo `reportes-detallados` queda con borrado lógico e inactivo: desaparece del constructor de roles
--    y del menú. No se borra físicamente (security.permissions lo referencia con ON DELETE RESTRICT y se
--    conserva la trazabilidad).
--
-- Idempotente: un segundo run no encuentra permisos fuera de `reportes` ni un módulo vivo que retirar.
-- =============================================================================

UPDATE security.permissions p
   SET module_id  = m.id,
       updated_at = now()
  FROM security.modules m
 WHERE m.code = 'reportes'
   AND m.deleted_at IS NULL
   AND p.slug IN ('reportes.detallados.read', 'reportes.detallados.export')
   AND p.module_id <> m.id;

UPDATE security.modules
   SET is_active  = false,
       deleted_at = now(),
       updated_at = now()
 WHERE code = 'reportes-detallados'
   AND deleted_at IS NULL;
