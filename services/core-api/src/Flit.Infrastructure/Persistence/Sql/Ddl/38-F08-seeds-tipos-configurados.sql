-- FEATURE-08 / HU-BE-07 (CFD-10) — Seeds de tipos de referencia como configuraciones.
-- Migración: 20260721100300_F08_SeedTiposConfigurados
-- Tipos canónicos del wizard: MATRICULA_NUEVA y TRASPASO_STANDARD reciben solo gate_profile
-- (NO se borran steps/form_fields operativos). PRENDA_INSCRIPCION y CAMBIO_LOCATARIO sí
-- materializan steps/sections/conformation/sources. Idempotente.
-- El flag F08_DynamicProcedures NO se activa aquí (activación deliberada por tenant en DEV — ver pie).

-- ─────────────────────────────────────────────────────────────────────────────
-- MATRICULA_NUEVA — gate_profile canónico (VIN, comprador, biometría[BUYER], firma, placa)
-- No reemplaza steps del wizard operativo.
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE tramites.procedure_types
SET name = 'Matrícula inicial',
    family = 'MATRICULAS',
    gate_profile = '{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true,"validateDuplicateProcedure":true}'::jsonb,
    publication_status = 'published',
    published_at = COALESCE(published_at, now()),
    is_active = true,
    updated_at = now()
WHERE code = 'MATRICULA_NUEVA';

INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
SELECT uuidv7(), 'MATRICULA_NUEVA', 'Matrícula inicial', 'MATRICULAS', 1,
    '{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true,"validateDuplicateProcedure":true}'::jsonb,
    'published', now(), true, now()
WHERE NOT EXISTS (SELECT 1 FROM tramites.procedure_types WHERE code = 'MATRICULA_NUEVA');

-- ─────────────────────────────────────────────────────────────────────────────
-- TRASPASO_STANDARD — gate_profile canónico (PLATE, vendedor+comprador, comercial, firma)
-- No reemplaza steps del wizard operativo.
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE tramites.procedure_types
SET name = 'Traspaso',
    family = 'TRASPASO',
    gate_profile = '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true,"validatePazSalvoImpuesto":true,"simitMode":"INTERNAL"}'::jsonb,
    publication_status = 'published',
    published_at = COALESCE(published_at, now()),
    is_active = true,
    updated_at = now()
WHERE code = 'TRASPASO_STANDARD';

INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
SELECT uuidv7(), 'TRASPASO_STANDARD', 'Traspaso', 'TRASPASO', 1,
    '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true,"validatePazSalvoImpuesto":true,"simitMode":"INTERNAL"}'::jsonb,
    'published', now(), true, now()
WHERE NOT EXISTS (SELECT 1 FROM tramites.procedure_types WHERE code = 'TRASPASO_STANDARD');

