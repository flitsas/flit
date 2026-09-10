-- HU #11313 (Feature #11309) — Documentos personalizados por compañía (ADR-0042). Historial
-- versionado y reversible del mandato / solicitud de trámite virtual que una compañía B2B carga para
-- sustituir el documento que genera FLIT, conservando el mismo tipo documental. Patrón de
-- admin.company_deeds (ADR-0033): metadatos en BD, PDF en storage (nunca en la fila). DDL IDEMPOTENTE
-- (CREATE ... IF NOT EXISTS + guardas de índices/políticas/triggers).
--
-- Único parcial de UNA sola fila activa por (tenant_id, document_type): patrón
-- uq_mandate_signer_companies_active. Único (tenant_id, document_type, version): la versión es
-- incremental y nunca se reutiliza para el mismo tipo. Ninguna versión se borra (restricción 9): el
-- confirm mueve la activa previa a 'historico', jamás la elimina.

CREATE TABLE IF NOT EXISTS admin.company_personalized_documents (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_personalized_documents PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_company_personalized_documents_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    document_type varchar(20) NOT NULL
        CONSTRAINT ck_company_personalized_documents_document_type
        CHECK (document_type IN ('mandato', 'tramite_virtual')),

    version integer NOT NULL CHECK (version > 0),

    status varchar(20) NOT NULL DEFAULT 'pendiente'
        CONSTRAINT ck_company_personalized_documents_status
        CHECK (status IN ('pendiente', 'activo', 'historico', 'rechazado')),

    is_active boolean NOT NULL DEFAULT false,

    filename varchar(500) NOT NULL,
    storage_path varchar(1000) NOT NULL,
    storage_sha256 varchar(64) NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    page_count integer,
    notes varchar(1000),

    activated_at timestamptz,
    activated_by uuid,
    deactivated_at timestamptz,
    deactivated_by uuid,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Una sola fila activa por (tenant, tipo) — el interruptor de la funcionalidad ES esta fila (§8 DT-7
-- del plan técnico): no hay booleano paralelo que pueda contradecirla.
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_personalized_documents_active
  ON admin.company_personalized_documents (tenant_id, document_type)
  WHERE is_active;

-- Versión incremental, nunca reutilizada, por (tenant, tipo).
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_personalized_documents_tenant_type_version
  ON admin.company_personalized_documents (tenant_id, document_type, version);

-- Índice de lectura del pipeline (resolutor de generación, HU #11316): la activa por (tenant, tipo).
CREATE INDEX IF NOT EXISTS ix_company_personalized_documents_tenant_type_active
  ON admin.company_personalized_documents (tenant_id, document_type)
  WHERE is_active;

COMMENT ON TABLE admin.company_personalized_documents IS
  'Historial versionado y reversible del documento personalizado por compañía (HU #11313, ADR-0042). El PDF vive en storage; aquí solo metadatos + hash.';
COMMENT ON COLUMN admin.company_personalized_documents.filename IS '@pii:low — nombre de archivo, puede llevar razón social.';

ALTER TABLE admin.company_personalized_documents ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_personalized_documents;
CREATE POLICY tenant_isolation ON admin.company_personalized_documents
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_company_personalized_documents_row_version ON admin.company_personalized_documents;
CREATE TRIGGER tr_company_personalized_documents_row_version BEFORE UPDATE ON admin.company_personalized_documents
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_company_personalized_documents_audit ON admin.company_personalized_documents;
CREATE TRIGGER tr_company_personalized_documents_audit AFTER INSERT OR UPDATE OR DELETE ON admin.company_personalized_documents
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ────────────────────────────────────────────────────────────────────────────────────────────────
-- tramites.procedure_instance_attachments — traza de qué versión entró al expediente (espejo exacto
-- de source_deed_id, HU #10936). Se materializa aquí, no en la HU #11316, porque es ampliación de
-- columna y no toca el pipeline de generación (Bug de conflicto único del Feature: FurCommand.cs).
-- ────────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_instance_attachments
  ADD COLUMN IF NOT EXISTS source_personalized_document_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_procedure_instance_attachments_personalized_document'
    ) THEN
        ALTER TABLE tramites.procedure_instance_attachments
          ADD CONSTRAINT fk_procedure_instance_attachments_personalized_document
          FOREIGN KEY (source_personalized_document_id)
          REFERENCES admin.company_personalized_documents(id)
          ON DELETE SET NULL ON UPDATE CASCADE;
    END IF;
END $$;

COMMENT ON COLUMN tramites.procedure_instance_attachments.source_personalized_document_id IS
  'HU #11313/#11316 — versión (admin.company_personalized_documents.id) usada cuando el adjunto es un documento personalizado de compañía (source=''company''). Espejo exacto de source_deed_id.';
