-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10520 — Catálogo completo de tipos de documento (paridad FLIT 1.0).
--
-- Siembra en tramites.document_types todos los documentos de la hoja
-- CatalogoDocumentos (Manual FLIT v8.0). Idempotente: ON CONFLICT (code) DO NOTHING
-- (no pisa la parametrización que el admin haya ajustado luego).
--
-- NO-REGRESIÓN: todos los tipos se siembran con la MISMA regla de carga vigente hoy
-- (pdf/jpeg/png/webp, 20 MB), de modo que la validación por tipo (RF08/09/10) produce
-- exactamente el comportamiento global actual hasta que un admin la ajuste por tipo.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('portada', 'Portada', 'Primera hoja del expediente consolidado (autogenerada)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('factura', 'Factura de Venta', 'Factura de venta del vehículo (matrícula inicial)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_ambiental', 'Certificado CEPD', 'Certificado de emisiones por debajo (CEPD)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('aduana', 'Certificado de Aduana / Declaración de Importación', 'Documento aduanero para vehículo importado', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('declaracion_aduana', 'Declaración de Importación', 'Declaración de importación (aduana)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('impronta', 'Improntas', 'Impronta de motor y chasis', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('paz_salvo', 'Paz y Salvo de Impuestos', 'Paz y salvo de impuestos del vehículo', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('paz_salvo_prenda', 'Paz y Salvo de Prenda', 'Paz y salvo / factura de levantamiento de prenda', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('inscripcion_prenda', 'Inscripción / Registro de Prenda', 'Documento de inscripción o registro de prenda', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('doc_identidad_propietario', 'Documento de identificación del propietario', 'Identificación del propietario/comprador (matrícula)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('doc_identidad_comprador', 'Documento de identificación del comprador', 'Identificación del comprador (traspaso)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('doc_identidad_vendedor', 'Documento de identificación del vendedor', 'Identificación del vendedor (traspaso)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('cedulas', 'Documento de identidad (cédula / carta-selfie)', 'Cédula o carta-selfie de identidad', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('poder_tramitador', 'Poder de tramitador (mandante)', 'Poder del vendedor/comprador/propietario al tramitador', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('anexos_generales', 'Documentos de trámite (anexos generales)', 'Anexos generales del trámite', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('factura_carroceria', 'Factura de Carrocería', 'Factura de carrocería (cambio de carrocería)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('compraventa', 'Formato de Compraventa', 'Contrato/formato de compraventa (traspaso)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('fur', 'FUR (Formato Único de Registro)', 'Formato Único de Registro', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('mandato', 'Mandato', 'Documento de mandato', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('tramite_virtual', 'Trámite Virtual', 'Documento de trámite virtual', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('tablas_certificadoras', 'Tablas certificadoras', 'Tablas certificadoras', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_vigencia_soat_rtm', 'Certificado de vigencia SOAT y RTM', 'Certificado de vigencia de SOAT y RTM', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('soat', 'SOAT (vigente)', 'SOAT vigente', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('rtm', 'Revisión Técnico Mecánica (RTM)', 'Revisión técnico-mecánica vigente', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('soat_manual', 'SOAT cargado manualmente', 'SOAT cargado manualmente por el cliente', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('paz_salvo_rnmc', 'Paz y Salvo RNMC', 'Paz y salvo de medidas correctivas (RNMC)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('rues', 'Certificado RUES', 'Certificado RUES (actor NIT)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('contrato_leasing', 'Contrato de Leasing', 'Contrato de leasing (traspaso unilateral)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('declaracion_arrendadora', 'Declaración compañía arrendadora', 'Declaración de la compañía arrendadora (leasing)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('transferencia_dominio', 'Certificado de transferencia de dominio', 'Certificado de transferencia de dominio', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('impronta_validada', 'Impronta validada', 'Certificado / hash de impronta validada', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('firma', 'Firma (baúl de firmas)', 'Firma o validación del baúl de firmas', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('escritura_publica', 'Escritura pública', 'Escritura pública (gestión de escrituras)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('camara_comercio', 'Cámara de Comercio + cédula representante', 'Certificado de cámara de comercio y cédula del representante', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('certificado_superfinanciera', 'Certificado Superintendencia Financiera', 'Certificado de la Superintendencia Financiera', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('liquidacion_impuesto', 'Liquidación / pago impuesto departamental', 'Liquidación o pago del impuesto departamental', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('licencia_transito', 'Licencia de Tránsito', 'Licencia de tránsito (resultado del trámite)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('tarjeta_propiedad', 'Tarjeta de Propiedad', 'Tarjeta de propiedad (resultado del trámite)', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('reporte_rtm_soat', 'Reporte RTM / Reporte SOAT', 'Reportes de RTM y SOAT', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('consolidado', 'PDF Consolidado (expediente completo)', 'Expediente consolidado del trámite', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('acta_remate', 'Acta de remate', 'Acta de remate', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('oficio_judicial', 'Oficio judicial', 'Oficio judicial', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('comprobante_derechos', 'Comprobante de derechos', 'Comprobante de pago de derechos', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('acta_entrega', 'Acta de entrega', 'Acta de entrega', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('cert_tradicion', 'Certificado de tradición', 'Certificado de tradición y libertad', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('otro', 'Otro documento', 'Otro documento del trámite', '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true)
ON CONFLICT (code) DO NOTHING;
