-- =============================================================================
-- core-ict — sp_processor_validation_external (HU3) — PORTADO de FLIT 1.0.
-- Construye las consultas a fuentes externas (source_query) para los pre-trámites con
-- negocio validado. Adaptado a schema ict.* + tenant_id.
-- TODO(ICT-SP-SEQUENCE): honrar completamente ict.external_integration_sequence (aquí se
-- construyen las consultas núcleo por tipo: VEHICLE/VIN + actor MAIN del vendedor).
-- =============================================================================

-- Índice PARCIAL de apoyo al drenado batcheado (mismo criterio que el SP de negocio): solo indexa las
-- filas PENDIENTES de validación externa, para que cada lote (LIMIT + ORDER BY created_at) sea un
-- index-scan barato. Se auto-mantiene pequeño (las filas salen del índice al pasar external_validation a 2).
CREATE INDEX IF NOT EXISTS ix_eim_pending_external
    ON ict.external_integration_master (created_at)
    WHERE external_validation = 0 AND process_status_id = 2 AND business_validation = 2 AND deleted_at IS NULL;

CREATE OR REPLACE PROCEDURE ict.sp_processor_validation_external()
LANGUAGE plpgsql
SECURITY DEFINER
AS $BODY$
DECLARE
    rec RECORD;
    v_batch_size integer;
BEGIN
    -- Tamaño de lote configurable EN CALIENTE (ict.job_settings.external_batch_size), default 500. Cada CALL
    -- procesa A LO SUMO v_batch_size filas en UNA transacción y retorna; el job (ExternalValidationJob) lo
    -- re-invoca en bucle hasta drenar. Igual que el SP de negocio: SIN COMMIT dentro del procedimiento
    -- (Postgres no lo permite aquí: SECURITY DEFINER + FOR sobre query).
    SELECT COALESCE(external_batch_size, 500) INTO v_batch_size FROM ict.job_settings WHERE id = 1;
    IF v_batch_size IS NULL OR v_batch_size < 1 THEN
        v_batch_size := 500;
    END IF;

    FOR rec IN
        SELECT m.id AS id_master, m.tenant_id, m.transaction_type, m.plate, m.vin,
               m.manager_user, m.manager_mail, m.company_manager_document,
               m.manager_id_transaction, m.url_web_hook
        FROM ict.external_integration_master m
        WHERE m.external_validation = 0 AND m.process_status_id = 2 AND m.business_validation = 2
          AND m.deleted_at IS NULL
        ORDER BY m.created_at
        LIMIT v_batch_size
    LOOP
        UPDATE ict.external_integration_master
        SET external_validation = 1, external_date_validation = now()
        WHERE id = rec.id_master;

        PERFORM ict.record_process_status(rec.id_master, rec.tenant_id, 2, 'IDENTIFICANDO FUENTES',
            rec.manager_user, rec.manager_mail, rec.company_manager_document);

        -- Consulta de vehículo por placa (traspasos) o por VIN (matrículas). La consulta por placa
        -- en RUNT necesita el documento del propietario/vendedor, así que se adjunta al source_query.
        IF rec.transaction_type IN (3, 4) AND rec.plate <> '' THEN
            INSERT INTO ict.external_integration_source_query
                (eim_id, tenant_id, actor_level, query_type, plate_complete, document_type, document_number)
            VALUES (rec.id_master, rec.tenant_id, 'VEHI', 'VEHICLE', rec.plate,
                COALESCE((SELECT eia.document_type FROM ict.external_integration_actors eia
                          WHERE eia.master_id = rec.id_master AND eia.actor_type = 'seller'
                          ORDER BY eia.document_number LIMIT 1), ''),
                COALESCE((SELECT eia.document_number FROM ict.external_integration_actors eia
                          WHERE eia.master_id = rec.id_master AND eia.actor_type = 'seller'
                          ORDER BY eia.document_number LIMIT 1), ''));
        END IF;

        IF rec.transaction_type IN (1, 2) AND COALESCE(rec.vin, '') <> '' THEN
            INSERT INTO ict.external_integration_source_query
                (eim_id, tenant_id, actor_level, query_type, vehicle_vin)
            VALUES (rec.id_master, rec.tenant_id, 'VEHI', 'VIN', rec.vin);
        END IF;

        -- RNMC (medidas correctivas) del actor principal (vendedor). Solo personas naturales.
        INSERT INTO ict.external_integration_source_query
            (eim_id, tenant_id, eia_id, actor_level, query_type, document_type, document_number)
        SELECT rec.id_master, rec.tenant_id, eia.id, 'MAIN', 'RNMC', eia.document_type, eia.document_number
        FROM ict.external_integration_actors eia
        WHERE eia.master_id = rec.id_master AND eia.actor_type = 'seller' AND eia.document_type <> 'NIT';

        -- DRIVER (paz y salvo del conductor) para VENDEDOR (MAIN) y COMPRADOR (ASSI). Calcado de v1
        -- (BackApiExternalTransactValiQueryExt: DRIVER_MAIN / DRIVER_ASSI). Solo personas naturales (el
        -- paz y salvo aplica a la licencia; un NIT no conduce). El INSERT...SELECT produce 0 filas si el
        -- actor no existe (p. ej. matrícula sin comprador). La novedad de paz y salvo es INFORMATIVA: NO
        -- bloquea el paso a borrador (ver ExternalSourceValidators.Warnings), fiel a v1 (validateDriverRequest
        -- registra la novedad y retorna OK, no un error).
        INSERT INTO ict.external_integration_source_query
            (eim_id, tenant_id, eia_id, actor_level, query_type, document_type, document_number)
        SELECT rec.id_master, rec.tenant_id, eia.id,
               CASE WHEN eia.actor_type = 'seller' THEN 'MAIN' ELSE 'ASSI' END,
               'DRIVER', eia.document_type, eia.document_number
        FROM ict.external_integration_actors eia
        WHERE eia.master_id = rec.id_master AND eia.actor_type IN ('seller', 'buyer') AND eia.document_type <> 'NIT';

        -- Fin: fuentes identificadas.
        UPDATE ict.external_integration_master
        SET external_validation = 2, external_date_validation = now()
        WHERE id = rec.id_master;

        PERFORM ict.record_process_status(rec.id_master, rec.tenant_id, 2, 'IDENTIFICADAS FUENTES',
            rec.manager_user, rec.manager_mail, rec.company_manager_document);

        PERFORM ict.record_pretramite_event(rec.id_master, rec.tenant_id,
            'en_validacion_externa', 'ok',
            jsonb_build_object('transaction_type', rec.transaction_type));
    END LOOP;
END;
$BODY$;
