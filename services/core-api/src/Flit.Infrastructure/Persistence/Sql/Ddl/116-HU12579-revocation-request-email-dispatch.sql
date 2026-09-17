-- ─────────────────────────────────────────────────────────────────────────────
-- HU #12579 (Feature #12565) — cola de despachos de correo por hito del sub-flujo
-- de revocatoria (solicitada | aprobada | rechazada).
--
-- SOLO ESQUEMA. La escribe el sink (RevocationRequestNotificationEnqueuer, ya existente
-- desde HU #12572/#12576 — aquí se conecta a esta cola además de su evento de auditoría) y la
-- consume el worker nuevo de esta HU (RevocationRequestEmailDispatchProcessor).
--
-- Gemela de tramites.plate_assignment_email_dispatches (DDL 71, ADR-0046 Opción B) con dos
-- diferencias deliberadas:
--   1. La llave de idempotencia es (revocation_request_id, milestone) en vez de
--      (procedure_instance_id, plate): la "noticia" de este sub-flujo es el HITO de un intento de
--      revocatoria concreto, no una placa. Un mismo procedure_instance_id puede tener varios
--      revocation_request_id (varios intentos, HU #12570) y cada uno dispara sus propios correos.
--   2. SÍ hay FK a revocation_request_id -> tramites.procedure_revocation_requests(id): a
--      diferencia del sub-estado de placa (que no tiene una tabla propia de la que colgarse), aquí
--      el evento de negocio YA es una fila real y estable (HU #12570); no hay outbox de por medio
--      ni riesgo de fan-out indebido a webhooks OT/ICT (ADR-0046 §Contexto punto 2 no aplica: esta
--      tabla no la lee ningún worker de integraciones, solo el processor de correo de abajo).
--
-- decision_reason: denormalizado desde tramites.procedure_revocation_requests.decision_reason en
-- el momento del encolado (milestone = 'rechazada'), para que el worker no tenga que releer la
-- fila padre al enviar — mismo criterio de denormalización que recipient_name.
--
-- IDEMPOTENTE: CREATE TABLE / INDEX IF NOT EXISTS + guardas de política RLS.
-- Re-ejecutarlo no duplica nada ni pierde datos.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tramites.revocation_request_email_dispatches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_revocation_request_email_dispatches PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_rre_dispatches_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL,

    revocation_request_id uuid NOT NULL
        CONSTRAINT fk_rre_dispatches_revocation_request
        REFERENCES tramites.procedure_revocation_requests(id) ON DELETE CASCADE ON UPDATE CASCADE,

    -- Denormalizado del padre (mismo valor todo el ciclo de vida de la fila padre); solo para
    -- trazabilidad/depuración, no participa en la idempotencia.
    attempt_number integer NOT NULL,

    -- Hito del sub-flujo que originó el correo (AC1 de HU #12579).
    milestone varchar(20) NOT NULL
        CONSTRAINT ck_rre_dispatches_milestone
        CHECK (milestone IN ('solicitada', 'aprobada', 'rechazada')),

    -- Nullable: cupo «omitido» (sin correo resoluble). Con correo, varchar(320).
    recipient varchar(320),

    -- Nombre de pila del radicador (EmailMessage.ToName).
    recipient_name varchar(200),

    -- Rol en el trámite. Hoy solo 'radicador' (acuse a quien radicó la solicitud/decisión), columna
    -- separada de recipient_kind por si un alcance futuro amplía destinatarios (ver XML doc del
    -- enqueuer sobre por qué hoy NO se amplía).
    recipient_role varchar(30) NOT NULL,

    -- Tipo de cupo: persona | empresa | representante_legal (mismo catálogo que la cola hermana).
    recipient_kind varchar(30) NOT NULL
        CONSTRAINT ck_rre_dispatches_recipient_kind
        CHECK (recipient_kind IN ('persona', 'empresa', 'representante_legal')),

    -- Plantilla del catálogo: tramites.revocatoria-solicitada | -aprobada | -rechazada.
    template_key varchar(100) NOT NULL,

    status varchar(20) NOT NULL
        CONSTRAINT ck_rre_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido')),

    failure_reason varchar(1000),

    -- Motivo de rechazo (DecideRevocationRequestApiRequest.Reason), solo cuando milestone =
    -- 'rechazada'. NULL en 'solicitada'/'aprobada'.
    decision_reason varchar(500),

    attempts int NOT NULL DEFAULT 0
        CONSTRAINT ck_rre_dispatches_attempts CHECK (attempts >= 0),

    queued_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz,

    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

-- Idempotencia por buzón dentro del mismo hito del mismo intento de revocatoria.
CREATE UNIQUE INDEX IF NOT EXISTS uq_rre_dispatches_request_milestone_recipient
  ON tramites.revocation_request_email_dispatches (revocation_request_id, milestone, lower(recipient))
  WHERE recipient IS NOT NULL;

-- Idempotencia de cupos vacíos (hoy a lo sumo uno: radicador sin correo resoluble).
CREATE UNIQUE INDEX IF NOT EXISTS uq_rre_dispatches_request_milestone_gap
  ON tramites.revocation_request_email_dispatches (revocation_request_id, milestone, recipient_role, recipient_kind)
  WHERE recipient IS NULL;

-- Cola del worker de envío.
CREATE INDEX IF NOT EXISTS ix_rre_dispatches_pending_queued_at
  ON tramites.revocation_request_email_dispatches (queued_at)
  WHERE status = 'pendiente';

CREATE INDEX IF NOT EXISTS ix_rre_dispatches_instance
  ON tramites.revocation_request_email_dispatches (procedure_instance_id);

COMMENT ON TABLE tramites.revocation_request_email_dispatches IS
  'HU #12579 (Feature #12565) — cola de despachos de correo por hito del sub-flujo de revocatoria (solicitada|aprobada|rechazada). Idempotencia por (revocation_request_id, milestone, destinatario). La escribe RevocationRequestNotificationEnqueuer; la consume RevocationRequestEmailDispatchProcessor.';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.tenant_id IS
  'Tenant cliente dueño del trámite y de la política de canal. NOT NULL: sin tenant la fila es irrastreable y la RLS nunca la devolvería.';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.milestone IS
  'Hito que originó el correo: solicitada | aprobada | rechazada.';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.recipient IS
  '@pii:medium — correo del destinatario (Ley 1581). Finalidad: trazabilidad del envío del aviso de revocatoria (probar a quién se le encoló/envió y con qué desenlace). NULL cuando el cupo quedó omitido por falta de correo. No usar para otra cosa.';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.recipient_role IS
  'Rol del destinatario en el trámite. Hoy únicamente radicador (acuse de su propia solicitud/decisión).';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.status IS
  'Desenlace del cupo: pendiente | enviado | fallido | omitido.';

COMMENT ON COLUMN tramites.revocation_request_email_dispatches.decision_reason IS
  'Motivo de rechazo denormalizado desde tramites.procedure_revocation_requests.decision_reason. Solo milestone=rechazada.';

ALTER TABLE tramites.revocation_request_email_dispatches ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.revocation_request_email_dispatches;
CREATE POLICY tenant_isolation ON tramites.revocation_request_email_dispatches
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
