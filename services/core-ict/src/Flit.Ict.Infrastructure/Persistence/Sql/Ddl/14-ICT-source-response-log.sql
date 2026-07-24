-- =============================================================================
-- core-ict — Bitácora de CADA llamada a una fuente externa (RUNT/RUES/SIMIT/RNMC/VIDEN).
-- Portado de ScriptFlit/20250414133000_DROP_TABLE_external_integration_source_response_log.sql.
--
-- Diferencia con external_integration_source_response: esa guarda SOLO la respuesta vigente
-- (una por source_query); esta guarda TODAS las llamadas —incluidos reintentos y fallos— con
-- la URL y el request, que es lo que permite auditar y depurar por qué una fuente respondió algo.
--
-- Cambios respecto a v1: id uuid (v1 bigserial), eisq_id uuid, request_url varchar en vez de
-- char(500) (char rellena con espacios), y tenant_id + RLS por coherencia con source_query/
-- source_response (v1 no tenía multi-tenancy por fila). Idempotente.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.external_integration_source_response_log (
    id             uuid NOT NULL DEFAULT uuidv7(),
    eisq_id        uuid NOT NULL,
    tenant_id      uuid NOT NULL,
    request_url    varchar(500) NOT NULL DEFAULT '',
    query_request  jsonb NOT NULL DEFAULT '{}'::jsonb,
    query_response jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz,
    deleted_at     timestamptz,
    CONSTRAINT pk_eisrl PRIMARY KEY (id),
    CONSTRAINT fk_eisrl_query FOREIGN KEY (eisq_id)
        REFERENCES ict.external_integration_source_query (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_eisrl_tenant_query
    ON ict.external_integration_source_response_log (tenant_id, eisq_id, created_at DESC);

ALTER TABLE ict.external_integration_source_response_log ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_source_response_log;
CREATE POLICY tenant_isolation ON ict.external_integration_source_response_log
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

COMMENT ON COLUMN ict.external_integration_source_response_log.query_request
    IS '@pii:medium Request enviado a la fuente externa (puede contener documentos de identidad).';
COMMENT ON COLUMN ict.external_integration_source_response_log.query_response
    IS '@pii:medium Respuesta cruda de la fuente externa.';
