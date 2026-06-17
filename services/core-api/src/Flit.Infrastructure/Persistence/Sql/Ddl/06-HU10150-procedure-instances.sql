-- HU #10150 — Runtime instancias | Feature #10128

CREATE TABLE tramites.procedure_instances (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instances PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    reference_number varchar(30) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'draft',
    transit_office_id uuid,
    submitted_at timestamptz,
    completed_at timestamptz,
    created_by_user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    rules_snapshot_at timestamptz,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_procedure_instances_tenant_reference UNIQUE (tenant_id, reference_number)
);
CREATE INDEX ix_procedure_instances_tenant_id_status_created_at
  ON tramites.procedure_instances(tenant_id, status, created_at DESC);

CREATE TABLE tramites.procedure_instance_actors (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_actors PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_entity_id uuid NOT NULL REFERENCES tramites.procedure_entities(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    actor_type varchar(20) NOT NULL,
    document_type varchar(10) NOT NULL,
    document_number varchar(20) NOT NULL,
    full_name varchar(200) NOT NULL,
    email varchar(320),
    phone varchar(20),
    metadata jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_procedure_instance_actors_instance_entity UNIQUE (procedure_instance_id, procedure_entity_id)
);
CREATE INDEX ix_procedure_instance_actors_tenant_id ON tramites.procedure_instance_actors(tenant_id);
COMMENT ON COLUMN tramites.procedure_instance_actors.document_number IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_actors.email IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_actors.full_name IS '@pii:medium';
COMMENT ON COLUMN tramites.procedure_instance_actors.phone IS '@pii:medium';

CREATE TABLE tramites.procedure_instance_field_values (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_field_values PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    form_field_id uuid NOT NULL REFERENCES tramites.form_fields(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    field_key varchar(80) NOT NULL,
    value_text text,
    value_json jsonb,
    source varchar(20) NOT NULL DEFAULT 'user',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_procedure_instance_field_values_instance_key UNIQUE (procedure_instance_id, field_key)
);
CREATE INDEX ix_procedure_instance_field_values_tenant_id ON tramites.procedure_instance_field_values(tenant_id);
COMMENT ON COLUMN tramites.procedure_instance_field_values.value_text IS '@pii:medium';

CREATE TABLE tramites.procedure_instance_status_history (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_status_history PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    from_status varchar(20),
    to_status varchar(20) NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changed_by uuid REFERENCES identity.users(id),
    reason varchar(500),
    metadata jsonb NOT NULL DEFAULT '{}'
);
CREATE INDEX ix_procedure_instance_status_history_tenant_id_instance
  ON tramites.procedure_instance_status_history(tenant_id, procedure_instance_id);

ALTER TABLE tramites.procedure_instances ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instances
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_actors ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_actors
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_field_values ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_field_values
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_status_history ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_status_history
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_procedure_instances_row_version ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_row_version BEFORE UPDATE ON tramites.procedure_instances
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_procedure_instances_audit ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instances
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_actors_audit ON tramites.procedure_instance_actors;
CREATE TRIGGER tr_procedure_instance_actors_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_actors
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_audit ON tramites.procedure_instance_field_values;
CREATE TRIGGER tr_procedure_instance_field_values_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_field_values
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_status_history_audit ON tramites.procedure_instance_status_history;
CREATE TRIGGER tr_procedure_instance_status_history_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_status_history
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

