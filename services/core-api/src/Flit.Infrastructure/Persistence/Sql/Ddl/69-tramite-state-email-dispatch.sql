-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11461 (Feature #11459) — cola de despachos de correo al cambio de estado.
--
-- SOLO ESQUEMA. Aquí no se inserta ni se lee una fila: eso es la HU #11465 (sink)
-- y la HU #11467 (worker). La idempotencia vive en los índices UNIQUE parciales
-- (ADR-0045): ningún consumidor posterior escribe deduplicación en C#.
--
-- IDEMPOTENTE: CREATE TABLE / INDEX IF NOT EXISTS + guardas de política RLS.
-- Re-ejecutarlo no duplica nada ni pierde datos.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tramites.procedure_state_change_email_dispatches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_state_change_email_dispatches PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_psce_dispatches_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- Evento de outbox que originó el cupo. ON DELETE CASCADE: si se purga el
    -- outbox, los despachos asociados no quedan huérfanos.
    outbox_id uuid NOT NULL
        CONSTRAINT fk_psce_dispatches_outbox
        REFERENCES tramites.procedure_state_change_outbox(id) ON DELETE CASCADE ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL,

    -- Nullable: cupo «omitido» (sin correo resoluble). Con correo, varchar(320).
    recipient varchar(320),

    -- Razón social / nombre del RL / nombre de la persona (EmailMessage.ToName).
    recipient_name varchar(200),

    -- Rol en el trámite (comprador | vendedor). Columna SEPARADA de recipient_kind.
    recipient_role varchar(30) NOT NULL,

    -- Tipo de cupo: persona | empresa | representante_legal.
    recipient_kind varchar(30) NOT NULL
        CONSTRAINT ck_psce_dispatches_recipient_kind
        CHECK (recipient_kind IN ('persona', 'empresa', 'representante_legal')),

    -- Plantilla del catálogo (tramites.aprobado | tramites.rechazado).
    template_key varchar(100) NOT NULL,

    status varchar(20) NOT NULL
        CONSTRAINT ck_psce_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido')),

    failure_reason varchar(1000),

    attempts int NOT NULL DEFAULT 0
        CONSTRAINT ck_psce_dispatches_attempts CHECK (attempts >= 0),

    queued_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz,

    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

-- Idempotencia por buzón dentro del mismo evento (ADR-0045).
CREATE UNIQUE INDEX IF NOT EXISTS uq_psce_dispatches_outbox_recipient
  ON tramites.procedure_state_change_email_dispatches (outbox_id, lower(recipient))
  WHERE recipient IS NOT NULL;

-- Idempotencia de cupos vacíos: hasta cuatro huecos por evento (comprador/vendedor × empresa/RL).
-- NO hay UNIQUE solo sobre outbox_id.
CREATE UNIQUE INDEX IF NOT EXISTS uq_psce_dispatches_outbox_gap
  ON tramites.procedure_state_change_email_dispatches (outbox_id, recipient_role, recipient_kind)
  WHERE recipient IS NULL;

-- Cola del worker de envío.
CREATE INDEX IF NOT EXISTS ix_psce_dispatches_pending_queued_at
  ON tramites.procedure_state_change_email_dispatches (queued_at)
  WHERE status = 'pendiente';

CREATE INDEX IF NOT EXISTS ix_psce_dispatches_instance
  ON tramites.procedure_state_change_email_dispatches (procedure_instance_id);

COMMENT ON TABLE tramites.procedure_state_change_email_dispatches IS
  'HU #11461 (Feature #11459) / ADR-0045 — cola de despachos de correo al cambio de estado del trámite. Idempotencia por (outbox_id, destinatario). La escribe el sink (HU #11465); la consume el worker (HU #11467).';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.tenant_id IS
  'Dueño de la fila. NOT NULL: sin tenant la fila es irrastreable y la RLS nunca la devolvería.';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.outbox_id IS
  'Fila de procedure_state_change_outbox que originó este cupo. Un outbox_id = un aviso de negocio.';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.recipient IS
  '@pii:medium — correo del destinatario (Ley 1581). Finalidad: trazabilidad del envío del aviso de cambio de estado (probar a quién se le encoló/envió y con qué desenlace). NULL cuando el cupo quedó omitido por falta de correo. No usar para otra cosa.';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.recipient_role IS
  'Rol del destinatario en el trámite (comprador | vendedor). Separada de recipient_kind para distinguir «vendedor empresa» de «representante legal del vendedor».';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.recipient_kind IS
  'Cupo de persona: persona | empresa | representante_legal.';

COMMENT ON COLUMN tramites.procedure_state_change_email_dispatches.status IS
  'Desenlace del cupo: pendiente | enviado | fallido | omitido.';

ALTER TABLE tramites.procedure_state_change_email_dispatches ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_state_change_email_dispatches;
CREATE POLICY tenant_isolation ON tramites.procedure_state_change_email_dispatches
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
