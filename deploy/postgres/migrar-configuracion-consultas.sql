-- Epic #13316 (HU #13344, ADR-0065) — copia la configuración de consultas de cada empresa de
-- admin.tenant_operational_policies a consultas.configuracion_empresa, con los MISMOS valores.
--
-- Lo corre el líder con un usuario administrador (el usuario de Consultas no puede leer admin, a propósito), después
-- de que core-consultas aplicó sus migraciones y antes de encender Consultas:Remoto:Habilitado en core-api:
--
--   psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-configuracion-consultas.sql
--
-- Idempotente: correrlo otra vez deja lo mismo (actualiza las filas existentes). Desde que la bandera está encendida,
-- core-api mantiene las dos copias iguales en cada guardado del SuperAdmin; esto solo carga lo que ya existía.
-- Mismas reglas que core-api al leer: '{}' = sin override (las cadenas globales) y fuente de comparendos
-- normalizada (todo lo que no es 'internal' es 'external').

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO consultas.configuracion_empresa (tenant_id, cadenas, failover_timeout_ms, fuente_multas, avaluos, actualizado_en)
SELECT p.tenant_id,
       NULLIF(p.consultation_provider_config, '{}'::jsonb),
       p.runt_failover_timeout_ms,
       CASE WHEN lower(btrim(coalesce(p.fines_query_source, ''))) = 'internal' THEN 'internal' ELSE 'external' END,
       NULLIF(p.avaluo_provider_config, '{}'::jsonb),
       now()
FROM admin.tenant_operational_policies p
ON CONFLICT (tenant_id) DO UPDATE
   SET cadenas             = EXCLUDED.cadenas,
       failover_timeout_ms = EXCLUDED.failover_timeout_ms,
       fuente_multas       = EXCLUDED.fuente_multas,
       avaluos             = EXCLUDED.avaluos,
       actualizado_en      = EXCLUDED.actualizado_en;

-- Verificación: cada empresa de admin quedó con los mismos valores en consultas (si no, nada se confirma).
DO $$
DECLARE
    diferentes integer;
BEGIN
    SELECT count(*) INTO diferentes
    FROM admin.tenant_operational_policies p
    LEFT JOIN consultas.configuracion_empresa c ON c.tenant_id = p.tenant_id
    WHERE c.tenant_id IS NULL
       OR c.cadenas IS DISTINCT FROM NULLIF(p.consultation_provider_config, '{}'::jsonb)
       OR c.failover_timeout_ms IS DISTINCT FROM p.runt_failover_timeout_ms
       OR c.avaluos IS DISTINCT FROM NULLIF(p.avaluo_provider_config, '{}'::jsonb);
    IF diferentes > 0 THEN
        RAISE EXCEPTION '% empresas no quedaron iguales en consultas.configuracion_empresa', diferentes;
    END IF;
END
$$;

SELECT count(*) AS empresas_en_consultas,
       count(*) FILTER (WHERE cadenas IS NOT NULL) AS con_cadena_propia
FROM consultas.configuracion_empresa;

COMMIT;