-- ─────────────────────────────────────────────────────────────────────────────
-- PRENDA_INSCRIPCION — entrada PLATE, propietario + acreedor, RUNT, prenda_decision
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE v_type uuid; v_step uuid;
BEGIN
    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
    VALUES (uuidv7(), 'PRENDA_INSCRIPCION', 'Inscribir prenda', 'OTROS', 1,
        '{"entryMode":"PLATE","requiresSignature":true,"hasPrendaGate":true,"validateOtOperability":true}'::jsonb,
        'published', now(), true, now())
    ON CONFLICT (code) DO UPDATE SET gate_profile = EXCLUDED.gate_profile, family = EXCLUDED.family,
        publication_status = 'published', published_at = now(), updated_at = now()
    RETURNING id INTO v_type;

    DELETE FROM tramites.procedure_steps WHERE procedure_type_id = v_type;
    DELETE FROM tramites.conformation_rules WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_type_sources WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_document_requirements WHERE procedure_type_id = v_type;

    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'consulta', 'Consulta', 1, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'consulta', 'Consulta', 1, 'single', 'vehicle_query', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'documentos', 'Documentos', 2, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'documentos', 'Documentos', 1, 'single', 'document_checklist', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'actores', 'Propietario y acreedor', 3, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'actores', 'Propietario y acreedor', 1, 'single', 'actor_form', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'prenda', 'Decisión de prenda', 4, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'prenda', 'Decisión de prenda', 1, 'single', 'prenda_decision', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'fur', 'Firma / FUR', 5, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'fur', 'Firma / FUR', 1, 'single', 'signature_fur', now());

    INSERT INTO tramites.conformation_rules (id, procedure_type_id, procedure_entity_id, is_active, sort_order, validation_profile, created_at)
    SELECT uuidv7(), v_type, pe.id, true, 1, '{"allowsNaturalPerson":true,"allowsJuridicalPerson":true,"requiresRunt":true}'::jsonb, now()
    FROM tramites.procedure_entities pe WHERE pe.code = 'OWNER';

    INSERT INTO tramites.procedure_type_sources (procedure_type_id, external_data_source_id, is_active, execution_order, config, created_at)
    SELECT v_type, ds.id, true, 1, '{}'::jsonb, now() FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT';
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- CAMBIO_LOCATARIO — entrada PLATE, locatario (LESSEE) + entidad (PJ), RUNT, firma
-- 4º tipo de referencia: valida la arista LESSEE sin gate estático equivalente (DoD del feature).
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE v_type uuid; v_step uuid;
BEGIN
    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
    VALUES (uuidv7(), 'CAMBIO_LOCATARIO', 'Cambio de locatario', 'OTROS', 1,
        '{"entryMode":"PLATE","requiresSignature":true,"validateOtOperability":true}'::jsonb,
        'published', now(), true, now())
    ON CONFLICT (code) DO UPDATE SET gate_profile = EXCLUDED.gate_profile, family = EXCLUDED.family,
        publication_status = 'published', published_at = now(), updated_at = now()
    RETURNING id INTO v_type;

    DELETE FROM tramites.procedure_steps WHERE procedure_type_id = v_type;
    DELETE FROM tramites.conformation_rules WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_type_sources WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_document_requirements WHERE procedure_type_id = v_type;

    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'consulta', 'Consulta', 1, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'consulta', 'Consulta', 1, 'single', 'vehicle_query', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'documentos', 'Documentos', 2, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'documentos', 'Documentos', 1, 'single', 'document_checklist', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'locatario', 'Locatario', 3, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'locatario', 'Locatario', 1, 'single', 'actor_form', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'identidad', 'Identidad', 4, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'identidad', 'Identidad', 1, 'single', 'biometric', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'fur', 'Firma / FUR', 5, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'fur', 'Firma / FUR', 1, 'single', 'signature_fur', now());

    INSERT INTO tramites.conformation_rules (id, procedure_type_id, procedure_entity_id, is_active, sort_order, validation_profile, created_at)
    SELECT uuidv7(), v_type, pe.id, true, 1, '{"allowsNaturalPerson":false,"allowsJuridicalPerson":true,"requiresRunt":true}'::jsonb, now()
    FROM tramites.procedure_entities pe WHERE pe.code = 'LESSEE';

    INSERT INTO tramites.procedure_type_sources (procedure_type_id, external_data_source_id, is_active, execution_order, config, created_at)
    SELECT v_type, ds.id, true, 1, '{}'::jsonb, now() FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT';
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- Activación del motor en DEV (deliberada, por tenant) — NO se ejecuta en esta migración.
-- Ejecutar manualmente en DEV el flag por tenant antes de la regresión E2E:
--
--   INSERT INTO admin.ot_feature_flags (id, tenant_id, flag_key, is_enabled, config_json, created_at)
--   VALUES (uuidv7(), '<tenant_dev>', 'F08_DynamicProcedures', true, '{}', now())
--   ON CONFLICT (tenant_id, flag_key) DO UPDATE SET is_enabled = true;
--
-- Con el flag activo y un snapshot por instancia (CaptureTypeSnapshot, BE-01) el wizard usa
-- DynamicGateEvaluator (BE-06). Tipos canónicos: MATRICULA_NUEVA, TRASPASO_STANDARD,
-- PRENDA_INSCRIPCION, CAMBIO_LOCATARIO.
-- ─────────────────────────────────────────────────────────────────────────────
