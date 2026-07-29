-- Catálogo canónico de tipos de trámite.
-- Migración: 20260729120000_CatalogoTiposTramiteCanonico
--
-- Objetivo:
--   1) Unificar creación operativa + consola documental en MATRICULA_NUEVA y TRASPASO_STANDARD.
--   2) Eliminar códigos duplicados/legacy no usados por el wizard.
--   3) Sembrar el resto de tipos de la lista de negocio (activos e inactivos).
--
-- Idempotente. No usa BEGIN/COMMIT propios (EF envuelve la migración).

SET LOCAL row_security = off;

-- ─────────────────────────────────────────────────────────────────────────────
-- 0. Asegurar tipos operativos (por si el ambiente solo tenía seeds parciales)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs, publication_status, version, gate_profile, created_at, row_version
)
VALUES
    (uuidv7(), 'MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS',
     'Matrícula inicial de vehículo (VIN-first). Tipo canónico del wizard.',
     true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'TRASPASO_STANDARD', 'Traspaso', 'TRASPASO',
     'Traspaso de propiedad (placa-first). Tipo canónico del wizard.',
     true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    family = EXCLUDED.family,
    description = EXCLUDED.description,
    is_active = true,
    publication_status = 'published',
    published_at = COALESCE(tramites.procedure_types.published_at, now()),
    updated_at = now();

-- Aplicar gate_profile F08 canónico (y, si existe, preferir el de los duplicados legacy).
UPDATE tramites.procedure_types
SET gate_profile = '{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true,"validateDuplicateProcedure":true}'::jsonb,
    updated_at = now()
WHERE code = 'MATRICULA_NUEVA'
  AND (gate_profile IS NULL OR gate_profile = '{}'::jsonb);

UPDATE tramites.procedure_types
SET gate_profile = '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true,"validatePazSalvoImpuesto":true,"simitMode":"INTERNAL"}'::jsonb,
    updated_at = now()
WHERE code = 'TRASPASO_STANDARD'
  AND (gate_profile IS NULL OR gate_profile = '{}'::jsonb);

UPDATE tramites.procedure_types dest
SET gate_profile = src.gate_profile,
    updated_at = now()
FROM tramites.procedure_types src
WHERE dest.code = 'MATRICULA_NUEVA'
  AND src.code = 'MATRICULA_INICIAL'
  AND src.gate_profile IS NOT NULL
  AND src.gate_profile <> '{}'::jsonb;

UPDATE tramites.procedure_types dest
SET gate_profile = src.gate_profile,
    updated_at = now()
FROM tramites.procedure_types src
WHERE dest.code = 'TRASPASO_STANDARD'
  AND src.code = 'TRASPASO_SIMPLE'
  AND src.gate_profile IS NOT NULL
  AND src.gate_profile <> '{}'::jsonb;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Remapear FKs de códigos obsoletos → canónicos
-- ─────────────────────────────────────────────────────────────────────────────
-- Instancias
UPDATE tramites.procedure_instances i
SET procedure_type_id = pt_new.id
FROM tramites.procedure_types pt_old
JOIN tramites.procedure_types pt_new ON pt_new.code = 'TRASPASO_STANDARD'
WHERE i.procedure_type_id = pt_old.id
  AND pt_old.code IN ('TRASPASO', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING');

UPDATE tramites.procedure_instances i
SET procedure_type_id = pt_new.id
FROM tramites.procedure_types pt_old
JOIN tramites.procedure_types pt_new ON pt_new.code = 'MATRICULA_NUEVA'
WHERE i.procedure_type_id = pt_old.id
  AND pt_old.code IN ('MATRICULA_INICIAL', 'MATRICULA_REACTIVACION', 'CAMBIO_SERVICIO');

-- Snapshots (tabla F08; puede no existir en ambientes muy viejos)
DO $$
BEGIN
    IF to_regclass('tramites.procedure_type_snapshots') IS NOT NULL THEN
        UPDATE tramites.procedure_type_snapshots s
        SET procedure_type_id = pt_new.id
        FROM tramites.procedure_types pt_old
        JOIN tramites.procedure_types pt_new ON pt_new.code = 'TRASPASO_STANDARD'
        WHERE s.procedure_type_id = pt_old.id
          AND pt_old.code IN ('TRASPASO', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING');

        UPDATE tramites.procedure_type_snapshots s
        SET procedure_type_id = pt_new.id
        FROM tramites.procedure_types pt_old
        JOIN tramites.procedure_types pt_new ON pt_new.code = 'MATRICULA_NUEVA'
        WHERE s.procedure_type_id = pt_old.id
          AND pt_old.code IN ('MATRICULA_INICIAL', 'MATRICULA_REACTIVACION', 'CAMBIO_SERVICIO');
    END IF;
END $$;

-- Representantes legales
DO $$
BEGIN
    IF to_regclass('admin.company_legal_representative_procedure_types') IS NOT NULL THEN
        UPDATE admin.company_legal_representative_procedure_types clr
        SET procedure_type_id = pt_new.id
        FROM tramites.procedure_types pt_old
        JOIN tramites.procedure_types pt_new ON pt_new.code = 'TRASPASO_STANDARD'
        WHERE clr.procedure_type_id = pt_old.id
          AND pt_old.code IN ('TRASPASO', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING')
          AND NOT EXISTS (
              SELECT 1 FROM admin.company_legal_representative_procedure_types x
              WHERE x.representative_id = clr.representative_id
                AND x.procedure_type_id = pt_new.id
          );

        UPDATE admin.company_legal_representative_procedure_types clr
        SET procedure_type_id = pt_new.id
        FROM tramites.procedure_types pt_old
        JOIN tramites.procedure_types pt_new ON pt_new.code = 'MATRICULA_NUEVA'
        WHERE clr.procedure_type_id = pt_old.id
          AND pt_old.code IN ('MATRICULA_INICIAL', 'MATRICULA_REACTIVACION', 'CAMBIO_SERVICIO')
          AND NOT EXISTS (
              SELECT 1 FROM admin.company_legal_representative_procedure_types x
              WHERE x.representative_id = clr.representative_id
                AND x.procedure_type_id = pt_new.id
          );

        DELETE FROM admin.company_legal_representative_procedure_types clr
        USING tramites.procedure_types pt
        WHERE clr.procedure_type_id = pt.id
          AND pt.code IN (
              'TRASPASO', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING',
              'MATRICULA_INICIAL', 'MATRICULA_REACTIVACION', 'CAMBIO_SERVICIO'
          );
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Borrar hijos de tipos obsoletos y luego los tipos
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    obsolete text[] := ARRAY[
        'TRASPASO', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING',
        'MATRICULA_INICIAL', 'MATRICULA_REACTIVACION', 'CAMBIO_SERVICIO'
    ];
BEGIN
    DELETE FROM tramites.procedure_document_requirements r
    USING tramites.procedure_types pt
    WHERE r.procedure_type_id = pt.id AND pt.code = ANY (obsolete);

    DELETE FROM tramites.document_order_overrides o
    USING tramites.procedure_types pt
    WHERE o.procedure_type_id = pt.id AND pt.code = ANY (obsolete);

    DELETE FROM tramites.document_requirement_overrides o
    USING tramites.procedure_types pt
    WHERE o.procedure_type_id = pt.id AND pt.code = ANY (obsolete);

    IF to_regclass('admin.ot_document_precedence') IS NOT NULL THEN
        DELETE FROM admin.ot_document_precedence o
        USING tramites.procedure_types pt
        WHERE o.procedure_type_id = pt.id AND pt.code = ANY (obsolete);
    END IF;

    IF to_regclass('tramites.procedure_type_sources') IS NOT NULL THEN
        DELETE FROM tramites.procedure_type_sources s
        USING tramites.procedure_types pt
        WHERE s.procedure_type_id = pt.id AND pt.code = ANY (obsolete);
    END IF;

    DELETE FROM tramites.conformation_rules c
    USING tramites.procedure_types pt
    WHERE c.procedure_type_id = pt.id AND pt.code = ANY (obsolete);

    -- steps → sections → form_fields (cascade parcial según DDL; cleanup explícito)
    DELETE FROM tramites.form_fields ff
    USING tramites.procedure_sections sec
    JOIN tramites.procedure_steps st ON st.id = sec.procedure_step_id
    JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
    WHERE ff.procedure_section_id = sec.id AND pt.code = ANY (obsolete);

    DELETE FROM tramites.procedure_sections sec
    USING tramites.procedure_steps st
    JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
    WHERE sec.procedure_step_id = st.id AND pt.code = ANY (obsolete);

    DELETE FROM tramites.procedure_steps st
    USING tramites.procedure_types pt
    WHERE st.procedure_type_id = pt.id AND pt.code = ANY (obsolete);

    DELETE FROM tramites.procedure_types WHERE code = ANY (obsolete);
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Upsert catálogo completo (activos + inactivos)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.procedure_types (
    id, code, name, family, description,
    is_active, external_refs, publication_status, version, gate_profile, created_at, row_version
)
VALUES
    -- Operativos / activos
    (uuidv7(), 'MATRICULA_NUEVA',           'Matrícula inicial',              'MATRICULAS', 'Matrícula inicial de vehículo.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'TRASPASO_STANDARD',         'Traspaso',                       'TRASPASO',   'Traspaso de propiedad.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CAMBIO_LOCATARIO',          'Cambio de locatario',            'OTROS',      'Cambio de locatario (leasing).', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CAMBIO_CARROCERIA',         'Cambio de carrocería',           'OTROS',      'Cambio de carrocería.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'BLINDAJE',                  'Blindaje',                       'OTROS',      'Blindaje de vehículo.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CAMBIO_COLOR',              'Cambio de color',                'OTROS',      'Cambio de color.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'DUPLICADO_PLACA',           'Duplicado de placa',             'OTROS',      'Duplicado de placa.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'DUPLICADO_TARJETA',         'Duplicado de tarjeta',           'OTROS',      'Duplicado de tarjeta de propiedad.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'LEVANTAMIENTO_PRENDA',      'Levantar prenda',                'OTROS',      'Levantamiento de prenda.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'PRENDA_INSCRIPCION',        'Inscribir prenda',               'OTROS',      'Inscripción de prenda.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'RADICADO_CUENTA',           'Radicado de cuenta',             'OTROS',      'Radicado de cuenta.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CONVERSION_COMBUSTIBLE',    'Conversiones de combustible',    'OTROS',      'Conversiones de combustible.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'TRASLADO_CUENTA',           'Traslado de cuenta',             'OTROS',      'Traslado de cuenta.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CANCELACION_MATRICULA',     'Cancelación de matrícula',       'MATRICULAS', 'Cancelación de matrícula.', true, '{}'::jsonb, 'published', 1, '{}'::jsonb, now(), 0),
    -- Inactivos (visibles en catálogo admin, ocultos en dropdown isActive)
    (uuidv7(), 'REGRABAR_MOTOR_CHASIS',     'Regrabar motor, chasis',         'OTROS',      'Regrabación de motor y/o chasis.', false, '{}'::jsonb, 'draft', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'REMATRICULA',               'Rematrícula',                    'MATRICULAS', 'Rematrícula de vehículo.', false, '{}'::jsonb, 'draft', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'LEVANTAR_INSCRIBIR_PRENDA', 'Levantar e inscribir prenda',    'OTROS',      'Levantamiento e inscripción de prenda en un solo trámite.', false, '{}'::jsonb, 'draft', 1, '{}'::jsonb, now(), 0),
    (uuidv7(), 'CAMBIO_ACREEDOR',           'Cambio acreedor',                'OTROS',      'Cambio de acreedor prendario.', false, '{}'::jsonb, 'draft', 1, '{}'::jsonb, now(), 0)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    family = EXCLUDED.family,
    description = EXCLUDED.description,
    is_active = EXCLUDED.is_active,
    publication_status = CASE
        WHEN EXCLUDED.is_active THEN 'published'
        ELSE tramites.procedure_types.publication_status
    END,
    published_at = CASE
        WHEN EXCLUDED.is_active THEN COALESCE(tramites.procedure_types.published_at, now())
        ELSE tramites.procedure_types.published_at
    END,
    updated_at = now();

-- Alinear familia/nombres de tipos F08 que podían quedar con family distinta.
UPDATE tramites.procedure_types
SET family = 'OTROS',
    name = 'Cambio de locatario',
    updated_at = now()
WHERE code = 'CAMBIO_LOCATARIO';

UPDATE tramites.procedure_types
SET family = 'OTROS',
    name = 'Inscribir prenda',
    updated_at = now()
WHERE code = 'PRENDA_INSCRIPCION';

UPDATE tramites.procedure_types
SET family = 'OTROS',
    name = 'Levantar prenda',
    updated_at = now()
WHERE code = 'LEVANTAMIENTO_PRENDA';

-- Quipux: solo los canónicos operativos (el resto queda sin clave → no elegible).
UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia', 'TRASPASO',
        'tipoTramite', 16,
        'tipoRequisito', 51,
        'prefijo', 'TR',
        'campoPlaca', 'plate',
        'campoVin', NULL,
        'maxLongitudEmpresa', 35,
        'variante', jsonb_build_object(
            'campo', 'es_unilateral',
            'cuandoVerdadero', jsonb_build_object('tipoTramite', 213, 'prefijo', 'TRU')
        )
    )
)
WHERE pt.code = 'TRASPASO_STANDARD'
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia', 'TRASPASO',
        'tipoTramite', 16,
        'tipoRequisito', 51,
        'prefijo', 'TR',
        'campoPlaca', 'plate',
        'campoVin', NULL,
        'maxLongitudEmpresa', 35,
        'variante', jsonb_build_object(
            'campo', 'es_unilateral',
            'cuandoVerdadero', jsonb_build_object('tipoTramite', 213, 'prefijo', 'TRU')
        )
      );

UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia', 'MATRICULA',
        'tipoTramite', 13,
        'tipoRequisito', 51,
        'prefijo', 'MI',
        'campoPlaca', NULL,
        'campoVin', 'vin',
        'maxLongitudEmpresa', 25,
        'variante', jsonb_build_object(
            'campo', 'es_leasing',
            'cuandoVerdadero', jsonb_build_object('prefijo', 'MIL')
        )
    )
)
WHERE pt.code = 'MATRICULA_NUEVA'
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia', 'MATRICULA',
        'tipoTramite', 13,
        'tipoRequisito', 51,
        'prefijo', 'MI',
        'campoPlaca', NULL,
        'campoVin', 'vin',
        'maxLongitudEmpresa', 25,
        'variante', jsonb_build_object(
            'campo', 'es_leasing',
            'cuandoVerdadero', jsonb_build_object('prefijo', 'MIL')
        )
      );
