-- HU #13083 (Feature #13066, Épica #12737) — el índice único de sync_version pasa a ser PARCIAL para que
-- la propagación de cambios no cause deadlocks con las tablas hijas.
--
-- Defecto (hallado por la prueba de concurrencia de la HU #13083): con un índice único corriente sobre
-- sync_version (DDL 124), PostgreSQL trata esa columna como posible clave de una FK. Todo UPDATE que la
-- cambia —el sello de sincronización, en cada cambio del trámite o de sus tablas hijas— toma entonces el
-- bloqueo FOR UPDATE en lugar de FOR NO KEY UPDATE. FOR UPDATE choca con el FOR KEY SHARE que toma la FK
-- de cualquier INSERT/UPDATE en una tabla hija (actores, historial, adjuntos…): dos escrituras simultáneas
-- en hijas del mismo trámite se esperan mutuamente y una muere con 40P01 (deadlock detected).
--
-- Un índice PARCIAL no cuenta como clave para FK ("partial indexes and expressional indexes are not
-- considered", documentación de PostgreSQL, Row-Level Locks). La columna es NOT NULL, así que el predicado
-- no deja fuera ninguna fila: se conservan la unicidad y el uso del índice en WHERE sync_version > x
-- (el planificador deduce IS NOT NULL de esa comparación). Mismo nombre que antes.
--
-- Idempotente y reaplicable.

DO $$
BEGIN
    IF EXISTS (SELECT 1
                 FROM pg_index i
                WHERE i.indexrelid = to_regclass('tramites.uq_procedure_instances_sync_version')
                  AND i.indpred IS NULL) THEN
        DROP INDEX tramites.uq_procedure_instances_sync_version;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_sync_version
    ON tramites.procedure_instances (sync_version)
    WHERE sync_version IS NOT NULL;

COMMENT ON INDEX tramites.uq_procedure_instances_sync_version IS
    'Unicidad de sync_version. PARCIAL a propósito (DDL 128): un índice único corriente convierte el sello de sincronización en cambio de clave (FOR UPDATE) y provoca deadlocks con los INSERT de las tablas hijas.';
