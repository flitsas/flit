-- =============================================================================
-- HU #14319 (Feature #14316, Épica #14296) — canal y OT del canal en el reporte detallado.
-- Migración: 20261105140000_HU14319_CanalEnReporteDetallado.
--
-- Recrea analytics.v_procedure_detail_report (DROP + CREATE, patrón del DDL 132) conservando TODA la
-- definición vigente (la del DDL 132, que a su vez es la del 90) y agregando al final:
--   channel                      COALESCE(pi.channel, 'tramites'): una fila sin canal (histórico, incluido el
--                                migrado de FLIT 1.0) se reporta como Trámites; ICT (integracion) NUNCA se
--                                disfraza de Trámites ni de Persona Natural.
--   channel_transit_office_id    organismo del punto de atención (solo pn_punto), NULL en los demás.
--   channel_transit_office_name  nombre del organismo (catalogs.transit_offices.name).
-- Sin dependientes: ninguna otra vista/matview/función SQL lee esta vista (CompanyQueryRepository y
-- AnalyticsFamilyFilter solo la citan en comentarios o por columnas con nombre, que no cambian).
-- Sin índice nuevo sobre channel: baja cardinalidad (5 valores) y el filtro siempre va acotado por tenant +
-- rango de fechas; el filtro por OT ya tiene el índice parcial del DDL 172.
-- Idempotente: DROP VIEW IF EXISTS + CREATE OR REPLACE.
-- =============================================================================
DROP VIEW IF EXISTS analytics.v_procedure_detail_report;

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
    END AS transfer_type,
    -- HU #14319: canal efectivo (NULL = histórico, incluido el migrado de FLIT 1.0, se lee como Trámites)
    -- y el organismo del punto de atención (solo pn_punto).
    COALESCE(pi.channel, 'tramites') AS channel,
    pi.channel_transit_office_id,
    cto.name AS channel_transit_office_name
FROM tramites.procedure_instances pi
JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
JOIN identity.users u ON u.id = pi.created_by_user_id
-- LEFT JOIN por la PK del catálogo: no multiplica ni descarta filas.
LEFT JOIN catalogs.transit_offices cto ON cto.id = pi.channel_transit_office_id
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
    'HU #10814 — consolidado BI en vivo para reportes detallados (persona, transformación, leasing). HU #14319 — canal efectivo (NULL se lee como tramites) y organismo del canal.';
