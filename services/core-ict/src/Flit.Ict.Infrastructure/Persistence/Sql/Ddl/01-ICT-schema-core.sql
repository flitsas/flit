-- =============================================================================
-- core-ict — Schema ICT (fundación / HU1)
-- Modelo híbrido: convenciones v2 (uuidv7, RLS por tenant_id, auditoría, triggers)
-- conservando nombres de tabla/columna v1 para portar los stored procedures.
-- Idempotente: se aplica en cada arranque (IctSchemaBootstrapper).
-- Depende de core-api: identity.tenants y la función uuidv7() deben existir (depends_on).
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS ict;
CREATE EXTENSION IF NOT EXISTS citext;

-- Trigger local de row_version (core-ict no depende del trg_row_version de core-api).
CREATE OR REPLACE FUNCTION ict.set_row_version() RETURNS trigger AS $$
BEGIN
    NEW.row_version := COALESCE(OLD.row_version, 0) + 1;
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- -----------------------------------------------------------------------------
-- ict.integration_clients — credenciales del login ICT (SIN RLS: se busca por
-- username antes de conocer el tenant; la seguridad se aplica en la app).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ict.integration_clients (
    id                     uuid NOT NULL DEFAULT uuidv7(),
    tenant_id              uuid NOT NULL,
    username               citext NOT NULL,
    password_hash          varchar(255) NOT NULL,
    previous_password_hash varchar(255),
    password_changed_at    timestamptz,
    must_rotate            boolean NOT NULL DEFAULT false,
    scopes                 jsonb NOT NULL DEFAULT '["ict.transactions.write","ict.status.read"]'::jsonb,
    is_active              boolean NOT NULL DEFAULT true,
    failed_login_attempts  smallint NOT NULL DEFAULT 0,
    locked_until           timestamptz,
    last_login_at          timestamptz,
    created_at             timestamptz NOT NULL DEFAULT now(),
    created_by             uuid,
    updated_at             timestamptz,
    updated_by             uuid,
    deleted_at             timestamptz,
    deleted_by             uuid,
    row_version            bigint NOT NULL DEFAULT 0,
    CONSTRAINT pk_integration_clients PRIMARY KEY (id),
    CONSTRAINT fk_integration_clients_tenant FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_integration_clients_username
    ON ict.integration_clients (username);
COMMENT ON COLUMN ict.integration_clients.password_hash IS '@pii:high';
CREATE OR REPLACE TRIGGER tr_integration_clients_row_version
    BEFORE UPDATE ON ict.integration_clients
    FOR EACH ROW EXECUTE FUNCTION ict.set_row_version();

-- -----------------------------------------------------------------------------
-- ict.procedure_type_mapping — transaction_type (1-16) -> ProcedureType code v2.
-- Catálogo global (sin RLS).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ict.procedure_type_mapping (
    id                        uuid NOT NULL DEFAULT uuidv7(),
    external_transaction_type smallint NOT NULL,
    procedure_type_code       varchar(60) NOT NULL,
    is_published              boolean NOT NULL DEFAULT false,
    description               varchar(200),
    created_at                timestamptz NOT NULL DEFAULT now(),
    created_by                uuid,
    updated_at                timestamptz,
    updated_by                uuid,
    deleted_at                timestamptz,
    deleted_by                uuid,
    row_version               bigint NOT NULL DEFAULT 0,
    CONSTRAINT pk_procedure_type_mapping PRIMARY KEY (id),
    CONSTRAINT uq_procedure_type_mapping_ext UNIQUE (external_transaction_type)
);
CREATE OR REPLACE TRIGGER tr_procedure_type_mapping_row_version
    BEFORE UPDATE ON ict.procedure_type_mapping
    FOR EACH ROW EXECUTE FUNCTION ict.set_row_version();

-- Seed del mapeo: publicados 1/2 y 3/4; el resto stubs (is_published=false).
INSERT INTO ict.procedure_type_mapping (external_transaction_type, procedure_type_code, is_published, description) VALUES
    (1,  'MATRICULA_NUEVA',   true,  'Matrícula inicial'),
    (2,  'MATRICULA_NUEVA',   true,  'Matrícula leasing'),
    (3,  'TRASPASO_STANDARD', true,  'Traspaso'),
    (4,  'TRASPASO_STANDARD', true,  'Traspaso unilateral'),
    (5,  'OTRO_TRAMITE_05',   false, 'Blindaje'),
    (6,  'OTRO_TRAMITE_06',   false, 'Cambio de carrocería'),
    (7,  'OTRO_TRAMITE_07',   false, 'Cambio de color'),
    (8,  'OTRO_TRAMITE_08',   false, 'Cambio de locatario'),
    (9,  'OTRO_TRAMITE_09',   false, 'Conversión de combustible'),
    (10, 'OTRO_TRAMITE_10',   false, 'Duplicado de placa'),
    (11, 'OTRO_TRAMITE_11',   false, 'Duplicado de tarjeta'),
    (12, 'OTRO_TRAMITE_12',   false, 'Inscribir prenda'),
    (13, 'OTRO_TRAMITE_13',   false, 'Levantar prenda'),
    (14, 'OTRO_TRAMITE_14',   false, 'Cancelación de matrícula'),
    (15, 'OTRO_TRAMITE_15',   false, 'Traslado de cuenta'),
    (16, 'OTRO_TRAMITE_16',   false, 'Radicado de cuenta')
ON CONFLICT (external_transaction_type) DO NOTHING;

-- -----------------------------------------------------------------------------
-- ict.external_integration_master — pre-trámite (staging). RLS por tenant_id.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ict.external_integration_master (
    id                                 uuid NOT NULL DEFAULT uuidv7(),
    tenant_id                          uuid NOT NULL,
    company_manager_document           varchar(12) NOT NULL DEFAULT '',
    manager_user                       varchar(50) NOT NULL DEFAULT '',
    manager_mail                       varchar(100) NOT NULL DEFAULT '',
    priority                           boolean NOT NULL DEFAULT false,
    delivery_address                   varchar(150) NOT NULL DEFAULT '',
    manager_id_transaction             varchar(20) NOT NULL DEFAULT '',
    transaction_operation              integer NOT NULL DEFAULT 0,
    transaction_flit                   varchar(20),
    transaction_type                   integer NOT NULL DEFAULT 0,
    plate                              varchar(15) NOT NULL DEFAULT '',
    vin                                varchar(18),
    selling_date                       varchar(10) NOT NULL DEFAULT '',
    selling_price                      numeric(19,2) NOT NULL DEFAULT 0.0,
    traffic_secretary_code             varchar(10) NOT NULL DEFAULT '',
    url_web_hook                       varchar(255) NOT NULL DEFAULT '',
    closed_document                    boolean NOT NULL DEFAULT false,
    process_without_attached_documents boolean NOT NULL DEFAULT false,
    process_status_id                  smallint NOT NULL DEFAULT 1,
    business_validation                smallint NOT NULL DEFAULT 0,
    business_date_validation           timestamptz,
    business_comments_validation       text NOT NULL DEFAULT '',
    external_validation                smallint NOT NULL DEFAULT 0,
    external_date_validation           timestamptz,
    external_comments_validation       text NOT NULL DEFAULT '',
    procedure_instance_id              uuid,
    created_at                         timestamptz NOT NULL DEFAULT now(),
    created_by                         uuid,
    updated_at                         timestamptz,
    updated_by                         uuid,
    deleted_at                         timestamptz,
    deleted_by                         uuid,
    row_version                        bigint NOT NULL DEFAULT 0,
    CONSTRAINT pk_external_integration_master PRIMARY KEY (id),
    CONSTRAINT fk_eim_tenant FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT ck_eim_business_validation CHECK (business_validation = ANY (ARRAY[0,1,2])),
    CONSTRAINT ck_eim_external_validation CHECK (external_validation = ANY (ARRAY[0,1,2,3])),
    CONSTRAINT ck_eim_process_status CHECK (process_status_id = ANY (ARRAY[1,2,3,4,5,6,7,8,9,10,11,12,13,14]))
);
CREATE INDEX IF NOT EXISTS ix_eim_tenant_manager_tx
    ON ict.external_integration_master (tenant_id, manager_id_transaction);
CREATE INDEX IF NOT EXISTS ix_eim_pipeline
    ON ict.external_integration_master (process_status_id, business_validation, external_validation)
    WHERE deleted_at IS NULL;
COMMENT ON COLUMN ict.external_integration_master.company_manager_document IS '@pii:medium';
ALTER TABLE ict.external_integration_master ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_master;
CREATE POLICY tenant_isolation ON ict.external_integration_master
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
CREATE OR REPLACE TRIGGER tr_eim_row_version
    BEFORE UPDATE ON ict.external_integration_master
    FOR EACH ROW EXECUTE FUNCTION ict.set_row_version();

-- -----------------------------------------------------------------------------
-- ict.external_integration_actors — actores del pre-trámite. RLS por tenant_id.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ict.external_integration_actors (
    id                                    uuid NOT NULL DEFAULT uuidv7(),
    master_id                             uuid NOT NULL,
    tenant_id                             uuid NOT NULL,
    actor_type                            varchar(10) NOT NULL DEFAULT '',
    document_type                         varchar(5) NOT NULL DEFAULT '',
    document_number                       varchar(12) NOT NULL DEFAULT '',
    name                                  varchar(100) NOT NULL DEFAULT '',
    first_last_name                       varchar(100) NOT NULL DEFAULT '',
    second_last_name                      varchar(100),
    phone                                 varchar(50) NOT NULL DEFAULT '',
    email                                 varchar(255) NOT NULL DEFAULT '',
    city                                  varchar(30),
    state                                 varchar(22),
    address                               varchar(150),
    expedition_date                       varchar(10),
    legal_representative_document_type    varchar(5),
    legal_representative_document_number  varchar(12),
    legal_representative_name             varchar(100),
    legal_representative_first_last_name  varchar(100),
    legal_representative_second_last_name varchar(100),
    legal_representative_email            varchar(255),
    legal_representative_phone            varchar(50),
    principal_mandante_document_type      varchar(5),
    principal_mandante_document_number    varchar(12),
    principal_mandante_name               varchar(100),
    principal_mandante_first_last_name    varchar(100),
    principal_mandante_second_last_name   varchar(100),
    principal_mandante_email              varchar(255),
    created_at                            timestamptz NOT NULL DEFAULT now(),
    created_by                            uuid,
    updated_at                            timestamptz,
    updated_by                            uuid,
    deleted_at                            timestamptz,
    deleted_by                            uuid,
    row_version                           bigint NOT NULL DEFAULT 0,
    CONSTRAINT pk_external_integration_actors PRIMARY KEY (id),
    CONSTRAINT fk_eia_master FOREIGN KEY (master_id)
        REFERENCES ict.external_integration_master (id) ON DELETE CASCADE,
    CONSTRAINT ck_eia_actor_type CHECK (actor_type IN ('seller', 'buyer', 'lessee'))
);
CREATE INDEX IF NOT EXISTS ix_eia_master ON ict.external_integration_actors (master_id);
COMMENT ON COLUMN ict.external_integration_actors.document_number IS '@pii:high';
COMMENT ON COLUMN ict.external_integration_actors.name IS '@pii:high';
ALTER TABLE ict.external_integration_actors ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON ict.external_integration_actors;
CREATE POLICY tenant_isolation ON ict.external_integration_actors
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
CREATE OR REPLACE TRIGGER tr_eia_row_version
    BEFORE UPDATE ON ict.external_integration_actors
    FOR EACH ROW EXECUTE FUNCTION ict.set_row_version();
