-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11484 (Feature #11482) — cola de despachos de correo al asignar placa.
--
-- SOLO ESQUEMA. Aquí no se inserta ni se lee una fila: eso es la HU #11485 (sink)
-- y la HU #11487 (worker). La idempotencia vive en los índices UNIQUE parciales
-- (ADR-0046): ningún consumidor posterior escribe deduplicación en C#.
--
-- Gemela de procedure_state_change_email_dispatches (DDL 69) SIN outbox_id ni FK
-- a procedure_state_change_outbox — el evento es (procedure_instance_id, placa).
--
-- IDEMPOTENTE: CREATE TABLE / INDEX IF NOT EXISTS + guardas de política RLS.
-- Re-ejecutarlo no duplica nada ni pierde datos.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tramites.plate_assignment_email_dispatches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_plate_assignment_email_dispatches PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_pae_dispatches_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL,

    plate varchar(20) NOT NULL,

    -- Nullable: cupo «omitido» (sin correo resoluble). Con correo, varchar(320).
    recipient varchar(320),

    -- Razón social / nombre del RL / nombre de la persona (EmailMessage.ToName).
    recipient_name varchar(200),

    -- Rol en el trámite (comprador | vendedor). Columna SEPARADA de recipient_kind.
    recipient_role varchar(30) NOT NULL,

    -- Tipo de cupo: persona | empresa | representante_legal.
    recipient_kind varchar(30) NOT NULL
        CONSTRAINT ck_pae_dispatches_recipient_kind
        CHECK (recipient_kind IN ('persona', 'empresa', 'representante_legal')),

    -- Plantilla del catálogo (tramites.asignacion-placa).
    template_key varchar(100) NOT NULL,

    status varchar(20) NOT NULL
        CONSTRAINT ck_pae_dispatches_status
        CHECK (status IN ('pendiente', 'enviado', 'fallido', 'omitido')),

    failure_reason varchar(1000),

    attempts int NOT NULL DEFAULT 0
        CONSTRAINT ck_pae_dispatches_attempts CHECK (attempts >= 0),

    queued_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz,

    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

-- Idempotencia por buzón dentro del mismo evento (procedure_instance_id + placa) (ADR-0046).
CREATE UNIQUE INDEX IF NOT EXISTS uq_pae_dispatches_instance_plate_recipient
  ON tramites.plate_assignment_email_dispatches (procedure_instance_id, upper(plate), lower(recipient))
  WHERE recipient IS NOT NULL;

-- Idempotencia de cupos vacíos: hasta dos huecos por evento (comprador PJ: empresa + RL).
CREATE UNIQUE INDEX IF NOT EXISTS uq_pae_dispatches_instance_plate_gap
  ON tramites.plate_assignment_email_dispatches (procedure_instance_id, upper(plate), recipient_role, recipient_kind)
  WHERE recipient IS NULL;

-- Cola del worker de envío.
CREATE INDEX IF NOT EXISTS ix_pae_dispatches_pending_queued_at
  ON tramites.plate_assignment_email_dispatches (queued_at)
  WHERE status = 'pendiente';

CREATE INDEX IF NOT EXISTS ix_pae_dispatches_instance
  ON tramites.plate_assignment_email_dispatches (procedure_instance_id);

COMMENT ON TABLE tramites.plate_assignment_email_dispatches IS
  'HU #11484 (Feature #11482) / ADR-0046 — cola de despachos de correo al asignar placa. Idempotencia por (procedure_instance_id, placa, destinatario). La escribe el sink (HU #11485); la consume el worker (HU #11487).';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.tenant_id IS
  'Tenant cliente dueño del trámite y de la política de canal. NOT NULL: sin tenant la fila es irrastreable y la RLS nunca la devolvería.';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.plate IS
  'Placa asignada que identifica el evento de negocio junto con procedure_instance_id. upper(plate) participa en la idempotencia.';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.recipient IS
  '@pii:medium — correo del destinatario (Ley 1581). Finalidad: trazabilidad del envío del aviso de asignación de placa (probar a quién se le encoló/envió y con qué desenlace). NULL cuando el cupo quedó omitido por falta de correo. No usar para otra cosa.';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.recipient_role IS
  'Rol del destinatario en el trámite (comprador | vendedor). Separada de recipient_kind para distinguir «vendedor empresa» de «representante legal del vendedor».';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.recipient_kind IS
  'Cupo de persona: persona | empresa | representante_legal.';

COMMENT ON COLUMN tramites.plate_assignment_email_dispatches.status IS
  'Desenlace del cupo: pendiente | enviado | fallido | omitido.';

ALTER TABLE tramites.plate_assignment_email_dispatches ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.plate_assignment_email_dispatches;
CREATE POLICY tenant_isolation ON tramites.plate_assignment_email_dispatches
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
