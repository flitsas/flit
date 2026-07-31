-- HU #10900 — Representantes legales por compañía + escrituras (ADR-0033, plan §4/§5.1).
-- Directorio maestro TENANT-SCOPED: la compañía representada (dimensión por NIT), su
-- representante legal (par compañía+representante), los tipos de trámite que puede firmar y las
-- escrituras (PDF) con vigencia y multi-compañía. Aislado por tenant con RLS
-- (app.current_tenant_id), mismo patrón que el baúl de firmas (32-HU10642-signature-vault.sql).
-- La firma del representante NO se duplica aquí: se REFERENCIA su fila del baúl
-- (signature_vault_id) o la validación de identidad vigente (identity_validation_ref).
-- document_number y NITs son PII (Ley 1581): COMMENT @pii + RLS + auditoría (triggers estándar).
-- DDL IDEMPOTENTE (CREATE ... IF NOT EXISTS + guardas para índices/políticas/triggers): puede
-- re-aplicarse sin efecto sobre una base ya migrada.

-- ── 1. admin.represented_companies ─────────────────────────────────────────────
-- Dimensión de la compañía representada (por NIT). La comparten representantes y escrituras.
CREATE TABLE IF NOT EXISTS admin.represented_companies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_represented_companies PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    document_type varchar(10) NOT NULL DEFAULT 'NIT'
        CONSTRAINT ck_represented_companies_document_type CHECK (document_type = 'NIT'),
    document_number varchar(20) NOT NULL,
    name varchar(200) NOT NULL,
    email varchar(200),
    address varchar(300),
    city varchar(120),
    phone varchar(40),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Dimensión única por (tenant, NIT): base del upsert por NIT y del multi-select de escrituras.
CREATE UNIQUE INDEX IF NOT EXISTS uq_represented_companies_tenant_document
  ON admin.represented_companies(tenant_id, document_number);

COMMENT ON COLUMN admin.represented_companies.document_number IS '@pii:medium';

