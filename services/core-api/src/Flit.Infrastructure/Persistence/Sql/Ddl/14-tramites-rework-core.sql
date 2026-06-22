-- Trámites Rework — Slice 1: schema núcleo (FEAT-2026-06-18-002)
-- Modalidad/tipología/checklist en procedure_instances + attachments, preflight,
-- comercial y eventos. Difiere biométrica/firmas/portal (slices 6-7).

-- ============================================================================
-- 1. ALTER procedure_instances — modalidad de entrada, tipología, checklist
-- ============================================================================
ALTER TABLE tramites.procedure_instances
  ADD COLUMN IF NOT EXISTS modalidad_entrada varchar(20) NOT NULL DEFAULT 'matricula_inicial',
  ADD COLUMN IF NOT EXISTS tipologia_codigo varchar(40),
  ADD COLUMN IF NOT EXISTS checklist_estado jsonb NOT NULL DEFAULT '{}';

-- ============================================================================
-- 2. procedure_instance_attachments — documentos en disco local
-- ============================================================================
CREATE TABLE tramites.procedure_instance_attachments (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_attachments PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    tipo varchar(40) NOT NULL,
    filename varchar(500) NOT NULL,
    mimetype varchar(150) NOT NULL,
    size_bytes bigint NOT NULL,
    sha256 varchar(64) NOT NULL,
    storage_path varchar(1000) NOT NULL,
    source varchar(20) NOT NULL DEFAULT 'user',
    uploaded_at timestamptz NOT NULL DEFAULT now(),
    uploaded_by uuid
);
CREATE INDEX ix_procedure_instance_attachments_tenant_id_instance
  ON tramites.procedure_instance_attachments(tenant_id, procedure_instance_id);
COMMENT ON COLUMN tramites.procedure_instance_attachments.filename IS '@pii:medium';
COMMENT ON COLUMN tramites.procedure_instance_attachments.tipo IS 'factura|aduana|impronta|soat|certificado_ambiental|compraventa|acta_remate|oficio_judicial|declaracion_aduana|comprobante_derechos|acta_entrega|otro';

-- ============================================================================
-- 3. procedure_instance_preflight_snapshots — semáforo persistido (green|yellow|red)
-- ============================================================================
CREATE TABLE tramites.procedure_instance_preflight_snapshots (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_preflight_snapshots PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    overall varchar(10) NOT NULL,
    checks jsonb NOT NULL DEFAULT '{}',
    provider varchar(40),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_procedure_instance_preflight_snapshots_tenant_id_instance_created
  ON tramites.procedure_instance_preflight_snapshots(tenant_id, procedure_instance_id, created_at DESC);
COMMENT ON COLUMN tramites.procedure_instance_preflight_snapshots.overall IS 'green|yellow|red (DI-1: literal yellow, no amber)';

-- ============================================================================
-- 4. procedure_instance_commercial — datos comerciales del traspaso (1:1)
-- ============================================================================
CREATE TABLE tramites.procedure_instance_commercial (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_commercial PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    valor_venta numeric(18,2),
    causal varchar(30),
    tasa_impuesto numeric(7,4),
    derechos numeric(18,2),
    metodo_pago varchar(40),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_procedure_instance_commercial_instance UNIQUE (procedure_instance_id)
);
COMMENT ON COLUMN tramites.procedure_instance_commercial.causal IS 'COMPRAVENTA|DONACION|DACION_EN_PAGO|ADJUDICACION';

-- ============================================================================
-- 5. procedure_instance_events — bitácora append-only (timeline + QR)
-- ============================================================================
CREATE TABLE tramites.procedure_instance_events (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_events PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    tipo varchar(60) NOT NULL,
    payload jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);
CREATE INDEX ix_procedure_instance_events_tenant_id_instance_created
  ON tramites.procedure_instance_events(tenant_id, procedure_instance_id, created_at DESC);

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.procedure_instance_attachments ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_attachments
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_preflight_snapshots ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_preflight_snapshots
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_commercial ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_commercial
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE tramites.procedure_instance_events ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_events
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- Triggers de auditoría (public.trg_audit_log)
-- ============================================================================
DROP TRIGGER IF EXISTS tr_procedure_instance_attachments_audit ON tramites.procedure_instance_attachments;
CREATE TRIGGER tr_procedure_instance_attachments_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_attachments
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_preflight_snapshots_audit ON tramites.procedure_instance_preflight_snapshots;
CREATE TRIGGER tr_procedure_instance_preflight_snapshots_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_preflight_snapshots
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_commercial_audit ON tramites.procedure_instance_commercial;
CREATE TRIGGER tr_procedure_instance_commercial_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_commercial
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_events_audit ON tramites.procedure_instance_events;
CREATE TRIGGER tr_procedure_instance_events_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_events
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
