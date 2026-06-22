-- Trámites Rework — Slice 7 Part B: portal público de participantes (magic-link) + consent Ley 1581.
-- Un participante (comprador|vendedor|mandatario) accede vía magic-link sin autenticación para
-- completar su parte del trámite: acepta el consentimiento de la Ley 1581 de 2012 -> sube documentos
-- -> realiza la biométrica -> firma.
--
-- SEGURIDAD: el token crudo NUNCA se persiste, solo su SHA-256 (token_hash, único). El token crudo
-- solo se devuelve una vez en la respuesta de invitación (no se envía email; en DEV el admin recibe
-- el magic-link). Token de un solo uso: completed_at lo revoca. Re-invitar rota el token + expiración.
-- TTL 24h. El consentimiento se guarda versionado con IP/UA como prueba de auditoría (Ley 1581).

-- ============================================================================
-- procedure_instance_participants
-- ============================================================================
CREATE TABLE tramites.procedure_instance_participants (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_participants PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    rol varchar(20) NOT NULL,
    nombre varchar(200) NOT NULL,
    email varchar(320) NOT NULL,
    telefono varchar(40),
    token_hash varchar(64) NOT NULL,
    whatsapp_opt_in boolean NOT NULL DEFAULT false,
    consent_1581_at timestamptz,
    consent_version varchar(40),
    consent_ip varchar(64),
    consent_user_agent varchar(120),
    expires_at timestamptz NOT NULL,
    completed_at timestamptz,
    last_reminder_at timestamptz,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz
);

CREATE INDEX ix_procedure_instance_participants_tenant_id_instance
  ON tramites.procedure_instance_participants(tenant_id, procedure_instance_id);

-- token_hash único: lookup público O(1) por hash, sin enumeración (solo SHA-256 persistido).
CREATE UNIQUE INDEX uq_procedure_instance_participants_token_hash
  ON tramites.procedure_instance_participants(token_hash);

COMMENT ON COLUMN tramites.procedure_instance_participants.rol IS 'comprador|vendedor|mandatario';
COMMENT ON COLUMN tramites.procedure_instance_participants.token_hash IS 'SHA-256 (hex) del token magic-link; el crudo nunca se persiste';
COMMENT ON COLUMN tramites.procedure_instance_participants.consent_version IS 'versión del texto de consentimiento Ley 1581 aceptada';
COMMENT ON COLUMN tramites.procedure_instance_participants.consent_ip IS 'IP del cliente al aceptar (prueba de auditoría Ley 1581)';
COMMENT ON COLUMN tramites.procedure_instance_participants.consent_user_agent IS 'User-Agent truncado al aceptar (prueba de auditoría Ley 1581)';
COMMENT ON COLUMN tramites.procedure_instance_participants.completed_at IS 'finalización: revoca el token (uso único)';

-- ============================================================================
-- RLS — aislamiento por tenant (igual que el resto del schema tramites)
-- ============================================================================
ALTER TABLE tramites.procedure_instance_participants ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_participants
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- Trigger de auditoría (public.trg_audit_log)
-- ============================================================================
DROP TRIGGER IF EXISTS tr_procedure_instance_participants_audit ON tramites.procedure_instance_participants;
CREATE TRIGGER tr_procedure_instance_participants_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_participants
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
