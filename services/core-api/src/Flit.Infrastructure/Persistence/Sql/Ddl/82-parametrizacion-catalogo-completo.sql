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
    ('CANCELACION_MATRICULA',             '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    -- Rematrícula: vehículo con historial que vuelve a matricularse ⇒ sí pide placa.
    ('REMATRICULA',                       '{"entryMode":"PLATE","requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true}'),

    -- TRASPASO ───────────────────────────────────────────────────────────────
    -- Unilateral: no comparece el vendedor; de ahí que no exija parte saliente.
    ('TRASPASO_UNILATERAL',               '{"entryMode":"PLATE","requiresBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true,"simitMode":"INTERNAL"}'),
    ('TRASPASO_TRANSFERENCIA_DE_DOMINIO', '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true}'),

    -- OTROS — novedades sobre un vehículo ya matriculado ──────────────────────
    ('CAMBIO_CARROCERIA',                 '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('BLINDAJE',                          '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('CAMBIO_COLOR',                      '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('DUPLICADO_TARJETA',                 '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('RADICADO_CUENTA',                   '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('CONVERSION_COMBUSTIBLE',            '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('TRASLADO_CUENTA',                   '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    ('REGRABAR_MOTOR_CHASIS',             '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'),
    -- Duplicado de placa: se emite una placa nueva ⇒ entra al flujo de asignación.
    ('DUPLICADO_PLACA',                   '{"entryMode":"PLATE","requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true}'),

    -- OTROS — con gravamen: activan el gate de prenda (R10) ───────────────────
    ('LEVANTAMIENTO_PRENDA',              '{"entryMode":"PLATE","requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}'),
    ('LEVANTAR_INSCRIBIR_PRENDA',         '{"entryMode":"PLATE","requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}'),
    ('CAMBIO_ACREEDOR',                   '{"entryMode":"PLATE","requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}')
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

    -- NOVEDAD — patrón de FEATURE-08 para tipos de OTROS sobre vehículo matriculado.
    ('NOVEDAD',             1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('NOVEDAD',             2, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('NOVEDAD',             3, 'propietario',  'Propietario',           1, 'PROPIETARIO','actor_form'),
    ('NOVEDAD',             4, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur'),

    -- PRENDA — igual que NOVEDAD más la decisión de gravamen (patrón PRENDA_INSCRIPCION).
    ('PRENDA',              1, 'consulta',     'Consulta del vehículo', 1, 'VEHICULO',  'vehicle_query'),
    ('PRENDA',              2, 'documentos',   'Documentos',            1, 'CHECKLIST', 'document_checklist'),
    ('PRENDA',              3, 'propietario',  'Propietario y acreedor',1, 'PROPIETARIO','actor_form'),
    ('PRENDA',              4, 'prenda',       'Decisión de prenda',    1, 'PRENDA',    'prenda_decision'),
    ('PRENDA',              5, 'fur',          'Resumen del trámite',   1, 'FUR',       'signature_fur');

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
