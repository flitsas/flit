-- =============================================================================
-- core-ict — ict.procedure_type_mapping alineado al catálogo canónico (ADR-0050).
--
-- El seed original mapeaba los 16 transaction_type v1 a solo cuatro codes: el 2
-- (matrícula leasing) apuntaba a MATRICULA_NUEVA y el 4 (traspaso unilateral) a
-- TRASPASO_STANDARD, así que un pre-trámite de esos tipos se materializaba en v2
-- como un trámite distinto del que el cliente envió. Los 12 restantes eran stubs
-- (OTRO_TRAMITE_05..16) que no existen en tramites.procedure_types.
--
-- Además, dos decisiones de la materialización se tomaban por número de
-- transacción quemado en C#: de dónde sale el organismo de tránsito (del RUNT en
-- traspaso, del gestor en matrícula) y si el borrador lleva datos comerciales.
-- Son propiedades DEL TIPO, no del número: pasan a ser columnas de este mapeo,
-- que es el catálogo que ICT sí gobierna.
--
-- `family` cierra el mismo hueco en AttachmentDocTypeResolver, que elegía la
-- columna doc_tipo_* de la tabla de asociación con un switch 1|2 / 3|4 / resto.
--
-- Aditivo e idempotente. No enciende ningún tipo: `is_published` solo pasa a true
-- cuando el tipo esté habilitado en core-api (`wizard_enabled`); mientras tanto la
-- materialización devuelve modalidad_not_available, que es el fallo seguro.
-- =============================================================================

ALTER TABLE ict.procedure_type_mapping
  ADD COLUMN IF NOT EXISTS family varchar(20) NOT NULL DEFAULT 'OTROS',
  ADD COLUMN IF NOT EXISTS requires_commercial_value boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS resolves_transit_office_from_runt boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN ict.procedure_type_mapping.family IS
  'Familia del tipo en v2 (MATRICULAS|TRASPASO|OTROS). Elige la columna doc_tipo_* de ict.external_integration_attachment_association.';
COMMENT ON COLUMN ict.procedure_type_mapping.requires_commercial_value IS
  'El borrador lleva valor y fecha de venta. Antes se decidía con transaction_type = 3 quemado en el cliente gRPC.';
COMMENT ON COLUMN ict.procedure_type_mapping.resolves_transit_office_from_runt IS
  'El organismo de tránsito sale del nombre que devolvió el RUNT (paridad v1 de traspaso) y no lo asigna el gestor.';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_type_mapping_family'
    ) THEN
        ALTER TABLE ict.procedure_type_mapping
          ADD CONSTRAINT ck_procedure_type_mapping_family
          CHECK (family IN ('MATRICULAS', 'TRASPASO', 'OTROS'));
    END IF;
END $$;

-- Los 16 tipos v1 contra sus codes canónicos de tramites.procedure_types.
-- El UPDATE es necesario además del INSERT: las filas del seed original ya existen
-- (ON CONFLICT DO NOTHING no las habría corregido).
INSERT INTO ict.procedure_type_mapping (
    external_transaction_type, procedure_type_code, is_published, description,
    family, requires_commercial_value, resolves_transit_office_from_runt
) VALUES
    (1,  'MATRICULA_NUEVA',        true,  'Matrícula inicial',           'MATRICULAS', false, false),
    (2,  'MATRICULA_LEASING',      false, 'Matrícula leasing',           'MATRICULAS', false, false),
    (3,  'TRASPASO_STANDARD',      true,  'Traspaso',                    'TRASPASO',   true,  true),
    (4,  'TRASPASO_UNILATERAL',    false, 'Traspaso unilateral',         'TRASPASO',   false, true),
    (5,  'BLINDAJE',               false, 'Blindaje',                    'OTROS',      false, false),
    (6,  'CAMBIO_CARROCERIA',      false, 'Cambio de carrocería',        'OTROS',      false, false),
    (7,  'CAMBIO_COLOR',           false, 'Cambio de color',             'OTROS',      false, false),
    (8,  'CAMBIO_LOCATARIO',       false, 'Cambio de locatario',         'OTROS',      false, false),
    (9,  'CONVERSION_COMBUSTIBLE', false, 'Conversión de combustible',   'OTROS',      false, false),
    (10, 'DUPLICADO_PLACA',        false, 'Duplicado de placa',          'OTROS',      false, false),
    (11, 'DUPLICADO_TARJETA',      false, 'Duplicado de tarjeta',        'OTROS',      false, false),
    (12, 'PRENDA_INSCRIPCION',     false, 'Inscribir prenda',            'OTROS',      false, false),
    (13, 'LEVANTAMIENTO_PRENDA',   false, 'Levantar prenda',             'OTROS',      false, false),
    (14, 'CANCELACION_MATRICULA',  false, 'Cancelación de matrícula',    'MATRICULAS', false, false),
    (15, 'TRASLADO_CUENTA',        false, 'Traslado de cuenta',          'OTROS',      false, false),
    (16, 'RADICADO_CUENTA',        false, 'Radicado de cuenta',          'OTROS',      false, false)
-- `is_published` queda FUERA del UPDATE a propósito: es la palanca local del
-- operador de ICT y no debe revertirse cada vez que arranque el servicio. La
-- barrera de verdad vive en core-api (`procedure_types.wizard_enabled`), que
-- rechaza con procedure_type_not_enabled lo que no esté habilitado.
ON CONFLICT (external_transaction_type) DO UPDATE SET
    procedure_type_code               = EXCLUDED.procedure_type_code,
    description                       = EXCLUDED.description,
    family                            = EXCLUDED.family,
    requires_commercial_value         = EXCLUDED.requires_commercial_value,
    resolves_transit_office_from_runt = EXCLUDED.resolves_transit_office_from_runt,
    updated_at                        = now();
