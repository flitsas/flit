-- =============================================================================
-- core-ict — Tablas del pipeline de validación (HU3): histórico de estados, catálogo de
-- estados, secuencia de consultas a fuentes, consultas/respuestas y cola de webhooks.
-- Conserva nombres v1 para portar los stored procedures con mínima fricción.
-- =============================================================================

-- Catálogo de estados internos (1-14). Global (sin RLS).
CREATE TABLE IF NOT EXISTS ict.external_integration_parameter_process_status (
    id               smallint NOT NULL,
    description      varchar(100) NOT NULL,
    name_description varchar(100) NOT NULL,
    CONSTRAINT pk_eipps PRIMARY KEY (id)
);
INSERT INTO ict.external_integration_parameter_process_status (id, description, name_description) VALUES
    (1,'Registered','REGISTRADO'), (2,'InValidation','EN VALIDACION'), (3,'Processed','PROCESADO'),
    (4,'WithNews','CON NOVEDADES'), (5,'Draft','BORRADOR'), (6,'Aborted','ANULADO')
ON CONFLICT (id) DO NOTHING;

-- Histórico de estados por pre-trámite (RLS por tenant).
CREATE TABLE IF NOT EXISTS ict.external_integration_process_status (
    id                                  uuid NOT NULL DEFAULT uuidv7(),
    id_eimas                            uuid NOT NULL,
    tenant_id                           uuid NOT NULL,
    id_parprosta                        smallint NOT NULL,
    message_validation                  varchar(500) NOT NULL DEFAULT '',
    status_process_status               smallint NOT NULL DEFAULT 0,
    status_process_registrationdate     timestamptz NOT NULL DEFAULT now(),
    status_process_userregistered       varchar(200) NOT NULL DEFAULT '',
    status_process_mail_userregistered  varchar(100) NOT NULL DEFAULT '',
    status_process_company_registered   varchar(20) NOT NULL DEFAULT '',
    status_process_role_userregistered  varchar(50) NOT NULL DEFAULT 'integration',
    created_at                          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_eips PRIMARY KEY (id),
    CONSTRAINT fk_eips_master FOREIGN KEY (id_eimas)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE,
    CONSTRAINT fk_eips_param FOREIGN KEY (id_parprosta)
        REFERENCES ict.external_integration_parameter_process_status (id)
);
CREATE INDEX IF NOT EXISTS ix_eips_master ON ict.external_integration_process_status (id_eimas);
ALTER TABLE ict.external_integration_process_status ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_process_status;
CREATE POLICY tenant_isolation ON ict.external_integration_process_status
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Configuración de qué consultas correr por tipo de trámite/actor (global).
CREATE TABLE IF NOT EXISTS ict.external_integration_sequence (
    id               uuid NOT NULL DEFAULT uuidv7(),
    sequence_type    smallint NOT NULL,      -- 0=vehiculo, 1=actor
    transaction_type smallint NOT NULL,
    actor_type       varchar(10),
    document_type    varchar(30),
    query_type       varchar(20) NOT NULL,   -- VEHICLE|VIN|RNMC|DRIVER
    is_active        boolean NOT NULL DEFAULT true,
    CONSTRAINT pk_eiseq PRIMARY KEY (id)
);

-- Consultas a fuentes externas construidas por el SP externo (RLS por tenant).
CREATE TABLE IF NOT EXISTS ict.external_integration_source_query (
    id                  uuid NOT NULL DEFAULT uuidv7(),
    eim_id              uuid NOT NULL,
    tenant_id           uuid NOT NULL,
    eia_id              uuid,
    eis_id              uuid,
    actor_level         varchar(5) NOT NULL DEFAULT 'MAIN',  -- VEHI|MAIN|LERE|ASSI
    query_type          varchar(20) NOT NULL,
    document_type       varchar(5) NOT NULL DEFAULT '',
    document_number     varchar(20) NOT NULL DEFAULT '',
    plate_complete      varchar(15) NOT NULL DEFAULT '',
    vehicle_vin         varchar(18) NOT NULL DEFAULT '',
    rnmc_date_expedition varchar(15) NOT NULL DEFAULT '',
    is_data_queried     boolean NOT NULL DEFAULT false,
    is_data_valid       boolean NOT NULL DEFAULT false,
    attempts            smallint NOT NULL DEFAULT 0,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,
    CONSTRAINT pk_eisq PRIMARY KEY (id),
    CONSTRAINT fk_eisq_master FOREIGN KEY (eim_id)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_eisq_master ON ict.external_integration_source_query (eim_id);
CREATE INDEX IF NOT EXISTS ix_eisq_pending ON ict.external_integration_source_query (is_data_queried) WHERE is_data_queried = false;
ALTER TABLE ict.external_integration_source_query ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_source_query;
CREATE POLICY tenant_isolation ON ict.external_integration_source_query
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Respuestas crudas de fuentes externas (RLS por tenant).
CREATE TABLE IF NOT EXISTS ict.external_integration_source_response (
    id             uuid NOT NULL DEFAULT uuidv7(),
    eisq_id        uuid NOT NULL,
    tenant_id      uuid NOT NULL,
    query_response jsonb NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_eisr PRIMARY KEY (id),
    CONSTRAINT fk_eisr_query FOREIGN KEY (eisq_id)
        REFERENCES ict.external_integration_source_query (id) ON DELETE CASCADE
);
ALTER TABLE ict.external_integration_source_response ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_source_response;
CREATE POLICY tenant_isolation ON ict.external_integration_source_response
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Cola de webhooks (outbox) al gestor (RLS por tenant).
CREATE TABLE IF NOT EXISTS ict.external_integration_webhook_master (
    id                       uuid NOT NULL DEFAULT uuidv7(),
    id_transaction           uuid NOT NULL,
    tenant_id                uuid NOT NULL,
    manager_id_transaction   varchar(20) NOT NULL DEFAULT '',
    transaction_type         integer NOT NULL,
    status_validation        smallint NOT NULL,
    message_validation       text NOT NULL DEFAULT '',
    ict_estado               varchar(30) NOT NULL DEFAULT '',
    payload                  jsonb,
    target_url               varchar(255) NOT NULL DEFAULT '',
    is_notified              boolean NOT NULL DEFAULT false,
    attempts                 smallint NOT NULL DEFAULT 0,
    next_attempt_at          timestamptz NOT NULL DEFAULT now(),
    date_notified            timestamptz,
    response_ok              boolean NOT NULL DEFAULT false,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz,
    CONSTRAINT pk_eiwm PRIMARY KEY (id),
    CONSTRAINT fk_eiwm_master FOREIGN KEY (id_transaction)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_eiwm_pending
    ON ict.external_integration_webhook_master (is_notified, next_attempt_at) WHERE is_notified = false;
ALTER TABLE ict.external_integration_webhook_master ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_webhook_master;
CREATE POLICY tenant_isolation ON ict.external_integration_webhook_master
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
