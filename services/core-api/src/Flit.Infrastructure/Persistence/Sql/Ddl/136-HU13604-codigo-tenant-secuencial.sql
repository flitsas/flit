-- HU #13604 (Feature #13602, Épica #13517) — El código de tenant pasa a ser un consecutivo COMP-XXXX.
-- Migración: 20261015180000_HU13604_CodigoTenantSecuencial.
--
-- Antes:  identity.tenants.code lo tecleaba el usuario al crear la compañía (o el OT): valores
--         arbitrarios y sin orden ("23", "OT-SABANETA", "EMPRESA_DEMO"...).
-- Después: COMP- + número con relleno a 4 dígitos (COMP-0001 … COMP-9999); desde 10000 crece sin
--         relleno (COMP-10000). Lo asigna Postgres con una SEQUENCE.
--
-- Alcance: TODOS los tenants (compañías raíz e hijas, concesiones, marcas blancas, FLIT y OT). Los OT
-- están en la misma tabla, con tenant_type RENTING; la decisión del PO es recodificarlos también.
--
-- Impacto verificado (Épica #13517): el código es una etiqueta de presentación. No viaja en el JWT ni en
-- rutas, caché, almacenamiento, radicados, PDF, correos ni integraciones (ICT identifica por tax_id) y
-- ninguna otra tabla lo copia: todo se relaciona por tenant_id. Por eso basta con reescribir la columna.
--
-- Idempotente: la recodificación va dentro de un guard que solo dispara la primera vez (la columna
-- legacy_code todavía no existe). Volver a correr el script no recodifica ni mueve la secuencia.
-- La simulación de solo lectura está en docs/sql/hu-13604-simular-recodificacion-tenants.sql.

CREATE SEQUENCE IF NOT EXISTS identity.tenant_code_seq AS bigint START WITH 1 MINVALUE 1;

-- Formato único del código. lpad sola truncaría (lpad('10000', 4) = '1000'): de ahí el CASE.
CREATE OR REPLACE FUNCTION identity.fn_format_tenant_code(p_n bigint)
RETURNS varchar(32)
LANGUAGE sql
IMMUTABLE STRICT
AS $$
    SELECT 'COMP-' || CASE WHEN p_n < 10000 THEN lpad(p_n::text, 4, '0') ELSE p_n::text END
$$;

CREATE OR REPLACE FUNCTION identity.fn_next_tenant_code()
RETURNS varchar(32)
LANGUAGE sql
VOLATILE
AS $$
    SELECT identity.fn_format_tenant_code(nextval('identity.tenant_code_seq'))
$$;

COMMENT ON FUNCTION identity.fn_next_tenant_code() IS
    'HU #13604: siguiente código de tenant (COMP-XXXX). DEFAULT de identity.tenants.code.';

-- ─────────────────────────────────────────────────────────────────────────────
-- Recodificación por antigüedad (una sola vez).
--
-- Guard: la ausencia de legacy_code, que se crea dentro del mismo bloque. Mientras no exista, la
-- migración no ha corrido.
--
-- uq_tenants_code es UNIQUE no diferible: Postgres lo valida fila a fila, así que reescribir en una sola
-- pasada puede chocar a mitad del UPDATE (un tenant cuyo código actual ya sea el nuevo de otro). Se hace
-- en dos pasadas: primero un valor intermedio con prefijo '#' (carácter que TextFieldPatterns.TenantCode
-- nunca dejó escribir, así que no existe en la tabla) y luego se quita el prefijo.
--
-- Cada tenant genera dos filas de auditoría (tr_tenants_audit) y su row_version sube en 2: esperado.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_total bigint;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'identity' AND table_name = 'tenants' AND column_name = 'legacy_code'
    ) THEN
        ALTER TABLE identity.tenants ADD COLUMN legacy_code varchar(32);

        WITH ord AS (
            SELECT id, row_number() OVER (ORDER BY created_at, id) AS n
            FROM identity.tenants
        )
        UPDATE identity.tenants t
           SET legacy_code = t.code,
               code        = '#' || identity.fn_format_tenant_code(ord.n)
          FROM ord
         WHERE t.id = ord.id;

        UPDATE identity.tenants
           SET code = substr(code, 2)
         WHERE code LIKE '#%';

        SELECT count(*) INTO v_total FROM identity.tenants;

        -- La siguiente compañía continúa desde el último código asignado (N + 1).
        PERFORM setval('identity.tenant_code_seq', v_total + 1, false);
    END IF;
END $$;

COMMENT ON COLUMN identity.tenants.legacy_code IS
    'HU #13604: código que tenía el tenant antes de la recodificación COMP-XXXX. NULL en tenants creados después.';

-- A partir de aquí lo asigna Postgres. Va en el DEFAULT y no solo en la aplicación, a propósito: así
-- queda cubierto todo camino de inserción (EF, seeders, migrador V1, SQL a mano).
ALTER TABLE identity.tenants
    ALTER COLUMN code SET DEFAULT identity.fn_next_tenant_code();

ALTER SEQUENCE identity.tenant_code_seq OWNED BY identity.tenants.code;
