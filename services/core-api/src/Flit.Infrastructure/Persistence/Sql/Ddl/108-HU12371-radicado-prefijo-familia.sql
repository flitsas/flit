-- HU #12371 (Feature #12150) — el radicado pasa de número pelado (4571) a prefijo de familia +
-- consecutivo relleno a siete dígitos: FT1-0000012.
--
-- Decisiones del PO (comentario en el Feature del 2026-09-10): ancho mínimo 7; contador GLOBAL
-- (la familia no cuenta aparte: FT1-0000001, FT2-0000002, FT1-0000003); inmutable desde la
-- creación; FT1 = MATRICULAS, FT2 = TRASPASO, FT3 = todo lo demás. Una cuarta familia sería FT4 y
-- se añade en el CASE de abajo, en Radicado.Prefijo (Flit.Tramites.Domain) y en la prueba que
-- compara los dos. En ningún otro sitio.
--
-- Cómo se reparte el trabajo entre columnas:
--   · consecutivo (NUEVA, bigint)  → el número pelado que da la secuencia. Por él se ORDENA y se
--                                    BUSCA. Ordenar por el texto agruparía por familia (FT1-… antes
--                                    que FT2-…), que no es lo pedido.
--   · reference_number (texto)     → el radicado compuesto. Conserva nombre y tipo a propósito: los
--                                    PDFs, los nombres de archivo, los exports, el gRPC de ICT y las
--                                    cuatro consolas de Quipux lo leen tal cual y reciben el formato
--                                    nuevo sin tocarse.
--
-- Quién compone: un trigger BEFORE INSERT, no la aplicación. Así lo reciben EF, los seeds, el
-- migrador V1 y cualquier INSERT directo, y ninguno puede componerlo distinto.
--
-- Sin BEGIN/COMMIT: corre dentro de la transacción de la migración de EF.

-- ── 1. Columna numérica ────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS consecutivo bigint;

-- Backfill desde el radicado numérico de la HU #12151. Solo filas con el formato viejo: si el
-- script se reejecuta sobre una base ya migrada, no hay nada numérico pelado y no toca nada.
UPDATE tramites.procedure_instances
   SET consecutivo = reference_number::bigint
 WHERE consecutivo IS NULL
   AND reference_number ~ '^[1-9][0-9]*$';

-- ── 2. Componer el radicado de lo existente (AC5) ──────────────────────────────────────────────
-- Primero cae el CHECK numérico de las HU #12151/#12153: el UPDATE de abajo escribe 'FT1-…' y
-- lo violaría. Su sucesor (ck_..._reference_formato) se crea al final, ya con todo compuesto.
ALTER TABLE tramites.procedure_instances
    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico;

-- MISMO número, con prefijo y relleno. Solo las filas que aún están en formato viejo: el guard es
-- el propio formato, así que reejecutar no reasigna nada (inmutabilidad).
UPDATE tramites.procedure_instances p
   SET reference_number = CASE upper(trim(pt.family))
                              WHEN 'MATRICULAS' THEN 'FT1'
                              WHEN 'TRASPASO'   THEN 'FT2'
                              ELSE                   'FT3'
                          END || '-' || lpad(p.consecutivo::text, greatest(7, length(p.consecutivo::text)), '0')
  FROM tramites.procedure_types pt
 WHERE pt.id = p.procedure_type_id
   AND p.reference_number ~ '^[1-9][0-9]*$';

-- ── 3. La secuencia pasa a ser de la columna numérica ──────────────────────────────────────────
-- Sin DEFAULT en ninguna de las dos columnas: el trigger es el único que asigna, y así puede
-- respetar un consecutivo que venga explícito (ver el trigger).
ALTER TABLE tramites.procedure_instances
    ALTER COLUMN reference_number DROP DEFAULT;

ALTER SEQUENCE tramites.procedure_instance_reference_seq
    OWNED BY tramites.procedure_instances.consecutivo;