ALTER TABLE admin.represented_companies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.represented_companies;
CREATE POLICY tenant_isolation ON admin.represented_companies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_represented_companies_row_version ON admin.represented_companies;
CREATE TRIGGER tr_represented_companies_row_version BEFORE UPDATE ON admin.represented_companies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_represented_companies_audit ON admin.represented_companies;
CREATE TRIGGER tr_represented_companies_audit AFTER INSERT OR UPDATE OR DELETE ON admin.represented_companies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ── 2. admin.company_legal_representatives ─────────────────────────────────────
-- El representante legal (registro = par compañía representada + representante). Vincula su firma
-- del baúl (nullable, ON DELETE SET NULL) o su validación de identidad vigente (uuid, nullable).
CREATE TABLE IF NOT EXISTS admin.company_legal_representatives (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_legal_representatives PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    represented_company_id uuid NOT NULL
        CONSTRAINT fk_company_legal_representatives_represented_companies
        REFERENCES admin.represented_companies(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    document_type varchar(10) NOT NULL,
    document_number varchar(20) NOT NULL,
    first_last_name varchar(120) NOT NULL,
    second_last_name varchar(120),
    name varchar(200) NOT NULL,
    email varchar(200),
    address varchar(300),
    city varchar(120),
    phone varchar(40),
    signature_vault_id uuid
        CONSTRAINT fk_company_legal_representatives_signature_vault
        REFERENCES admin.signature_vault(id) ON DELETE SET NULL ON UPDATE CASCADE,
    identity_validation_ref uuid,
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Unicidad del par (compañía, representante) POR TENANT (ADR-0033 D6): no exclusividad global.
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_legal_representatives_tenant_company_document
  ON admin.company_legal_representatives(tenant_id, represented_company_id, document_number);
-- Índices de FK (checklist A9) + búsqueda por documento (precarga/lookup por NIT del wizard).
CREATE INDEX IF NOT EXISTS ix_company_legal_representatives_represented_company_id
  ON admin.company_legal_representatives(represented_company_id);
CREATE INDEX IF NOT EXISTS ix_company_legal_representatives_signature_vault_id
  ON admin.company_legal_representatives(signature_vault_id);
CREATE INDEX IF NOT EXISTS ix_company_legal_representatives_tenant_document
  ON admin.company_legal_representatives(tenant_id, document_type, document_number);

COMMENT ON COLUMN admin.company_legal_representatives.document_number IS '@pii:high';

ALTER TABLE admin.company_legal_representatives ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_legal_representatives;
CREATE POLICY tenant_isolation ON admin.company_legal_representatives
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_company_legal_representatives_row_version ON admin.company_legal_representatives;
CREATE TRIGGER tr_company_legal_representatives_row_version BEFORE UPDATE ON admin.company_legal_representatives
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_company_legal_representatives_audit ON admin.company_legal_representatives;
CREATE TRIGGER tr_company_legal_representatives_audit AFTER INSERT OR UPDATE OR DELETE ON admin.company_legal_representatives
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ── 3. admin.company_legal_representative_procedure_types ───────────────────────
-- Puente M:N: qué tipos de trámite puede firmar el representante. Sin tenant_id propio (ADR-0033
-- §4): el aislamiento por tenant se hereda del representante padre (tenant_isolation vía EXISTS).
CREATE TABLE IF NOT EXISTS admin.company_legal_representative_procedure_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_legal_representative_procedure_types PRIMARY KEY (id),
    representative_id uuid NOT NULL
        CONSTRAINT fk_clr_procedure_types_representative
        REFERENCES admin.company_legal_representatives(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL
        CONSTRAINT fk_clr_procedure_types_procedure_type
        REFERENCES tramites.procedure_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_clr_procedure_types_representative_type
  ON admin.company_legal_representative_procedure_types(representative_id, procedure_type_id);
CREATE INDEX IF NOT EXISTS ix_clr_procedure_types_procedure_type_id
  ON admin.company_legal_representative_procedure_types(procedure_type_id);

-- RLS tenant_isolation transitiva: el puente solo es visible si su representante padre pertenece al
-- tenant activo. Aplica también en WITH CHECK (INSERT/UPDATE), evitando enlazar tipos a un
-- representante de otro tenant.
ALTER TABLE admin.company_legal_representative_procedure_types ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_legal_representative_procedure_types;
CREATE POLICY tenant_isolation ON admin.company_legal_representative_procedure_types
  USING (EXISTS (
    SELECT 1 FROM admin.company_legal_representatives r
    WHERE r.id = representative_id
      AND r.tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid));

-- ── 4. admin.company_deeds ─────────────────────────────────────────────────────
-- Escrituras (PDF) con descripción, vigencia [desde, hasta] y baja lógica (is_active). El PDF vive
-- en storage (storage_path/storage_sha256), nunca en la fila.
CREATE TABLE IF NOT EXISTS admin.company_deeds (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_deeds PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    description varchar(500) NOT NULL,
    storage_path varchar(1000) NOT NULL,
    storage_sha256 varchar(64) NOT NULL,
    vigencia_desde date NOT NULL,
    vigencia_hasta date NOT NULL,
    CONSTRAINT ck_company_deeds_vigencia CHECK (vigencia_hasta >= vigencia_desde),
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Consumo (collapse paso 1): escrituras activas y vigentes del tenant.
CREATE INDEX IF NOT EXISTS ix_company_deeds_tenant_active_vigencia
  ON admin.company_deeds(tenant_id, is_active, vigencia_hasta);

ALTER TABLE admin.company_deeds ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_deeds;
CREATE POLICY tenant_isolation ON admin.company_deeds
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_company_deeds_row_version ON admin.company_deeds;
CREATE TRIGGER tr_company_deeds_row_version BEFORE UPDATE ON admin.company_deeds
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_company_deeds_audit ON admin.company_deeds;
CREATE TRIGGER tr_company_deeds_audit AFTER INSERT OR UPDATE OR DELETE ON admin.company_deeds
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ── 5. admin.company_deed_companies ────────────────────────────────────────────
-- Puente M:N escritura ↔ compañía representada. Sin tenant_id propio: aislamiento heredado de la
-- escritura padre (tenant_isolation vía EXISTS), igual que el puente de tipos de trámite.
CREATE TABLE IF NOT EXISTS admin.company_deed_companies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_deed_companies PRIMARY KEY (id),
    deed_id uuid NOT NULL
        CONSTRAINT fk_company_deed_companies_deed
        REFERENCES admin.company_deeds(id) ON DELETE CASCADE ON UPDATE CASCADE,
    represented_company_id uuid NOT NULL
        CONSTRAINT fk_company_deed_companies_represented_company
        REFERENCES admin.represented_companies(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_company_deed_companies_deed_company
  ON admin.company_deed_companies(deed_id, represented_company_id);
CREATE INDEX IF NOT EXISTS ix_company_deed_companies_represented_company_id
  ON admin.company_deed_companies(represented_company_id);

ALTER TABLE admin.company_deed_companies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.company_deed_companies;
CREATE POLICY tenant_isolation ON admin.company_deed_companies
  USING (EXISTS (
    SELECT 1 FROM admin.company_deeds d
    WHERE d.id = deed_id
      AND d.tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid));
