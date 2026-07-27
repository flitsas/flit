-- HU #10932 (Feature #10929, RL-flujo-ajustes) — Representante multiempresa con firma única por persona.
-- Un representante legal deja de ser "un par compañía+persona" para pasar a ser la PERSONA
-- (única por (tenant, documento)) que puede tener VARIAS compañías, con una sola firma/identidad.
-- Se introduce el puente admin.legal_representative_companies, se hace backfill desde las filas
-- actuales, se colapsan los duplicados por (tenant, documento) al canónico (menor created_at) y se
-- ajusta la unicidad a (tenant, documento) parcial sobre activos. represented_company_id se depreca
-- (nullable, se conserva como "compañía primaria" para compatibilidad). DDL IDEMPOTENTE.

-- ── Puente representante ↔ compañía representada ────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.legal_representative_companies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_legal_representative_companies PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    representative_id uuid NOT NULL
        CONSTRAINT fk_lrc_representative
        REFERENCES admin.company_legal_representatives(id) ON DELETE CASCADE ON UPDATE CASCADE,
    represented_company_id uuid NOT NULL
        CONSTRAINT fk_lrc_represented_company
        REFERENCES admin.represented_companies(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_lrc_representative_company
  ON admin.legal_representative_companies(representative_id, represented_company_id);
CREATE INDEX IF NOT EXISTS ix_lrc_represented_company_id
  ON admin.legal_representative_companies(represented_company_id);
CREATE INDEX IF NOT EXISTS ix_lrc_tenant_id
  ON admin.legal_representative_companies(tenant_id);

ALTER TABLE admin.legal_representative_companies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.legal_representative_companies;
CREATE POLICY tenant_isolation ON admin.legal_representative_companies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ── Backfill: cada representante existente aporta su compañía actual al puente ───
INSERT INTO admin.legal_representative_companies (tenant_id, representative_id, represented_company_id)
SELECT r.tenant_id, r.id, r.represented_company_id
FROM admin.company_legal_representatives r
WHERE r.represented_company_id IS NOT NULL
ON CONFLICT (representative_id, represented_company_id) DO NOTHING;

-- ── Colapso de duplicados por (tenant, documento) al canónico (menor created_at, luego menor id) ──
-- 1) Borrar links de compañía de duplicados que colisionarían con el canónico.
DELETE FROM admin.legal_representative_companies lrc
USING (
  SELECT id, first_value(id) OVER (
           PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
  FROM admin.company_legal_representatives) rk
WHERE lrc.representative_id = rk.id AND rk.id <> rk.canonical_id
  AND EXISTS (
    SELECT 1 FROM admin.legal_representative_companies x
    WHERE x.representative_id = rk.canonical_id
      AND x.represented_company_id = lrc.represented_company_id);

-- 2) Repuntar el resto de links de compañía al canónico.
UPDATE admin.legal_representative_companies lrc
SET representative_id = rk.canonical_id
FROM (
  SELECT id, first_value(id) OVER (
           PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
  FROM admin.company_legal_representatives) rk
WHERE lrc.representative_id = rk.id AND rk.id <> rk.canonical_id;

-- 3) Borrar links de tipos de trámite de duplicados que colisionarían con el canónico.
DELETE FROM admin.company_legal_representative_procedure_types pt
USING (
  SELECT id, first_value(id) OVER (
           PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
  FROM admin.company_legal_representatives) rk
WHERE pt.representative_id = rk.id AND rk.id <> rk.canonical_id
  AND EXISTS (
    SELECT 1 FROM admin.company_legal_representative_procedure_types x
    WHERE x.representative_id = rk.canonical_id
      AND x.procedure_type_id = pt.procedure_type_id);

-- 4) Repuntar el resto de links de tipos de trámite al canónico.
UPDATE admin.company_legal_representative_procedure_types pt
SET representative_id = rk.canonical_id
FROM (
  SELECT id, first_value(id) OVER (
           PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
  FROM admin.company_legal_representatives) rk
WHERE pt.representative_id = rk.id AND rk.id <> rk.canonical_id;

-- 5) Mover firma/identidad al canónico si el canónico no las tiene.
UPDATE admin.company_legal_representatives c
SET signature_vault_id = COALESCE(c.signature_vault_id, d.signature_vault_id),
    identity_validation_ref = COALESCE(c.identity_validation_ref, d.identity_validation_ref)
FROM (
  SELECT canonical_id,
         (array_agg(signature_vault_id) FILTER (WHERE signature_vault_id IS NOT NULL))[1] AS signature_vault_id,
         (array_agg(identity_validation_ref) FILTER (WHERE identity_validation_ref IS NOT NULL))[1] AS identity_validation_ref
  FROM (
    SELECT id, signature_vault_id, identity_validation_ref,
           first_value(id) OVER (
             PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
    FROM admin.company_legal_representatives) z
  WHERE id <> canonical_id
  GROUP BY canonical_id) d
WHERE c.id = d.canonical_id;

-- 6) Desactivar los duplicados (baja lógica; el canónico queda activo).
UPDATE admin.company_legal_representatives c
SET is_active = false
FROM (
  SELECT id, first_value(id) OVER (
           PARTITION BY tenant_id, document_number ORDER BY created_at, id) AS canonical_id
  FROM admin.company_legal_representatives) rk
WHERE c.id = rk.id AND rk.id <> rk.canonical_id AND c.is_active;

-- ── Nueva unicidad de la persona: (tenant, documento) parcial sobre activos ──────
ALTER TABLE admin.company_legal_representatives ALTER COLUMN represented_company_id DROP NOT NULL;
DROP INDEX IF EXISTS admin.uq_company_legal_representatives_tenant_company_document;
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_legal_representatives_tenant_document
  ON admin.company_legal_representatives(tenant_id, document_number)
  WHERE is_active;
