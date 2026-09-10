-- =============================================================================
-- Multiple Propietario: ordinal + ownership_percentage en procedure_instance_actors.
-- Migración: 20260901120000_MultiplePropietarioActors.
-- ADR-0053 (Propuesto) · docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §3.
--
-- Hasta hoy `procedure_instance_actors` admite exactamente un actor por rol (comprador, vendedor,
-- locatario) por trámite: lo impone el índice único (procedure_instance_id, procedure_entity_id).
-- El negocio necesita copropiedad: hasta 4 personas por lado, cada una con su propio porcentaje.
--
-- `ordinal` (1..4) distingue la posición del actor dentro de su rol: 1 es el actor principal, el
-- mismo que ya existe hoy ("solidario", absorbe el residuo en el frontend). `ownership_percentage`
-- es NULL cuando el lado tiene un solo actor (comportamiento actual, sin bloque de porcentaje en la
-- UI) y obligatorio en aplicación (no en BD) cuando el lado tiene 2+ actores — la suma=100 por lado
-- es una regla de negocio sobre el conjunto de filas, no expresable en un CHECK de una sola fila
-- (ver ADR-0053, Tradeoff aceptado); vive en Flit.Tramites.Application.
--
-- Aditiva y backward-compatible: toda fila existente ya trae ordinal=1 por el DEFAULT (no requiere
-- backfill) y ownership_percentage=NULL — cero impacto en trámites en curso o cerrados. El índice
-- único original de esta tabla se creó como CONSTRAINT UNIQUE en el CREATE TABLE (06-HU10150), no
-- como índice plano: Postgres no permite `DROP INDEX` directo sobre el índice que respalda una
-- constraint (exige `DROP CONSTRAINT`). El índice nuevo se crea como índice plano (sin constraint
-- de por medio), igual patrón que el resto de índices únicos recientes del repo (39-HU10900).
--
-- Idempotente y reaplicable.
-- =============================================================================

ALTER TABLE tramites.procedure_instance_actors
    ADD COLUMN IF NOT EXISTS ordinal integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS ownership_percentage numeric(5,2) NULL;

COMMENT ON COLUMN tramites.procedure_instance_actors.ordinal IS
    'Posicion del actor dentro de su rol (1=principal/solidario, 2..4=agregados). ADR-0053.';
COMMENT ON COLUMN tramites.procedure_instance_actors.ownership_percentage IS
    'Porcentaje de propiedad (2 decimales); NULL cuando el rol tiene un solo actor. ADR-0053. @pii:low';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_procedure_instance_actors_ordinal')
    THEN
        ALTER TABLE tramites.procedure_instance_actors
            ADD CONSTRAINT ck_procedure_instance_actors_ordinal
            CHECK (ordinal BETWEEN 1 AND 4);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_procedure_instance_actors_ownership_pct')
    THEN
        ALTER TABLE tramites.procedure_instance_actors
            ADD CONSTRAINT ck_procedure_instance_actors_ownership_pct
            CHECK (ownership_percentage IS NULL
                   OR (ownership_percentage > 0 AND ownership_percentage <= 100));
    END IF;
END $$;

-- El uq_procedure_instance_actors_instance_entity original ES una constraint (no un índice plano):
-- se retira con DROP CONSTRAINT, nunca con DROP INDEX (ver nota de cabecera).
ALTER TABLE tramites.procedure_instance_actors
    DROP CONSTRAINT IF EXISTS uq_procedure_instance_actors_instance_entity;

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_actors_instance_entity_ordinal
    ON tramites.procedure_instance_actors (procedure_instance_id, procedure_entity_id, ordinal);
