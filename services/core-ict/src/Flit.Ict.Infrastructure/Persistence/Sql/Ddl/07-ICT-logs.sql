-- =============================================================================
-- core-ict — Logs en Postgres (HU5), reemplazo de DynamoDB v1. Request/response/headers
-- REDACTADOS (nunca crudo). RLS por tenant_id (tenant nullable para logs de auth pre-login).
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.integration_log (
    id             uuid NOT NULL DEFAULT uuidv7(),
    tenant_id      uuid,
    log_type       varchar(20) NOT NULL DEFAULT 'transaction',  -- auth|transaction|webhook|external
    direction      varchar(10) NOT NULL DEFAULT 'inbound',      -- inbound|outbound
    method         varchar(10) NOT NULL DEFAULT '',
    path           text NOT NULL DEFAULT '',
    status_code    integer NOT NULL DEFAULT 0,
    request        jsonb,   -- REDACTADO
    response       jsonb,   -- REDACTADO
    headers        jsonb,   -- authorization/tokens REDACTADOS
    correlation_id uuid,
    duration_ms    integer NOT NULL DEFAULT 0,
    usuario        varchar(200),
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_integration_log PRIMARY KEY (id),
    CONSTRAINT ck_integration_log_type CHECK (log_type IN ('auth', 'transaction', 'webhook', 'external')),
    CONSTRAINT ck_integration_log_dir CHECK (direction IN ('inbound', 'outbound'))
);
CREATE INDEX IF NOT EXISTS ix_integration_log_tenant_date
    ON ict.integration_log (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_integration_log_type_date
    ON ict.integration_log (log_type, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_integration_log_correlation
    ON ict.integration_log (correlation_id) WHERE correlation_id IS NOT NULL;
-- Nota RLS: la tabla admite logs sin tenant (auth pre-login). Se consulta filtrando por tenant en
-- la app (el submódulo frontend usa el JWT de plataforma; superadmin ve todos). No se activa RLS
-- estricta para no ocultar los logs de auth. TODO(ICT-LOG-RLS): política que permita tenant NULL.
COMMENT ON COLUMN ict.integration_log.request IS '@pii:redacted';
COMMENT ON COLUMN ict.integration_log.response IS '@pii:redacted';

-- Timeline sanitizado por pre-trámite (eventos de negocio; retención más larga).
CREATE TABLE IF NOT EXISTS ict.pretramite_events (
    id            uuid NOT NULL DEFAULT uuidv7(),
    master_id     uuid NOT NULL,
    tenant_id     uuid NOT NULL,
    stage         varchar(40) NOT NULL,
    outcome       varchar(40) NOT NULL DEFAULT '',
    detail        jsonb,   -- SANITIZADO
    correlation_id uuid,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_pretramite_events PRIMARY KEY (id),
    CONSTRAINT fk_pretramite_events_master FOREIGN KEY (master_id)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_pretramite_events_master ON ict.pretramite_events (master_id, created_at);
ALTER TABLE ict.pretramite_events ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.pretramite_events;
CREATE POLICY tenant_isolation ON ict.pretramite_events
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
