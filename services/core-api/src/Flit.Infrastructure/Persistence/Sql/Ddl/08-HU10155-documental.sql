-- HU #10155 — Documental | Feature #10138

CREATE TABLE tramites.document_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_document_types PRIMARY KEY (id),
    code varchar(50) NOT NULL,
    name varchar(150) NOT NULL,
    description varchar(500),
    mime_types_allowed jsonb NOT NULL DEFAULT '["application/pdf"]',
    max_size_bytes bigint NOT NULL DEFAULT 10485760,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_document_types_code UNIQUE (code)
);

CREATE TABLE tramites.procedure_document_requirements (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_document_requirements PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    document_type_id uuid NOT NULL REFERENCES tramites.document_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    is_mandatory boolean NOT NULL DEFAULT true,
    default_sort_order smallint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_procedure_document_requirements UNIQUE (procedure_type_id, document_type_id)
);

CREATE TABLE tramites.document_order_overrides (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_document_order_overrides PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    document_type_id uuid NOT NULL REFERENCES tramites.document_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    scope_type varchar(20) NOT NULL,
    scope_ref_id uuid,
    sort_order smallint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_document_order_overrides UNIQUE (procedure_type_id, document_type_id, scope_type, scope_ref_id)
);

CREATE TABLE tramites.procedure_document_snapshots (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_document_snapshots PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL UNIQUE REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    snapshot jsonb NOT NULL,
    snapshot_at timestamptz NOT NULL DEFAULT now(),
    snapshot_by uuid REFERENCES identity.users(id)
);
CREATE INDEX ix_procedure_document_snapshots_tenant_id ON tramites.procedure_document_snapshots(tenant_id);

ALTER TABLE tramites.procedure_document_snapshots ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_document_snapshots
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_procedure_document_snapshots_audit ON tramites.procedure_document_snapshots;
CREATE TRIGGER tr_procedure_document_snapshots_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_document_snapshots
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

