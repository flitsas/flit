-- =============================================================================
-- Bug #13304 (numeral 12) — el actor del trámite acepta lo mismo que envía ICT.
--
-- ICT admite nombre de hasta 320 y teléfono de hasta 50; core-api tenía
-- procedure_instance_actors.full_name varchar(200) y phone varchar(20), así que al
-- materializar el borrador desde ICT el actor reventaba con 22001.
--
-- Se amplían también procedure_instances.vendedor_nombre / comprador_nombre: los
-- llena el trigger tr_procedure_instance_actors_denorm (DDL 47) con NEW.full_name;
-- sin ampliarlos, el 22001 solo se movería al trigger.
--
-- Y procedure_instance_biometric_validations.name: recibe el nombre del actor al
-- crear la validación de identidad, y se guarda DESPUÉS de crearla en Kyverum; con
-- más de 200 la validación quedaría creada en Kyverum sin registro en FLIT.
-- Descartadas (no reciben ProcedureInstanceActor.full_name): admin.mandate_signers,
-- admin.signature_vault y tramites.persons (captura propia de Admin/prevalidación) y
-- el recipient_name de los despachos de correo (el enqueuer ya trunca a 200).
--
-- Postgres no permite ALTER TYPE de una columna que lee una vista, y
-- analytics.v_procedure_detail_report lee pia.full_name: se elimina, se alteran las
-- columnas y se recrea con la definición vigente EXACTA (DDL 90) y su COMMENT. La
-- vista no tiene GRANTs propios ni otras vistas/matviews/funciones SQL dependen de
-- estas cinco columnas (la función del trigger es plpgsql: no crea dependencia).
--
-- Ampliar un varchar es binario-compatible: sin reescritura de tabla ni de los
-- índices ix_procedure_instances_tenant_id_{vendedor,comprador}_nombre. Los COMMENT
-- @pii:medium de las columnas se conservan con ALTER TYPE.
-- Idempotente: DROP VIEW IF EXISTS + ALTER al mismo tipo es no-op + CREATE OR REPLACE.
-- =============================================================================
DROP VIEW IF EXISTS analytics.v_procedure_detail_report;

ALTER TABLE tramites.procedure_instance_actors
    ALTER COLUMN full_name TYPE varchar(320),
    ALTER COLUMN phone TYPE varchar(50);

ALTER TABLE tramites.procedure_instances
    ALTER COLUMN vendedor_nombre TYPE varchar(320),
    ALTER COLUMN comprador_nombre TYPE varchar(320);

ALTER TABLE tramites.procedure_instance_biometric_validations
    ALTER COLUMN name TYPE varchar(320);

-- Definición vigente de la vista: copia literal del DDL 90 (sin su cabecera).
CREATE OR REPLACE VIEW analytics.v_procedure_detail_report AS
SELECT
    pi.id,
    pi.tenant_id,
    pi.reference_number,
    pi.transit_office_id,
    pi.procedure_type_id,
    pt.name AS procedure_type_name,
    CASE
        WHEN upper(pt.family) = 'MATRICULAS' THEN 'matriculas'
        WHEN upper(pt.family) = 'TRASPASO'   THEN 'traspasos'
        ELSE 'otros'
    END AS category,
    pi.status,
    u.display_name AS created_by_display_name,
    pi.submitted_at,
    pi.completed_at,
    pi.created_at,
    coalesce(person.document_number, '') AS person_document,
    coalesce(person.full_name, '') AS person_full_name,
    coalesce(lower(fv_leasing.value_text) IN ('true', '1', 'si', 'sí'), false) AS is_leasing,
    coalesce(
        lower(fv_color.value_text) IN ('true', '1', 'si', 'sí')
        OR lower(fv_fuel.value_text) IN ('true', '1', 'si', 'sí')
        OR lower(fv_body.value_text) IN ('true', '1', 'si', 'sí')
        OR (prenda.decision IS NOT NULL AND prenda.decision NOT IN ('sin_prenda', 'omitir')),
        false
    ) AS has_transformation,
    nullif(
        trim(both ', ' FROM concat_ws(', ',
            CASE WHEN lower(fv_color.value_text) IN ('true', '1', 'si', 'sí') THEN 'Color' END,
            CASE WHEN lower(fv_fuel.value_text) IN ('true', '1', 'si', 'sí') THEN 'Combustible' END,
            CASE WHEN lower(fv_body.value_text) IN ('true', '1', 'si', 'sí') THEN 'Carrocería' END,
            CASE WHEN prenda.decision IS NOT NULL AND prenda.decision NOT IN ('sin_prenda', 'omitir')
                 THEN 'Prenda' END
        )),
        ''
    ) AS transformation_detail,
    coalesce(commercial.metodo_pago, '') AS payment_type,
    CASE
        -- Fuera de la familia TRASPASO no hay traspaso que clasificar. El CASE no tenía esta
        -- guarda, así que un cambio de color se reportaba como «BILATERAL».
        WHEN upper(pt.family) <> 'TRASPASO' THEN NULL
        -- El tipo manda sobre el indicio. Con dos tipos en el catálogo la clase de traspaso solo
        -- podía deducirse del adjunto o del campo suelto; ahora TRASPASO_UNILATERAL lo dice en su
        -- nombre, y deducir lo contrario hacía que la fila se contradijera con su propio tipo.
        WHEN pt.code = 'TRASPASO_TRANSFERENCIA_DE_DOMINIO' THEN 'TRANSFERENCIA DE DOMINIO'
        WHEN pt.code = 'TRASPASO_UNILATERAL' THEN 'UNILATERAL'
        WHEN domain_att.id IS NOT NULL THEN 'TRANSFERENCIA DE DOMINIO'
        WHEN lower(fv_unilateral.value_text) IN ('true', '1', 'si', 'sí') THEN 'UNILATERAL'
        ELSE 'BILATERAL'
    END AS transfer_type
FROM tramites.procedure_instances pi
JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
JOIN identity.users u ON u.id = pi.created_by_user_id
LEFT JOIN LATERAL (
    SELECT pia.document_number, pia.full_name
    FROM tramites.procedure_instance_actors pia
    WHERE pia.procedure_instance_id = pi.id
      AND pia.actor_type IN ('comprador', 'propietario')
    ORDER BY CASE pia.actor_type WHEN 'comprador' THEN 1 WHEN 'propietario' THEN 2 ELSE 3 END
    LIMIT 1
) person ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'es_leasing'
    LIMIT 1
) fv_leasing ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'cambio_color'
    LIMIT 1
) fv_color ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'cambio_combustible'
    LIMIT 1
) fv_fuel ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'cambio_carroceria'
    LIMIT 1
) fv_body ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'es_unilateral'
    LIMIT 1
) fv_unilateral ON TRUE
LEFT JOIN LATERAL (
    SELECT pip.decision
    FROM tramites.procedure_instance_prenda pip
    WHERE pip.procedure_instance_id = pi.id AND pip.estado = 'vigente'
    LIMIT 1
) prenda ON TRUE
LEFT JOIN tramites.procedure_instance_commercial commercial
    ON commercial.procedure_instance_id = pi.id
LEFT JOIN LATERAL (
    SELECT att.id
    FROM tramites.procedure_instance_attachments att
    WHERE att.procedure_instance_id = pi.id
      AND att.tipo = 'transferencia_dominio'
    LIMIT 1
) domain_att ON TRUE
WHERE pi.deleted_at IS NULL;

COMMENT ON VIEW analytics.v_procedure_detail_report IS
    'HU #10814 — consolidado BI en vivo para reportes detallados (persona, transformación, leasing).';
