-- =============================================================================
-- core-ict — Adjuntos del pre-trámite (HU2). Documentos permitidos, configuración de
-- obligatorios por tipo de trámite, y adjuntos cargados vía File Manager (solo metadata;
-- el binario vive en S3 del File Manager, referenciado por storage_path).
-- =============================================================================

-- Catálogo de documentos permitidos (global, sin RLS).
CREATE TABLE IF NOT EXISTS ict.external_integration_allowed_documents (
    id          integer NOT NULL,
    name        varchar(255) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz,
    deleted_at  timestamptz,
    CONSTRAINT pk_eiad PRIMARY KEY (id)
);

-- Documentos obligatorios por tipo de trámite (referencia el mapeo por transaction_type).
CREATE TABLE IF NOT EXISTS ict.external_integration_configuration_documents (
    id                        uuid NOT NULL DEFAULT uuidv7(),
    external_transaction_type smallint NOT NULL,
    id_eiad                   integer NOT NULL,
    is_mandatory              boolean NOT NULL DEFAULT true,
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz,
    deleted_at                timestamptz,
    CONSTRAINT pk_eicd PRIMARY KEY (id),
    CONSTRAINT fk_eicd_eiad FOREIGN KEY (id_eiad)
        REFERENCES ict.external_integration_allowed_documents (id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_eicd_type ON ict.external_integration_configuration_documents (external_transaction_type);

-- Adjuntos del pre-trámite (metadata; RLS por tenant_id).
CREATE TABLE IF NOT EXISTS ict.external_integration_transaction_attachments (
    id                   uuid NOT NULL DEFAULT uuidv7(),
    master_id            uuid NOT NULL,
    tenant_id            uuid NOT NULL,
    id_attachment        integer NOT NULL,
    filename             varchar(255) NOT NULL,
    mime_type            varchar(100) NOT NULL DEFAULT 'application/pdf',
    size_bytes           bigint NOT NULL DEFAULT 0,
    sha256               varchar(64) NOT NULL DEFAULT '',
    storage_path         varchar(255) NOT NULL DEFAULT '',
    created_at           timestamptz NOT NULL DEFAULT now(),
    created_by           uuid,
    updated_at           timestamptz,
    updated_by           uuid,
    deleted_at           timestamptz,
    deleted_by           uuid,
    row_version          bigint NOT NULL DEFAULT 0,
    CONSTRAINT pk_eita PRIMARY KEY (id),
    CONSTRAINT fk_eita_master FOREIGN KEY (master_id)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE,
    CONSTRAINT fk_eita_doc FOREIGN KEY (id_attachment)
        REFERENCES ict.external_integration_allowed_documents (id) ON DELETE RESTRICT,
    CONSTRAINT uq_eita_master_sha UNIQUE (master_id, sha256)
);
CREATE INDEX IF NOT EXISTS ix_eita_master ON ict.external_integration_transaction_attachments (master_id);
ALTER TABLE ict.external_integration_transaction_attachments ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_transaction_attachments;
CREATE POLICY tenant_isolation ON ict.external_integration_transaction_attachments
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
CREATE OR REPLACE TRIGGER tr_eita_row_version
    BEFORE UPDATE ON ict.external_integration_transaction_attachments
    FOR EACH ROW EXECUTE FUNCTION ict.set_row_version();
