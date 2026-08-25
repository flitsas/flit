-- =============================================================================
-- La clase de traspaso deja de inventarse fuera de la familia TRASPASO.
--
-- `transfer_type` se calculaba para TODAS las filas sin mirar la familia, así que
-- caía siempre en el ELSE: en Reportes Detallados un «Cambio de color» y un
-- «Duplicado de placa» aparecían con Traspaso = BILATERAL. Con dos tipos en el
-- catálogo el ruido era invisible —casi todo era traspaso—; con los veintiuno de
-- ADR-0050 la columna miente en la mayoría de las filas.
--
-- Y al revés: un TRASPASO_UNILATERAL se reportaba como BILATERAL, porque la clase
-- se deducía del adjunto de transferencia o del campo suelto `es_unilateral`, que
-- nadie llena cuando el tipo ya lo dice. Desde ADR-0050 el tipo ES la fuente de
-- verdad de la conformación, así que aquí también manda él y el indicio queda de
-- respaldo para TRASPASO_STANDARD.
--
-- Redefine con CREATE OR REPLACE (mismas columnas y tipos). `transfer_type` pasa a
-- admitir NULL, que es lo correcto: no es que no se sepa la clase, es que la
-- pregunta no aplica.
-- =============================================================================

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
