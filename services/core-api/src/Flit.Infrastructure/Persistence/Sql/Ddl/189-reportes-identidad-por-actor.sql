-- =============================================================================
-- Reportes de la empresa — validación de identidad por persona en el reporte detallado.
-- Migración: 20261111120000_ReportesIdentidadPorActor.
--
-- Recrea analytics.v_procedure_detail_report (DROP + CREATE, patrón del DDL 174) conservando TODA la
-- definición vigente (la del DDL 174) y agregando al final la columna `actors` (jsonb): un arreglo con
-- UNA entrada por persona (comprador/vendedor/propietario) y el resultado de su validación de identidad.
--
-- Reglas (idénticas a ActorValidacionBuilder en C#, que arma la misma lista para la Consulta personalizada):
--   * Rol: la BD solo guarda actores 'comprador' y 'vendedor'. «propietario» se DERIVA de la familia:
--     fuera de TRASPASO el actor guardado como 'comprador' sale como 'propietario'. Otros tipos (locatario)
--     no se emiten.
--   * Enlace actor↔validación (no hay FK): mismo trámite, mismo party_role y documento del SUJETO de
--     identidad (representante legal si el actor es persona jurídica y lo tiene completo; si no, el actor)
--     o, en su defecto, el documento del propio actor. Sin coincidencia por documento y con UN solo actor en
--     el rol, se le atribuyen las validaciones del rol; con 2 o más actores sin coincidencia sale
--     'sin_validacion' (nunca se atribuye la identidad de una persona a otra).
--   * estadoValidacion: estado de la validación más reciente; lo desconocido sale 'sin_validacion'.
--   * intentos: resultados aplicados + capturas recibidas contados en tramites.identity_validation_audit
--     (stage webhook_applied | manual_captura_recibida | reconcile con desenlace aprobado/rechazado), sumando
--     todas las validaciones y todos los ciclos del actor. Sin ninguna fila de bitácora (datos migrados) se
--     suman los attempts de las filas.
--   * rechazo: datos CRUDOS del último rechazo (código de lista cerrada, motivo, subject_motivo,
--     coincidencias). El texto en español del motivo NO se arma aquí: lo traduce DetailedReportReadRepository
--     con IdentityRejectionReasonText, para que haya un solo vocabulario y ninguna mención al proveedor.
-- La columna `person_*` y las demás se conservan intactas. Sin dependientes de la vista.
-- Idempotente: DROP VIEW IF EXISTS + CREATE.
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
    cto.name AS channel_transit_office_name,
    -- Validación de identidad por persona (ver cabecera del DDL).
    act.actors
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
LEFT JOIN LATERAL (
    SELECT coalesce(
        jsonb_agg(
            jsonb_build_object(
                'rol', a.rol_out,
                'ordinal', a.ordinal,
                'nombre', a.full_name,
                'tipoDocumento', nullif(trim(a.document_type), ''),
                'documento', nullif(trim(a.document_number), ''),
                'estadoValidacion', coalesce(a.estado, 'sin_validacion'),
                'intentos', coalesce(a.intentos, 0),
                'rechazo', a.rechazo
            )
            ORDER BY a.orden_rol, a.ordinal
        ),
        '[]'::jsonb
    ) AS actors
    FROM (
        SELECT
            x.ordinal,
            x.full_name,
            x.document_type,
            x.document_number,
            x.rol_out,
            CASE x.rol_out WHEN 'comprador' THEN 0 WHEN 'vendedor' THEN 1 ELSE 2 END AS orden_rol,
            -- Códigos de transporte: los dos estados del flujo asistido se renombran para que esa palabra no viaje en el
            -- JSON (la BD conserva sus valores). Lo que no se reconoce sale 'sin_validacion'.
            CASE
                WHEN last_v.status IN ('aprobado', 'rechazado', 'en_proceso', 'enviado', 'pendiente_envio',
                                       'error_envio', 'expirado')
                    THEN last_v.status
                WHEN last_v.status = 'manual_activo' THEN 'esperando_captura'
                WHEN last_v.status = 'pendiente_revision_manual' THEN 'pendiente_revision'
                ELSE 'sin_validacion'
            END AS estado,
            CASE
                WHEN vm.ids IS NULL THEN 0
                WHEN aud.hay THEN aud.cuenta
                ELSE coalesce(att.total, 0)
            END AS intentos,
            CASE WHEN rej.id IS NULL THEN NULL ELSE jsonb_build_object(
                'codigo', rej.rejection_reason_code,
                'motivo', rej.detail ->> 'motivo',
                'subjectMotivo', rej.provider_payload ->> 'subject_motivo',
                'documentoOk', rej.provider_payload -> 'coincidencias' -> 'documento',
                'nombreOk', rej.provider_payload -> 'coincidencias' -> 'nombre'
            ) END AS rechazo
        FROM (
            SELECT
                pia.ordinal,
                pia.full_name,
                pia.document_type,
                pia.document_number,
                lower(trim(pia.actor_type)) AS rol_db,
                CASE
                    WHEN lower(trim(pia.actor_type)) = 'comprador' AND upper(pt.family) <> 'TRASPASO'
                        THEN 'propietario'
                    ELSE lower(trim(pia.actor_type))
                END AS rol_out,
                -- Documento del SUJETO de identidad: el representante legal de una persona jurídica (si lo tiene
                -- completo); en cualquier otro caso, el propio actor.
                CASE WHEN lower(trim(pia.person_type)) = 'juridical'
                          AND nullif(trim(pia.metadata -> 'representanteLegal' ->> 'tipoDocumento'), '') IS NOT NULL
                          AND nullif(trim(pia.metadata -> 'representanteLegal' ->> 'numeroDocumento'), '') IS NOT NULL
                     THEN trim(pia.metadata -> 'representanteLegal' ->> 'tipoDocumento')
                     ELSE trim(pia.document_type) END AS sujeto_tipo,
                CASE WHEN lower(trim(pia.person_type)) = 'juridical'
                          AND nullif(trim(pia.metadata -> 'representanteLegal' ->> 'tipoDocumento'), '') IS NOT NULL
                          AND nullif(trim(pia.metadata -> 'representanteLegal' ->> 'numeroDocumento'), '') IS NOT NULL
                     THEN trim(pia.metadata -> 'representanteLegal' ->> 'numeroDocumento')
                     ELSE trim(pia.document_number) END AS sujeto_num,
                count(*) OVER (
                    PARTITION BY pia.procedure_instance_id, lower(trim(pia.actor_type))
                ) AS actores_del_rol
            FROM tramites.procedure_instance_actors pia
            WHERE pia.procedure_instance_id = pi.id
              AND lower(trim(pia.actor_type)) IN ('comprador', 'vendedor')
        ) x
        -- Validaciones del actor: por documento; si ninguna coincide y es el único del rol, las del rol.
        LEFT JOIN LATERAL (
            SELECT coalesce(
                array_agg(v.id) FILTER (WHERE
                    (upper(trim(v.document_number)) = upper(x.sujeto_num)
                        AND (nullif(trim(v.document_type), '') IS NULL OR nullif(x.sujeto_tipo, '') IS NULL
                             OR upper(trim(v.document_type)) = upper(x.sujeto_tipo)))
                    OR
                    (upper(trim(v.document_number)) = upper(trim(x.document_number))
                        AND (nullif(trim(v.document_type), '') IS NULL OR nullif(trim(x.document_type), '') IS NULL
                             OR upper(trim(v.document_type)) = upper(trim(x.document_type))))
                ),
                CASE WHEN x.actores_del_rol = 1 THEN array_agg(v.id) END
            ) AS ids
            FROM tramites.procedure_instance_biometric_validations v
            WHERE v.procedure_instance_id = pi.id
              AND lower(trim(v.party_role)) = x.rol_db
        ) vm ON TRUE
        LEFT JOIN LATERAL (
            SELECT v.status
            FROM tramites.procedure_instance_biometric_validations v
            WHERE v.id = ANY (vm.ids)
            ORDER BY v.created_at DESC, coalesce(v.updated_at, v.created_at) DESC
            LIMIT 1
        ) last_v ON TRUE
        LEFT JOIN LATERAL (
            SELECT v.id, v.rejection_reason_code, v.detail, v.provider_payload
            FROM tramites.procedure_instance_biometric_validations v
            WHERE v.id = ANY (vm.ids) AND v.status = 'rechazado'
            ORDER BY coalesce(v.updated_at, v.created_at) DESC
            LIMIT 1
        ) rej ON TRUE
        LEFT JOIN LATERAL (
            SELECT
                count(*) > 0 AS hay,
                count(*) FILTER (WHERE au.stage IN ('webhook_applied', 'manual_captura_recibida')
                    OR (au.stage = 'reconcile' AND au.outcome IN ('aprobado', 'rechazado'))) AS cuenta
            FROM tramites.identity_validation_audit au
            WHERE au.validation_id = ANY (vm.ids)
        ) aud ON TRUE
        LEFT JOIN LATERAL (
            SELECT sum(greatest(v.attempts, 0))::int AS total
            FROM tramites.procedure_instance_biometric_validations v
            WHERE v.id = ANY (vm.ids)
        ) att ON TRUE
    ) a
) act ON TRUE
WHERE pi.deleted_at IS NULL;

COMMENT ON VIEW analytics.v_procedure_detail_report IS
    'HU #10814 — consolidado BI en vivo para reportes detallados (persona, transformación, leasing). HU #14319 — canal efectivo (NULL se lee como tramites) y organismo del canal. Reportes — columna actors: validación de identidad por persona.';
