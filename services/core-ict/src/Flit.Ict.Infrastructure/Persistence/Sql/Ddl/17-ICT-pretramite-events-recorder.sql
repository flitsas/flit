-- =============================================================================
-- core-ict — Registrador del timeline de negocio del pre-trámite (HU5 / E1).
-- Escribe ict.pretramite_events (timeline sanitizado; APPEND-ONLY: una fila por evento,
-- a diferencia de record_process_status que colapsa sub-pasos por estado).
--
-- UNA sola ruta de escritura, invocada por: (a) los SPs de validación (PERFORM) para las
-- etapas 100% SQL (en_validacion_negocio / _externa), y (b) el código C# (ExecuteSqlInterpolated)
-- en register/orquestación/edición/reproceso/anulación. Así se esquiva RLS de forma uniforme.
--
-- detail debe llegar SANITIZADO por allowlist (sin PII/tokens): tipos, códigos, mensajes de
-- regla, counts, nombres de campo — nunca document_number/mail/dirección/observation ni payloads
-- crudos de fuentes externas.
--
-- SECURITY DEFINER: los jobs/SPs corren cross-tenant (mismo criterio que record_process_status).
-- clock_timestamp() da orden estrictamente creciente aunque haya varias transiciones en la misma
-- transacción. Idempotente (CREATE OR REPLACE).
-- =============================================================================

CREATE OR REPLACE FUNCTION ict.record_pretramite_event(
    p_master      uuid,
    p_tenant      uuid,
    p_stage       text,
    p_outcome     text DEFAULT 'ok',
    p_detail      jsonb DEFAULT NULL,
    p_correlation uuid DEFAULT NULL)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $BODY$
BEGIN
    INSERT INTO ict.pretramite_events
        (master_id, tenant_id, stage, outcome, detail, correlation_id, created_at)
    VALUES
        (p_master, p_tenant, p_stage, COALESCE(p_outcome, 'ok'), p_detail, p_correlation, clock_timestamp());
END;
$BODY$;
