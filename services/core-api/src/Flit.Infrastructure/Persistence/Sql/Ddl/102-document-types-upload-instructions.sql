-- HU #12065 (Feature #12064) — instrucción de cargue por tipo de documento.
--
-- `description` es la nota interna que el admin ve en su listado ("Paz y salvo de impuestos del
-- vehículo"); `upload_instructions` es el texto que lee el GESTOR en la tarjeta del paso Requisitos
-- ("Sube el Paz y Salvo de impuestos vehiculares expedido por la Secretaría de Hacienda"). Son dos
-- audiencias distintas, por eso son dos columnas y no un reemplazo.
--
-- Idempotente: ADD COLUMN IF NOT EXISTS + UPDATE por `code`. El UPDATE solo siembra el valor
-- INICIAL: `WHERE upload_instructions IS NULL` para no pisar lo que el admin haya escrito después
-- desde el módulo documental (la parametrización manda sobre el seed).

ALTER TABLE tramites.document_types
    ADD COLUMN IF NOT EXISTS upload_instructions varchar(500);

COMMENT ON COLUMN tramites.document_types.upload_instructions IS
    'Instruccion de cargue que lee el gestor en la tarjeta del paso Requisitos (HU #12065). Distinta de description, que es la nota interna del administrador. NULL = la tarjeta no muestra instruccion.';

-- Textos iniciales entregados por producto. Los tipos que no aparecen aquí quedan en NULL a
-- propósito: se definen desde la UI de administración.
UPDATE tramites.document_types AS d
SET upload_instructions = s.texto
FROM (VALUES
    -- Identidad de las partes
    ('doc_identidad_propietario', 'Adjunta el documento de identidad del propietario o comprador para validar la matrícula.'),
    ('doc_identidad_comprador',   'Adjunta la cédula o documento de identidad del comprador para gestionar el traspaso.'),
    ('doc_identidad_vendedor',    'Adjunta el documento de identidad del vendedor para continuar con el traspaso.'),
    ('cedulas',                   'Adjunta la cédula de ciudadanía o la carta-selfie para la verificación de identidad.'),
    -- Origen del vehículo
    ('factura',                   'Adjunta la factura de venta expedida por el concesionario para la matrícula inicial.'),
    ('factura_carroceria',        'Adjunta la factura de compra o modificación correspondiente al cambio de carrocería.'),
    ('declaracion_aduana',        'Adjunta la Declaración de Importación o documento aduanero oficial del vehículo.'),
    ('aduana',                    'Adjunta la Declaración de Importación o documento aduanero oficial del vehículo.'),
    ('certificado_ambiental',     'Sube el Certificado de Emisiones de Prueba Dinámica (CEPD) correspondiente al vehículo.'),
    -- Vigencias
    ('soat',                      'Adjunta la póliza del SOAT vigente del vehículo para validar su cobertura activa.'),
    ('soat_manual',               'Adjunta la póliza del SOAT vigente del vehículo para validar su cobertura activa.'),
    ('rtm',                       'Sube el certificado de la Revisión Técnico-Mecánica (RTM) vigente del vehículo.'),
    -- Paz y salvos / prenda
    ('paz_salvo',                 'Sube el Paz y Salvo de impuestos vehiculares expedido por la Secretaría de Hacienda.'),
    ('paz_salvo_prenda',          'Adjunta la certificación o factura que confirma la cancelación de la prenda.'),
    ('inscripcion_prenda',        'Adjunta el documento oficial de inscripción de prenda a favor de la entidad crediticia.'),
    ('limitacion_propiedad',      'Adjunta el documento de limitación a la propiedad y garantía a favor del acreedor prendario.'),
    ('paz_salvo_rnmc',            'Adjunta el certificado de Paz y Salvo del Registro Nacional de Medidas Correctivas (RNMC).'),
    -- Poderes y representación
    ('poder_tramitador',          'Adjunta el poder otorgado por las partes al tramitador para realizar la gestión.'),
    ('autorizacion_apoderado',    'Adjunta el poder de autorización correspondiente al apoderado registrado por la compañía.'),
    ('escritura_publica',         'Adjunta la copia de la escritura pública que soporta la representación o el trámite.'),
    ('escritura',                 'Adjunta la copia de la escritura pública que soporta la representación o el trámite.'),
    ('escritura_comprador',       'Adjunta la escritura pública correspondiente a la compañía del comprador.'),
    ('camara_comercio',           'Adjunta el certificado de Cámara de Comercio (no mayor a 30 días) y la cédula del representante legal.'),
    ('rues',                      'Sube el certificado RUES correspondiente a la persona jurídica vinculada al trámite.'),
    ('certificado_rues',          'Sube el certificado RUES correspondiente a la persona jurídica vinculada al trámite.'),
    ('certificado_superfinanciera', 'Sube el certificado emitido por la Superintendencia Financiera para la entidad.'),
    -- Traspaso
    ('compraventa',               'Adjunta el contrato o formato de compraventa debidamente firmado para el traspaso.'),
    ('contrato_leasing',          'Sube la copia del contrato de leasing para formalizar el traspaso unilateral.'),
    ('declaracion_arrendadora',   'Adjunta la declaración emitida por la compañía arrendadora para el trámite de leasing.'),
    ('transferencia_dominio',     'Sube el certificado que acredita la transferencia de dominio del vehículo.'),
    -- Improntas y verificación física
    ('impronta',                  'Adjunta el documento o plantilla con las improntas legibles del motor y chasis.'),
    ('impronta_validada',         'Documento de validación y verificación del hash de improntas del vehículo.'),
    ('certificado_dijin',         'Sube la certificación de revisión técnica física expedida por la DIJIN o la Policía Nacional.'),
    ('certificado_blindaje',      'Adjunta la certificación de instalación o desmonte del blindaje del vehículo.'),
    -- Pagos. Sin el fragmento "Valor: $[Monto]" del listado de producto: el texto es estático y no
    -- interpola datos del trámite, así que el placeholder se vería literal en pantalla.
    ('liquidacion_impuesto',      'Sube el comprobante de liquidación y pago del impuesto departamental.'),
    ('comprobante_derechos',      'Adjunta el comprobante de pago por derechos de trámite ante el organismo de tránsito.'),
    -- Vía judicial / administrativa
    ('acta_remate',               'Adjunta la copia oficial del acta de remate que adjudica el vehículo.'),
    ('oficio_judicial',           'Sube la copia del oficio o providencia judicial emitida por la autoridad competente.'),
    ('acta_entrega',              'Adjunta el acta de entrega formal del vehículo debidamente suscrita.'),
    ('cert_tradicion',            'Sube el Certificado de Tradición y Libertad del vehículo actualizado.'),
    ('certificado_aseguradora_perito', 'Adjunta el certificado de pérdida total emitido por la compañía aseguradora o el perito.'),
    ('certificado_autoridad_administrativa', 'Sube el certificado o concepto emitido por la autoridad administrativa correspondiente.'),
    -- Cajón de sastre
    ('anexos_generales',          'Sube los documentos o soportes adicionales requeridos para este trámite.'),
    ('otro',                      'Adjunta otro documento complementario necesario para la solicitud.'),
    -- Documentos que produce el sistema: no se cargan, pero el texto describe qué son allí donde
    -- se listan (guía informativa del paso 1, expediente).
    ('portada',                   'Hoja de presentación autogenerada que encabeza el expediente digital del trámite.'),
    ('consolidado',               'Descarga el expediente digital completo con todos los documentos validados para este trámite.'),
    ('fur',                       'Formato Único de Registro (FUR) generado automáticamente con la información del trámite.'),
    ('mandato',                   'Documento de mandato autogenerado listo para la firma de las partes interesadas.'),
    ('carta_declaratoria',        'Documento de carta declaratoria autogenerado para soportar el traspaso unilateral.'),
    ('certificado_identidad',     'Certificado autogenerado de la validación de identidad biométrica completada con éxito.'),
    ('certificado_identidad_vendedor', 'Certificado autogenerado de la validación de identidad biométrica completada con éxito.'),
    ('licencia_transito',         'Visualiza o descarga la Licencia de Tránsito emitida como resultado exitoso de la radicación.'),
    ('tarjeta_propiedad',         'Visualiza o descarga la Licencia de Tránsito emitida como resultado exitoso de la radicación.')
) AS s(code, texto)
WHERE d.code = s.code
  AND d.upload_instructions IS NULL;
