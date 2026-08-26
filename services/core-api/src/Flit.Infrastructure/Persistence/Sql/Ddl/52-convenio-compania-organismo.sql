-- Convenio comercial entre una COMPAÑÍA GESTORA y un ORGANISMO DE TRÁNSITO, y firma física del
-- mandatario por organismo.
--
-- ── Por qué el convenio NO es el grant ──────────────────────────────────────────
-- `admin.tenant_transit_office_grants` ya relaciona compañía y organismo, pero es OTRA cosa: es el
-- PERMISO para radicar, y la radicación lo exige (TramiteLifecycleService responde
-- `organismo_no_habilitado` sin él). Si el convenio fuera ese grant, todo trámite radicado tendría
-- convenio por construcción y el caso "sin convenio ⇒ el mandatario firma" no ocurriría jamás.
--
-- El convenio es un acuerdo comercial: existe o no con independencia de que la compañía pueda radicar
-- allí, y es lo que decide si el contrato de mandato lleva o no bloque de firma del mandatario.
--
-- Tabla propia y no una columna en el grant porque revocar el permiso BORRA la fila del grant
-- (`TransitGrantRepository.RemoveGrantAsync`), y eso se llevaría por delante el acuerdo comercial.
--
-- ── Firma física del mandatario ─────────────────────────────────────────────────
-- Va en el puente mandatario↔organismo y no en el mandatario: la misma persona puede firmar a mano
-- ante un organismo y electrónicamente ante otro.
-- DDL IDEMPOTENTE.

CREATE TABLE IF NOT EXISTS admin.company_transit_office_agreements (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_transit_office_agreements PRIMARY KEY (id),
    company_tenant_id uuid NOT NULL
        CONSTRAINT fk_ctoa_tenant
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    -- Baja lógica: retirar el convenio conserva la traza de que existió, que es lo que explica por qué
    -- un mandato emitido en su momento salió sin bloque de mandatario.
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- Un solo convenio por (compañía, organismo). Sin filtro parcial: la fila se conserva y se conmuta
-- `is_active`, así que reactivar reutiliza la misma fila en vez de acumular histórico duplicado.
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_transit_office_agreements
  ON admin.company_transit_office_agreements(company_tenant_id, transit_office_id);

-- La consulta del documento: "¿esta compañía tiene convenio con este organismo?" y, para el caso de
-- resguardo, "¿este organismo tiene convenio con alguna compañía?".
CREATE INDEX IF NOT EXISTS ix_company_transit_office_agreements_office
  ON admin.company_transit_office_agreements(transit_office_id, is_active);

-- Sin RLS: igual que `mandate_signers` y sus puentes, se consulta cruzando compañía y organismo desde
-- ambos lados (el admin de la compañía lo marca; el generador del documento lo lee en el tenant del
-- trámite). Meterle RLS por compañía dejaría la lectura del organismo sin resultados.

-- ── Firma física del mandatario, por organismo ──────────────────────────────────
ALTER TABLE admin.mandate_signer_transit_offices
    ADD COLUMN IF NOT EXISTS signs_physically boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN admin.mandate_signer_transit_offices.signs_physically IS
    'El mandatario firma a mano ante ese organismo: el documento deja la línea de guiones bajos con sus '
    'datos debajo y no estampa firma del baúl ni sello de identidad.';
