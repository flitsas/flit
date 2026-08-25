-- =============================================================================
-- ADR-0050 — retira los pasos que el seed de desarrollo dejaba en sort_order = 1.
--
-- `12-HU10200-dev-seed.sql` y `15-tramites-traspaso-dev-seed.sql` creaban un paso
-- único —DATOS_VEHICULO y CONSULTA_PLACA— «para que el wizard funcione
-- end-to-end», de cuando los tipos se publicaban sin parametrización alguna.
--
-- El DDL 81 los borraba al parametrizar los tipos canónicos, pero
-- `DevelopmentAuthSeeder` re-ejecuta esos seeds en CADA arranque de Development
-- (Program.cs, después de migrar), así que volvían a aparecer: MATRICULA_NUEVA
-- terminaba con seis pasos y TRASPASO_STANDARD con siete, dos de ellos en
-- sort_order = 1, y el asistente pintaba uno de más —vacío, sección
-- `generic_form`— en la primera posición.
--
-- El origen ya está cortado (los bloques se retiraron de ambos seeds); esto
-- limpia lo que quedó sembrado. Los pasos legítimos vienen del catálogo y no se
-- tocan: el borrado es por CÓDIGO, no por posición.
--
-- Idempotente. En producción no encuentra nada —el sembrador de desarrollo no
-- corre allí y el DDL 81 ya los había borrado—, así que es un no-op.
-- =============================================================================

DELETE FROM tramites.procedure_steps ps
 USING tramites.procedure_types pt
 WHERE pt.id = ps.procedure_type_id
   AND ps.code IN ('DATOS_VEHICULO', 'CONSULTA_PLACA')
   AND pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD');

-- Guarda: los dos canónicos deben quedar con UN solo paso inicial. Dos pasos
-- compitiendo por la primera posición es exactamente el defecto que esto corrige,
-- y es invisible salvo que se abra el asistente.
DO $$
DECLARE
    duplicados text;
BEGIN
    SELECT string_agg(code, ', ') INTO duplicados
    FROM (
        SELECT pt.code
        FROM tramites.procedure_types pt
        JOIN tramites.procedure_steps ps ON ps.procedure_type_id = pt.id AND ps.is_active
        WHERE pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD')
        GROUP BY pt.code, ps.sort_order
        HAVING count(*) > 1
    ) AS t;

    IF duplicados IS NOT NULL THEN
        RAISE EXCEPTION
            'Hay pasos compitiendo por la misma posición en: %. Revisa qué los siembra.', duplicados;
    END IF;
END $$;
