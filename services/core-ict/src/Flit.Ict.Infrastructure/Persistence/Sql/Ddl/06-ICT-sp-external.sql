-- =============================================================================
-- core-ict — sp_processor_validation_external (HU3) — PORTADO de FLIT 1.0.
-- Construye las consultas a fuentes externas (source_query) para los pre-trámites con
-- negocio validado. Adaptado a schema ict.* + tenant_id.
-- TODO(ICT-SP-SEQUENCE): honrar completamente ict.external_integration_sequence (aquí se
-- construyen las consultas núcleo por tipo: VEHICLE/VIN + actor MAIN del vendedor).
-- =============================================================================

CREATE OR REPLACE PROCEDURE ict.sp_processor_validation_external()
LANGUAGE plpgsql
SECURITY DEFINER
AS $BODY$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT m.id AS id_master, m.tenant_id, m.transaction_type, m.plate, m.vin,
               m.manager_user, m.manager_mail, m.company_manager_document,
               m.manager_id_transaction, m.url_web_hook
        FROM ict.external_integration_master m
        WHERE m.external_validation = 0 AND m.process_status_id = 2 AND m.business_validation = 2
          AND m.deleted_at IS NULL
    LOOP
        UPDATE ict.external_integration_master
        SET external_validation = 1, external_date_validation = now()
        WHERE id = rec.id_master;

        INSERT INTO ict.external_integration_process_status
            (id_eimas, tenant_id, id_parprosta, message_validation, status_process_userregistered,
             status_process_mail_userregistered, status_process_company_registered)
        VALUES (rec.id_master, rec.tenant_id, 2, 'IDENTIFICANDO FUENTES',
             rec.manager_user, rec.manager_mail, rec.company_manager_document);

        -- Consulta de vehículo por placa (traspasos) o por VIN (matrículas).
        IF rec.transaction_type IN (3, 4) AND rec.plate <> '' THEN
            INSERT INTO ict.external_integration_source_query
                (eim_id, tenant_id, actor_level, query_type, plate_complete)
            VALUES (rec.id_master, rec.tenant_id, 'VEHI', 'VEHICLE', rec.plate);
        END IF;

        IF rec.transaction_type IN (1, 2) AND COALESCE(rec.vin, '') <> '' THEN
            INSERT INTO ict.external_integration_source_query
                (eim_id, tenant_id, actor_level, query_type, vehicle_vin)
            VALUES (rec.id_master, rec.tenant_id, 'VEHI', 'VIN', rec.vin);
        END IF;

        -- Consulta de actor principal (vendedor) — RNMC/DRIVER.
        INSERT INTO ict.external_integration_source_query
            (eim_id, tenant_id, eia_id, actor_level, query_type, document_type, document_number)
        SELECT rec.id_master, rec.tenant_id, eia.id, 'MAIN', 'RNMC', eia.document_type, eia.document_number
        FROM ict.external_integration_actors eia
        WHERE eia.master_id = rec.id_master AND eia.actor_type = 'seller' AND eia.document_type <> 'NIT';

        -- Fin: fuentes identificadas.
        UPDATE ict.external_integration_master
        SET external_validation = 2, external_date_validation = now()
        WHERE id = rec.id_master;

        INSERT INTO ict.external_integration_process_status
            (id_eimas, tenant_id, id_parprosta, message_validation, status_process_userregistered,
             status_process_mail_userregistered, status_process_company_registered)
        VALUES (rec.id_master, rec.tenant_id, 2, 'IDENTIFICADAS FUENTES',
             rec.manager_user, rec.manager_mail, rec.company_manager_document);
    END LOOP;
END;
$BODY$;
