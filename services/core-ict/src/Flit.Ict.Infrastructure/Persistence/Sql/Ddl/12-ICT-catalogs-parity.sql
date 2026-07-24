-- =============================================================================
-- core-ict — Catálogos v1 que faltaban para completar las 19 tablas del núcleo ICT.
-- Portados de ScriptFlit/20250312085812_CREATE_TABLES_integracion_terceros_tablas_iniciales.sql.
-- Son GLOBALES (sin tenant_id, sin RLS), igual que 04-ICT-catalogs.sql. Se conservan los ids
-- naturales de v1 porque son los códigos que viajan en el contrato del cliente.
-- Idempotente: CREATE TABLE IF NOT EXISTS + INSERT ... ON CONFLICT DO NOTHING.
-- =============================================================================

-- Tipos de trámite ICT (1..11) con jerarquía de dos niveles: 1=Matrícula, 2=Traspaso, 3=Otros.
-- v1 lo usaba como destino de FK de transaction_type; en v2 la resolución al tipo de trámite de
-- la plataforma la hace ict.procedure_type_mapping — este catálogo aporta el NOMBRE y el PADRE.
CREATE TABLE IF NOT EXISTS ict.external_integration_procedure_type (
    id                  integer NOT NULL,
    name                varchar(50) NOT NULL,
    parent_procedure_id integer,
    is_active           boolean NOT NULL DEFAULT true,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,
    deleted_at          timestamptz,
    CONSTRAINT pk_eipt PRIMARY KEY (id),
    CONSTRAINT fk_eipt_parent FOREIGN KEY (parent_procedure_id)
        REFERENCES ict.external_integration_procedure_type (id) ON DELETE RESTRICT
);
INSERT INTO ict.external_integration_procedure_type (id, name, parent_procedure_id) VALUES
    (1,'Matrícula Inicial',1), (2,'Matrícula Inicial Leasing',1),
    (3,'Traspaso',2), (4,'Traspaso Unilateral',2),
    (5,'Blindaje',3), (6,'Cambio de Carrocería',3), (7,'Cambio de Color',3),
    (8,'Cambio de Locatario',3), (9,'Conversiones de Combustible',3),
    (10,'Duplicado de Placa',3), (11,'Duplicado de Tarjeta',3)
ON CONFLICT (id) DO NOTHING;

-- Qué hacer con la limitación/garantía mobiliaria (prenda). Destino lógico de
-- external_integration_master.limitations_operation_type (contrato: Tabla Tipo Operacion Garantia).
CREATE TABLE IF NOT EXISTS ict.external_integration_guarantee_operation_type (
    id         integer NOT NULL,
    name       varchar(50) NOT NULL,
    is_active  boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    deleted_at timestamptz,
    CONSTRAINT pk_eigot PRIMARY KEY (id)
);
INSERT INTO ict.external_integration_guarantee_operation_type (id, name) VALUES
    (1,'Levantar'), (2,'Registrar'), (3,'Omitir')
ON CONFLICT (id) DO NOTHING;

-- Transformaciones RUNT. OJO: dos identificadores, como en v1 —
--   id                     = PK interna (1,2,3), la que muestra el contrato como "ID"
--   id_transformation_type = CÓDIGO RUNT real (5,9,17), el que viaja en el payload del cliente
--                            (more_transaction_transaction_type[].transactionType) y el que
--                            referencian las FKs.
CREATE TABLE IF NOT EXISTS ict.external_integration_transformation_type (
    id                     integer NOT NULL,
    id_transformation_type integer NOT NULL,
    name                   varchar(50) NOT NULL,
    is_active              boolean NOT NULL DEFAULT true,
    created_at             timestamptz NOT NULL DEFAULT now(),
    updated_at             timestamptz,
    deleted_at             timestamptz,
    CONSTRAINT pk_eitt PRIMARY KEY (id),
    CONSTRAINT uq_eitt_runt_code UNIQUE (id_transformation_type)
);
INSERT INTO ict.external_integration_transformation_type (id, id_transformation_type, name) VALUES
    (1, 5,'Cambio de color'), (2, 9,'Transformación'), (3, 17,'Cambio de carrocería')
ON CONFLICT (id) DO NOTHING;
