-- Impuesto vehicular fuente real (registro diferido: .claude/state/pending-work-items/impuesto-fuente-real.md).
-- Migración: 20261120000000_ImpuestoTenantProductCredentials · ADR-0092 (Propuesto).
-- Credencial de un producto para una empresa (v1: solo diagnostico/impuesto_api_key), cifrada con Data Protection
-- en core-identity (propósito Flit.Platform.TenantProductCredential.v1 + tenant + código). Excepciones documentadas en ADR-0092:
--   A6  sin soft delete: «Borrar» elimina la fila; un secreto revocado no se retiene.
--   A16 sin trg_audit_log: copiaría el texto cifrado a la auditoría técnica. La auditoría legible la escribe el repositorio en
--       admin.tenant_config_audit_logs (EntityName=TenantProductCredential) sin valor ni huella.
CREATE TABLE IF NOT EXISTS platform.tenant_product_credentials (
    id               uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id        uuid         NOT NULL,
    product_code     varchar(40)  NOT NULL,
    credential_code  varchar(40)  NOT NULL,
    secret_protected text         NOT NULL,
    created_at       timestamptz  NOT NULL DEFAULT now(),
    created_by       uuid         NULL,
    updated_at       timestamptz  NOT NULL DEFAULT now(),
    updated_by       uuid         NULL,
    row_version      bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_tenant_product_credentials PRIMARY KEY (id),
    CONSTRAINT uq_tenant_product_credentials_tenant_product_credential UNIQUE (tenant_id, product_code, credential_code),
    CONSTRAINT fk_tenant_product_credentials_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_tenant_product_credentials_products FOREIGN KEY (product_code)
        REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_tenant_product_credentials_code
        CHECK ((product_code, credential_code) IN (('diagnostico', 'impuesto_api_key'))),
    CONSTRAINT ck_tenant_product_credentials_secret_length CHECK (length(secret_protected) BETWEEN 16 AND 4000)
);

CREATE INDEX IF NOT EXISTS ix_tenant_product_credentials_product_code
    ON platform.tenant_product_credentials (product_code);

COMMENT ON TABLE platform.tenant_product_credentials IS
    'ADR-0092 · Credencial de un producto por empresa (v1: diagnostico/impuesto_api_key). Solo core-identity la lee y la escribe. La UI solo ve configurada/no configurada.';
COMMENT ON COLUMN platform.tenant_product_credentials.secret_protected IS
    '@secret @pii:none · Valor cifrado con Data Protection (propósito Flit.Platform.TenantProductCredential.v1, subpropósitos tenant y código). Nunca en DTOs, logs, bus, outbox ni auditoría.';
COMMENT ON COLUMN platform.tenant_product_credentials.row_version IS
    'Token de concurrencia (public.trg_row_version). core-consultas la usa como versión de su caché.';

DROP TRIGGER IF EXISTS tr_tenant_product_credentials_row_version ON platform.tenant_product_credentials;
CREATE TRIGGER tr_tenant_product_credentials_row_version BEFORE UPDATE ON platform.tenant_product_credentials
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

ALTER TABLE platform.tenant_product_credentials ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON platform.tenant_product_credentials;
CREATE POLICY tenant_isolation ON platform.tenant_product_credentials
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- Down (en la migración): DROP TABLE IF EXISTS platform.tenant_product_credentials;
