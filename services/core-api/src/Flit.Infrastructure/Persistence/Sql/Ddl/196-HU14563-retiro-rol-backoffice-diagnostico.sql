-- HU #14563 (decisión D24 · L1, 2026-11-13) — retira el rol global de producto backoffice_diagnostico.
-- Migración: 20261113120000_HU14563_RetiroRolBackofficeDiagnostico.
--
-- El rol «Backoffice FLIT de Diagnóstico» lo creó el manifiesto de core-diagnostico. La decisión D23 lo sacó del
-- manifiesto (sus capacidades son del SuperAdmin), pero ProductManifestStore no retira los roles que desaparecen del
-- manifiesto: seguía activo y asignable. Qué hace, en este orden y en una sola transacción:
--   1. Cierra en soft delete las asignaciones vigentes del rol (el usuario conserva el histórico).
--   2. Borra sus filas de security.role_permissions (la tabla no tiene soft delete; el borrado queda en audit_logs).
--   3. Deja el rol inactivo y en soft delete, sin borrado físico (trazabilidad y auditoría).
-- Las tres sentencias usan now() y por eso comparten la misma marca de tiempo: el Down la usa para restituir solo lo
-- que cerró esta migración. Alcance: code = 'backoffice_diagnostico' AND product_code = 'diagnostico' AND
-- tenant_id IS NULL. Un rol propio de una compañía con el mismo código no se toca. Idempotente: si el rol ya está
-- retirado o no existe, ninguna sentencia escribe filas.
--
-- Triggers: tr_ura_mirror_product_admins solo actúa sobre asignaciones de AdminCompany (no es el caso);
-- tr_ura_product_code solo se dispara al cambiar role_id; tr_roles_tenant_code_not_global devuelve NEW cuando
-- deleted_at no es nulo; row_version y audit_log se encargan de la concurrencia y del rastro.

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. Asignaciones vigentes del rol → soft delete
-- ─────────────────────────────────────────────────────────────────────────────────────────────
UPDATE security.user_role_assignments a
   SET deleted_at = now(),
       updated_at = now()
  FROM security.roles r
 WHERE a.role_id = r.id
   AND r.code = 'backoffice_diagnostico' AND r.product_code = 'diagnostico' AND r.tenant_id IS NULL
   AND a.deleted_at IS NULL;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. Permisos del rol → borrado (role_permissions no tiene deleted_at)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
DELETE FROM security.role_permissions rp
 USING security.roles r
 WHERE rp.role_id = r.id
   AND r.code = 'backoffice_diagnostico' AND r.product_code = 'diagnostico' AND r.tenant_id IS NULL;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3. Rol → inactivo y en soft delete (se conserva la fila)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
UPDATE security.roles
   SET is_active  = false,
       deleted_at = COALESCE(deleted_at, now()),
       updated_at = now()
 WHERE code = 'backoffice_diagnostico' AND product_code = 'diagnostico' AND tenant_id IS NULL
   AND (is_active OR deleted_at IS NULL);
