-- =============================================================================
-- core-ict — Traducción de cada documento adjunto del catálogo ICT al tipo de documento
-- del trámite en FLIT 2.0. Portado de
-- ScriptFlit/20250539164000_CREATE_TABLE_external_integration_attachment_association.sql.
--
-- CAMBIO DE FONDO RESPECTO A v1: en v1 esta tabla guardaba el NOMBRE DE COLUMNA destino
-- (idAttachedImprints, idAttachedPaymentReceipt, ...) de las tres tablas master separadas
-- (vehicle_registration_master / vehicle_transfer_master / vehicle_otherservice_master).
-- En v2 esas tablas no existen: los adjuntos viven en tramites.procedure_instance_attachments
-- con una columna `tipo` cuyo vocabulario lo define el checklist por tipología
-- (Flit.Tramites.Domain/Tramites/Catalog/TramiteTipologiaCatalog.cs → ChecklistItem.DocTipo).
-- Por eso aquí se guarda el CÓDIGO DocTipo destino, no un nombre de columna.
--
-- Vocabulario v2 disponible hoy:
--   matrícula inicial : factura, aduana, impronta, soat, certificado_ambiental,
--                       declaracion_aduana, acta_remate, oficio_judicial, otro
--   traspaso          : compraventa, impronta, soat, rtm, paz_salvo, cedulas, cert_tradicion
--
-- ESTADO DEL SEED (decisión de negocio 2026-07-23/24, ampliado 2026-07-30): mapeados los documentos
-- con equivalente en el checklist v2 (improntas, paz y salvo, factura, aduana, cédulas comprador/
-- vendedor, otros) y clasificados con tipo "loose" los de leasing (Contrato LEASING, Declaración cía
-- arrendadora), Certificado CEPD, poderes de apoderado (comprador/vendedor) y Blindaje — así los 13
-- documentos del contrato quedan RELACIONADOS. Los "loose" (certificado_cepd, poder_comprador,
-- poder_vendedor, blindaje, contrato_leasing, declaracion_arrendadora) aún no son ítems del checklist
-- de la plataforma: clasifican el adjunto (en vez de 'otro') pero no auto-satisfacen un ítem. Las
-- familias (matrícula/traspaso/otros) se derivan de external_integration_configuration_documents (la
-- obligatoriedad del contrato). Nota de convención: los documentos
-- separados de comprador y vendedor NO colapsan en un único 'cedulas' — siguen la convención de la
-- plataforma (comprador = tipo base `cedulas`; vendedor = sufijo `cedulas_vendedor`). Un documento
-- sin fila aquí se registra igual pero queda sin clasificar (queda como 'otro').
-- Idempotente.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.external_integration_attachment_association (
    id                 uuid NOT NULL DEFAULT uuidv7(),
    eiad_id            integer NOT NULL,
    doc_tipo_matricula varchar(40) NOT NULL DEFAULT '',
    doc_tipo_traspaso  varchar(40) NOT NULL DEFAULT '',
    doc_tipo_otros     varchar(40) NOT NULL DEFAULT '',
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz,
    deleted_at         timestamptz,
    CONSTRAINT pk_eiaa PRIMARY KEY (id),
    CONSTRAINT uq_eiaa_document UNIQUE (eiad_id),
    CONSTRAINT fk_eiaa_eiad FOREIGN KEY (eiad_id)
        REFERENCES ict.external_integration_allowed_documents (id) ON DELETE RESTRICT
);

COMMENT ON TABLE ict.external_integration_attachment_association
    IS 'Mapea el documento adjunto ICT (allowed_documents) al DocTipo del checklist de v2, por modalidad.';
COMMENT ON COLUMN ict.external_integration_attachment_association.doc_tipo_matricula
    IS 'DocTipo destino en modalidad matrícula (vacío = sin clasificar).';
COMMENT ON COLUMN ict.external_integration_attachment_association.doc_tipo_traspaso
    IS 'DocTipo destino en modalidad traspaso (vacío = sin clasificar).';
COMMENT ON COLUMN ict.external_integration_attachment_association.doc_tipo_otros
    IS 'DocTipo destino en otros trámites (vacío = sin clasificar).';

