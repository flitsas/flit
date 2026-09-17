-- HU #12570 (Feature #12565) — tabla propia de solicitudes de revocatoria de trámite aprobado.
-- Independiente de TramiteEstado/TramiteStateMachine (ADR-0022): el trámite permanece 'Aprobado'
-- en tramites.procedure_instances durante todo este sub-flujo; el ciclo de vida de los intentos
-- de revocatoria vive únicamente aquí, vía attempt_number (mismo espíritu que plate_flow_status:
-- sub-estado ortogonal que NO toca el estado principal).
--
-- attempt_number: acumula todos los intentos sin sobrescribir el trámite principal (AC1) — una
-- fila nueva por cada solicitud, nunca se actualiza una fila previa para "reintentar".
-- status: 'solicitada' (creada) -> 'en_revision' (opcional, en manos del OT) -> 'aprobada' |
-- 'rechazada' (decisión final). decided_at/decided_by/decision_reason/decision_document_id solo
-- se llenan al decidir (CHECK de consistencia).
-- AC2: uq_procedure_revocation_requests_active_per_instance impide más de una fila con status en
-- ('solicitada','en_revision') simultáneamente para el mismo procedure_instance_id (evita
-- solicitudes activas duplicadas) vía índice único parcial.
-- support_document_id / decision_document_id referencian tramites.procedure_instance_attachments
-- (el documento adjunto de soporte de la solicitud / de la decisión), igual que el resto de FKs a
-- documentos del módulo trámites.
-- DDL IDEMPOTENTE (CREATE ... IF NOT EXISTS + guardas para índices/políticas/triggers).

CREATE TABLE IF NOT EXISTS tramites.procedure_revocation_requests (
    id                     uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_revocation_requests PRIMARY KEY (id),
    tenant_id              uuid         NOT NULL
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id  uuid         NOT NULL
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    attempt_number         integer      NOT NULL,
    status                 varchar(20)  NOT NULL DEFAULT 'solicitada'
        CONSTRAINT ck_procedure_revocation_requests_status
        CHECK (status IN ('solicitada', 'en_revision', 'aprobada', 'rechazada')),
    reason                 varchar(500) NULL,
    support_document_id    uuid         NULL
        REFERENCES tramites.procedure_instance_attachments(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    requested_by           uuid         NOT NULL
        REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    requested_at           timestamptz  NOT NULL DEFAULT now(),
    decided_by             uuid         NULL
        REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    decided_at             timestamptz  NULL,
    decision_reason        varchar(500) NULL,
    decision_document_id   uuid         NULL
        REFERENCES tramites.procedure_instance_attachments(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    row_version            bigint       NOT NULL DEFAULT 0,
    CONSTRAINT ck_procedure_revocation_requests_attempt_number CHECK (attempt_number >= 1),
    CONSTRAINT uq_procedure_revocation_requests_instance_attempt UNIQUE (procedure_instance_id, attempt_number),
    CONSTRAINT ck_procedure_revocation_requests_decision_consistency CHECK (
        (status IN ('solicitada', 'en_revision') AND decided_at IS NULL AND decided_by IS NULL)
        OR
        (status IN ('aprobada', 'rechazada') AND decided_at IS NOT NULL AND decided_by IS NOT NULL)
    )
);

-- Consumo principal: historial de intentos de revocatoria de un trámite, más recientes primero.
CREATE INDEX IF NOT EXISTS ix_procedure_revocation_requests_tenant_instance
  ON tramites.procedure_revocation_requests(tenant_id, procedure_instance_id, requested_at DESC);

-- AC2: como máximo una solicitud activa (solicitada|en_revision) por procedure_instance_id.
CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_revocation_requests_active_per_instance
  ON tramites.procedure_revocation_requests(tenant_id, procedure_instance_id)
  WHERE status IN ('solicitada', 'en_revision');

COMMENT ON TABLE tramites.procedure_revocation_requests IS
  'HU #12570 (Feature #12565): solicitudes de revocatoria de un trámite Aprobado. Una fila por intento (attempt_number); no sobrescribe ni reabre tramites.procedure_instances.';
COMMENT ON COLUMN tramites.procedure_revocation_requests.attempt_number IS
  'Número de intento de revocatoria para el mismo procedure_instance_id, monotónico desde 1.';
COMMENT ON COLUMN tramites.procedure_revocation_requests.status IS
  'solicitada|en_revision (activas, ver uq_procedure_revocation_requests_active_per_instance) | aprobada|rechazada (decisión final).';

ALTER TABLE tramites.procedure_revocation_requests ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_revocation_requests;
CREATE POLICY tenant_isolation ON tramites.procedure_revocation_requests
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Triggers negocio (checklist A16): row_version por fila (aprobar/rechazar actualiza la misma
-- fila de la solicitud) + audit_log estándar. NO es append-only puro como
-- procedure_instance_status_history: la solicitud se decide en la misma fila, no en una nueva.
DROP TRIGGER IF EXISTS tr_procedure_revocation_requests_row_version ON tramites.procedure_revocation_requests;
CREATE TRIGGER tr_procedure_revocation_requests_row_version BEFORE UPDATE ON tramites.procedure_revocation_requests
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_procedure_revocation_requests_audit ON tramites.procedure_revocation_requests;
CREATE TRIGGER tr_procedure_revocation_requests_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_revocation_requests
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
