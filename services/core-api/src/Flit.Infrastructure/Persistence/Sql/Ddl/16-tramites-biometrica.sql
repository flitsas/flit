-- Trámites Rework — Slice 6: biométrica (mock).
-- Validación remota de identidad de una parte vía 3 fotos (selfie + cédula frontal/reverso),
-- magic-link con token (TTL 24h, máx 5 intentos), scorer MOCK determinista (threshold >= 60).
-- Estados: enviado -> en_proceso -> aprobado | rechazado | expirado.

-- ============================================================================
-- procedure_instance_biometric_validations
-- ============================================================================
CREATE TABLE tramites.procedure_instance_biometric_validations (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_biometric_validations PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    parte varchar(20),
    nombre varchar(200) NOT NULL,
    tipo_doc varchar(20) NOT NULL,
    documento varchar(40) NOT NULL,
    email varchar(320) NOT NULL,
    estado varchar(20) NOT NULL DEFAULT 'enviado',
    token_hash varchar(64) NOT NULL,
    expires_at timestamptz NOT NULL,
    intentos int NOT NULL DEFAULT 0,
    max_intentos int NOT NULL DEFAULT 5,
    score int,
    detalle jsonb,
    foto_rostro_path varchar(1000),
    foto_cedula_frontal_path varchar(1000),
    foto_cedula_reverso_path varchar(1000),
    validado_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

CREATE INDEX ix_procedure_instance_biometric_validations_procedure_instance
  ON tramites.procedure_instance_biometric_validations(procedure_instance_id);
CREATE INDEX ix_procedure_instance_biometric_validations_tenant_id_instance
  ON tramites.procedure_instance_biometric_validations(tenant_id, procedure_instance_id);
CREATE UNIQUE INDEX uq_procedure_instance_biometric_validations_token_hash
  ON tramites.procedure_instance_biometric_validations(token_hash);

COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.parte IS 'comprador|vendedor (null en matrícula inicial)';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.estado IS 'enviado|en_proceso|aprobado|rechazado|expirado';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.token_hash IS 'SHA-256(token) — el token crudo nunca se persiste';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.nombre IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.documento IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.email IS '@pii:high';

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.procedure_instance_biometric_validations ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_biometric_validations
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- Trigger de auditoría (public.trg_audit_log)
-- ============================================================================
DROP TRIGGER IF EXISTS tr_procedure_instance_biometric_validations_audit ON tramites.procedure_instance_biometric_validations;
CREATE TRIGGER tr_procedure_instance_biometric_validations_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_biometric_validations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
