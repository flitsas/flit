-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10521 (RF31) — Parámetros documentales por compañía gestora.
--
-- Fija el estado (OCULTO | OBLIGATORIO | OPCIONAL) de un tipo de documento por gestora
-- (equivalente a los switches de v1.0: registrationRequiresAttachedCepdField, etc.).
-- Aditivo: sin filas, el checklist queda con su comportamiento base (sin regresión).
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.company_document_params (
    id uuid NOT NULL DEFAULT uuidv7(),
    tenant_id uuid NOT NULL,
    document_type_code varchar(50) NOT NULL,
    state varchar(20) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT pk_company_document_params PRIMARY KEY (id),
    CONSTRAINT uq_company_document_params UNIQUE (tenant_id, document_type_code),
    CONSTRAINT ck_company_document_params_state
        CHECK (state IN ('OCULTO', 'OBLIGATORIO', 'OPCIONAL'))
);

CREATE INDEX IF NOT EXISTS ix_company_document_params_tenant
    ON admin.company_document_params (tenant_id);
