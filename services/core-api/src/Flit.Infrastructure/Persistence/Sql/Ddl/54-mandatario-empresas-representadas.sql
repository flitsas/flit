-- Para qué EMPRESAS REPRESENTADAS firma un mandatario en cada organismo.
--
-- ── Por qué una tabla nueva y no `mandate_signer_companies` ────────────────────
-- Esa tabla ya relaciona mandatario, organismo y compañía, pero su `company_tenant_id` es la COMPAÑÍA
-- GESTORA (el tenant), no la empresa representada: es la que responde "qué mandatarios tiene esta
-- gestora en este organismo", que es la consulta que hace el trámite hoy. Meter aquí el segundo
-- significado dejaría ese filtro ambiguo y rompería lo que ya funciona.
--
-- Las empresas representadas son las que se dan de alta dentro del formulario del representante legal
-- (`admin.represented_companies`), únicas por (tenant, NIT).
--
-- ── Ausencia = aplica a todas ──────────────────────────────────────────────────
-- Un mandatario SIN filas para un organismo sirve para cualquier empresa allí. Es deliberado: los
-- mandatarios que ya existen no tienen ninguna, y sin esta regla desaparecerían de todos los trámites
-- al desplegar hasta que alguien los reasociara uno por uno.
-- DDL IDEMPOTENTE.

CREATE TABLE IF NOT EXISTS admin.mandate_signer_represented_companies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_msrc PRIMARY KEY (id),
    mandate_signer_id uuid NOT NULL
        CONSTRAINT fk_msrc_mandate_signer
        REFERENCES admin.mandate_signers(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    represented_company_id uuid NOT NULL
        CONSTRAINT fk_msrc_represented_company
        REFERENCES admin.represented_companies(id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- Baja lógica, igual que los otros puentes del mandatario: retirar una empresa conserva el
    -- histórico y libera la unicidad.
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Una sola asignación ACTIVA por (mandatario, organismo, empresa). Filtrado por is_active para que
-- retirar y volver a agregar la misma empresa no choque con el histórico.
CREATE UNIQUE INDEX IF NOT EXISTS uq_msrc_activa
  ON admin.mandate_signer_represented_companies(
      mandate_signer_id, transit_office_id, represented_company_id)
  WHERE is_active;

-- La consulta del trámite: "para esta empresa, en este organismo, ¿qué mandatarios aplican?".
CREATE INDEX IF NOT EXISTS ix_msrc_office_company
  ON admin.mandate_signer_represented_companies(transit_office_id, represented_company_id, is_active);

-- Y la del formulario: "qué empresas tiene asociadas este mandatario".
CREATE INDEX IF NOT EXISTS ix_msrc_signer
  ON admin.mandate_signer_represented_companies(mandate_signer_id, is_active);