-- ── 4. El trigger que compone ──────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION tramites.fn_procedure_instances_radicado()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_familia text;
BEGIN
    IF NEW.consecutivo IS NULL THEN
        IF NEW.reference_number ~ '^[1-9][0-9]*$' THEN
            -- Un número pelado explícito: los seeds de desarrollo (16/21/35) traen rangos
            -- sintéticos 91/92/93… porque corren antes de que existiera la secuencia. Se respeta
            -- el número en vez de gastar uno de la secuencia real.
            NEW.consecutivo := NEW.reference_number::bigint;
        ELSIF NEW.reference_number ~ '^FT[1-9]-[0-9]+$' THEN
            -- Ya viene compuesto (p. ej. una restauración fila a fila): se lee el número.
            NEW.consecutivo := substring(NEW.reference_number from '[0-9]+$')::bigint;
        ELSE
            NEW.consecutivo := nextval('tramites.procedure_instance_reference_seq');
        END IF;
    END IF;

    SELECT upper(trim(pt.family)) INTO v_familia
      FROM tramites.procedure_types pt
     WHERE pt.id = NEW.procedure_type_id;

    -- lpad TRUNCA cuando el texto es más largo que el ancho: lpad('12345678', 7, '0') da
    -- '1234567'. Por eso el ancho es greatest(7, length(...)): siete es el mínimo, y pasado el
    -- 9.999.999 el número gana un dígito en vez de perderlo. Verificado contra Postgres real: la
    -- primera versión de este trigger recortaba los rangos sintéticos de los seeds a FT1-9100000.
    --
    -- Sin ELSE que reviente: una familia desconocida cae en FT3, igual que
    -- ProcedureFamilyCodes.FromCodeOrOtros degrada a OTROS. Bloquear la creación de trámites por
    -- una familia mal escrita sería peor que un prefijo genérico.
    NEW.reference_number := CASE v_familia
                                WHEN 'MATRICULAS' THEN 'FT1'
                                WHEN 'TRASPASO'   THEN 'FT2'
                                ELSE                   'FT3'
                            END || '-' || lpad(NEW.consecutivo::text, greatest(7, length(NEW.consecutivo::text)), '0');
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_procedure_instances_radicado ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_radicado
    BEFORE INSERT ON tramites.procedure_instances
    FOR EACH ROW
    EXECUTE FUNCTION tramites.fn_procedure_instances_radicado();

-- ── 5. Inmutabilidad (AC4) ─────────────────────────────────────────────────────────────────────
-- El radicado es un identificador, no una descripción: no cambia aunque cambie el tipo o la
-- familia. EF no manda estas columnas en el UPDATE (AfterSaveBehavior = Ignore), y para todo lo
-- demás está esto. Falla ruidoso: un UPDATE silenciosamente ignorado es más difícil de depurar.
CREATE OR REPLACE FUNCTION tramites.fn_procedure_instances_radicado_inmutable()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.reference_number IS DISTINCT FROM OLD.reference_number
       OR NEW.consecutivo IS DISTINCT FROM OLD.consecutivo THEN
        RAISE EXCEPTION 'El radicado % del trámite % es inmutable', OLD.reference_number, OLD.id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_procedure_instances_radicado_inmutable ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_radicado_inmutable
    BEFORE UPDATE OF reference_number, consecutivo ON tramites.procedure_instances
    FOR EACH ROW
    EXECUTE FUNCTION tramites.fn_procedure_instances_radicado_inmutable();

-- ── 6. Restricciones e índices ─────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_instances
    ALTER COLUMN consecutivo SET NOT NULL;

-- {7,} y no {7}: el ancho es mínimo. Pasado el 9.999.999 el número gana un dígito y sigue
-- siendo válido; también lo son los rangos sintéticos de diez dígitos de los seeds.
ALTER TABLE tramites.procedure_instances
    ADD CONSTRAINT ck_procedure_instances_reference_formato
    CHECK (reference_number ~ '^FT[1-9]-[0-9]{7,}$');

ALTER TABLE tramites.procedure_instances
    ADD CONSTRAINT ck_procedure_instances_consecutivo_positivo
    CHECK (consecutivo > 0);

-- Unicidad también sobre el número: la de reference_number (uq_procedure_instances_reference)
-- sigue vigente, pero dos familias distintas con el mismo consecutivo serían textos distintos y
-- pasarían por ella. El contador es global: el número no se repite.
CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_consecutivo
    ON tramites.procedure_instances (consecutivo);

-- El índice de apoyo al orden (longitud, texto) de la HU #12153 deja de tener sentido: ahora se
-- ordena por la columna numérica, y el único la cubre.
DROP INDEX IF EXISTS tramites.ix_procedure_instances_reference_orden;
