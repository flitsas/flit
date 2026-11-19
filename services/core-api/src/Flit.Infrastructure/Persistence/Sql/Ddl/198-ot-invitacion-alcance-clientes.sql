-- =============================================================================
-- Mejora sin HU — alcance de compañías cliente para invitaciones a un OT (complemento del DDL 197).
-- Migración: 20261117130000_OtInvitacionAlcanceClientes.
--
-- Al invitar a un usuario del OT con alcance 'especificos' el usuario aún no existe, así que
-- admin.ot_user_client_scopes (FK a identity.users) no sirve. Se guardan las compañías elegidas ligadas a la invitación;
-- al aceptarse (activación de cuenta) se materializan en ot_user_client_scopes en la MISMA transacción que crea al usuario.
--
-- AUSENCIA DE FILAS = 'todos'. Sin PII. Sin RLS (misma razón que el DDL 197). Idempotente.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.ot_invitation_client_scope_clients (
    invitation_id    uuid        NOT NULL,
    client_tenant_id uuid        NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid        NULL,

    CONSTRAINT pk_ot_invitation_client_scope_clients PRIMARY KEY (invitation_id, client_tenant_id),
    CONSTRAINT fk_ot_invitation_client_scope_clients_invitations FOREIGN KEY (invitation_id)
        REFERENCES security.user_invitations (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_ot_invitation_client_scope_clients_tenants FOREIGN KEY (client_tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_ot_invitation_client_scope_clients_client_tenant_id
    ON admin.ot_invitation_client_scope_clients (client_tenant_id);

COMMENT ON TABLE admin.ot_invitation_client_scope_clients IS
    'Compañías cliente elegidas al invitar a un usuario de OT con alcance especificos; se materializan al aceptar la invitación. Sin PII.';

-- Sin trigger trg_audit_log: lee NEW.id y esta tabla (PK compuesta) no tiene columna id (ver DDL 197).
DROP TRIGGER IF EXISTS tr_ot_invitation_client_scope_clients_audit ON admin.ot_invitation_client_scope_clients;
