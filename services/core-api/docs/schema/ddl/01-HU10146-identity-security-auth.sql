-- HU #10146 — identity + security auth (requires 00-bootstrap via SchemaBootstrap)

CREATE TABLE identity.tenants (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenants PRIMARY KEY (id),
    code varchar(32) NOT NULL,
    legal_name varchar(255) NOT NULL,
    tax_id varchar(20) NOT NULL,
    tenant_type varchar(20) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL,
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    row_version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_tenants_code UNIQUE (code)
);
COMMENT ON COLUMN identity.tenants.tax_id IS '@pii:medium';

CREATE TABLE identity.users (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_users PRIMARY KEY (id),
    email varchar(320) NOT NULL,
    display_name varchar(150) NOT NULL,
    status varchar(20) NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_users_email UNIQUE (email)
);
COMMENT ON COLUMN identity.users.email IS '@pii:high';
COMMENT ON COLUMN identity.users.display_name IS '@pii:medium';

CREATE TABLE security.user_credentials (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_credentials PRIMARY KEY (id),
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    password_hash varchar(255) NOT NULL,
    must_change_password boolean NOT NULL DEFAULT false,
    password_changed_at timestamptz,
    failed_login_attempts smallint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz,
    row_version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_user_credentials_user_id UNIQUE (user_id)
);
COMMENT ON COLUMN security.user_credentials.password_hash IS '@pii:high';

CREATE TABLE security.password_reset_tokens (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    token_hash varchar(128) NOT NULL,
    purpose varchar(30) NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    requested_by uuid,
    created_at timestamptz NOT NULL,
    CONSTRAINT uq_password_reset_tokens_token_hash UNIQUE (token_hash)
);
CREATE INDEX ix_password_reset_tokens_user_id ON security.password_reset_tokens(user_id);
COMMENT ON COLUMN security.password_reset_tokens.token_hash IS '@pii:high';

CREATE TABLE security.user_temp_suspensions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_temp_suspensions PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL,
    reason varchar(500) NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid
);
CREATE INDEX ix_user_temp_suspensions_tenant_id_user_id ON security.user_temp_suspensions(tenant_id, user_id);

ALTER TABLE security.user_temp_suspensions ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.user_temp_suspensions
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_tenants_row_version ON identity.tenants;
CREATE TRIGGER tr_tenants_row_version BEFORE UPDATE ON identity.tenants
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenants_audit ON identity.tenants;
CREATE TRIGGER tr_tenants_audit AFTER INSERT OR UPDATE OR DELETE ON identity.tenants
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_users_row_version ON identity.users;
CREATE TRIGGER tr_users_row_version BEFORE UPDATE ON identity.users
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_users_audit ON identity.users;
CREATE TRIGGER tr_users_audit AFTER INSERT OR UPDATE OR DELETE ON identity.users
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_credentials_row_version ON security.user_credentials;
CREATE TRIGGER tr_user_credentials_row_version BEFORE UPDATE ON security.user_credentials
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_credentials_audit ON security.user_credentials;
CREATE TRIGGER tr_user_credentials_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_credentials
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_password_reset_tokens_audit ON security.password_reset_tokens;
CREATE TRIGGER tr_password_reset_tokens_audit AFTER INSERT OR UPDATE OR DELETE ON security.password_reset_tokens
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_temp_suspensions_row_version ON security.user_temp_suspensions;
CREATE TRIGGER tr_user_temp_suspensions_row_version BEFORE UPDATE ON security.user_temp_suspensions
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_temp_suspensions_audit ON security.user_temp_suspensions;
CREATE TRIGGER tr_user_temp_suspensions_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_temp_suspensions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

