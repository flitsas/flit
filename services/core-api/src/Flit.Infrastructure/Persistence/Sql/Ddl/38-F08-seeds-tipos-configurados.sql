-- FEATURE-08 / HU-BE-07 (CFD-10) — Seeds de los 4 tipos de referencia como configuraciones completas.
-- Migración: 20260721100300_F08_SeedTiposConfigurados
-- Materializa MATRICULA_INICIAL, TRASPASO_SIMPLE, PRENDA_INSCRIPCION y CAMBIO_LOCATARIO con su
-- gate_profile + procedure_steps + procedure_sections (con section_type) + conformation_rules +
-- procedure_type_sources + procedure_document_requirements, para que el motor dinámico (BE-06) los
-- opere por configuración. Idempotente: UPSERT del tipo + reemplazo de hijos por tipo.
-- Los requisitos documentales se insertan solo si el document_type existe en el catálogo (guardado).
-- El flag F08_DynamicProcedures NO se activa aquí (activación deliberada por tenant en DEV — ver pie).

-- ─────────────────────────────────────────────────────────────────────────────
-- MATRICULA_INICIAL — entrada VIN, comprador (PN/PJ), RUNT, biometría[BUYER], firma, placa
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE v_type uuid; v_step uuid;
BEGIN
    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
    VALUES (uuidv7(), 'MATRICULA_INICIAL', 'Matrícula Inicial', 'matricula', 1,
        '{"entryMode":"VIN","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"requiresPlateRequest":true,"validateOtOperability":true,"validateDuplicateProcedure":true}'::jsonb,
        'published', now(), true, now())
    ON CONFLICT (code) DO UPDATE SET gate_profile = EXCLUDED.gate_profile, family = EXCLUDED.family,
        publication_status = 'published', published_at = now(), updated_at = now()
    RETURNING id INTO v_type;

    DELETE FROM tramites.procedure_steps WHERE procedure_type_id = v_type;
    DELETE FROM tramites.conformation_rules WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_type_sources WHERE procedure_type_id = v_type;
    DELETE FROM tramites.procedure_document_requirements WHERE procedure_type_id = v_type;

    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'consulta', 'Consulta del vehículo', 1, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'consulta', 'Consulta del vehículo', 1, 'single', 'vehicle_query', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'documentos', 'Documentos', 2, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'documentos', 'Documentos', 1, 'single', 'document_checklist', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'comprador', 'Comprador', 3, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'comprador', 'Comprador', 1, 'single', 'actor_form', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'identidad', 'Identidad', 4, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'identidad', 'Identidad', 1, 'single', 'biometric', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'placa', 'Solicitud de placa', 5, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'placa', 'Solicitud de placa', 1, 'single', 'plate_request', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'fur', 'Firma / FUR', 6, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'fur', 'Firma / FUR', 1, 'single', 'signature_fur', now());

    INSERT INTO tramites.conformation_rules (id, procedure_type_id, procedure_entity_id, is_active, sort_order, validation_profile, created_at)
    SELECT uuidv7(), v_type, pe.id, true, 1, '{"allowsNaturalPerson":true,"allowsJuridicalPerson":true,"allowsMultiple":false,"requiresRunt":true}'::jsonb, now()
    FROM tramites.procedure_entities pe WHERE pe.code = 'BUYER';

    INSERT INTO tramites.procedure_type_sources (procedure_type_id, external_data_source_id, is_active, execution_order, config, created_at)
    SELECT v_type, ds.id, true, 1, '{}'::jsonb, now() FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT';
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- TRASPASO_SIMPLE — entrada PLATE, vendedor+comprador (múltiple), RUNT+SIMIT, valor comercial, firma
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE v_type uuid; v_step uuid;
BEGIN
    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
    VALUES (uuidv7(), 'TRASPASO_SIMPLE', 'Traspaso Simple', 'traspaso', 1,
        '{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true,"validatePazSalvoImpuesto":true,"simitMode":"INTERNAL"}'::jsonb,
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
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'vendedor', 'Vendedor', 3, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'vendedor', 'Vendedor', 1, 'single', 'actor_form', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'comprador', 'Comprador', 4, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'comprador', 'Comprador', 1, 'single', 'actor_form', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'comercial', 'Valor comercial', 5, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'comercial', 'Valor comercial', 1, 'single', 'commercial', now());
    INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active, created_at) VALUES (uuidv7(), v_type, 'fur', 'Firma / FUR', 6, true, now()) RETURNING id INTO v_step;
    INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type, created_at) VALUES (uuidv7(), v_step, 'fur', 'Firma / FUR', 1, 'single', 'signature_fur', now());

    INSERT INTO tramites.conformation_rules (id, procedure_type_id, procedure_entity_id, is_active, sort_order, validation_profile, created_at)
    SELECT uuidv7(), v_type, pe.id, true, 1, '{"allowsNaturalPerson":true,"allowsJuridicalPerson":true,"requiresRunt":true}'::jsonb, now()
    FROM tramites.procedure_entities pe WHERE pe.code = 'OWNER';
    INSERT INTO tramites.conformation_rules (id, procedure_type_id, procedure_entity_id, is_active, sort_order, validation_profile, created_at)
    SELECT uuidv7(), v_type, pe.id, true, 2, '{"allowsNaturalPerson":true,"allowsJuridicalPerson":true,"allowsMultiple":true,"requiresRunt":true,"requiresSimit":true}'::jsonb, now()
    FROM tramites.procedure_entities pe WHERE pe.code = 'BUYER';

    INSERT INTO tramites.procedure_type_sources (procedure_type_id, external_data_source_id, is_active, execution_order, config, created_at)
    SELECT v_type, ds.id, true, 1, '{}'::jsonb, now() FROM tramites.external_data_sources ds WHERE ds.code = 'RUNT';
    INSERT INTO tramites.procedure_type_sources (procedure_type_id, external_data_source_id, is_active, execution_order, config, created_at)
    SELECT v_type, ds.id, true, 2, '{"simitMode":"INTERNAL"}'::jsonb, now() FROM tramites.external_data_sources ds WHERE ds.code = 'SIMIT';
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- PRENDA_INSCRIPCION — entrada PLATE, propietario + acreedor, RUNT, prenda_decision
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE v_type uuid; v_step uuid;
BEGIN
    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, created_at)
    VALUES (uuidv7(), 'PRENDA_INSCRIPCION', 'Prenda — Inscripción', 'prenda', 1,
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
    VALUES (uuidv7(), 'CAMBIO_LOCATARIO', 'Cambio de Locatario', 'leasing', 1,
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
-- DynamicGateEvaluator (BE-06). Regresión E2E de los 4 tipos: pendiente de ejecución en DEV (QA).
-- ─────────────────────────────────────────────────────────────────────────────
