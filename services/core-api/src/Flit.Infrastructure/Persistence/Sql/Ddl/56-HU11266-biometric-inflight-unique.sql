-- HU #11266 (D12) — índice único parcial anti-carrera de envío de validación de identidad.
-- A lo sumo UNA fila EN VUELO por (tenant, documento normalizado Trim+Upper).
-- Estados cubiertos: pendiente_envio | enviado | en_proceso.
-- Rechazado / expirado / error de envío / aprobado NO entran → permiten un nuevo inicio (AC2).
--
-- Normalización alineada con DocumentCanonicalNormalization (Trim + Upper):
--   upper(btrim(document_type)), upper(btrim(document_number))
-- soft-delete: deleted_at IS NULL (columna añadida en HU #10865).
--
-- Si ya hay duplicados en vuelo, NO se crea el índice (WARNING) — misma estrategia que
-- 53-nit-unico-por-compania.sql — para no tumbar el deploy; el handler ya traduce 23505→409.
-- DDL IDEMPOTENTE.

DO $$
DECLARE
    duplicados integer;
BEGIN
    SELECT count(*) INTO duplicados
    FROM (
        SELECT tenant_id,
               upper(btrim(document_type)) AS doc_type_norm,
               upper(btrim(document_number)) AS doc_number_norm
        FROM tramites.procedure_instance_biometric_validations
        WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
          AND deleted_at IS NULL
        GROUP BY tenant_id, upper(btrim(document_type)), upper(btrim(document_number))
        HAVING count(*) > 1
    ) d;

    IF duplicados = 0 THEN
        CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_doc_norm
            ON tramites.procedure_instance_biometric_validations (
                tenant_id,
                upper(btrim(document_type)),
                upper(btrim(document_number))
            )
            WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso')
              AND deleted_at IS NULL;

        COMMENT ON INDEX tramites.uq_biometric_validations_inflight_doc_norm IS
            'HU #11266 — anti-carrera: una sola validación en vuelo por (tenant, documento Trim+Upper).';
    ELSE
        RAISE WARNING
            'uq_biometric_validations_inflight_doc_norm NO se creó: hay % grupos con más de una validación en vuelo. '
            'Consolida o terminaliza duplicados y vuelve a aplicar este DDL (es idempotente).',
            duplicados;
    END IF;
END $$;
