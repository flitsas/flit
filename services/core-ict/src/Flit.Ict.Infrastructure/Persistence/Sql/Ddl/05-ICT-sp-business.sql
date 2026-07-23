-- =============================================================================
-- core-ict — sp_processor_validation_business (HU3) — PORTADO de FLIT 1.0.
-- Adaptaciones: schema ict.*, company_manager_id -> tenant_id, disponibilidad contra la
-- tabla única tramites.procedure_instances + procedure_instance_field_values, secretarías
-- contra catalogs.transit_offices y grants en admin.tenant_transit_office_grants.
-- SECURITY DEFINER: los jobs corren cross-tenant; el rol dueño debe poder saltar RLS
-- (superusuario en dev; BYPASSRLS en producción). TODO(ICT-RLS-SP): rol dedicado.
-- TODO(ICT-SP-FIELDS): completar desde el fuente v1 las validaciones exhaustivas de
-- representante legal / mandante principal (aquí se portan las validaciones núcleo).
-- =============================================================================

CREATE OR REPLACE PROCEDURE ict.sp_processor_validation_business()
LANGUAGE plpgsql
SECURITY DEFINER
AS $BODY$
DECLARE
    rec RECORD;
    resultcomments text;
BEGIN
    FOR rec IN
        SELECT m.id AS id_master, m.tenant_id, m.manager_id_transaction, m.transaction_type,
               m.manager_user, m.manager_mail, m.company_manager_document, m.closed_document,
               m.process_without_attached_documents, m.traffic_secretary_code, m.url_web_hook
        FROM ict.external_integration_master m
        WHERE m.business_validation = 0 AND m.process_status_id = 1 AND m.deleted_at IS NULL
    LOOP
        -- Inicio: en validación de negocio.
        UPDATE ict.external_integration_master
        SET process_status_id = 2, business_validation = 1, business_date_validation = now()
        WHERE id = rec.id_master;

        PERFORM ict.record_process_status(rec.id_master, rec.tenant_id, 2, 'VALIDANDO REGLAS DE NEGOCIO',
            rec.manager_user, rec.manager_mail, rec.company_manager_document);

        -- ===== Sección Vehículo =====
        IF rec.transaction_type IN (3, 4) THEN
            UPDATE ict.external_integration_master
            SET business_comments_validation = business_comments_validation || ' plate tiene un formato errado;'
            WHERE id = rec.id_master
              AND NOT (plate ~* '^[A-Z]{3}\d{3}$' OR plate ~* '^[A-Z]{3}\d{2}[A-Z]{0,1}$'
                    OR plate ~* '^[A-Z]{2}\d{6}$' OR plate ~* '^[A-Z]{1}\d{5}$' OR plate ~* '^\d{3}[A-Z]{3}$');
        END IF;

        IF rec.transaction_type IN (1, 2) THEN
            UPDATE ict.external_integration_master eim
            SET business_comments_validation = business_comments_validation || ' traffic_secretary_code no es valido o no esta activo;'
            WHERE eim.id = rec.id_master AND eim.traffic_secretary_code <> ''
              AND NOT EXISTS (SELECT 1 FROM catalogs.transit_offices ts
                              WHERE ts.code = eim.traffic_secretary_code AND ts.is_active = true);

            UPDATE ict.external_integration_master
            SET business_comments_validation = business_comments_validation || ' vin tiene caracteres no validos;'
            WHERE id = rec.id_master AND length(COALESCE(vin, '')) NOT BETWEEN 16 AND 18;
        END IF;

        -- ===== Sección Vendedor (seller) =====
        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' seller/document_number invalido;'
        FROM ict.external_integration_actors eia
        WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'seller'
          AND eia.document_number !~ '^\d{6,11}$';

        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' seller/name no puede estar vacio;'
        FROM ict.external_integration_actors eia
        WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'seller'
          AND length(TRIM(eia.name)) = 0;

        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' seller/first_last_name no puede estar vacio;'
        FROM ict.external_integration_actors eia
        WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'seller'
          AND eia.document_type <> 'NIT' AND length(TRIM(eia.first_last_name)) = 0;

        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' seller/email formato invalido;'
        FROM ict.external_integration_actors eia
        WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'seller'
          AND eia.email !~ '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$';

        -- ===== Sección Comprador (buyer), solo traspaso (3) =====
        IF rec.transaction_type = 3 THEN
            UPDATE ict.external_integration_master eim
            SET business_comments_validation = business_comments_validation || ' buyer/document_number invalido;'
            FROM ict.external_integration_actors eia
            WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'buyer'
              AND eia.document_number !~ '^\d{6,11}$';

            UPDATE ict.external_integration_master eim
            SET business_comments_validation = business_comments_validation || ' buyer/name no puede estar vacio;'
            FROM ict.external_integration_actors eia
            WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'buyer'
              AND length(TRIM(eia.name)) = 0;

            UPDATE ict.external_integration_master eim
            SET business_comments_validation = business_comments_validation || ' buyer/email formato invalido;'
            FROM ict.external_integration_actors eia
            WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'buyer'
              AND eia.email !~ '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$';
        END IF;

        -- ===== Sección Locatario (lessee): 2, 4, 8 =====
        IF rec.transaction_type IN (2, 4, 8) THEN
            UPDATE ict.external_integration_master eim
            SET business_comments_validation = business_comments_validation || ' lessee/document_number invalido;'
            FROM ict.external_integration_actors eia
            WHERE eim.id = eia.master_id AND eim.id = rec.id_master AND eia.actor_type = 'lessee'
              AND eia.document_number !~ '^\d{6,11}$';
        END IF;

        -- ===== Sección General =====
        UPDATE ict.external_integration_master
        SET business_comments_validation = business_comments_validation || ' delivery_address no debe ir vacio;'
        WHERE id = rec.id_master AND length(TRIM(delivery_address)) = 0;

        IF rec.transaction_type = 3 THEN
            UPDATE ict.external_integration_master
            SET business_comments_validation = business_comments_validation || ' selling_date vacio o formato no valido (dd-mm-yyyy);'
            WHERE id = rec.id_master
              AND ((length(TRIM(selling_date)) > 0 AND selling_date !~ '^\d{2}-\d{2}-\d{4}$')
                   OR length(TRIM(selling_date)) = 0);
        END IF;

        -- ===== Sección Integración =====
        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' company_manager_document no corresponde a la compania;'
        WHERE eim.id = rec.id_master AND eim.company_manager_document <> ''
          AND NOT EXISTS (SELECT 1 FROM identity.tenants t
                          WHERE t.id = eim.tenant_id AND t.tax_id = eim.company_manager_document);

        UPDATE ict.external_integration_master
        SET business_comments_validation = business_comments_validation || ' manager_mail vacio o invalido;'
        WHERE id = rec.id_master
          AND (length(TRIM(manager_mail)) = 0
               OR manager_mail !~ '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$');

        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' transaction_operation invalido;'
        WHERE eim.id = rec.id_master
          AND NOT EXISTS (SELECT 1 FROM ict.external_integration_operation_type o WHERE o.id = eim.transaction_operation);

        UPDATE ict.external_integration_master eim
        SET business_comments_validation = business_comments_validation || ' transaction_type invalido;'
        WHERE eim.id = rec.id_master
          AND NOT EXISTS (SELECT 1 FROM ict.procedure_type_mapping m WHERE m.external_transaction_type = eim.transaction_type);

        -- ===== Disponibilidad (adaptada a tabla única de trámites v2) =====
        -- VIN activo (matrículas 1/2).
        IF rec.transaction_type IN (1, 2) THEN
            IF EXISTS (
                SELECT 1 FROM tramites.procedure_instances pi
                JOIN tramites.procedure_instance_field_values fv
                  ON fv.procedure_instance_id = pi.id AND fv.field_key = 'vin'
                WHERE fv.value_text = (SELECT vin FROM ict.external_integration_master WHERE id = rec.id_master)
                  AND pi.status NOT IN ('anulado', 'rechazado') AND pi.deleted_at IS NULL
            ) THEN
                UPDATE ict.external_integration_master
                SET business_comments_validation = business_comments_validation || ' Ya existe un tramite activo para el VIN;'
                WHERE id = rec.id_master;
            END IF;

            -- Secretaría habilitada para la compañía (grant positivo v2).
            IF NOT EXISTS (
                SELECT 1 FROM admin.tenant_transit_office_grants g
                JOIN catalogs.transit_offices ts ON ts.id = g.transit_office_id
                WHERE g.tenant_id = rec.tenant_id AND ts.code = rec.traffic_secretary_code AND g.is_enabled = true
            ) AND rec.traffic_secretary_code <> '' THEN
                UPDATE ict.external_integration_master
                SET business_comments_validation = business_comments_validation || ' La secretaria no esta habilitada para la compania;'
                WHERE id = rec.id_master;
            END IF;
        END IF;

        -- Placa activa (traspasos 3/4).
        IF rec.transaction_type IN (3, 4) THEN
            IF EXISTS (
                SELECT 1 FROM tramites.procedure_instances pi
                JOIN tramites.procedure_instance_field_values fv
                  ON fv.procedure_instance_id = pi.id AND fv.field_key = 'plate'
                WHERE fv.value_text = (SELECT plate FROM ict.external_integration_master WHERE id = rec.id_master)
                  AND pi.status NOT IN ('anulado', 'rechazado') AND pi.deleted_at IS NULL
            ) THEN
                UPDATE ict.external_integration_master
                SET business_comments_validation = business_comments_validation || ' Ya existe un tramite activo para la placa;'
                WHERE id = rec.id_master;
            END IF;
        END IF;

        -- Otros trámites (5-16): aproximación por procedure_type_id (v2 no tiene requested_process).
        -- TODO(ICT-DISP-5-16): validar granularidad con negocio.
        IF rec.transaction_type BETWEEN 5 AND 16 THEN
            IF EXISTS (
                SELECT 1 FROM tramites.procedure_instances pi
                JOIN tramites.procedure_instance_field_values fv
                  ON fv.procedure_instance_id = pi.id AND fv.field_key = 'plate'
                JOIN ict.procedure_type_mapping m ON m.procedure_type_code = (
                    SELECT pt.code FROM tramites.procedure_types pt WHERE pt.id = pi.procedure_type_id)
                WHERE fv.value_text = (SELECT plate FROM ict.external_integration_master WHERE id = rec.id_master)
                  AND m.external_transaction_type = rec.transaction_type
                  AND pi.status NOT IN ('anulado', 'rechazado') AND pi.deleted_at IS NULL
            ) THEN
                UPDATE ict.external_integration_master
                SET business_comments_validation = business_comments_validation || ' Ya existe un tramite activo de este tipo para la placa;'
                WHERE id = rec.id_master;
            END IF;
        END IF;

        -- ===== Cierre: novedad o validado =====
        SELECT TRIM(business_comments_validation) INTO resultcomments
        FROM ict.external_integration_master WHERE id = rec.id_master;

        IF length(COALESCE(resultcomments, '')) = 0 THEN
            UPDATE ict.external_integration_master
            SET business_validation = 2, business_date_validation = now()
            WHERE id = rec.id_master;

            INSERT INTO ict.external_integration_webhook_master
                (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                 message_validation, ict_estado, target_url)
            VALUES (rec.id_master, rec.tenant_id, rec.manager_id_transaction, rec.transaction_type, 2,
                 'REGLAS DE NEGOCIO VALIDADAS SATISFACTORIAMENTE', 'en_validacion_externa', rec.url_web_hook);

            PERFORM ict.record_process_status(rec.id_master, rec.tenant_id, 2,
                'REGLAS DE NEGOCIO VALIDADAS SATISFACTORIAMENTE',
                rec.manager_user, rec.manager_mail, rec.company_manager_document);
        ELSE
            UPDATE ict.external_integration_master
            SET business_validation = 2, business_date_validation = now(), process_status_id = 4
            WHERE id = rec.id_master;

            INSERT INTO ict.external_integration_webhook_master
                (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                 message_validation, ict_estado, target_url)
            VALUES (rec.id_master, rec.tenant_id, rec.manager_id_transaction, rec.transaction_type, 4,
                 'CON NOVEDADES VALIDANDO REGLAS DE NEGOCIO: ' || resultcomments, 'con_novedades', rec.url_web_hook);

            PERFORM ict.record_process_status(rec.id_master, rec.tenant_id, 4,
                'CON NOVEDADES VALIDANDO REGLAS DE NEGOCIO: ' || resultcomments,
                rec.manager_user, rec.manager_mail, rec.company_manager_document);
        END IF;
    END LOOP;
END;
$BODY$;
