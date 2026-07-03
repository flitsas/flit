-- Trámites — N 03 (RNF01, ADR-0022): outbox transaccional de cambios de estado del trámite.
-- Cada transición de procedure_instances.status encola aquí (misma transacción) el evento a
-- notificar a las integraciones OT (webhooks). Un worker en background despacha las filas
-- pendientes (published_at IS NULL) y sella published_at. Mismo patrón que
-- tramites.identity_validation_outbox (17-tramites-kyverum.sql).

-- ============================================================================
-- procedure_state_change_outbox
-- ============================================================================
CREATE TABLE IF NOT EXISTS tramites.procedure_state_change_outbox (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_state_change_outbox PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL,
    from_status varchar(20),
    to_status varchar(20) NOT NULL,
    reason varchar(500),
    changed_by_user_id uuid,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    attempts int NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_procedure_state_change_outbox_instance
  ON tramites.procedure_state_change_outbox(procedure_instance_id);

-- Cola de pendientes para el worker de despacho.
CREATE INDEX IF NOT EXISTS ix_procedure_state_change_outbox_unpublished
  ON tramites.procedure_state_change_outbox(occurred_at)
  WHERE published_at IS NULL;

COMMENT ON COLUMN tramites.procedure_state_change_outbox.from_status IS 'Estado origen (vocabulario de negocio ADR-0022); null solo en la creación';
COMMENT ON COLUMN tramites.procedure_state_change_outbox.to_status IS 'Estado destino: borrador | anulado | preparado | entregado | aprobado | rechazado';
COMMENT ON COLUMN tramites.procedure_state_change_outbox.reason IS 'Motivo de la transición (RF05) — viaja en el payload del webhook OT';

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.procedure_state_change_outbox ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_state_change_outbox;
CREATE POLICY tenant_isolation ON tramites.procedure_state_change_outbox
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
