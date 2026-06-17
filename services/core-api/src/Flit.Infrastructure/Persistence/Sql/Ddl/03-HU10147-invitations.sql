-- HU #10147 — Invitaciones | Feature #10115

CREATE TABLE security.user_invitations (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_invitations PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    email varchar(320) NOT NULL,
    role_id uuid NOT NULL REFERENCES security.roles(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    token_hash varchar(128) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'pending',
    invited_by uuid NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    accepted_at timestamptz,
    expires_at timestamptz,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_user_invitations_token_hash UNIQUE (token_hash)
);
CREATE UNIQUE INDEX uq_user_invitations_tenant_email_pending
  ON security.user_invitations(tenant_id, email) WHERE status = 'pending';
CREATE INDEX ix_user_invitations_tenant_id ON security.user_invitations(tenant_id);

COMMENT ON COLUMN security.user_invitations.email IS '@pii:high';
COMMENT ON COLUMN security.user_invitations.token_hash IS '@pii:high';

ALTER TABLE security.user_invitations ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.user_invitations
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_user_invitations_row_version ON security.user_invitations;
CREATE TRIGGER tr_user_invitations_row_version BEFORE UPDATE ON security.user_invitations
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_invitations_audit ON security.user_invitations;
CREATE TRIGGER tr_user_invitations_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_invitations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

