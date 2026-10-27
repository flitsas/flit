-- =============================================================================
-- HU #13655 (Feature #13104 F-D2, Épica #12753) — rol de sistema «Gestor OT» (gestor_tramites_ot).
-- Migración: 20261021120000_HU13655_RolGestorOt.
--
-- Por qué: el único rol TRANSIT_OFFICE de sistema era ot_admin, así que todo usuario de un organismo era
-- administrador y su ficha de permisos quedaba en solo lectura para el Admin OT (FB-9). Con este rol el Admin OT
-- invita operadores no administradores y les ajusta permisos individuales dentro de su tope.
--
-- Qué hace:
--   1. Crea el rol global (tenant_id NULL) gestor_tramites_ot, de sistema, TRANSIT_OFFICE, producto 'tramites'
--      (el de ot_admin y el de los módulos ot-*). Si ya existe como rol global vigente (bases donde alguien lo creó
--      a mano como «Operador OT»), solo le fija nombre, descripción y marca de sistema; no le quita permisos.
--      Si algún organismo tiene un rol propio con ese código, no se crea el global (el disparador
--      tr_roles_tenant_code_not_global lo rechazaría) y la migración sigue.
--   2. Permisos base, los mínimos para operar la bandeja del organismo:
--        ot.tramites.manage  bandeja de trámites (aprobar, rechazar, consolidados, placas, Quipux).
--        ot.reportes.manage  el Inicio del usuario OT («Tu cola de trabajo») lee /admin/ot/metrics/operational,
--                            que el Anexo A protege con este slug; sin él el Inicio respondería 403.
--      NO recibe ot.improntas.manage, ot.mandatos.manage, ot.mandatarios.manage, ot.documentos.manage,
--      ot.usuarios.manage ni ot.integracion.manage: el Admin OT los otorga por usuario dentro de su tope.
--      A diferencia del backfill del DDL 138 (que conservó lo que los roles OT ya veían en el dock), un rol
--      nuevo nace con lo mínimo.
-- Depende del DDL 137 (permisos ot.* creados). Idempotente: NOT EXISTS + ON CONFLICT DO NOTHING.
-- =============================================================================

-- 1a. Crear el rol si no existe ningún rol vigente con ese código (global o de un organismo).
INSERT INTO security.roles (code, name, description, is_system, is_active, target_entity_type, product_code)
SELECT 'gestor_tramites_ot', 'Gestor OT',
       'Opera la bandeja de trámites del organismo de tránsito. El administrador del organismo le puede habilitar más módulos.',
       true, true, 'TRANSIT_OFFICE', 'tramites'
 WHERE NOT EXISTS (
       SELECT 1 FROM security.roles
        WHERE lower(code) = 'gestor_tramites_ot' AND deleted_at IS NULL);

-- 1b. Rol global ya existente: nombre, descripción y marca de sistema oficiales (sin tocar sus permisos).
UPDATE security.roles
   SET name = 'Gestor OT',
       description = 'Opera la bandeja de trámites del organismo de tránsito. El administrador del organismo le puede habilitar más módulos.',
       is_system = true
 WHERE code = 'gestor_tramites_ot'
   AND target_entity_type = 'TRANSIT_OFFICE'
   AND tenant_id IS NULL
   AND deleted_at IS NULL
   AND (name IS DISTINCT FROM 'Gestor OT' OR is_system IS DISTINCT FROM true
        OR description IS DISTINCT FROM 'Opera la bandeja de trámites del organismo de tránsito. El administrador del organismo le puede habilitar más módulos.');

-- 2. Permisos base (solo módulos del mismo producto del rol: tr_role_permissions_same_product).
INSERT INTO security.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM security.roles r
  JOIN security.permissions p ON p.slug IN ('ot.tramites.manage', 'ot.reportes.manage')
  JOIN security.modules m ON m.id = p.module_id
 WHERE r.code = 'gestor_tramites_ot'
   AND r.target_entity_type = 'TRANSIT_OFFICE'
   AND r.tenant_id IS NULL
   AND r.deleted_at IS NULL
   AND m.product_code = r.product_code
ON CONFLICT (role_id, permission_id) DO NOTHING;
