-- =============================================================================
-- Familia de acción del hecho de prenda + índice único parcial dual (ADR-0055, Opción B).
-- Migración: 20260907130000_HU12128_PrendaAccionFamiliaDual
-- HU #12128 (Feature #10585 / ADR-0055) — Database Agent.
--
-- `uq_procedure_instance_prenda_vigente` (24-HU10585-prenda.sql) garantiza hoy a lo sumo UNA fila
-- 'vigente' por instancia, sin distinguir de qué acción se trata. El negocio pide capturar en la
-- MISMA instancia (PRENDA_INSCRIPCION / LEVANTAMIENTO_PRENDA) hasta DOS hechos vigentes: uno de
-- constitución (decision IN ('solicitar','registrar')) y uno de levantamiento (decision='levantar').
--
-- `accion_familia` es una columna DERIVADA de `decision` (no una nueva decisión de negocio): agrupa
-- las cinco decisiones existentes en dos familias mutuamente excluyentes entre sí:
--   - 'constitucion'   <- decision IN ('solicitar', 'registrar')
--   - 'levantamiento'  <- decision = 'levantar'
--   - NULL             <- decision IN ('omitir', 'sin_prenda') — no son hechos de gravamen reales
--     (omitir/sin_prenda declaran la AUSENCIA de gravamen, no un hecho de constitución o
--     levantamiento que deba coexistir con otro), así que no participan de la familia.
--
-- El índice único parcial se reemplaza por uno sobre (procedure_instance_id, accion_familia)
-- WHERE estado='vigente': permite hasta DOS filas vigentes por instancia (una por familia no-nula),
-- pero nunca dos de la MISMA familia — cumple AC1/AC2.
--
-- Matrícula y Traspaso (R4/R10/R17, HU #10585/#10595/#10599) NO cambian de comportamiento real
-- (AC3): sus decisiones siguen siendo mutuamente excluyentes en la práctica porque nunca activan la
-- acción complementaria (eso lo gatea la capa de aplicación en HU-DOM-APP #12129, no esta
-- migración). Nota de diseño: como Postgres no colisiona dos NULLs en un índice único, el caso
-- 'omitir'/'sin_prenda' (accion_familia NULL) queda sin respaldo de índice para "una sola vigente";
-- ese invariante lo sostiene hoy `RegistrarPrendaHandler` (versiona por reemplazo antes de insertar),
-- sin cambios en esta HU — ADR-0055, "Impacto por capa → Infraestructura".
--
-- Aditiva y backward-compatible: toda fila existente se backfillea determinísticamente desde
-- `decision`; ninguna decisión ni fila se pierde. Idempotente y reaplicable.
-- =============================================================================

ALTER TABLE tramites.procedure_instance_prenda
    ADD COLUMN IF NOT EXISTS accion_familia varchar(20) NULL;

-- Backfill: deriva accion_familia de decision para todas las filas existentes (vigentes y
-- reemplazadas). Idempotente: si ya corrió, el WHERE evita tocar filas ya correctas.
UPDATE tramites.procedure_instance_prenda
   SET accion_familia = CASE decision
           WHEN 'solicitar' THEN 'constitucion'
           WHEN 'registrar' THEN 'constitucion'
           WHEN 'levantar'  THEN 'levantamiento'
           ELSE NULL
       END
 WHERE accion_familia IS DISTINCT FROM (
       CASE decision
           WHEN 'solicitar' THEN 'constitucion'
           WHEN 'registrar' THEN 'constitucion'
           WHEN 'levantar'  THEN 'levantamiento'
           ELSE NULL
       END);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_procedure_instance_prenda_accion_familia')
    THEN
        ALTER TABLE tramites.procedure_instance_prenda
            ADD CONSTRAINT ck_procedure_instance_prenda_accion_familia
            CHECK (accion_familia IN ('constitucion', 'levantamiento') OR accion_familia IS NULL);
    END IF;
END $$;

COMMENT ON COLUMN tramites.procedure_instance_prenda.accion_familia IS
    'Familia de acción del hecho de gravamen, derivada de decision (ADR-0055): constitucion '
    '(solicitar|registrar) o levantamiento (levantar); NULL en omitir|sin_prenda. Sostiene el '
    'índice único parcial dual: hasta 2 filas vigentes por instancia, nunca dos de la misma familia.';

-- El invariante IT-3 pasa de "a lo sumo 1 vigente por instancia" a "a lo sumo 1 vigente por
-- instancia Y por familia (hasta 2 en total)". uq_procedure_instance_prenda_vigente es un índice
-- plano (CREATE UNIQUE INDEX, no una table CONSTRAINT) — se retira con DROP INDEX, no DROP
-- CONSTRAINT (ver 24-HU10585-prenda.sql).
DROP INDEX IF EXISTS tramites.uq_procedure_instance_prenda_vigente;

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_prenda_vigente_familia
    ON tramites.procedure_instance_prenda (procedure_instance_id, accion_familia)
    WHERE estado = 'vigente';
