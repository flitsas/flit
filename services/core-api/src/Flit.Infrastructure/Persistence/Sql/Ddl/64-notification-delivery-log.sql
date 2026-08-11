-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11357 (Feature #11348) — Bitácora de envíos de notificación + interruptor
-- propio de documentos personalizados.
--
-- SOLO ESQUEMA. Aquí no se escribe una sola fila de bitácora ni se lee el campo
-- nuevo: eso es la HU #11363 (escritura) y la HU #11362 (enrutamiento).
--
-- ORDEN DE DESPLIEGUE — NO NEGOCIABLE:
--   Esta migración DEBE estar aplicada ANTES de desplegar la HU #11362
--   (enrutamiento por canal). Si se despliega #11362 primero, el guard de
--   documentos personalizados deja de leer notification_channel y el tenant que
--   hoy está en 'tenant_api' PIERDE sus documentos personalizados: la
--   elegibilidad todavía no habría sido trasladada al campo propio que crea el
--   backfill 65.
--
-- TENSIÓN CON EL ADR-0042 (léela antes de tocar personalized_documents_enabled):
--   ADR-0042 («Documentos personalizados por compañía», Propuesto) dice que el
--   interruptor de la funcionalidad ES la existencia de una versión activa por
--   (tenant, tipo de documento), y que «no hay un booleano paralelo que pueda
--   contradecirla». La columna que se agrega abajo es, en la letra, ese booleano.
--   La resolución documental de esa contradicción es el ADR-0043 (entregable de
--   la HU #11362) — NO se resuelve en este DDL. Aquí solo se materializa el
--   campo y se deja la remisión, para que nadie lea la columna sin enterarse de
--   la discusión.
--
-- IDEMPOTENTE: CREATE TABLE IF NOT EXISTS / ADD COLUMN IF NOT EXISTS + guardas
-- en índices y políticas. Re-ejecutarlo no duplica nada ni pierde datos.
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- 1) admin.notification_delivery_logs — bitácora de envíos (AC1, AC5)
-- ============================================================================
-- Tabla de bitácora APPEND-ONLY, con la misma forma que su vecina
-- admin.ot_api_call_logs (09-HU10152-ot-admin.sql): sin soft delete, sin
-- row_version y sin trigger de auditoría. Una bitácora que se puede editar o
-- borrar en blando no es evidencia; y auditar una tabla que ya es la auditoría
-- del envío solo duplicaría cada fila en audit_log. La retención/purga es
-- decisión de operación, no de esta HU.
CREATE TABLE IF NOT EXISTS admin.notification_delivery_logs (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_notification_delivery_logs PRIMARY KEY (id),

    -- NOT NULL sin default: una fila de bitácora sin dueño no es rastreable y la
    -- política RLS jamás la volvería a mostrar (AC5).
    tenant_id uuid NOT NULL
        CONSTRAINT fk_notification_delivery_logs_tenant
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,

    -- Plantilla del catálogo de notificaciones (Feature #11347). Sin FK a
    -- propósito: la bitácora debe sobrevivir al renombrado o baja de una
    -- plantilla; se guarda la llave tal como se usó al enviar.
    template_key varchar(100) NOT NULL,

    -- Canal por el que salió el envío ('flit_smtp' | 'tenant_api' | los que
    -- agregue el enrutamiento). Sin CHECK a propósito: un vocabulario cerrado
    -- aquí haría que la BD RECHACE la evidencia de un envío por un canal nuevo,
    -- que es justo lo que la bitácora existe para registrar.
    channel varchar(30) NOT NULL,

    recipient varchar(320) NOT NULL,

    result varchar(20) NOT NULL
        CONSTRAINT ck_notification_delivery_logs_result
        CHECK (result IN ('enviado', 'fallido')),

    -- Causa del fallo. Libre y nullable: en 'enviado' no hay causa que contar.
    failure_reason varchar(1000),

    duration_ms integer NOT NULL
        CONSTRAINT ck_notification_delivery_logs_duration_ms
        CHECK (duration_ms >= 0),

    occurred_at timestamptz NOT NULL DEFAULT now(),

    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

-- Lectura canónica del banco de pruebas y del histórico: lo último de ESTE
-- tenant. tenant_id primero (nunca se consulta la bitácora sin tenant), fecha
-- descendente porque siempre se pide la cola reciente.
CREATE INDEX IF NOT EXISTS ix_notification_delivery_logs_tenant_occurred_at
  ON admin.notification_delivery_logs (tenant_id, occurred_at DESC);

COMMENT ON TABLE admin.notification_delivery_logs IS
  'HU #11357 (Feature #11348) — bitácora append-only de envíos de notificación: plantilla, canal, destinatario, resultado, causa y duración. La escribe la HU #11363; aquí solo el esquema.';

COMMENT ON COLUMN admin.notification_delivery_logs.tenant_id IS
  'Dueño de la fila. NOT NULL: sin tenant la fila es irrastreable y la RLS nunca la devolvería.';
COMMENT ON COLUMN admin.notification_delivery_logs.template_key IS
  'Llave de la plantilla usada (catálogo del Feature #11347), copiada al enviar. Sin FK: la bitácora sobrevive a la baja de la plantilla.';
COMMENT ON COLUMN admin.notification_delivery_logs.channel IS
  'Canal efectivo del envío (flit_smtp | tenant_api | ...). Sin CHECK: la bitácora no puede rechazar la evidencia de un canal nuevo.';
COMMENT ON COLUMN admin.notification_delivery_logs.recipient IS
  '@pii:medium — correo/identificador del destinatario (Ley 1581). Finalidad: auditoría de envíos (probar a quién se le envió, cuándo y con qué resultado). No usar para otra cosa.';
COMMENT ON COLUMN admin.notification_delivery_logs.result IS
  'Desenlace del envío: enviado | fallido.';
COMMENT ON COLUMN admin.notification_delivery_logs.failure_reason IS
  'Causa del fallo cuando result = fallido (p. ej. configuración incompleta del canal). NULL si el envío salió.';
COMMENT ON COLUMN admin.notification_delivery_logs.duration_ms IS
  'Duración del intento en milisegundos, extremo a extremo del adaptador de canal.';
COMMENT ON COLUMN admin.notification_delivery_logs.occurred_at IS
  'Marca de tiempo del intento de envío (no la de inserción de la fila).';

ALTER TABLE admin.notification_delivery_logs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.notification_delivery_logs;
CREATE POLICY tenant_isolation ON admin.notification_delivery_logs
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- 2) admin.tenant_operational_policies — interruptor propio (AC2)
-- ============================================================================
-- Default false = comportamiento actual para todo tenant nuevo. La elegibilidad
-- de los tenants YA existentes la traslada el backfill 65, que corre después.
ALTER TABLE admin.tenant_operational_policies
  ADD COLUMN IF NOT EXISTS personalized_documents_enabled boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN admin.tenant_operational_policies.personalized_documents_enabled IS
  'HU #11357 — interruptor propio de documentos personalizados por compañía. Sustituye el uso de notification_channel = tenant_api como interruptor de facto (Bug #11311). OJO: el ADR-0042 declara que el interruptor ES la versión activa por (tenant, tipo) y que "no hay un booleano paralelo"; la contradicción se resuelve en el ADR-0043 (HU #11362), no aquí. Backfill de elegibilidad: DDL 65.';
