-- ADR-0050 / CFD-09 — parametrización base del resto del catálogo canónico.
-- Migración: 20260822100000_ParametrizacionCatalogoCompleto
--
-- Completa lo que 81-parametrizacion-tipos-operativos.sql hizo con MATRICULA_NUEVA y
-- TRASPASO_STANDARD: da pasos y secciones a los demás tipos, para que el wizard pueda conformarse
-- desde el catálogo en lugar de caer al recorrido estático heredado.
--
-- ⚠️ ESTO ES UNA BASE TÉCNICA, NO UN DISEÑO FUNCIONAL VALIDADO. Los recorridos salen de agrupar los
--    tipos por naturaleza (quién interviene, si hay gravamen, si se emite placa) siguiendo el patrón
--    que FEATURE-08 ya había definido para PRENDA_INSCRIPCION y CAMBIO_LOCATARIO en
--    38-F08-seeds-tipos-configurados.sql. Qué documentos, actores y validaciones exige realmente cada
--    trámite ante el organismo de tránsito es una decisión de negocio que debe revisarse tipo por tipo.
--
--    Regla de la familia OTROS (aclarada por negocio): se pide la PLACA y el DUEÑO del vehículo, y
--    después los documentos propios del trámite. Interviene UN SOLO actor —el titular, que no vende
--    ni compra: solo hace cambios sobre su vehículo (color, combustible, carrocería, blindaje,
--    levantamiento de prenda, duplicado de tarjeta…)—. Ese titular se persiste con el ActorType
--    'comprador', igual que en matrícula inicial, porque el modelo no tiene un rol 'propietario'
--    (ver RegistrarDocumentoQuipuxHandler: "No existe un ActorType owner/propietario en el modelo").
--    El paso se TITULA "Propietario" y su sección se CODIFICA como COMPRADOR: el título es lo que
--    lee el operador y el código es lo que el motor usa para saber qué actor exigir.
--
--    Por eso NINGUNO queda con wizard_enabled = true: siguen sin poder elegirse al crear un trámite.
--    Encenderlos exige antes matriz documental, causales y homologación Quipux/ICT (ADR-0050 §Cambios
--    operacionales). Esta migración no cambia lo que el gestor puede hacer hoy.
--
-- PRENDA_INSCRIPCION y CAMBIO_LOCATARIO no se tocan: ya los parametrizó el seed 38.
-- Idempotente: borra y recrea los pasos de los tipos que enumera.

