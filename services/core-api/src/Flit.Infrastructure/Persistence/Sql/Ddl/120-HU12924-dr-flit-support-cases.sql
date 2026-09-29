-- HU #12924 (Feature #12915, Épica #12718 DR. FLIT) — casos de soporte y adjuntos previos a la confirmación.
-- Migración: 20260925110000_HU12924_DrFlitSupportCases · ADR-0060 (Propuesto) §7.2 y §9.
--
-- Qué crea:
--   1. dr_flit.support_cases: un registro por intento de radicar un caso desde el chat. Se escribe en
--      'pending' ANTES de llamar a Azure DevOps y pasa a 'created' (con ado_work_item_id) o 'failed' (con
--      last_error, sin PII). Guarda lo que se envió a ADO: soporte lo necesita para triage y reconciliación.
--      Los datos de contacto SÍ se persisten (con @pii documentado): la regla "sin PII" es de los logs, no
--      de esta tabla de negocio, que retiene lo mismo que ya quedó en el work item.
--   2. dr_flit.support_case_attachments: adjuntos subidos ANTES de confirmar (el usuario los sube mientras
--      llena el formulario). Nacen sin caso (support_case_id NULL) y con expires_at = subida + 24 h; al
--      confirmar se vinculan al caso. Los que vencen sin confirmarse se purgan (fila y binario).
--
-- Tablas de negocio: soft delete (A6), trg_row_version y trg_audit_log (A16), RLS tenant_isolation sin FORCE
-- (la app conecta como owner y filtra por tenant_id explícito). Idempotente y reversible (Down en la migración).

CREATE SCHEMA IF NOT EXISTS dr_flit;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. dr_flit.support_cases
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS dr_flit.support_cases (
    id                         uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id                  uuid         NOT NULL,
    created_by_user_id         uuid         NOT NULL,
    status                     varchar(10)  NOT NULL DEFAULT 'pending',
    ado_project                varchar(100) NOT NULL,
    ado_work_item_id           integer      NULL,
    title                      varchar(200) NOT NULL,
    environment                varchar(3)   NOT NULL,
    affected_module            varchar(100) NULL,
    priority                   varchar(5)   NOT NULL,
    incidence                  varchar(10)  NOT NULL,
    attachment_count           integer      NOT NULL DEFAULT 0,
    attachment_upload_failures integer      NOT NULL DEFAULT 0,
    requester_name             varchar(200) NOT NULL,
    requester_email            varchar(320) NOT NULL,
    requester_phone            varchar(30)  NULL,
    requester_company          varchar(200) NULL,
    problem_detail             text         NOT NULL,
    expected_result            text         NOT NULL,
    last_error                 varchar(100) NULL,
    created_at                 timestamptz  NOT NULL DEFAULT now(),
    created_by                 uuid         NULL,
    updated_at                 timestamptz  NOT NULL DEFAULT now(),
    updated_by                 uuid         NULL,
    deleted_at                 timestamptz  NULL,
    deleted_by                 uuid         NULL,
    row_version                bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_support_cases PRIMARY KEY (id),
    CONSTRAINT fk_support_cases_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_support_cases_users FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT ck_support_cases_status CHECK (status IN ('pending', 'created', 'failed')),
    CONSTRAINT ck_support_cases_environment CHECK (environment IN ('DEV', 'QA', 'PDN')),
    CONSTRAINT ck_support_cases_priority CHECK (priority IN ('Alta', 'Media', 'Baja')),
    CONSTRAINT ck_support_cases_incidence CHECK (incidence IN ('una_vez', 'a_veces', 'siempre')),
    CONSTRAINT ck_support_cases_created_requires_work_item CHECK (status <> 'created' OR ado_work_item_id IS NOT NULL),
    CONSTRAINT ck_support_cases_failed_requires_error CHECK (status <> 'failed' OR last_error IS NOT NULL),
    CONSTRAINT ck_support_cases_attachment_counts CHECK (attachment_count >= 0 AND attachment_upload_failures >= 0)
);

-- Cobertura de las FK (A9) · cola de fallidos para una futura reconciliación (fuera de v1, §7.2).
CREATE INDEX IF NOT EXISTS ix_support_cases_tenant_id ON dr_flit.support_cases (tenant_id);
CREATE INDEX IF NOT EXISTS ix_support_cases_created_by_user_id ON dr_flit.support_cases (created_by_user_id);
CREATE INDEX IF NOT EXISTS ix_support_cases_failed ON dr_flit.support_cases (created_at)
    WHERE status = 'failed' AND deleted_at IS NULL;

COMMENT ON TABLE dr_flit.support_cases IS
    'ADR-0060 (HU #12924/#12925) · Intentos de radicar un caso de soporte desde DR. FLIT como Bug en Azure DevOps. pending → created | failed. Sin worker de reconciliación en v1: el usuario reintenta desde la UI.';
COMMENT ON COLUMN dr_flit.support_cases.status IS
    'pending: fila escrita antes de llamar a ADO · created: Bug creado (ado_work_item_id) · failed: ADO no respondió tras el reintento (last_error).';
