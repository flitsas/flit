-- ADR-0050 / CFD-09 — parametrización real de los dos tipos operativos.
-- Migración: 20260822093000_ParametrizacionTiposOperativos
--
-- MATRICULA_NUEVA y TRASPASO_STANDARD solo tenían UN paso con una sección 'generic_form',
-- heredada de los seeds DEV (12-HU10200-dev-seed.sql, 15-tramites-traspaso-dev-seed.sql). Con esa
-- configuración, encender F08_DynamicProcedures colapsaba el wizard a un único paso siempre
-- completo: 'generic_form' cae en el default del DynamicGateEvaluator y no evalúa ningún gate.
--
-- Aquí se siembran los pasos reales, con los MISMOS códigos que emite WizardStateQuery.StepKey en
-- el camino estático (matrícula: consulta_vin → comprador → documentos → identidad → fur;
-- traspaso: consulta → vendedor → comprador → documentos → identidad → fur), para que el motor
-- dinámico alcance paridad y el frontend no vea cambiar las claves de paso.
--
-- El paso 'documentos' de traspaso lleva DOS secciones (checklist + comercial): los datos
-- comerciales se absorbieron ahí en la paridad de pasos de 2026-08.
--
-- Idempotente y reaplicable: borra y recrea los pasos de esos dos tipos. Si detecta form_fields
-- locked (configuración hecha a mano desde el configurador) NO toca nada y lo avisa.

DO $$
DECLARE
    v_locked integer;
    v_type_id uuid;
    v_step_id uuid;
    v_section_id uuid;
    r RECORD;
BEGIN
    SELECT count(*) INTO v_locked
      FROM tramites.form_fields ff
      JOIN tramites.procedure_sections sec ON sec.id = ff.procedure_section_id
      JOIN tramites.procedure_steps st ON st.id = sec.procedure_step_id
      JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
     WHERE pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD')
       AND ff.is_locked;

    IF v_locked > 0 THEN
        RAISE NOTICE 'ADR-0050: % campos locked en los tipos operativos; parametrización omitida para no destruir configuración manual.', v_locked;
        RETURN;
    END IF;

    -- Borrado en cascada de secciones y campos (FK ON DELETE CASCADE).
    DELETE FROM tramites.procedure_steps
     WHERE procedure_type_id IN (
         SELECT id FROM tramites.procedure_types
          WHERE code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD'));

    FOR r IN
        SELECT *
          FROM (VALUES
            -- type_code,          step_code,      step_title,               step_order, section_code,    section_title,                  section_type,          section_order
            ('MATRICULA_NUEVA',  'consulta_vin', 'Consulta VIN',            1, 'VEHICULO',    'Identificación del vehículo',  'vehicle_query',      1),
            ('MATRICULA_NUEVA',  'comprador',    'Comprador',               2, 'COMPRADOR',   'Datos del comprador',          'actor_form',         1),
            ('MATRICULA_NUEVA',  'documentos',   'Documentos',              3, 'CHECKLIST',   'Documentos del trámite',       'document_checklist', 1),
            ('MATRICULA_NUEVA',  'identidad',    'Identidad',               4, 'BIOMETRIA',   'Validación de identidad',      'biometric',          1),
            ('MATRICULA_NUEVA',  'fur',          'Resumen del trámite',     5, 'FUR',         'Resumen y firma',              'signature_fur',      1),

            ('TRASPASO_STANDARD', 'consulta',    'Consulta del vehículo',   1, 'VEHICULO',    'Identificación del vehículo',  'vehicle_query',      1),
            ('TRASPASO_STANDARD', 'vendedor',    'Vendedor',                2, 'VENDEDOR',    'Datos del vendedor',           'actor_form',         1),
            ('TRASPASO_STANDARD', 'comprador',   'Comprador',               3, 'COMPRADOR',   'Datos del comprador',          'actor_form',         1),
            ('TRASPASO_STANDARD', 'documentos',  'Documentos',              4, 'CHECKLIST',   'Documentos del trámite',       'document_checklist', 1),
            ('TRASPASO_STANDARD', 'documentos',  'Documentos',              4, 'COMERCIAL',   'Datos comerciales',            'commercial',         2),
            ('TRASPASO_STANDARD', 'identidad',   'Identidad',               5, 'BIOMETRIA',   'Validación de identidad',      'biometric',          1),
            ('TRASPASO_STANDARD', 'fur',         'Resumen del trámite',     6, 'FUR',         'Resumen y firma',              'signature_fur',      1)
          ) AS t(type_code, step_code, step_title, step_order, section_code, section_title, section_type, section_order)
         ORDER BY t.type_code, t.step_order, t.section_order
    LOOP
        SELECT id INTO v_type_id FROM tramites.procedure_types WHERE code = r.type_code;
        CONTINUE WHEN v_type_id IS NULL;

        SELECT id INTO v_step_id
          FROM tramites.procedure_steps
         WHERE procedure_type_id = v_type_id AND code = r.step_code;

        IF v_step_id IS NULL THEN
            INSERT INTO tramites.procedure_steps
                (id, procedure_type_id, code, title, sort_order, is_active)
            VALUES
                (uuidv7(), v_type_id, r.step_code, r.step_title, r.step_order::smallint, true)
            RETURNING id INTO v_step_id;
        END IF;

        INSERT INTO tramites.procedure_sections
            (id, procedure_step_id, code, title, sort_order, layout, section_type)
        VALUES
            (uuidv7(), v_step_id, r.section_code, r.section_title, r.section_order::smallint, 'single', r.section_type)
        RETURNING id INTO v_section_id;

        -- Campos de la consulta del vehículo: los que ya traían los seeds DEV, ahora colgando del
        -- paso correcto. El resto de secciones las pinta su renderer, no form_fields.
        IF r.section_type = 'vehicle_query' AND r.type_code = 'MATRICULA_NUEVA' THEN
            INSERT INTO tramites.form_fields
                (id, procedure_section_id, field_key, label, field_type, is_required, sort_order)
            VALUES
                (uuidv7(), v_section_id, 'vin',          'VIN / Número de chasis', 'text',   true,  1),
                (uuidv7(), v_section_id, 'plate',        'Placa',                  'text',   false, 2),
                (uuidv7(), v_section_id, 'vehicle_year', 'Año del modelo',         'number', false, 3);
        ELSIF r.section_type = 'vehicle_query' AND r.type_code = 'TRASPASO_STANDARD' THEN
            INSERT INTO tramites.form_fields
                (id, procedure_section_id, field_key, label, field_type, is_required, sort_order)
            VALUES
                (uuidv7(), v_section_id, 'plate',                  'Placa',                       'text', true,  1),
                (uuidv7(), v_section_id, 'owner_document_type',    'Tipo de documento del titular', 'text', true,  2),
                (uuidv7(), v_section_id, 'owner_document_number',  'Documento del titular',       'text', true,  3);
        END IF;
    END LOOP;

    RAISE NOTICE 'ADR-0050: parametrización sembrada para MATRICULA_NUEVA (5 pasos) y TRASPASO_STANDARD (6 pasos).';
END $$;
