-- =============================================================================
-- core-ict — Registrador del histórico de estados (compatibilidad v1).
-- v1 mantenía UNA fila por estado en statusProcess[] (Registrado, En Validacion,
-- Procesado, Con Novedades, Borrador...). El pipeline v2 hace varios sub-pasos dentro
-- de un mismo estado (p.ej. "VALIDANDO REGLAS" + "REGLAS VALIDADAS" + "IDENTIFICANDO
-- FUENTES" + "IDENTIFICADAS FUENTES" son todos el estado 2 "En Validacion"): si cada
-- sub-paso insertara una fila, el cliente vería el estado repetido.
--
-- ict.record_process_status colapsa esos sub-pasos: solo INSERTA una fila cuando el
-- estado cambia respecto al último; si es el mismo estado, REFRESCA el mensaje/fecha de
-- la fila vigente. Usa clock_timestamp() (no now()) para que cada transición tenga una
-- marca estrictamente creciente y el orden del histórico sea determinista aunque varias
-- transiciones ocurran dentro de la misma transacción (p.ej. 2 -> 4 en el SP de negocio).
--
-- SECURITY DEFINER: los jobs corren cross-tenant (mismo criterio que los SP portados).
-- Idempotente (CREATE OR REPLACE).
-- =============================================================================

CREATE OR REPLACE FUNCTION ict.record_process_status(
    p_master        uuid,
    p_tenant        uuid,
    p_code          integer,
    p_message       text,
    p_user          text DEFAULT '',
    p_mail          text DEFAULT '',
    p_company       text DEFAULT '',
    p_observation   text DEFAULT '',
    p_role          text DEFAULT 'integration')
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $BODY$
DECLARE
    v_last_id   uuid;
    v_last_code smallint;
BEGIN
    -- Estado más reciente del histórico de este pre-trámite.
    SELECT s.id, s.id_parprosta
    INTO v_last_id, v_last_code
    FROM ict.external_integration_process_status s
    WHERE s.id_eimas = p_master
    ORDER BY s.status_process_registrationdate DESC, s.created_at DESC
    LIMIT 1;

    IF v_last_code IS DISTINCT FROM p_code THEN
        -- Transición: nueva fila (una por estado, como v1).
        INSERT INTO ict.external_integration_process_status
            (id_eimas, tenant_id, id_parprosta, message_validation, status_process_observation,
             status_process_registrationdate, status_process_userregistered,
             status_process_mail_userregistered, status_process_company_registered,
             status_process_role_userregistered)
        VALUES (p_master, p_tenant, p_code, COALESCE(p_message, ''), COALESCE(p_observation, ''),
             clock_timestamp(), COALESCE(p_user, ''), COALESCE(p_mail, ''),
             COALESCE(p_company, ''), COALESCE(p_role, 'integration'));
    ELSE
        -- Mismo estado (sub-paso): refresca la fila vigente sin duplicar.
        UPDATE ict.external_integration_process_status
        SET message_validation = COALESCE(p_message, ''),
            status_process_observation = COALESCE(p_observation, ''),
            status_process_registrationdate = clock_timestamp()
        WHERE id = v_last_id;
    END IF;
END;
$BODY$;
