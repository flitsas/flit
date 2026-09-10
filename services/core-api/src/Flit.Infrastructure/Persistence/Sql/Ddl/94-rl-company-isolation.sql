-- =============================================================================
-- Ficha de empresa por representante legal (aislada) + visibilidad de bajas +
-- una escritura activa por ficha.
-- Migración: 20260826180000_RlCompanyIsolation.
--
-- Producto: cada RL tiene su propia fila de NIT (nombre/contacto independientes).
-- Baja lógica de RL o de ficha: no se ve en admin ni se apalanca en trámites nuevos.
-- Adjuntos ya entregados conservan source_deed_id.
--
-- Soft-delete con is_active (mismo patrón del directorio RL/escrituras; no
-- deleted_at) para no romper lecturas históricas ni FKs de mandatarios.
-- Idempotente.
-- =============================================================================

ALTER TABLE admin.represented_companies
    ADD COLUMN IF NOT EXISTS representative_id uuid,
    ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_represented_companies_representative')
    THEN
        ALTER TABLE admin.represented_companies
            ADD CONSTRAINT fk_represented_companies_representative
            FOREIGN KEY (representative_id)
            REFERENCES admin.company_legal_representatives(id)
            ON DELETE RESTRICT ON UPDATE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_represented_companies_representative_id
    ON admin.represented_companies(representative_id);

-- Dueño canónico: el vínculo más antiguo. El resto recibe una copia de la ficha.
UPDATE admin.represented_companies c
SET representative_id = ranked.representative_id
FROM (
    SELECT represented_company_id,
           representative_id,
           ROW_NUMBER() OVER (
               PARTITION BY represented_company_id
               ORDER BY created_at, representative_id) AS rn
    FROM admin.legal_representative_companies
) ranked
WHERE c.id = ranked.represented_company_id
  AND ranked.rn = 1
  AND c.representative_id IS NULL;

CREATE TEMP TABLE IF NOT EXISTS tmp_rl_company_clone_map (
    old_company_id uuid NOT NULL,
    representative_id uuid NOT NULL,
    new_company_id uuid NOT NULL
);

TRUNCATE tmp_rl_company_clone_map;

INSERT INTO tmp_rl_company_clone_map (old_company_id, representative_id, new_company_id)
SELECT ranked.represented_company_id, ranked.representative_id, uuidv7()
FROM (
    SELECT represented_company_id,
           representative_id,
           ROW_NUMBER() OVER (
               PARTITION BY represented_company_id
               ORDER BY created_at, representative_id) AS rn
    FROM admin.legal_representative_companies
) ranked
WHERE ranked.rn > 1;

-- Quitar unicidad (tenant, NIT) antes de clonar fichas que reutilizan el mismo NIT.
DROP INDEX IF EXISTS admin.uq_represented_companies_tenant_document;

INSERT INTO admin.represented_companies (
    id, tenant_id, document_type, document_number, name, email, address, city, phone,
    row_version, created_at, created_by, updated_at, updated_by, representative_id, is_active)
SELECT
    m.new_company_id,
    c.tenant_id,
    c.document_type,
    c.document_number,
    c.name,
    c.email,
    c.address,
    c.city,
    c.phone,
    0,
    now(),
    c.created_by,
    now(),
    c.updated_by,
    m.representative_id,
    true
FROM tmp_rl_company_clone_map m
JOIN admin.represented_companies c ON c.id = m.old_company_id
WHERE NOT EXISTS (
    SELECT 1 FROM admin.represented_companies x WHERE x.id = m.new_company_id)
  AND NOT EXISTS (
    SELECT 1 FROM admin.represented_companies x
    WHERE x.tenant_id = c.tenant_id
      AND x.representative_id = m.representative_id
      AND x.document_number = c.document_number
      AND x.is_active);

UPDATE admin.legal_representative_companies lrc
SET represented_company_id = m.new_company_id
FROM tmp_rl_company_clone_map m
WHERE lrc.represented_company_id = m.old_company_id
  AND lrc.representative_id = m.representative_id;

UPDATE admin.company_legal_representatives r
SET represented_company_id = m.new_company_id,
    updated_at = now()
FROM tmp_rl_company_clone_map m
WHERE r.id = m.representative_id
  AND r.represented_company_id = m.old_company_id;

-- Postgres no permite referenciar la tabla destino del UPDATE dentro del JOIN del FROM.
-- Si un intento previo ya reasignó el vínculo RL→ficha, se alinea por la ficha actual del RL.
UPDATE admin.company_deed_companies cdc
SET represented_company_id = r.represented_company_id
FROM admin.company_deeds d,
     admin.company_legal_representatives r
WHERE d.id = cdc.deed_id
  AND r.id = d.representative_id
  AND d.representative_id IS NOT NULL
  AND r.represented_company_id IS NOT NULL
  AND cdc.represented_company_id IS DISTINCT FROM r.represented_company_id;

DROP TABLE IF EXISTS tmp_rl_company_clone_map;

CREATE UNIQUE INDEX IF NOT EXISTS uq_represented_companies_owner_nit
    ON admin.represented_companies (tenant_id, representative_id, document_number)
    WHERE is_active AND representative_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_represented_companies_orphan_nit
    ON admin.represented_companies (tenant_id, document_number)
    WHERE is_active AND representative_id IS NULL;

-- Una escritura activa por ficha (RL × compañía): se conserva la más reciente.
WITH ranked AS (
    SELECT d.id,
           ROW_NUMBER() OVER (
               PARTITION BY d.representative_id, cdc.represented_company_id
               ORDER BY COALESCE(d.updated_at, d.created_at) DESC, d.id DESC) AS rn
    FROM admin.company_deeds d
    JOIN admin.company_deed_companies cdc ON cdc.deed_id = d.id
    WHERE d.is_active
      AND d.representative_id IS NOT NULL
)
UPDATE admin.company_deeds d
SET is_active = false,
    updated_at = now()
FROM ranked r
WHERE d.id = r.id
  AND r.rn > 1
  AND d.is_active;