-- ============================================================================
-- 1. Perfil de conformación por tipo
-- ============================================================================
WITH perfiles(code, gate_profile) AS (VALUES
    -- MATRICULAS ─────────────────────────────────────────────────────────────
    -- Leasing: como la matrícula inicial, pero el adquirente es la entidad financiera.
    ('MATRICULA_LEASING',                 '{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true}'),
    -- Cancelación: el vehículo ya tiene placa y no se emite una nueva.
    ('CANCELACION_MATRICULA',             '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    -- Rematrícula: vehículo con historial que vuelve a matricularse ⇒ sí pide placa.
    ('REMATRICULA',                       '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true}'),

    -- TRASPASO ───────────────────────────────────────────────────────────────
    -- Unilateral: no comparece el vendedor; de ahí que no exija parte saliente.
    ('TRASPASO_UNILATERAL',               '{"entryMode":"PLATE","requiresBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true,"simitMode":"INTERNAL"}'),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true}'),

    -- OTROS — novedades sobre un vehículo ya matriculado ──────────────────────
    ('CAMBIO_CARROCERIA',                 '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('BLINDAJE',                          '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('CAMBIO_COLOR',                      '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('DUPLICADO_TARJETA',                 '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('RADICADO_CUENTA',                   '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('CONVERSION_COMBUSTIBLE',            '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('TRASLADO_CUENTA',                   '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    ('REGRABAR_MOTOR_CHASIS',             '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true}'),
    -- Duplicado de placa: se emite una placa nueva ⇒ entra al flujo de asignación.
    ('DUPLICADO_PLACA',                   '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true}'),

    -- OTROS — con gravamen: activan el gate de prenda (R10) ───────────────────
    ('LEVANTAMIENTO_PRENDA',              '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}'),
    ('LEVANTAR_INSCRIBIR_PRENDA',         '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}'),
    ('CAMBIO_ACREEDOR',                   '{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}')
)
UPDATE tramites.procedure_types pt
   SET gate_profile = p.gate_profile::jsonb,
       updated_at = now()
  FROM perfiles p
 WHERE pt.code = p.code
   AND pt.gate_profile IS DISTINCT FROM p.gate_profile::jsonb;

-- ============================================================================
-- 2. Recorridos — se borran y recrean los pasos de los tipos parametrizados aquí
-- ============================================================================
DELETE FROM tramites.procedure_steps
 WHERE procedure_type_id IN (
     SELECT id FROM tramites.procedure_types
      WHERE code IN (
          'MATRICULA_LEASING', 'CANCELACION_MATRICULA', 'REMATRICULA',
          'TRASPASO_UNILATERAL', 'TRASPASO_TRANSFERENCIA_DE_DOMINIO',
          'CAMBIO_CARROCERIA', 'BLINDAJE', 'CAMBIO_COLOR', 'DUPLICADO_TARJETA',
          'RADICADO_CUENTA', 'CONVERSION_COMBUSTIBLE', 'TRASLADO_CUENTA',
          'REGRABAR_MOTOR_CHASIS', 'DUPLICADO_PLACA',
          'LEVANTAMIENTO_PRENDA', 'LEVANTAR_INSCRIBIR_PRENDA', 'CAMBIO_ACREEDOR'));

-- Cada tipo se asigna a un recorrido; el recorrido define sus pasos y secciones.
CREATE TEMP TABLE _asignacion(code text PRIMARY KEY, recorrido text) ON COMMIT DROP;
INSERT INTO _asignacion VALUES
    ('MATRICULA_LEASING',                 'MATRICULA'),
    ('CANCELACION_MATRICULA',             'NOVEDAD'),
    ('REMATRICULA',                       'NOVEDAD'),
    ('TRASPASO_UNILATERAL',               'TRASPASO_UNILATERAL'),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'TRASPASO'),
    ('CAMBIO_CARROCERIA',                 'NOVEDAD'),
    ('BLINDAJE',                          'NOVEDAD'),
    ('CAMBIO_COLOR',                      'NOVEDAD'),
    ('DUPLICADO_TARJETA',                 'NOVEDAD'),
    ('RADICADO_CUENTA',                   'NOVEDAD'),
    ('CONVERSION_COMBUSTIBLE',            'NOVEDAD'),
    ('TRASLADO_CUENTA',                   'NOVEDAD'),
    ('REGRABAR_MOTOR_CHASIS',             'NOVEDAD'),
    ('DUPLICADO_PLACA',                   'NOVEDAD'),
    ('LEVANTAMIENTO_PRENDA',              'PRENDA'),
    ('LEVANTAR_INSCRIBIR_PRENDA',         'PRENDA'),
    ('CAMBIO_ACREEDOR',                   'PRENDA');

CREATE TEMP TABLE _recorrido(
    recorrido text, step_order smallint, step_code text, step_title text,
    sec_order smallint, sec_code text, sec_type text) ON COMMIT DROP;
INSERT INTO _recorrido VALUES
    -- MATRICULA — paridad con el recorrido de MATRICULA_NUEVA (seed 81).
    ('MATRICULA',           1, 'consulta_vin', 'Consulta VIN',          1, 'VEHICULO',  'vehicle_query'),
    ('MATRICULA',           2, 'comprador',    'Comprador',             1, 'COMPRADOR', 'actor_form'),
    ('MATRICULA',           3, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('MATRICULA',           4, 'identidad',    'Identidad',             1, 'BIOMETRIA', 'biometric'),
    ('MATRICULA',           5, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur'),

    -- TRASPASO — paridad con TRASPASO_STANDARD (seed 81).
    ('TRASPASO',            1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('TRASPASO',            2, 'vendedor',     'Vendedor',              1, 'VENDEDOR',  'actor_form'),
    ('TRASPASO',            3, 'comprador',    'Comprador',             1, 'COMPRADOR', 'actor_form'),
    ('TRASPASO',            4, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('TRASPASO',            4, 'documentos',   'Documentos',            2, 'COMERCIAL', 'commercial'),
    ('TRASPASO',            5, 'identidad',    'Identidad',             1, 'BIOMETRIA', 'biometric'),
    ('TRASPASO',            6, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur'),

    -- TRASPASO_UNILATERAL — sin paso de vendedor: no comparece.
    ('TRASPASO_UNILATERAL', 1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('TRASPASO_UNILATERAL', 2, 'comprador',    'Comprador',             1, 'COMPRADOR', 'actor_form'),
    ('TRASPASO_UNILATERAL', 3, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('TRASPASO_UNILATERAL', 3, 'documentos',   'Documentos',            2, 'COMERCIAL', 'commercial'),
    ('TRASPASO_UNILATERAL', 4, 'identidad',    'Identidad',             1, 'BIOMETRIA', 'biometric'),
    ('TRASPASO_UNILATERAL', 5, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur'),

    -- NOVEDAD — familia OTROS: placa y dueño primero, luego los documentos del trámite.
    -- La sección del titular se codifica COMPRADOR (el ActorType con el que se persiste) y se
    -- titula "Propietario" (lo que lee el operador).
    ('NOVEDAD',             1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('NOVEDAD',             2, 'propietario',  'Propietario',           1, 'COMPRADOR', 'actor_form'),
    ('NOVEDAD',             3, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('NOVEDAD',             4, 'identidad',    'Identidad',             1, 'BIOMETRIA', 'biometric'),
    ('NOVEDAD',             5, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur'),

    -- PRENDA — igual que NOVEDAD más la decisión de gravamen (patrón PRENDA_INSCRIPCION).
    ('PRENDA',              1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('PRENDA',              2, 'propietario',  'Propietario',           1, 'COMPRADOR', 'actor_form'),
    ('PRENDA',              3, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('PRENDA',              4, 'prenda',       'Decisión de prenda',    1, 'PRENDA',    'prenda_decision'),
    ('PRENDA',              5, 'identidad',    'Identidad',             1, 'BIOMETRIA', 'biometric'),
    ('PRENDA',              6, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur');

INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
SELECT DISTINCT uuidv7(), pt.id, r.step_code, r.step_title, r.step_order, true
  FROM _asignacion a
  JOIN tramites.procedure_types pt ON pt.code = a.code
  JOIN _recorrido r ON r.recorrido = a.recorrido
 GROUP BY pt.id, r.step_code, r.step_title, r.step_order;

INSERT INTO tramites.procedure_sections
    (id, procedure_step_id, code, title, sort_order, layout, section_type)
SELECT uuidv7(), st.id, r.sec_code, r.step_title, r.sec_order, 'single', r.sec_type
  FROM _asignacion a
  JOIN tramites.procedure_types pt ON pt.code = a.code
  JOIN _recorrido r ON r.recorrido = a.recorrido
  JOIN tramites.procedure_steps st
    ON st.procedure_type_id = pt.id AND st.code = r.step_code;

-- ============================================================================
-- 3. Matriz documental por tipo (procedure_document_requirements)
-- ============================================================================
-- PROPUESTA BASE, pendiente de validación con negocio. Se usan únicamente codes que ya existen en
-- tramites.document_types: no se inventa catálogo. Donde el trámite exige un documento que el
-- catálogo aún no tiene código propio —certificado de blindaje, certificación de conversión a gas,
-- denuncia por pérdida de placa— se usa 'otro' como obligatorio y queda anotado abajo; darles code
-- propio es trabajo de la parametrización documental, no de este seed.
--
-- Común a la familia OTROS: tarjeta de propiedad y documento de identidad del titular, más SOAT y
-- paz y salvo de impuestos como respaldo opcional.

DELETE FROM tramites.procedure_document_requirements
 WHERE procedure_type_id IN (
     SELECT id FROM tramites.procedure_types
      WHERE code IN (
          'MATRICULA_LEASING', 'CANCELACION_MATRICULA', 'REMATRICULA',
          'TRASPASO_UNILATERAL', 'TRASPASO_TRANSFERENCIA_DE_DOMINIO',
          'CAMBIO_CARROCERIA', 'BLINDAJE', 'CAMBIO_COLOR', 'DUPLICADO_TARJETA',
          'RADICADO_CUENTA', 'CONVERSION_COMBUSTIBLE', 'TRASLADO_CUENTA',
          'REGRABAR_MOTOR_CHASIS', 'DUPLICADO_PLACA',
          'LEVANTAMIENTO_PRENDA', 'LEVANTAR_INSCRIBIR_PRENDA', 'CAMBIO_ACREEDOR'));

CREATE TEMP TABLE _requisitos(type_code text, doc_code text, obligatorio boolean, orden smallint)
    ON COMMIT DROP;

-- Base común de la familia OTROS y de las novedades sobre vehículo ya matriculado.
INSERT INTO _requisitos
SELECT t.code, d.doc, d.obl, d.orden
  FROM (VALUES
      ('CANCELACION_MATRICULA'), ('REMATRICULA'), ('CAMBIO_CARROCERIA'), ('BLINDAJE'),
      ('CAMBIO_COLOR'), ('DUPLICADO_TARJETA'), ('RADICADO_CUENTA'), ('CONVERSION_COMBUSTIBLE'),
      ('TRASLADO_CUENTA'), ('REGRABAR_MOTOR_CHASIS'), ('DUPLICADO_PLACA'),
      ('LEVANTAMIENTO_PRENDA'), ('LEVANTAR_INSCRIBIR_PRENDA'), ('CAMBIO_ACREEDOR')
  ) AS t(code)
 CROSS JOIN (VALUES
      ('tarjeta_propiedad',        true,  1::smallint),
      ('doc_identidad_propietario', true,  2::smallint),
      ('soat',                     false, 3::smallint),
      ('paz_salvo',                false, 4::smallint)
  ) AS d(doc, obl, orden);

-- Documentos propios de cada trámite.
INSERT INTO _requisitos VALUES
    -- Cambios físicos sobre el vehículo: hay que reimprontar y acreditar el trabajo.
    ('CAMBIO_CARROCERIA',      'factura_carroceria', true,  10),
    ('CAMBIO_CARROCERIA',      'impronta',           true,  11),
    ('REGRABAR_MOTOR_CHASIS',  'impronta',           true,  10),
    ('CONVERSION_COMBUSTIBLE', 'certificado_ambiental', true, 10),
    -- 'otro' hasta que el catálogo tenga código propio (certificado de blindaje).
    ('BLINDAJE',               'otro',               true,  10),
    ('CAMBIO_COLOR',           'otro',               false, 10),
    -- Duplicados: denuncia o constancia de pérdida ('otro' por la misma razón).
    ('DUPLICADO_TARJETA',      'otro',               true,  10),
    ('DUPLICADO_PLACA',        'otro',               true,  10),
    -- Cuenta / traslado: el vehículo cambia de organismo, se exige estar a paz y salvo.
    ('RADICADO_CUENTA',        'paz_salvo',          true,  10),
    ('RADICADO_CUENTA',        'cert_tradicion',     false, 11),
    ('TRASLADO_CUENTA',        'paz_salvo',          true,  10),
    ('TRASLADO_CUENTA',        'cert_tradicion',     false, 11),
    -- Matrícula: cancelar o rematricular exige historial del vehículo.
    ('CANCELACION_MATRICULA',  'cert_tradicion',     true,  10),
    ('CANCELACION_MATRICULA',  'oficio_judicial',    false, 11),
    ('REMATRICULA',            'cert_tradicion',     true,  10),
    -- Gravámenes.
    ('LEVANTAMIENTO_PRENDA',      'paz_salvo_prenda',   true, 10),
    ('LEVANTAR_INSCRIBIR_PRENDA', 'paz_salvo_prenda',   true, 10),
    ('LEVANTAR_INSCRIBIR_PRENDA', 'inscripcion_prenda', true, 11),
    ('CAMBIO_ACREEDOR',           'inscripcion_prenda', true, 10),
    ('CAMBIO_ACREEDOR',           'limitacion_propiedad', false, 11),
    -- Leasing: contrato y factura del vehículo nuevo.
    ('MATRICULA_LEASING', 'contrato_leasing',      true,  1),
    ('MATRICULA_LEASING', 'factura',               true,  2),
    ('MATRICULA_LEASING', 'doc_identidad_comprador', true, 3),
    ('MATRICULA_LEASING', 'aduana',                false, 4),
    ('MATRICULA_LEASING', 'impronta',              false, 5),
    -- Traspasos no estándar.
    ('TRASPASO_UNILATERAL', 'tarjeta_propiedad',       true,  1),
    ('TRASPASO_UNILATERAL', 'doc_identidad_comprador', true,  2),
    ('TRASPASO_UNILATERAL', 'compraventa',             true,  3),
    ('TRASPASO_UNILATERAL', 'soat',                    false, 4),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'tarjeta_propiedad',       true,  1),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'doc_identidad_vendedor',  true,  2),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'doc_identidad_comprador', true,  3),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', 'transferencia_dominio',   true,  4);

INSERT INTO tramites.procedure_document_requirements
    (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
SELECT uuidv7(), pt.id, dt.id, bool_or(r.obligatorio), min(r.orden)
  FROM _requisitos r
  JOIN tramites.procedure_types pt ON pt.code = r.type_code
  JOIN tramites.document_types dt ON dt.code = r.doc_code
 GROUP BY pt.id, dt.id
    ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;
