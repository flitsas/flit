-- Rol único por usuario — revierte el modelo aditivo de la HU #10506.
--
-- Decisión del responsable funcional: un usuario tiene UN rol; lo que define lo que puede hacer
-- son los PERMISOS de ese rol. Si un "documentador" necesita crear trámites, se le agrega el
-- permiso al rol, no un segundo rol. La HU #10506 había tumbado uq_ura_active_user_tenant
-- (un rol activo por usuario/tenant) para permitir N; aquí se restituye esa invariante.

-- 1. Cierra las asignaciones sobrantes ANTES de crear el índice, que si no fallaría con los
--    datos existentes. Se conserva la MÁS RECIENTE por (usuario, tenant): es la última decisión
--    que tomó un administrador. Las demás quedan en soft-delete, así que el histórico y el
--    rastro de auditoría se conservan y la operación es auditable.
WITH ranked AS (
    SELECT id,
           row_number() OVER (
               PARTITION BY user_id, tenant_id
               ORDER BY assigned_at DESC, id DESC
           ) AS rn
    FROM security.user_role_assignments
    WHERE deleted_at IS NULL
)
UPDATE security.user_role_assignments a
   SET deleted_at = now()
  FROM ranked
 WHERE a.id = ranked.id
   AND ranked.rn > 1;

-- 2. Restituye la unicidad de negocio: un único rol activo por usuario y tenant. El índice de la
--    HU #10506 (que incluía role_id) deja de aplicar.
DROP INDEX IF EXISTS security.uq_ura_active_user_tenant_role;

CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant
    ON security.user_role_assignments(user_id, tenant_id)
    WHERE deleted_at IS NULL;

COMMENT ON INDEX security.uq_ura_active_user_tenant IS
  'Rol único por usuario: un usuario tiene una sola asignación activa por tenant. Lo que define lo que puede hacer son los permisos de ese rol (revierte el modelo aditivo de HU #10506).';

-- 3. Una invitación tampoco lleva varios roles: la tabla puente se conserva (la activación la
--    sigue leyendo) pero se limita a una fila activa por invitación. Se conserva el rol PRIMARIO
--    (user_invitations.role_id, el primero que se seleccionó) para que la fila que sobrevive sea
--    la misma que ya muestran los listados de invitaciones pendientes.
WITH ranked_invitation_roles AS (
    SELECT ir.id,
           row_number() OVER (
               PARTITION BY ir.invitation_id
               ORDER BY (ir.role_id = i.role_id) DESC, ir.created_at, ir.id
           ) AS rn
    FROM security.invitation_roles ir
    JOIN security.user_invitations i ON i.id = ir.invitation_id
    WHERE ir.deleted_at IS NULL
)
UPDATE security.invitation_roles ir
   SET deleted_at = now()
  FROM ranked_invitation_roles
 WHERE ir.id = ranked_invitation_roles.id
   AND ranked_invitation_roles.rn > 1;

DROP INDEX IF EXISTS security.uq_invitation_roles_single;

CREATE UNIQUE INDEX IF NOT EXISTS uq_invitation_roles_single
    ON security.invitation_roles(invitation_id)
    WHERE deleted_at IS NULL;

COMMENT ON INDEX security.uq_invitation_roles_single IS
  'Rol único por usuario: una invitación asigna exactamente un rol (revierte AC4 de HU #10506).';
