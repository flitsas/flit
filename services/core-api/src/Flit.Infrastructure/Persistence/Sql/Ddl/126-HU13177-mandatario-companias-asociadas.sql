-- HU #13177 (Feature #13119 F7, Épica #13090 Mandatarios) — ADR-0061 / ADR-0066.
-- Migración: 20260930190000_HU13177_MandatarioCompaniasAsociadas.
--
-- A QUÉ COMPAÑÍAS DE FLIT (por tenant) SE ASOCIA UN MANDATARIO EN UN ORGANISMO (nivel 3 de la prelación).
--
-- ── Por qué una tabla nueva ────────────────────────────────────────────────────────────────────
-- * NO es `mandate_signer_companies`: esa es la compañía PROPIETARIA del mandatario (nivel 2 de la prelación).
--   Esta es la asociación a OTRAS compañías (nivel 3): «el mandatario de A también firma por B».
-- * NO es `mandate_signer_represented_companies` (DDL 54): aquella apunta a fichas de Representantes Legales
--   (admin.represented_companies) y acotaba por NIT del vendedor; queda sin uso y NO se toca aquí (su borrado es de F8).
-- * Tabla y no columna: un mandatario puede asociarse a varias compañías por organismo.
--
-- ── Ausencia ───────────────────────────────────────────────────────────────────────────────────
-- Un mandatario sin filas aplica solo a su propia compañía.
--
-- ── RLS ────────────────────────────────────────────────────────────────────────────────────────
-- Decisión explícita: SIN RLS y SIN tenant_id propio (tabla puente, igual que admin.mandate_signer_companies y
-- DDL 54). El aislamiento lo aplica la capa de aplicación (perfil OT / Admin de Compañía) antes de leer o escribir.
--
-- DDL IDEMPOTENTE (IF NOT EXISTS). Sin backfill: arranca vacía.

CREATE TABLE IF NOT EXISTS admin.mandate_signer_associated_companies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_msac PRIMARY KEY (id),
    mandate_signer_id uuid NOT NULL
        CONSTRAINT fk_msac_mandate_signer
        REFERENCES admin.mandate_signers(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    associated_company_tenant_id uuid NOT NULL
        CONSTRAINT fk_msac_company_tenant
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- Baja lógica: retirar una compañía conserva el histórico y libera la unicidad.
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Una sola asociación ACTIVA por (mandatario, organismo, compañía). Si la primera está inactiva, se puede volver a crear.
CREATE UNIQUE INDEX IF NOT EXISTS uq_msac_activa
  ON admin.mandate_signer_associated_companies(
      mandate_signer_id, transit_office_id, associated_company_tenant_id)
  WHERE is_active;

-- La consulta del trámite: «para esta compañía, en este organismo, ¿qué mandatarios asociados hay?».
CREATE INDEX IF NOT EXISTS ix_msac_office_company
  ON admin.mandate_signer_associated_companies(transit_office_id, associated_company_tenant_id, is_active);

-- Un índice por cada FK.
CREATE INDEX IF NOT EXISTS ix_msac_signer
  ON admin.mandate_signer_associated_companies(mandate_signer_id, is_active);

CREATE INDEX IF NOT EXISTS ix_msac_company_tenant
  ON admin.mandate_signer_associated_companies(associated_company_tenant_id);