COMMENT ON COLUMN dr_flit.support_cases.ado_work_item_id IS 'Id del Bug en el proyecto de soporte. Es el número de caso que ve el usuario.';
COMMENT ON COLUMN dr_flit.support_cases.environment IS 'DR_FLIT_DEPLOY_ENVIRONMENT del backend (nunca ASPNETCORE_ENVIRONMENT). Default DEV.';
COMMENT ON COLUMN dr_flit.support_cases.affected_module IS 'Módulo ya resuelto contra el allow-list configurado (o su default).';
COMMENT ON COLUMN dr_flit.support_cases.last_error IS 'Código del fallo del proveedor (http_502, timeout, not_configured…). Nunca PII ni cuerpos de respuesta.';
COMMENT ON COLUMN dr_flit.support_cases.requester_name IS '@pii:medium';
COMMENT ON COLUMN dr_flit.support_cases.requester_email IS '@pii:high';
COMMENT ON COLUMN dr_flit.support_cases.requester_phone IS '@pii:high';
COMMENT ON COLUMN dr_flit.support_cases.requester_company IS '@pii:low';
COMMENT ON COLUMN dr_flit.support_cases.problem_detail IS '@pii:medium — texto libre del usuario.';
COMMENT ON COLUMN dr_flit.support_cases.expected_result IS '@pii:low — texto libre del usuario.';

DROP TRIGGER IF EXISTS tr_support_cases_row_version ON dr_flit.support_cases;
CREATE TRIGGER tr_support_cases_row_version BEFORE UPDATE ON dr_flit.support_cases
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_support_cases_audit ON dr_flit.support_cases;
CREATE TRIGGER tr_support_cases_audit AFTER INSERT OR UPDATE OR DELETE ON dr_flit.support_cases
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE dr_flit.support_cases ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON dr_flit.support_cases;
CREATE POLICY tenant_isolation ON dr_flit.support_cases
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. dr_flit.support_case_attachments
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS dr_flit.support_case_attachments (
    id                  uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id           uuid         NOT NULL,
    uploaded_by_user_id uuid         NOT NULL,
    support_case_id     uuid         NULL,
    storage_path        varchar(500) NOT NULL,
    filename            varchar(255) NOT NULL,
    content_type        varchar(100) NOT NULL,
    size_bytes          bigint       NOT NULL,
    sha256              char(64)     NOT NULL,
    expires_at          timestamptz  NOT NULL,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    created_by          uuid         NULL,
    updated_at          timestamptz  NOT NULL DEFAULT now(),
    updated_by          uuid         NULL,
    deleted_at          timestamptz  NULL,
    deleted_by          uuid         NULL,
    row_version         bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_support_case_attachments PRIMARY KEY (id),
    CONSTRAINT fk_support_case_attachments_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_support_case_attachments_users FOREIGN KEY (uploaded_by_user_id)
        REFERENCES identity.users (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_support_case_attachments_support_cases FOREIGN KEY (support_case_id)
        REFERENCES dr_flit.support_cases (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT ck_support_case_attachments_size CHECK (size_bytes > 0),
    CONSTRAINT ck_support_case_attachments_sha256 CHECK (sha256 ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_support_case_attachments_tenant_id ON dr_flit.support_case_attachments (tenant_id);
CREATE INDEX IF NOT EXISTS ix_support_case_attachments_uploaded_by_user_id ON dr_flit.support_case_attachments (uploaded_by_user_id);
CREATE INDEX IF NOT EXISTS ix_support_case_attachments_support_case_id ON dr_flit.support_case_attachments (support_case_id);
-- Purga de los que vencieron sin confirmarse en un caso.
CREATE INDEX IF NOT EXISTS ix_support_case_attachments_pending_expiry ON dr_flit.support_case_attachments (expires_at)
    WHERE support_case_id IS NULL AND deleted_at IS NULL;

COMMENT ON TABLE dr_flit.support_case_attachments IS
    'ADR-0060 (HU #12924) · Adjuntos subidos antes de confirmar un caso de DR. FLIT. Binario en el almacenamiento de adjuntos (etiqueta dr-flit-support). Sin caso y vencidos (expires_at) se purgan.';
COMMENT ON COLUMN dr_flit.support_case_attachments.support_case_id IS 'NULL hasta que el usuario confirma el caso; entonces se vincula.';
COMMENT ON COLUMN dr_flit.support_case_attachments.storage_path IS 'Id opaco del almacenamiento (file-manager). No es una URL pública.';
COMMENT ON COLUMN dr_flit.support_case_attachments.filename IS '@pii:low — nombre original del archivo.';
COMMENT ON COLUMN dr_flit.support_case_attachments.expires_at IS 'Subida + TTL configurado (DrFlit:SupportCase:AttachmentTtlHours, default 24 h).';

DROP TRIGGER IF EXISTS tr_support_case_attachments_row_version ON dr_flit.support_case_attachments;
CREATE TRIGGER tr_support_case_attachments_row_version BEFORE UPDATE ON dr_flit.support_case_attachments
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_support_case_attachments_audit ON dr_flit.support_case_attachments;
CREATE TRIGGER tr_support_case_attachments_audit AFTER INSERT OR UPDATE OR DELETE ON dr_flit.support_case_attachments
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE dr_flit.support_case_attachments ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON dr_flit.support_case_attachments;
CREATE POLICY tenant_isolation ON dr_flit.support_case_attachments
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );
