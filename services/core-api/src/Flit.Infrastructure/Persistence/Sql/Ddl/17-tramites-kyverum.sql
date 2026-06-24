-- Trámites — HU #10233: Integrar Kyverum Verify (fase 1) + contratos de eventos (fase 2).
-- 1) Extiende procedure_instance_biometric_validations con los campos del proveedor Kyverum.
-- 2) Crea tramites.identity_validation_outbox (outbox transaccional de eventos de identidad).
-- El scoring/captura ahora puede delegarse a Kyverum (provider='kyverum'); 'mock' sigue por defecto.

-- ============================================================================
-- ALTER procedure_instance_biometric_validations — campos del proveedor
-- ============================================================================
ALTER TABLE tramites.procedure_instance_biometric_validations
  ADD COLUMN IF NOT EXISTS provider varchar(20) NOT NULL DEFAULT 'mock',
  ADD COLUMN IF NOT EXISTS kyverum_verification_id varchar(200),
  ADD COLUMN IF NOT EXISTS capture_url varchar(2000),
  ADD COLUMN IF NOT EXISTS webhook_secret_encrypted varchar(2000),
  ADD COLUMN IF NOT EXISTS provider_status varchar(40),
  ADD COLUMN IF NOT EXISTS provider_payload jsonb;

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.provider IS 'mock|kyverum';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.kyverum_verification_id IS 'Id de verificación en Kyverum (correlación con el webhook)';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.webhook_secret_encrypted IS 'Secreto HMAC del webhook CIFRADO (Data Protection) — nunca en claro';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.provider_payload IS 'Payload del proveedor sanitizado (sin PII cruda ni secretos)';

-- Unicidad del verification_id solo cuando está presente (filas mock lo dejan NULL).
CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_biometric_validations_kyverum_verification_id
  ON tramites.procedure_instance_biometric_validations(kyverum_verification_id)
  WHERE kyverum_verification_id IS NOT NULL;

-- ============================================================================
-- identity_validation_outbox — outbox transaccional de eventos (fase 2)
-- ============================================================================
CREATE TABLE IF NOT EXISTS tramites.identity_validation_outbox (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_identity_validation_outbox PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    validation_id uuid NOT NULL,
    event_type varchar(50) NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    attempts int NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_identity_validation_outbox_validation
  ON tramites.identity_validation_outbox(validation_id);

-- Índice único PARCIAL: a lo sumo un evento 'completed' por validación ⇒ idempotencia del webhook.
CREATE UNIQUE INDEX IF NOT EXISTS uq_identity_validation_outbox_completed
  ON tramites.identity_validation_outbox(validation_id)
  WHERE event_type = 'identity_validation.completed';

-- Cola de pendientes para el worker de fase 2.
CREATE INDEX IF NOT EXISTS ix_identity_validation_outbox_unpublished
  ON tramites.identity_validation_outbox(occurred_at)
  WHERE published_at IS NULL;

COMMENT ON COLUMN tramites.identity_validation_outbox.event_type IS 'identity_validation.requested | identity_validation.completed';
COMMENT ON COLUMN tramites.identity_validation_outbox.payload IS 'Evento sanitizado (ids/estado/proveedor) — sin PII ni secretos';

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.identity_validation_outbox ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.identity_validation_outbox;
CREATE POLICY tenant_isolation ON tramites.identity_validation_outbox
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
