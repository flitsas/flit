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
    ('PAS','Pasaporte'), ('TI','Tarjeta de identidad'),
    ('NUIP','N.U.I.P'), ('CD','Carné diplomático')
ON CONFLICT (document_type_code) DO NOTHING;

-- Catálogo REAL de documentos adjuntos (contrato "Contratos Integraciones", hoja "3 - Attachments",
-- tabla guía v1.8). Reemplaza el rango genérico "Documento N". ON CONFLICT DO UPDATE para pisar el
-- seed placeholder anterior en instalaciones existentes.
INSERT INTO ict.external_integration_allowed_documents (id, name) VALUES
    (1,  'Improntas'),
    (2,  'Paz y Salvo'),
    (3,  'Factura venta'),
    (4,  'Aduana/importación'),
    (5,  'Documento Comprador'),
    (6,  'Contrato LEASING'),
    (7,  'Documento Vendedor'),
    (8,  'Certificado CEPD'),
    (9,  'Otros Anexos'),
    (10, 'Declaración compañía arrendadora'),
    (11, 'Poder Comprador - Apoderado'),
    (12, 'Poder Vendedor - Apoderado'),
    (13, 'Blindaje')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, updated_at = now();

-- Retirar los placeholders sobrantes (id > 13) que NO estén referenciados por un adjunto ni por la matriz.
DELETE FROM ict.external_integration_allowed_documents a
WHERE a.id > 13
  AND NOT EXISTS (SELECT 1 FROM ict.external_integration_transaction_attachments t WHERE t.id_attachment = a.id)
  AND NOT EXISTS (SELECT 1 FROM ict.external_integration_configuration_documents c WHERE c.id_eiad = a.id);

-- Matriz de obligatoriedad documento × tipo de trámite (contrato hoja "3 - Attachments": columnas
-- MI / MI-LEASING / TR-BILATERAL / TR-UNILATERAL / Otro Trámite; 1=Obligatorio, 2=Opcional). El SP de
-- negocio exige los is_mandatory=TRUE (salvo el waiver process_without_attached_documents). Idempotente
-- por (external_transaction_type, id_eiad).
CREATE UNIQUE INDEX IF NOT EXISTS uq_eicd_type_doc
    ON ict.external_integration_configuration_documents (external_transaction_type, id_eiad);

INSERT INTO ict.external_integration_configuration_documents (external_transaction_type, id_eiad, is_mandatory) VALUES
    -- 1 · Matrícula Inicial
    (1, 1, true),  (1, 3, true),  (1, 4, true),  (1, 5, true),  (1, 8, false), (1, 9, false), (1, 11, false),
    -- 2 · Matrícula Inicial Leasing
    (2, 1, true),  (2, 3, true),  (2, 4, true),  (2, 5, true),  (2, 6, true),  (2, 8, false), (2, 9, false),
    -- 3 · Traspaso Bilateral
    (3, 1, true),  (3, 2, true),  (3, 5, true),  (3, 7, true),  (3, 9, false), (3, 11, false),(3, 12, false),
    -- 4 · Traspaso Unilateral
    (4, 2, true),  (4, 5, true),  (4, 6, true),
    -- 5 · Blindaje (columna "Otro Trámite" del contrato — el doc Blindaje es obligatorio).
    --     TODO(ICT-DOCS-OTROS): el contrato trae una sola columna "Otro Trámite" para los tipos 5-16;
    --     validar con negocio la obligatoriedad documental por cada tipo específico.
    (5, 13, true)
ON CONFLICT (external_transaction_type, id_eiad) DO UPDATE SET is_mandatory = EXCLUDED.is_mandatory, updated_at = now();
