-- Epic #13316 (HU #13348, ADR-0065) — copia los valores de avalúo del modo mock de tramites.avaluo_mock_values a
-- consultas.valores_mock_avaluo. Solo hace falta en DEV/QA (en producción la tabla de origen está vacía): la migración
-- de core-consultas ya siembra los fixtures de la Feature #10707; esto trae los que se hayan agregado a mano.
--
-- Lo corre el líder con un usuario administrador (el usuario de Consultas no puede leer tramites, a propósito), después
-- de que core-consultas aplicó sus migraciones:
--
--   psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-valores-mock-avaluo.sql
--
-- Idempotente: correrlo otra vez deja lo mismo (actualiza el valor de las filas existentes).

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO consultas.valores_mock_avaluo (id, clave, fuente, valor_cop)
SELECT gen_random_uuid(), upper(btrim(m.match_key)), m.source, m.value_cop
FROM tramites.avaluo_mock_values m
ON CONFLICT (clave, fuente) DO UPDATE SET valor_cop = EXCLUDED.valor_cop;

-- Verificación: cada valor de tramites quedó igual en consultas (si no, nada se confirma).
DO $$
DECLARE
    diferentes integer;
BEGIN
    SELECT count(*) INTO diferentes
    FROM tramites.avaluo_mock_values m
    LEFT JOIN consultas.valores_mock_avaluo c ON c.clave = upper(btrim(m.match_key)) AND c.fuente = m.source
    WHERE c.id IS NULL OR c.valor_cop <> m.value_cop;
    IF diferentes > 0 THEN
        RAISE EXCEPTION '% valores no quedaron iguales en consultas.valores_mock_avaluo', diferentes;
    END IF;
END
$$;

SELECT count(*) AS valores_en_consultas FROM consultas.valores_mock_avaluo;

COMMIT;
