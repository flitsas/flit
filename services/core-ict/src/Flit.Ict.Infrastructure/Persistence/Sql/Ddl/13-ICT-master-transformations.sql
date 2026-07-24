-- =============================================================================
-- core-ict — Transformaciones aplicadas a un pre-trámite (puente N:M).
-- Es donde aterriza el bloque `more_transaction_transaction_type` del contrato v1:
--   [{ "transactionType": 5, "description": "Azul AguaMarina" }, ...]
-- Portado de ScriptFlit/20250312085812_...:139. Diferencias con v1:
--   · el master es uuid (no bigint), así que la FK y la PK compuesta usan uuid;
--   · lleva tenant_id + RLS, porque cuelga de external_integration_master que sí la tiene
--     (v1 no tenía multi-tenancy a nivel de fila).
-- Idempotente.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.external_integration_master_transformation_type (
    master_id              uuid NOT NULL,
    id_transformation_type integer NOT NULL,
    tenant_id              uuid NOT NULL,
    description            varchar(50) NOT NULL DEFAULT '',
    created_at             timestamptz NOT NULL DEFAULT now(),
    updated_at             timestamptz,
    deleted_at             timestamptz,
    CONSTRAINT pk_eimtt PRIMARY KEY (master_id, id_transformation_type),
    CONSTRAINT fk_eimtt_master FOREIGN KEY (master_id)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE,
    CONSTRAINT fk_eimtt_type FOREIGN KEY (id_transformation_type)
        REFERENCES ict.external_integration_transformation_type (id_transformation_type) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_eimtt_tenant_master
    ON ict.external_integration_master_transformation_type (tenant_id, master_id);

ALTER TABLE ict.external_integration_master_transformation_type ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_master_transformation_type;
CREATE POLICY tenant_isolation ON ict.external_integration_master_transformation_type
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

COMMENT ON COLUMN ict.external_integration_master_transformation_type.id_transformation_type
    IS 'Código RUNT de la transformación (5/9/17), no la PK interna del catálogo.';
COMMENT ON COLUMN ict.external_integration_master_transformation_type.description
    IS 'Valor libre del gestor para la transformación (p.ej. el color aplicado).';
