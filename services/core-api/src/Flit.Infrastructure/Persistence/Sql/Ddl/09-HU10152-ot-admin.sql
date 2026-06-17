-- HU #10152 — OT admin | Feature #10133

CREATE TABLE catalogs.transit_offices (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_transit_offices PRIMARY KEY (id),
    code varchar(10) NOT NULL,
    name varchar(200) NOT NULL,
    department_code varchar(5) NOT NULL,
    city_code varchar(10) NOT NULL,
    address varchar(500),
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_transit_offices_code UNIQUE (code)
);

ALTER TABLE tramites.procedure_instances
  ADD CONSTRAINT fk_procedure_instances_transit_offices
  FOREIGN KEY (transit_office_id) REFERENCES catalogs.transit_offices(id)
  ON DELETE RESTRICT ON UPDATE CASCADE;

ALTER TABLE admin.tenant_transit_office_grants
  ADD CONSTRAINT fk_tenant_transit_office_grants_transit_offices
  FOREIGN KEY (transit_office_id) REFERENCES catalogs.transit_offices(id)
  ON DELETE RESTRICT ON UPDATE CASCADE;

CREATE TABLE admin.transit_office_profiles (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_transit_office_profiles PRIMARY KEY (id),
    tenant_id uuid NOT NULL UNIQUE REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    operation_mode varchar(20) NOT NULL DEFAULT 'dashboard',
    quipux_read_only boolean NOT NULL DEFAULT false,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

CREATE TABLE admin.ot_feature_flags (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_feature_flags PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    flag_key varchar(80) NOT NULL,
    is_enabled boolean NOT NULL DEFAULT false,
    config jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_ot_feature_flags_tenant_key UNIQUE (tenant_id, flag_key)
);
CREATE INDEX ix_ot_feature_flags_tenant_id ON admin.ot_feature_flags(tenant_id);

CREATE TABLE admin.ot_webhook_subscriptions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_webhook_subscriptions PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    event_type varchar(50) NOT NULL,
    target_url varchar(1000) NOT NULL,
    secret_hash varchar(128) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);
CREATE INDEX ix_ot_webhook_subscriptions_tenant_id ON admin.ot_webhook_subscriptions(tenant_id);
COMMENT ON COLUMN admin.ot_webhook_subscriptions.secret_hash IS '@pii:high';

CREATE TABLE admin.ot_api_call_logs (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_api_call_logs PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    direction varchar(10) NOT NULL,
    endpoint varchar(500) NOT NULL,
    http_method varchar(10) NOT NULL,
    payload_hash varchar(64) NOT NULL,
    response_code smallint,
    duration_ms integer,
    called_at timestamptz NOT NULL DEFAULT now(),
    correlation_id uuid,
    error_message varchar(500)
);
CREATE INDEX ix_ot_api_call_logs_tenant_id_called_at ON admin.ot_api_call_logs(tenant_id, called_at DESC);

CREATE TABLE admin.ot_document_precedence (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_document_precedence PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    document_type_id uuid NOT NULL REFERENCES tramites.document_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    sort_order smallint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_ot_document_precedence UNIQUE (tenant_id, procedure_type_id, document_type_id)
);
CREATE INDEX ix_ot_document_precedence_tenant_id ON admin.ot_document_precedence(tenant_id);

CREATE TABLE admin.ot_document_tags (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_document_tags PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    code varchar(50) NOT NULL,
    name varchar(100) NOT NULL,
    color varchar(7) NOT NULL DEFAULT '#162744',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_ot_document_tags_tenant_code UNIQUE (tenant_id, code)
);
CREATE INDEX ix_ot_document_tags_tenant_id ON admin.ot_document_tags(tenant_id);

ALTER TABLE admin.transit_office_profiles ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.transit_office_profiles
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.ot_feature_flags ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.ot_feature_flags
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.ot_webhook_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.ot_webhook_subscriptions
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.ot_api_call_logs ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.ot_api_call_logs
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.ot_document_precedence ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.ot_document_precedence
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE admin.ot_document_tags ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.ot_document_tags
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_transit_office_profiles_row_version ON admin.transit_office_profiles;
CREATE TRIGGER tr_transit_office_profiles_row_version BEFORE UPDATE ON admin.transit_office_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_transit_office_profiles_audit ON admin.transit_office_profiles;
CREATE TRIGGER tr_transit_office_profiles_audit AFTER INSERT OR UPDATE OR DELETE ON admin.transit_office_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_feature_flags_audit ON admin.ot_feature_flags;
CREATE TRIGGER tr_ot_feature_flags_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_feature_flags
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_webhook_subscriptions_audit ON admin.ot_webhook_subscriptions;
CREATE TRIGGER tr_ot_webhook_subscriptions_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_webhook_subscriptions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_api_call_logs_audit ON admin.ot_api_call_logs;
CREATE TRIGGER tr_ot_api_call_logs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_api_call_logs
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_document_precedence_audit ON admin.ot_document_precedence;
CREATE TRIGGER tr_ot_document_precedence_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_document_precedence
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_document_tags_audit ON admin.ot_document_tags;
CREATE TRIGGER tr_ot_document_tags_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_document_tags
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

