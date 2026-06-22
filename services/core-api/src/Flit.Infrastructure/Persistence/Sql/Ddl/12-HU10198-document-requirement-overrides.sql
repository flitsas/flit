-- HU #10198 — Obligatoriedad documental por Organismo de Tránsito | Feature #10138

-- Override de obligatoriedad de un documento a nivel de OT (granular SOLO para OT, no
-- para Cliente). Tres estados: REQUIRED (obligatorio), OPTIONAL (opcional) y
-- NOT_APPLICABLE (no aplica → el documento se oculta de la matriz de ese OT). La ausencia
-- de fila significa "hereda el default del trámite" (procedure_document_requirements).
CREATE TABLE tramites.document_requirement_overrides (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_document_requirement_overrides PRIMARY KEY (id),
    procedure_type_id uuid NOT NULL REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    document_type_id uuid NOT NULL REFERENCES tramites.document_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    requirement_state varchar(20) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_document_requirement_overrides UNIQUE (procedure_type_id, document_type_id, transit_office_id),
    CONSTRAINT ck_document_requirement_overrides_state
        CHECK (requirement_state IN ('REQUIRED', 'OPTIONAL', 'NOT_APPLICABLE'))
);

CREATE INDEX ix_document_requirement_overrides_proc_ot
    ON tramites.document_requirement_overrides (procedure_type_id, transit_office_id);
