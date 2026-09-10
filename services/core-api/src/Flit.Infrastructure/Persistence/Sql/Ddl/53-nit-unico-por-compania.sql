-- El NIT identifica a la empresa ante el Estado, así que dos tenants con el mismo NIT son la misma
-- empresa duplicada: aparecen dos veces —y con la misma razón social— en cualquier listado que las
-- ofrezca, sin forma de distinguirlas. `identity.tenants` solo tenía UNIQUE sobre `code`.
--
-- ── Por qué el índice va CONDICIONADO ───────────────────────────────────────────
-- Crear el índice único a secas haría fallar la migración en cualquier base que YA tenga duplicados,
-- y dejaría el despliegue a medias por un dato histórico que este cambio no puede arreglar solo.
-- Se crea únicamente si no hay duplicados; si los hay, se deja constancia por WARNING y el índice
-- queda pendiente hasta que alguien consolide esas empresas a mano.
--
-- La puerta de entrada sí queda cerrada en cualquier caso: CreateCompanyHandler rechaza un NIT
-- repetido con 422 antes de llegar a la base.
-- DDL IDEMPOTENTE.

DO $$
DECLARE
    duplicados integer;
BEGIN
    SELECT count(*) INTO duplicados
    FROM (
        SELECT tax_id
        FROM identity.tenants
        WHERE tax_id IS NOT NULL AND btrim(tax_id) <> ''
        GROUP BY tax_id
        HAVING count(*) > 1
    ) d;

    IF duplicados = 0 THEN
        CREATE UNIQUE INDEX IF NOT EXISTS uq_tenants_tax_id
          ON identity.tenants(tax_id)
          WHERE tax_id IS NOT NULL AND btrim(tax_id) <> '';
    ELSE
        RAISE WARNING
            'uq_tenants_tax_id NO se creó: hay % NIT repetidos en identity.tenants. '
            'Consolida esas compañías y vuelve a aplicar este DDL (es idempotente).',
            duplicados;
    END IF;
END $$;
