-- HU #13073 (Feature #13062, Épica #12737) — Versión de sincronización en el trámite.
--
-- Cada trámite lleva una marca de agua para que un sistema externo (Flito) pueda pedir «lo que
-- cambió desde mi última lectura» por cursor:
--   sync_version    bigint      — global y estrictamente creciente, sale de una SEQUENCE.
--   sync_changed_at timestamptz — fecha del último cambio.
--
-- Las mantiene la BASE, no la aplicación: un trigger BEFORE INSERT OR UPDATE las sobrescribe en
-- cada escritura. Así quedan cubiertos todos los caminos (EF, gRPC de ICT, portal público, batch,
-- migrador V1, SQL a mano) y la aplicación no puede fijar una versión a mano (AC3): cualquier valor
-- que traiga la sentencia se pisa con el siguiente de la secuencia.
--
-- Alcance de esta HU: solo el propio trámite. La propagación desde las tablas hijas (actores,
-- campos, historial, adjuntos, comercial) y la asignación inicial del histórico en orden de
-- creación van en la HU #13074. Hasta entonces las filas existentes quedan con sync_version = 0,
-- que es menor que cualquier valor de la secuencia; el feed externo aún no existe, así que nadie
-- las lee.
--
-- Idempotente y reaplicable.

CREATE SEQUENCE IF NOT EXISTS tramites.procedure_sync_seq AS bigint START WITH 1;

-- now() es STABLE: PostgreSQL guarda el DEFAULT como valor rápido y no reescribe la tabla.
ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS sync_version    bigint      NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS sync_changed_at timestamptz NOT NULL DEFAULT now();

COMMENT ON COLUMN tramites.procedure_instances.sync_version IS
    'Versión de sincronización global y creciente (tramites.procedure_sync_seq). La asigna el trigger tr_procedure_instances_sync_stamp; no se escribe desde la aplicación. HU #13073.';
COMMENT ON COLUMN tramites.procedure_instances.sync_changed_at IS
    'Fecha del último cambio del trámite para la sincronización externa. La asigna el trigger tr_procedure_instances_sync_stamp. HU #13073.';

ALTER SEQUENCE tramites.procedure_sync_seq
    OWNED BY tramites.procedure_instances.sync_version;

CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger AS $$
BEGIN
    -- Se sobrescribe siempre, venga lo que venga en NEW: es lo que impide fijar la versión a mano.
    NEW.sync_version    := nextval('tramites.procedure_sync_seq');
    NEW.sync_changed_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Sin lista de columnas (UPDATE OF ...): cualquier cambio del trámite —estado, gestor, organismo,
-- borrado lógico, denormalizados— debe mover la versión.
DROP TRIGGER IF EXISTS tr_procedure_instances_sync_stamp ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_sync_stamp
    BEFORE INSERT OR UPDATE ON tramites.procedure_instances
    FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_sync_stamp();
