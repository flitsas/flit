-- HU #10506 — Roles y Permisos — Soporte multi-rol por usuario | Feature #10504

-- 1. Modelo ADITIVO de asignación de roles: un usuario puede tener varios roles activos
--    simultáneos en el mismo tenant (permisos = unión). El índice único parcial de
--    20260624193655_Fix_UserRoleAssignment_UniqueConstraint (uq_ura_active_user_tenant, sobre
--    (user_id, tenant_id)) solo permitía UN rol activo por usuario/tenant -- se reemplaza por
--    uno que agrega role_id: permite N roles activos por (user, tenant), pero sigue impidiendo
--    duplicar el MISMO rol dos veces (AC2).
DROP INDEX IF EXISTS security.uq_ura_active_user_tenant;

CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant_role
    ON security.user_role_assignments(user_id, tenant_id, role_id)
    WHERE deleted_at IS NULL;

COMMENT ON INDEX security.uq_ura_active_user_tenant_role IS
  'HU #10506: unicidad de negocio del modelo aditivo multi-rol -- un usuario no puede tener el MISMO rol activo dos veces en el mismo tenant, pero sí varios roles DISTINTOS activos simultáneos.';

-- 2. Tabla puente N:M invitación-roles (AC4: invitar con varios roles simultáneos).
--    user_invitations.role_id se conserva como el rol "primario" (el primero seleccionado, por
--    compatibilidad con consumidores que aún leen ese único campo); esta tabla es la fuente
--    completa de N roles por invitación. Al aceptar la invitación (ActivateAccountHandler /
--    UserActivationRepository) se crea una fila user_role_assignments POR CADA rol aquí listado.
--    A diferencia de security.roles/role_permissions (catálogo GLOBAL desde HU #10505), esta
--    fila SÍ es un hecho de negocio propio del tenant de la invitación: lleva tenant_id + RLS +
--    columnas de auditoría estándar (checklist A4/A5/A10/A16), igual que user_invitations.
CREATE TABLE IF NOT EXISTS security.invitation_roles (
    id uuid NOT NULL DEFAULT uuidv7(),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    invitation_id uuid NOT NULL REFERENCES security.user_invitations(id) ON DELETE CASCADE,
    role_id uuid NOT NULL REFERENCES security.roles(id) ON DELETE CASCADE,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT pk_invitation_roles PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_invitation_roles_tenant_id_invitation_id_role_id
    ON security.invitation_roles(tenant_id, invitation_id, role_id);
CREATE INDEX IF NOT EXISTS ix_invitation_roles_invitation_id ON security.invitation_roles(invitation_id);
CREATE INDEX IF NOT EXISTS ix_invitation_roles_role_id ON security.invitation_roles(role_id);

ALTER TABLE security.invitation_roles ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.invitation_roles
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_invitation_roles_row_version ON security.invitation_roles;
CREATE TRIGGER tr_invitation_roles_row_version BEFORE UPDATE ON security.invitation_roles
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_invitation_roles_audit ON security.invitation_roles;
CREATE TRIGGER tr_invitation_roles_audit AFTER INSERT OR UPDATE OR DELETE ON security.invitation_roles
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE security.invitation_roles IS
  'HU #10506 AC4/AC5: tabla puente N:M invitación-roles -- una invitación requiere AL MENOS un rol (validado en CreateInvitationHandler antes de crear la fila) y puede tener varios simultáneos.';
