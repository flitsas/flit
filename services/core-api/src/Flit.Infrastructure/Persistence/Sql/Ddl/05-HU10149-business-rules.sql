-- HU #10149 — Reglas de negocio | Feature #10120

CREATE TABLE tramites.business_rules (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_business_rules PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    name varchar(150) NOT NULL,
    description varchar(500),
    is_active boolean NOT NULL DEFAULT true,
    priority smallint NOT NULL DEFAULT 0,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid
);
CREATE INDEX ix_business_rules_tenant_id ON tramites.business_rules(tenant_id);

CREATE TABLE tramites.business_rule_procedure_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_business_rule_procedure_types PRIMARY KEY (id),
    business_rule_id uuid NOT NULL REFERENCES tramites.business_rules(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT uq_business_rule_procedure_types UNIQUE (business_rule_id, procedure_type_id)
);

CREATE TABLE tramites.rule_condition_groups (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_rule_condition_groups PRIMARY KEY (id),
    business_rule_id uuid NOT NULL REFERENCES tramites.business_rules(id) ON DELETE CASCADE ON UPDATE CASCADE,
    parent_group_id uuid REFERENCES tramites.rule_condition_groups(id) ON DELETE CASCADE ON UPDATE CASCADE,
    logical_operator varchar(3) NOT NULL DEFAULT 'AND',
    sort_order smallint NOT NULL DEFAULT 0
);

CREATE TABLE tramites.rule_conditions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_rule_conditions PRIMARY KEY (id),
    condition_group_id uuid NOT NULL REFERENCES tramites.rule_condition_groups(id) ON DELETE CASCADE ON UPDATE CASCADE,
    left_operand_type varchar(20) NOT NULL,
    left_operand varchar(200) NOT NULL,
    operator varchar(20) NOT NULL,
    right_operand_type varchar(20) NOT NULL,
    right_operand varchar(200) NOT NULL,
    sort_order smallint NOT NULL DEFAULT 0
);

CREATE TABLE tramites.rule_actions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_rule_actions PRIMARY KEY (id),
    business_rule_id uuid NOT NULL REFERENCES tramites.business_rules(id) ON DELETE CASCADE ON UPDATE CASCADE,
    action_type varchar(30) NOT NULL,
    target_ref varchar(200) NOT NULL,
    action_config jsonb NOT NULL DEFAULT '{}',
    sort_order smallint NOT NULL DEFAULT 0
);

CREATE TABLE tramites.api_endpoint_catalog (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_api_endpoint_catalog PRIMARY KEY (id),
    code varchar(80) NOT NULL,
    name varchar(150) NOT NULL,
    url_template varchar(1000) NOT NULL,
    http_method varchar(10) NOT NULL,
    headers jsonb NOT NULL DEFAULT '{}',
    body_template jsonb,
    external_data_source_id uuid REFERENCES tramites.external_data_sources(id) ON DELETE SET NULL ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_api_endpoint_catalog_code UNIQUE (code)
);

ALTER TABLE tramites.business_rules ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.business_rules;
CREATE POLICY tenant_isolation ON tramites.business_rules
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_business_rules_row_version ON tramites.business_rules;
CREATE TRIGGER tr_business_rules_row_version BEFORE UPDATE ON tramites.business_rules
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_business_rules_audit ON tramites.business_rules;
CREATE TRIGGER tr_business_rules_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.business_rules
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

