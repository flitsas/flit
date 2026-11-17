-- =============================================================================
-- Mejora sin HU — alcance de compañías cliente por usuario de un organismo de tránsito (OT).
-- Migración: 20261117120000_OtUsuarioAlcanceClientes.
--
-- Por defecto un usuario del OT ve todas las compañías cliente del OT ('todos'). Un Admin OT o el Super Admin pueden
-- restringirlo a compañías concretas ('especificos', mínimo 1 — regla del caso de uso).
--
--   admin.ot_user_client_scopes         — cabecera: una fila por (usuario, organismo) con modo todos|especificos.
--   admin.ot_user_client_scope_clients  — hija: compañías cliente asignadas cuando mode = 'especificos'.
--
-- AUSENCIA DE FILA = 'todos': no hay backfill y ningún usuario existente pierde acceso al desplegar.
--
-- Sin RLS (igual que company_transit_office_agreements y las lecturas cross-tenant del hub OT): se leen cruzando el tenant
-- del OT con tenants cliente bajo SET LOCAL row_security = off (OtTenantScope / OtVisibleCompanies), y el Super Admin
-- escribe sin ser del tenant del OT. tenant_id es el tenant del OT dueño del usuario; el alcance lo impone el caso de uso.
-- Sin PII (solo identificadores). Idempotente.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.ot_user_client_scopes (
    id                uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id         uuid         NOT NULL,
    user_id           uuid         NOT NULL,
    transit_office_id uuid         NOT NULL,
    mode              varchar(12)  NOT NULL DEFAULT 'todos',
    created_at        timestamptz  NOT NULL DEFAULT now(),
    created_by        uuid         NULL,
    updated_at        timestamptz  NOT NULL DEFAULT now(),
    updated_by        uuid         NULL,
    row_version       bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_ot_user_client_scopes PRIMARY KEY (id),
    CONSTRAINT fk_ot_user_client_scopes_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_ot_user_client_scopes_users FOREIGN KEY (user_id)
        REFERENCES identity.users (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_ot_user_client_scopes_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_ot_user_client_scopes_user_office UNIQUE (user_id, transit_office_id),
    CONSTRAINT ck_ot_user_client_scopes_mode CHECK (mode IN ('todos', 'especificos'))
);

CREATE INDEX IF NOT EXISTS ix_ot_user_client_scopes_tenant_id ON admin.ot_user_client_scopes (tenant_id);
CREATE INDEX IF NOT EXISTS ix_ot_user_client_scopes_transit_office_id ON admin.ot_user_client_scopes (transit_office_id);

COMMENT ON TABLE admin.ot_user_client_scopes IS
    'Alcance de compañías cliente de un usuario de un OT. Ausencia de fila = todos. Sin PII; sin RLS (lectura cross-tenant).';
COMMENT ON COLUMN admin.ot_user_client_scopes.tenant_id IS 'Tenant del OT dueño del usuario.';
COMMENT ON COLUMN admin.ot_user_client_scopes.mode IS
    'todos = todas las compañías cliente del OT; especificos = solo las de ot_user_client_scope_clients (mínimo 1).';
COMMENT ON COLUMN admin.ot_user_client_scopes.updated_by IS
    'Usuario que cambió el alcance por última vez (identity.users.id, sin FK: el rastro sobrevive al usuario).';

DROP TRIGGER IF EXISTS tr_ot_user_client_scopes_row_version ON admin.ot_user_client_scopes;
CREATE TRIGGER tr_ot_user_client_scopes_row_version BEFORE UPDATE ON admin.ot_user_client_scopes
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_ot_user_client_scopes_audit ON admin.ot_user_client_scopes;
CREATE TRIGGER tr_ot_user_client_scopes_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_user_client_scopes
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

CREATE TABLE IF NOT EXISTS admin.ot_user_client_scope_clients (
    scope_id         uuid        NOT NULL,
    client_tenant_id uuid        NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid        NULL,

    CONSTRAINT pk_ot_user_client_scope_clients PRIMARY KEY (scope_id, client_tenant_id),
    CONSTRAINT fk_ot_user_client_scope_clients_scopes FOREIGN KEY (scope_id)
        REFERENCES admin.ot_user_client_scopes (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_ot_user_client_scope_clients_tenants FOREIGN KEY (client_tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT
);

-- La PK cubre scope_id; este índice cubre la FK a la compañía.
CREATE INDEX IF NOT EXISTS ix_ot_user_client_scope_clients_client_tenant_id
    ON admin.ot_user_client_scope_clients (client_tenant_id);

COMMENT ON TABLE admin.ot_user_client_scope_clients IS
    'Compañías cliente asignadas a un usuario de OT con mode = especificos. Sin PII.';

-- Sin trigger trg_audit_log en esta tabla: esa función lee NEW.id y esta tabla (PK compuesta scope_id + client_tenant_id) no tiene
-- columna id, así que el INSERT fallaría ("el registro new no tiene un campo id"). El cambio de alcance queda auditado por el
-- trigger de la cabecera y por security.user_permission_audit (acción CLIENT_SCOPE_CHANGED).
DROP TRIGGER IF EXISTS tr_ot_user_client_scope_clients_audit ON admin.ot_user_client_scope_clients;
