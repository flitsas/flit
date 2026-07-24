-- HU #10154 — Admin compañías | Feature #10118

CREATE TABLE admin.tenant_profiles (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_profiles PRIMARY KEY (id),
    tenant_id uuid NOT NULL UNIQUE REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    trade_name varchar(255) NOT NULL,
    contact_email varchar(320),
    contact_phone varchar(20),
    address varchar(500),
    city_code varchar(10),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid
);
COMMENT ON COLUMN admin.tenant_profiles.contact_email IS '@pii:high';
COMMENT ON COLUMN admin.tenant_profiles.contact_phone IS '@pii:medium';
COMMENT ON COLUMN admin.tenant_profiles.address IS '@pii:medium';

CREATE TABLE admin.tenant_operational_policies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_operational_policies PRIMARY KEY (id),
    tenant_id uuid NOT NULL UNIQUE REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    allow_initial_registration boolean NOT NULL DEFAULT true,
    allow_misc_new_vehicles boolean NOT NULL DEFAULT true,
    only_own_vehicles boolean NOT NULL DEFAULT false,
    signature_vault_enabled boolean NOT NULL DEFAULT false,
    plate_preassign_enabled boolean NOT NULL DEFAULT false,
    notification_channel varchar(20) NOT NULL DEFAULT 'flit_smtp',
    notification_target varchar(20) NOT NULL DEFAULT 'submitter',
    payment_methods jsonb NOT NULL DEFAULT '[]',
    runt_provider_strategy varchar(20) NOT NULL DEFAULT 'verifik',
    runt_failover_timeout_ms integer NOT NULL DEFAULT 4000,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

CREATE TABLE admin.tenant_whitelist_users (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_whitelist_users PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    email varchar(320) NOT NULL,
    reason varchar(500),
    added_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_tenant_whitelist_users_tenant_email UNIQUE (tenant_id, email)
);
CREATE INDEX ix_tenant_whitelist_users_tenant_id ON admin.tenant_whitelist_users(tenant_id);
COMMENT ON COLUMN admin.tenant_whitelist_users.email IS '@pii:high';

CREATE TABLE admin.tenant_transit_office_grants (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_transit_office_grants PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_tenant_transit_office_grants UNIQUE (tenant_id, transit_office_id)
);
CREATE INDEX ix_tenant_transit_office_grants_tenant_id ON admin.tenant_transit_office_grants(tenant_id);

CREATE TABLE admin.tenant_config_audit_logs (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_config_audit_logs PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    entity_name varchar(80) NOT NULL,
    field_name varchar(80) NOT NULL,
    old_value jsonb,
    new_value jsonb,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changed_by uuid REFERENCES identity.users(id),
    correlation_id uuid
);
CREATE INDEX ix_tenant_config_audit_logs_tenant_id_changed_at
  ON admin.tenant_config_audit_logs(tenant_id, changed_at DESC);

ALTER TABLE admin.tenant_profiles ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.tenant_profiles
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.tenant_operational_policies ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.tenant_operational_policies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.tenant_whitelist_users ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.tenant_whitelist_users
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.tenant_transit_office_grants ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.tenant_transit_office_grants
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

ALTER TABLE admin.tenant_config_audit_logs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_config_audit_logs;
CREATE POLICY tenant_isolation ON admin.tenant_config_audit_logs
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_tenant_profiles_row_version ON admin.tenant_profiles;
CREATE TRIGGER tr_tenant_profiles_row_version BEFORE UPDATE ON admin.tenant_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenant_profiles_audit ON admin.tenant_profiles;
CREATE TRIGGER tr_tenant_profiles_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_operational_policies_row_version ON admin.tenant_operational_policies;
CREATE TRIGGER tr_tenant_operational_policies_row_version BEFORE UPDATE ON admin.tenant_operational_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenant_operational_policies_audit ON admin.tenant_operational_policies;
CREATE TRIGGER tr_tenant_operational_policies_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_operational_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_whitelist_users_audit ON admin.tenant_whitelist_users;
CREATE TRIGGER tr_tenant_whitelist_users_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_whitelist_users
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_transit_office_grants_audit ON admin.tenant_transit_office_grants;
CREATE TRIGGER tr_tenant_transit_office_grants_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_grants
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_config_audit_logs_audit ON admin.tenant_config_audit_logs;
CREATE TRIGGER tr_tenant_config_audit_logs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_config_audit_logs
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

