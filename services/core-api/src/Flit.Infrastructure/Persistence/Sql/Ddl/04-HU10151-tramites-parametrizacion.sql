-- HU #10151 — Motor dinámico parametrización | Feature #10116

CREATE TABLE tramites.procedure_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_types PRIMARY KEY (id),
    code varchar(50) NOT NULL,
    name varchar(150) NOT NULL,
    family varchar(30) NOT NULL,
    description varchar(500),
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_procedure_types_code UNIQUE (code)
);

CREATE TABLE tramites.procedure_entities (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_entities PRIMARY KEY (id),
    code varchar(30) NOT NULL,
    name varchar(100) NOT NULL,
    sort_order smallint NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_procedure_entities_code UNIQUE (code)
);

CREATE TABLE tramites.conformation_rules (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_conformation_rules PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_entity_id uuid NOT NULL REFERENCES tramites.procedure_entities(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    is_active boolean NOT NULL DEFAULT true,
    sort_order smallint NOT NULL DEFAULT 0,
    validation_profile jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_conformation_rules_type_entity UNIQUE (procedure_type_id, procedure_entity_id)
);

CREATE TABLE tramites.procedure_steps (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_steps PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    code varchar(50) NOT NULL,
    title varchar(150) NOT NULL,
    sort_order smallint NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);
CREATE INDEX ix_procedure_steps_procedure_type_id ON tramites.procedure_steps(procedure_type_id);

CREATE TABLE tramites.procedure_sections (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_sections PRIMARY KEY (id),
    procedure_step_id uuid NOT NULL REFERENCES tramites.procedure_steps(id) ON DELETE CASCADE ON UPDATE CASCADE,
    code varchar(50) NOT NULL,
    title varchar(150) NOT NULL,
    sort_order smallint NOT NULL,
    layout varchar(20) NOT NULL DEFAULT 'single',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);
CREATE INDEX ix_procedure_sections_procedure_step_id ON tramites.procedure_sections(procedure_step_id);

CREATE TABLE tramites.form_fields (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_form_fields PRIMARY KEY (id),
    procedure_section_id uuid NOT NULL REFERENCES tramites.procedure_sections(id) ON DELETE CASCADE ON UPDATE CASCADE,
    field_key varchar(80) NOT NULL,
    label varchar(150) NOT NULL,
    field_type varchar(20) NOT NULL,
    is_required boolean NOT NULL DEFAULT false,
    sort_order smallint NOT NULL DEFAULT 0,
    options jsonb,
    validation_schema jsonb NOT NULL DEFAULT '{}',
    default_value text,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_form_fields_section_key UNIQUE (procedure_section_id, field_key)
);

CREATE TABLE tramites.external_data_sources (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_external_data_sources PRIMARY KEY (id),
    code varchar(30) NOT NULL,
    name varchar(100) NOT NULL,
    base_url varchar(500) NOT NULL,
    auth_type varchar(20) NOT NULL DEFAULT 'none',
    timeout_ms integer NOT NULL DEFAULT 5000,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_external_data_sources_code UNIQUE (code)
);

CREATE TABLE tramites.field_api_bindings (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_field_api_bindings PRIMARY KEY (id),
    form_field_id uuid NOT NULL REFERENCES tramites.form_fields(id) ON DELETE CASCADE ON UPDATE CASCADE,
    external_data_source_id uuid NOT NULL REFERENCES tramites.external_data_sources(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    trigger_event varchar(20) NOT NULL,
    request_mapping jsonb NOT NULL DEFAULT '{}',
    response_mapping jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);
CREATE INDEX ix_field_api_bindings_form_field_id ON tramites.field_api_bindings(form_field_id);
-- Triggers negocio (checklist A16)
