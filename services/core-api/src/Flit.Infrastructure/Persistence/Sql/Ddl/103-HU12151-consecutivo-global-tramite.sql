-- HU #12151 (Feature #12150) — El radicado pasa a ser un consecutivo GLOBAL.
--
-- Antes:  TRM-{año}-{seq:D6}, único por (tenant, año) vía uq_procedure_instances_tenant_reference.
--         Dos compañías podían tener las dos TRM-2026-000001.
-- Después: un entero pelado ("4571"), único en TODA la plataforma, asignado por una SEQUENCE.
--
-- La columna NO cambia de nombre ni de tipo: sigue siendo tramites.procedure_instances.
-- reference_number (varchar). Lo único que cambia es quién la llena. Por eso los PDFs (FUR,
-- compraventa, certificado RNMC), sus nombres de archivo, el export a Excel, el gRPC de ICT y
-- las cuatro consolas de Quipux siguen funcionando sin tocarlos: reciben un texto, como siempre.
--
-- Idempotente: la renumeración va dentro de un guard que solo dispara la primera vez. Volver a
-- correr el script NO puede renumerar de nuevo, porque eso rompería la inmutabilidad del AC5.

CREATE SEQUENCE IF NOT EXISTS tramites.procedure_instance_reference_seq AS bigint START WITH 1;

-- ─────────────────────────────────────────────────────────────────────────────
-- Renumeración por antigüedad (una sola vez).
--
-- El guard es la ausencia del índice único global, que se crea al final de este script: mientras
-- no exista, la migración no ha corrido. Sin guard, una segunda ejecución reasignaría números a
-- trámites que ya los tienen — justo lo que el AC5 prohíbe.
--
-- En un ambiente sin trámites (producción) el UPDATE afecta 0 filas y la secuencia arranca en 1.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'uq_procedure_instances_reference' AND n.nspname = 'tramites'
    ) THEN
        WITH ord AS (
            SELECT id, row_number() OVER (ORDER BY created_at, id) AS n
            FROM tramites.procedure_instances
        )
        UPDATE tramites.procedure_instances p
           SET reference_number = ord.n::text
          FROM ord
         WHERE p.id = ord.id;

        PERFORM setval(
            'tramites.procedure_instance_reference_seq',
            COALESCE((SELECT max(reference_number::bigint) FROM tramites.procedure_instances), 0) + 1,
            false);
    END IF;
END $$;

-- A partir de aquí la asigna Postgres. Se pone en el DEFAULT y no en la aplicación a propósito:
-- así queda cubierto TODO camino de inserción, incluidos los que no pasan por EF (ICT, seeds,
-- el migrador V1, SQL a mano).
ALTER TABLE tramites.procedure_instances
    ALTER COLUMN reference_number
    SET DEFAULT nextval('tramites.procedure_instance_reference_seq')::text;

ALTER SEQUENCE tramites.procedure_instance_reference_seq
    OWNED BY tramites.procedure_instances.reference_number;

-- El formato numérico es lo que permite ordenar por ::bigint sin que el cast reviente nunca
-- (HU #12153). Drop + add para que el script sea reejecutable.
ALTER TABLE tramites.procedure_instances
    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico;

ALTER TABLE tramites.procedure_instances
    ADD CONSTRAINT ck_procedure_instances_reference_numerico
    CHECK (reference_number ~ '^[0-9]+$');

-- El índice único deja de llevar tenant_id: esa era exactamente la causa de que dos compañías
-- pudieran compartir radicado. Es una CONSTRAINT, no un índice suelto, así que DROP INDEX falla.
ALTER TABLE tramites.procedure_instances
    DROP CONSTRAINT IF EXISTS uq_procedure_instances_tenant_reference;

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_reference
    ON tramites.procedure_instances (reference_number);