-- Seed del mapeo (confirmado con negocio, 2026-07-23). eiad_id = id del documento en la tabla guía
-- del contrato ICT. Vacío ('') = ese documento no aplica/no tiene equivalente en esa modalidad.
--   DocTipos de v2 que existen HOY (TramiteTipologiaCatalog):
--     matrícula: factura, aduana, impronta, soat, certificado_ambiental, declaracion_aduana,
--                acta_remate, oficio_judicial, otro
--     traspaso : compraventa, impronta, soat, rtm, paz_salvo, cedulas, cert_tradicion
--
-- CÉDULAS SEPARADAS POR PARTE — se sigue la MISMA convención que ya usa la plataforma para el
-- certificado de identidad (Flit.Tramites.Application/.../FurCommand.cs:590-593, escrito por el flujo
-- de firma FUR biométrica como Source='system'): el COMPRADOR usa el tipo BASE y las demás partes
-- llevan sufijo _{rol}. Así como certificado_identidad (comprador) / certificado_identidad_vendedor,
-- aquí: cedulas (comprador) / cedulas_vendedor. Ventaja: el doc del comprador cae en el ítem `cedulas`
-- que YA existe en el checklist de traspaso (lo satisface sin cambios); el del vendedor coexiste como
-- documento aparte (igual que el certificado de identidad del vendedor). En matrícula solo hay una
-- parte (el titular), así que va al tipo base `cedulas`.
-- DOCUMENTOS DE LEASING (6, 10) — clasificados ahora que el locatario/leasing entró en alcance.
-- v2 aún no tiene un ítem de checklist para el contrato de leasing ni la declaración de la compañía
-- arrendadora, así que se les asigna un tipo "loose" descriptivo (la columna `tipo` de
-- procedure_instance_attachments es varchar(40) libre, sin CHECK): así el adjunto queda CLASIFICADO
-- (contrato_leasing / declaracion_arrendadora) en vez de caer como 'otro'. Ambos son de matrícula
-- leasing (tipo 2). TODO(ICT-LEASING-CHECKLIST): promover estos tipos a ítems del checklist de
-- matrícula en TramiteTipologiaCatalog cuando negocio los publique.
INSERT INTO ict.external_integration_attachment_association
    (eiad_id, doc_tipo_matricula, doc_tipo_traspaso, doc_tipo_otros) VALUES
    (1,  'impronta',                'impronta',         ''),          -- Improntas (MI/MIL/TR bilateral)
    (2,  '',                        'paz_salvo',        ''),          -- Paz y Salvo (traspasos)
    (3,  'factura',                 '',                 ''),          -- Factura de venta (matrículas)
    (4,  'aduana',                  '',                 ''),          -- Aduana/importación (matrículas) -> aduana
    (5,  'cedulas',                 'cedulas',          ''),          -- Documento Comprador -> tipo BASE (convención comprador)
    (6,  'contrato_leasing',        '',                 ''),          -- Contrato LEASING (matrícula leasing tipo 2) -> tipo loose
    (7,  '',                        'cedulas_vendedor', ''),          -- Documento Vendedor -> sufijo _vendedor (convención)
    (8,  'certificado_cepd',        '',                 ''),          -- Certificado CEPD (matrículas 1/2) -> tipo loose
    (9,  'otro',                    '',                 ''),          -- Otros Anexos
    (10, 'declaracion_arrendadora', '',                 ''),          -- Declaración cía arrendadora (leasing) -> tipo loose
    (11, 'poder_comprador',         'poder_comprador',  ''),          -- Poder Comprador-Apoderado (MI 1 + TR bilateral 3) -> tipo loose
    (12, '',                        'poder_vendedor',   ''),          -- Poder Vendedor-Apoderado (TR bilateral 3) -> tipo loose
    (13, '',                        '',                 'blindaje')   -- Blindaje (otros trámites, tipo 5) -> tipo loose
ON CONFLICT (eiad_id) DO UPDATE SET
    doc_tipo_matricula = EXCLUDED.doc_tipo_matricula,
    doc_tipo_traspaso  = EXCLUDED.doc_tipo_traspaso,
    doc_tipo_otros     = EXCLUDED.doc_tipo_otros,
    updated_at         = now();

-- TODO(ICT-LEASING-CHECKLIST / ICT-DOCS-LOOSE): estos DocTipos son "loose" (clasifican el adjunto pero
-- NO son ítems del checklist de la plataforma, así que no auto-satisfacen un ítem):
--   contrato_leasing, declaracion_arrendadora (leasing, eiad 6/10)
--   certificado_cepd (matrícula, eiad 8)
--   poder_comprador, poder_vendedor (apoderado, eiad 11/12)
--   blindaje (otros trámites, eiad 13)
-- Promover a ítems reales de TramiteTipologiaCatalog cuando negocio los publique en el checklist v2.
-- Nota de consistencia (pendiente de decisión de negocio): configuration_documents habilita eiad 6
-- (Contrato LEASING) también para el traspaso unilateral (tipo 4); aquí sigue clasificado SOLO en
-- matrícula. Si se quiere clasificar también en traspaso, añadir doc_tipo_traspaso='contrato_leasing'.
