-- =============================================================================
-- HU #14469 (Feature FLIT2 OT, Épica #14297) — pago del portal registrado en el trámite.
-- Migración: 20261109120000_HU14469_PagoPortalTramite. Idempotente y aditiva: no altera ninguna tabla existente.
--
-- El punto de atención (canal pn_punto) solo radica cuando el pago está confirmado en caja. El portal manda el pago en el RPC
-- ObtenerSolicitudParaRadicar y core-api lo guarda ligado al trámite creado. El trámite sigue en borrador.
--
--   tramites.procedure_instance_portal_payments  un pago por trámite (uq por procedure_instance_id) y un cobro del portal por
--                                                fila (uq por charge_id): reintentar la misma radicación no duplica el pago.
--
-- Sin datos personales. El total se guarda en numeric(15,2) por convención de montos; el contrato del portal lo entrega en pesos
-- enteros.
-- =============================================================================

CREATE TABLE IF NOT EXISTS tramites.procedure_instance_portal_payments (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_portal_payments PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_procedure_instance_portal_payments_tenant
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_procedure_instance_portal_payments_procedure_instance
        REFERENCES tramites.procedure_instances (id) ON DELETE CASCADE ON UPDATE CASCADE,

    amount_cop numeric(15, 2) NOT NULL,
    payment_method varchar(20) NOT NULL,
    cash_method varchar(20),
    cash_method_detail varchar(200),
    paid_at timestamptz NOT NULL,
    charge_id varchar(100) NOT NULL,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,

    CONSTRAINT ck_procedure_instance_portal_payments_amount CHECK (amount_cop >= 0),
    CONSTRAINT ck_procedure_instance_portal_payments_method CHECK (payment_method IN ('caja', 'sin_cobro')),
    CONSTRAINT ck_procedure_instance_portal_payments_cash_method
        CHECK (cash_method IS NULL OR cash_method IN ('efectivo', 'datafono', 'transferencia', 'otro')),
    CONSTRAINT ck_procedure_instance_portal_payments_method_consistency CHECK (
        (payment_method = 'caja' AND cash_method IS NOT NULL
            AND (cash_method <> 'otro' OR NULLIF(btrim(cash_method_detail), '') IS NOT NULL))
        OR (payment_method = 'sin_cobro' AND amount_cop = 0 AND cash_method IS NULL))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_portal_payments_instance
    ON tramites.procedure_instance_portal_payments (tenant_id, procedure_instance_id)
    WHERE deleted_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_portal_payments_charge
    ON tramites.procedure_instance_portal_payments (tenant_id, charge_id)
    WHERE deleted_at IS NULL;

-- Cubre la FK al trámite (el índice único de arriba lleva tenant_id primero).
CREATE INDEX IF NOT EXISTS ix_procedure_instance_portal_payments_instance
    ON tramites.procedure_instance_portal_payments (procedure_instance_id);

COMMENT ON TABLE tramites.procedure_instance_portal_payments IS
    'Pago confirmado en caja del punto de atención, ligado al trámite creado desde el portal (HU #14469). Un pago por trámite. Sin PII.';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.amount_cop IS
    'Total cobrado en pesos colombianos (el portal lo entrega sin decimales).';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.payment_method IS
    'caja | sin_cobro.';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.cash_method IS
    'efectivo | datafono | transferencia | otro; NULL si payment_method = sin_cobro.';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.cash_method_detail IS
    'Detalle libre cuando cash_method = otro; NULL en los demás casos.';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.paid_at IS
    'Fecha y hora del pago en el portal (UTC).';
COMMENT ON COLUMN tramites.procedure_instance_portal_payments.charge_id IS
    'Id del cobro en el portal; llave de idempotencia del registro.';

ALTER TABLE tramites.procedure_instance_portal_payments ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_instance_portal_payments;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_portal_payments
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_procedure_instance_portal_payments_row_version ON tramites.procedure_instance_portal_payments;
CREATE TRIGGER tr_procedure_instance_portal_payments_row_version BEFORE UPDATE ON tramites.procedure_instance_portal_payments
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_instance_portal_payments_audit ON tramites.procedure_instance_portal_payments;
CREATE TRIGGER tr_procedure_instance_portal_payments_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_portal_payments
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
