-- HU #13074 (Feature #13062, Épica #12737) — Propagación de cambios de datos relacionados y
-- asignación inicial de la versión de sincronización.
--
-- Sobre el DDL 122 (HU #13073):
--   1. Un cambio en actores, campos del vehículo, historial de estados, adjuntos o datos comerciales
--      también mueve la versión del trámite (AC1).
--   2. La versión se asigna UNA vez por transacción y trámite: un guardado que toca el trámite y
--      varias filas relacionadas —en una o en varias sentencias, con o sin los triggers de
--      denormalización del DDL 47— produce un único incremento (AC2, AC4).
--   3. El histórico recibe versión en orden de creación, sin auditoría ni row_version (AC3).
--
-- ── Por qué el «toque» al trámite NO sube row_version ni escribe auditoría ──────────────────────
-- row_version es el token de concurrencia de EF (ProcedureInstanceConfiguration). Si el UPDATE que
-- propaga un cambio de una tabla hija lo subiera, un SaveChanges que guarda el trámite y una fila
-- hija juntos (transición de estado + historial, por ejemplo) enviaría el UPDATE del trámite con un
-- token obsoleto según el orden en que EF emita los comandos, y reventaría con
-- DbUpdateConcurrencyException: el mismo fallo que ya esquivan a mano ConsolidadoVigenciaTracker y
-- OtClientProcedureRepository.AssignPlateAsync con los denormalizados del DDL 47. Y trg_audit_log
-- guardaría la fila completa del trámite en audit.audit_logs por cada cambio de una hija, que ya
-- queda auditado en su propia tabla.
-- Por eso row_version y auditoría del trámite se saltan cuando lo ÚNICO que cambia son las columnas
-- de sincronización (fn_procedure_instance_solo_sync). Cualquier otro UPDATE se comporta como antes.
--
-- Idempotente y reaplicable.

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Transacción que selló la versión
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS sync_xact xid8 NULL;

COMMENT ON COLUMN tramites.procedure_instances.sync_xact IS
    'Transacción que asignó la sync_version vigente. Permite sellar una sola vez por transacción. La asigna el trigger tr_procedure_instances_sync_stamp. HU #13074.';

-- Una vez por transacción: si esta misma transacción ya selló el trámite, se conservan la versión y
-- la fecha. Se restauran desde OLD, así que tampoco dentro de la misma transacción se puede fijar la
-- versión a mano (AC3 de la HU #13073).
CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND OLD.sync_xact = pg_current_xact_id() THEN
        NEW.sync_version    := OLD.sync_version;
        NEW.sync_changed_at := OLD.sync_changed_at;
        NEW.sync_xact       := OLD.sync_xact;
    ELSE
        NEW.sync_version    := nextval('tramites.procedure_sync_seq');
        NEW.sync_changed_at := now();
        NEW.sync_xact       := pg_current_xact_id();
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. row_version y auditoría del trámite ignoran los cambios solo de sincronización
-- ─────────────────────────────────────────────────────────────────────────────
-- true cuando OLD y NEW difieren ÚNICAMENTE en columnas sync_*. Un UPDATE que no cambia nada sigue
-- devolviendo false (row_version sube y se audita, como siempre).
CREATE OR REPLACE FUNCTION tramites.fn_procedure_instance_solo_sync(
    o tramites.procedure_instances, n tramites.procedure_instances) RETURNS boolean AS $$
    SELECT (to_jsonb(o) - 'sync_version' - 'sync_changed_at' - 'sync_xact')
         = (to_jsonb(n) - 'sync_version' - 'sync_changed_at' - 'sync_xact')
       AND to_jsonb(o) <> to_jsonb(n);
$$ LANGUAGE sql STABLE;

DROP TRIGGER IF EXISTS tr_procedure_instances_row_version ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_row_version BEFORE UPDATE ON tramites.procedure_instances
    FOR EACH ROW WHEN (NOT tramites.fn_procedure_instance_solo_sync(OLD, NEW))
    EXECUTE FUNCTION public.trg_row_version();

-- Un WHEN que usa OLD y NEW no puede ir en un trigger que también dispara en INSERT o DELETE:
-- la auditoría se parte en dos triggers con la misma función.
DROP TRIGGER IF EXISTS tr_procedure_instances_audit ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_audit AFTER INSERT OR DELETE ON tramites.procedure_instances
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
DROP TRIGGER IF EXISTS tr_procedure_instances_audit_update ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_audit_update AFTER UPDATE ON tramites.procedure_instances
    FOR EACH ROW WHEN (NOT tramites.fn_procedure_instance_solo_sync(OLD, NEW))
    EXECUTE FUNCTION public.trg_audit_log();

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Propagación desde las tablas hijas
-- ─────────────────────────────────────────────────────────────────────────────
-- A nivel de SENTENCIA y con transition tables: una sentencia que toca N filas de un mismo trámite
-- hace un solo UPDATE de ese trámite. Los trámites que esta transacción ya selló no se tocan (su
-- versión no cambiaría y el UPDATE sería trabajo en vano).
-- Sin guard de pg_trigger_depth(): ningún trigger del trámite escribe en estas tablas, así que no
-- hay ciclo posible, y un guard dejaría fuera escrituras legítimas hechas desde otros triggers.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_child_sync_touch() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        UPDATE tramites.procedure_instances p SET sync_changed_at = now()
         WHERE p.id IN (SELECT procedure_instance_id FROM new_rows)
           AND p.sync_xact IS DISTINCT FROM pg_current_xact_id();
    ELSIF TG_OP = 'UPDATE' THEN
        -- Las dos: una fila que cambia de trámite mueve a ambos.
        UPDATE tramites.procedure_instances p SET sync_changed_at = now()
         WHERE p.id IN (SELECT procedure_instance_id FROM new_rows
                        UNION SELECT procedure_instance_id FROM old_rows)
           AND p.sync_xact IS DISTINCT FROM pg_current_xact_id();
    ELSE
        UPDATE tramites.procedure_instances p SET sync_changed_at = now()
         WHERE p.id IN (SELECT procedure_instance_id FROM old_rows)
           AND p.sync_xact IS DISTINCT FROM pg_current_xact_id();
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- PostgreSQL exige un trigger por evento cuando se usan transition tables.
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'procedure_instance_actors',
        'procedure_instance_field_values',
        'procedure_instance_status_history',
        'procedure_instance_attachments',
        'procedure_instance_commercial']
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_ins ON tramites.%I', t, t);
        EXECUTE format('CREATE TRIGGER tr_%s_sync_touch_ins AFTER INSERT ON tramites.%I '
                    || 'REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT '
                    || 'EXECUTE FUNCTION tramites.trg_procedure_child_sync_touch()', t, t);

        EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_upd ON tramites.%I', t, t);
        EXECUTE format('CREATE TRIGGER tr_%s_sync_touch_upd AFTER UPDATE ON tramites.%I '
                    || 'REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT '
                    || 'EXECUTE FUNCTION tramites.trg_procedure_child_sync_touch()', t, t);

        EXECUTE format('DROP TRIGGER IF EXISTS tr_%s_sync_touch_del ON tramites.%I', t, t);
        EXECUTE format('CREATE TRIGGER tr_%s_sync_touch_del AFTER DELETE ON tramites.%I '
                    || 'REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT '
                    || 'EXECUTE FUNCTION tramites.trg_procedure_child_sync_touch()', t, t);
    END LOOP;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Asignación inicial del histórico (AC3)
-- ─────────────────────────────────────────────────────────────────────────────
-- Solo las filas que aún no tienen versión (sync_version = 0), en orden de creación. Se reserva un
-- bloque de la secuencia en vez de numerar desde 1: si el DDL 122 ya se desplegó, los trámites
-- creados desde entonces tienen versiones propias y no se pueden pisar ni duplicar.
-- Con los triggers de usuario del trámite desactivados (patrón DDL 47): la asignación no es una
-- acción de negocio, no debe auditarse ni subir row_version. DISABLE TRIGGER toma un bloqueo
-- exclusivo sobre la tabla hasta el final de la transacción, así que nadie inserta mientras se
-- reserva el bloque. Reaplicar el script no hace nada: ya no quedan filas en 0.
ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER;

DO $$
DECLARE
    v_pendientes bigint;
    v_base       bigint;
BEGIN
    SELECT count(*) INTO v_pendientes FROM tramites.procedure_instances WHERE sync_version = 0;

    IF v_pendientes > 0 THEN
        v_base := nextval('tramites.procedure_sync_seq') - 1;
        PERFORM setval('tramites.procedure_sync_seq', v_base + v_pendientes);

        WITH ord AS (
            SELECT id, row_number() OVER (ORDER BY created_at, id) AS n
              FROM tramites.procedure_instances
             WHERE sync_version = 0
        )
        UPDATE tramites.procedure_instances p
           SET sync_version    = v_base + ord.n,
               sync_changed_at = now()
          FROM ord
         WHERE p.id = ord.id;
    END IF;
END $$;

ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER;
