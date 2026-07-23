-- =============================================================================
-- core-ict — Catálogos v1 requeridos por los stored procedures (HU3). Globales (sin RLS).
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.external_integration_operation_type (
    id        integer NOT NULL,
    name      varchar(20),
    is_active boolean DEFAULT true,
    CONSTRAINT pk_eiot PRIMARY KEY (id)
);
INSERT INTO ict.external_integration_operation_type (id, name) VALUES
    (1,'Crear'), (2,'Actualizar'), (3,'Eliminar')
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS ict.external_integration_document_type (
    document_type_code varchar(5) NOT NULL,
    name               varchar(50),
    is_active          boolean DEFAULT true,
    CONSTRAINT pk_eidt PRIMARY KEY (document_type_code)
);
INSERT INTO ict.external_integration_document_type (document_type_code, name) VALUES
    ('CC','Cédula de ciudadanía'), ('CE','Cédula de extranjería'), ('NIT','NIT'),
    ('PAS','Pasaporte'), ('TI','Tarjeta de identidad')
ON CONFLICT (document_type_code) DO NOTHING;
