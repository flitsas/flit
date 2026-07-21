-- Integración Quipux (QX) — radicación de trámites en secretarías de tránsito.
-- Paridad funcional con FLIT 1.0 (BackCrudTransfer + BackCrudQuipux + Lambdas cron), corrigiendo
-- sus defectos estructurales: aquí el estado de la radicación es una fila propia (no el último
-- log), la idempotencia la garantiza un índice único parcial (no la esperanza), y el par
-- request/response NUNCA se persiste crudo (1.0 guardaba json_send/json_response con PII dentro).
--
-- Activador: la SECRETARIA DESTINO, en catalogs.transit_offices — divipo_code + la bandera de la
-- familia del trámite (quipux_registration / quipux_transfer / quipux_other). Es el equivalente de
-- traffic_secretaries.id_parinttrasec_transfer/_registration/_otherservice = 2 de FLIT 1.0, que
-- también vivía en el catálogo de secretarías.
-- OJO: NO es admin.transit_office_profiles.operation_mode = 'quipux'. Ese perfil describe al
-- OT-CLIENTE cuya consola queda en solo lectura porque aprueba dentro de Quipux, y solo existe para
-- los organismos que son tenants de FLIT (12 de 317). La secretaría a la que se radica normalmente
-- no es cliente. Comparten el nombre y son cosas distintas.
--
-- Mapeo por datos, no por código: los tipos de trámite Quipux (16/213/13) y la familia salen de
-- tramites.procedure_types.external_refs->'quipux'. Añadir un trámite nuevo es un UPDATE.

-- ============================================================================
-- admin.quipux_settings — configuración operativa (fila única)
-- ============================================================================
-- Vive en BD y no en env vars por requisito: cambiar credenciales, URLs o cadencia debe ser un
-- UPDATE, sin desplegar. Los secretos van cifrados con Data Protection (keyring persistido en
-- Postgres), así que un dump de la tabla no los expone en claro.
-- Sin tenant_id: el contrato con Quipux es de FLIT como consumidor (usuario FLIT, consumidor
-- 1003), no de cada cliente. Por eso tampoco lleva RLS.
CREATE TABLE IF NOT EXISTS admin.quipux_settings (
    id                          uuid PRIMARY KEY DEFAULT uuidv7(),
    enabled                     boolean NOT NULL DEFAULT false,

    url_login                   varchar(500) NOT NULL DEFAULT '',
    url_register_document       varchar(500) NOT NULL DEFAULT '',
    url_validate_status         varchar(500) NOT NULL DEFAULT '',
    username                    varchar(120) NOT NULL DEFAULT '',
    password_enc                text NOT NULL DEFAULT '',
    consumer_code               varchar(40) NOT NULL DEFAULT '',

    bucket                      varchar(200) NOT NULL DEFAULT '',
    s3_prefix                   varchar(100) NOT NULL DEFAULT 'FLIT/',
    aws_region                  varchar(40) NOT NULL DEFAULT 'us-east-1',
    aws_access_key_id           varchar(200) NOT NULL DEFAULT '',
    aws_secret_access_key_enc   text NOT NULL DEFAULT '',

    officer_document_type       smallint NOT NULL DEFAULT 3,
    officer_document_number     varchar(40) NOT NULL DEFAULT '',

    register_interval_minutes   integer NOT NULL DEFAULT 15 CHECK (register_interval_minutes BETWEEN 1 AND 1440),
    poll_interval_minutes       integer NOT NULL DEFAULT 15 CHECK (poll_interval_minutes BETWEEN 1 AND 1440),
    batch_size                  integer NOT NULL DEFAULT 20 CHECK (batch_size BETWEEN 1 AND 500),
    max_attempts                integer NOT NULL DEFAULT 5 CHECK (max_attempts BETWEEN 1 AND 100),
    max_polls                   integer NOT NULL DEFAULT 500 CHECK (max_polls BETWEEN 1 AND 100000),
    timeout_seconds             integer NOT NULL DEFAULT 60 CHECK (timeout_seconds BETWEEN 1 AND 600),

    row_version                 bigint NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NULL,
    updated_by                  uuid NULL
);

-- Fila única: la configuración es global, no una lista.
CREATE UNIQUE INDEX IF NOT EXISTS uq_quipux_settings_singleton
  ON admin.quipux_settings ((true));

COMMENT ON TABLE  admin.quipux_settings IS 'Configuración de la integración Quipux. Fila única. Editable en caliente: los workers releen en cada ciclo.';
COMMENT ON COLUMN admin.quipux_settings.enabled IS 'Interruptor maestro. false (o sin fila) = integración inerte. No se usa IsDevelopment(): el compose de PDN corre con ASPNETCORE_ENVIRONMENT=Development.';
COMMENT ON COLUMN admin.quipux_settings.password_enc IS '@pii:high — contraseña Quipux cifrada con Data Protection. Nunca en claro ni en logs.';
COMMENT ON COLUMN admin.quipux_settings.aws_secret_access_key_enc IS '@pii:high — secret key AWS cifrada con Data Protection. Nunca en claro ni en logs.';
COMMENT ON COLUMN admin.quipux_settings.bucket IS 'Bucket S3 DE QUIPUX (qxinterconnect), no de FLIT: Quipux lee el PDF de ahí, por eso contenidoDocumento es la key y no el binario.';
COMMENT ON COLUMN admin.quipux_settings.consumer_code IS 'Código de consumidor asignado por Quipux a FLIT (1003). Viaja en el login y en cada payload.';
COMMENT ON COLUMN admin.quipux_settings.officer_document_number IS 'NIT de FLIT como entidad que radica. En 1.0 estaba hardcodeado en el servicio.';
COMMENT ON COLUMN admin.quipux_settings.register_interval_minutes IS 'Cadencia del registrador. Default 15 = la cadencia real de FLIT 1.0 (7 días/semana).';
COMMENT ON COLUMN admin.quipux_settings.max_polls IS 'Tope de consultas por submission: acota el sondeo de un trámite que Quipux nunca resuelve.';

-- ============================================================================
-- tramites.quipux_submissions — radicación por trámite
-- ============================================================================
CREATE TABLE IF NOT EXISTS tramites.quipux_submissions (
    id                    uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id             uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL,

    document_name         varchar(200) NOT NULL,
    attachment_id         uuid NOT NULL,
    quipux_procedure_type   integer NOT NULL,
    quipux_requirement_type integer NOT NULL,
    divipo_code             varchar(20) NOT NULL,

    status                varchar(20) NOT NULL DEFAULT 'pendiente'
                            CHECK (status IN ('pendiente','registrado','aprobado','rechazado','fallido')),
    qx_register_code      integer NULL,
    qx_procedure_code     integer NULL,
    rejection_reason      varchar(1000) NULL,

    attempts              integer NOT NULL DEFAULT 0,
    claimed_at            timestamptz NULL,
    registered_at         timestamptz NULL,
    last_polled_at        timestamptz NULL,
    poll_count            integer NOT NULL DEFAULT 0,
    completed_at          timestamptz NULL,

    row_version           bigint NOT NULL DEFAULT 0,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NULL
);

-- Idempotencia en BD, no en código: a lo sumo UNA radicación viva por trámite. Es lo que impide
-- el defecto de 1.0 donde dos ejecuciones solapadas del cron (sin lock ni marca de tomado)
-- radicaban el mismo trámite dos veces en Quipux con nombres distintos.
CREATE UNIQUE INDEX IF NOT EXISTS uq_quipux_submissions_activa
  ON tramites.quipux_submissions (procedure_instance_id)
  WHERE status IN ('pendiente','registrado');

-- El nombre del documento es la llave de correlación con Quipux: debe ser único de verdad.
CREATE UNIQUE INDEX IF NOT EXISTS uq_quipux_submissions_document_name
  ON tramites.quipux_submissions (document_name);

-- Soporta el claim del registrador: WHERE status='pendiente' AND attempts < max
-- AND (claimed_at IS NULL OR claimed_at < cutoff) ORDER BY created_at.
CREATE INDEX IF NOT EXISTS ix_quipux_submissions_claim_pendiente
  ON tramites.quipux_submissions (created_at)
  WHERE status = 'pendiente';

-- Soporta el claim del consultor: WHERE status='registrado' AND last_polled_at < cutoff.
CREATE INDEX IF NOT EXISTS ix_quipux_submissions_claim_registrada
  ON tramites.quipux_submissions (last_polled_at)
  WHERE status = 'registrado';

CREATE INDEX IF NOT EXISTS ix_quipux_submissions_tenant_status
  ON tramites.quipux_submissions (tenant_id, status);

CREATE INDEX IF NOT EXISTS ix_quipux_submissions_procedure
  ON tramites.quipux_submissions (procedure_instance_id);

COMMENT ON TABLE  tramites.quipux_submissions IS 'Radicación de un trámite en Quipux. Reemplaza al par quipux_integration_log/quipux_integration_states de 1.0, donde el estado vivía implícito en el último log y un fallo entre el cambio de estado y el INSERT dejaba trámites huérfanos.';
COMMENT ON COLUMN tramites.quipux_submissions.document_name IS 'El "documento" que se envía a Quipux. Se calcula UNA vez al encolar y NO se recalcula en reintentos: en 1.0 llevaba HHmm y cambiaba en cada intento, lo que impedía consultar y podía duplicar.';
COMMENT ON COLUMN tramites.quipux_submissions.attachment_id IS 'Adjunto consolidado_maestro a subir al bucket de Quipux (el documentoFlit de 1.0).';
COMMENT ON COLUMN tramites.quipux_submissions.quipux_procedure_type IS 'Código Quipux (16 traspaso bilateral, 213 unilateral, 13 matrícula). Resuelto desde procedure_types.external_refs, nunca hardcodeado.';
COMMENT ON COLUMN tramites.quipux_submissions.attempts IS 'Se incrementa ANTES de procesar: si el proceso muere, el intento igual cuenta. attempts >= max_attempts = dead-letter observable.';
COMMENT ON COLUMN tramites.quipux_submissions.rejection_reason IS 'Descripción del rechazo de Quipux. Equivale a rejectionObservation de 1.0; se copia al reason del historial del trámite.';

ALTER TABLE tramites.quipux_submissions ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.quipux_submissions;
CREATE POLICY tenant_isolation ON tramites.quipux_submissions
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- tramites.quipux_submission_events — bitácora por trámite
-- ============================================================================
-- Trazabilidad "de la ejecución a la finalización" de cada radicación. Modela
-- tramites.identity_validation_audit: vocabulario cerrado de stage/outcome, detail jsonb
-- SANITIZADO (nunca request/response crudos) y escritura con scope propio para que el rastro
-- sobreviva a un rollback del caso de uso.
CREATE TABLE IF NOT EXISTS tramites.quipux_submission_events (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id      uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    submission_id  uuid NOT NULL REFERENCES tramites.quipux_submissions(id) ON DELETE CASCADE ON UPDATE CASCADE,
    stage          varchar(40) NOT NULL
                     CHECK (stage IN ('claimed','consolidado_generado','s3_subido',
                                      'registro_enviado','registro_respuesta','registro_error',
                                      'consulta_enviada','consulta_respuesta','consulta_error',
                                      'aprobado','rechazado','dead_letter',
                                      'reintento_manual','cancelado_manual')),
    outcome        varchar(20) NOT NULL
                     CHECK (outcome IN ('ok','error_transitorio','error_definitivo','omitido')),
    detail         jsonb NULL,
    correlation_id uuid NULL,
    occurred_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_quipux_submission_events_submission
  ON tramites.quipux_submission_events (submission_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_quipux_submission_events_tenant
  ON tramites.quipux_submission_events (tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_quipux_submission_events_correlation
  ON tramites.quipux_submission_events (correlation_id)
  WHERE correlation_id IS NOT NULL;

COMMENT ON COLUMN tramites.quipux_submission_events.detail IS 'jsonb SANITIZADO. Nunca el request/response crudo: 1.0 guardaba json_send/json_response completos, con PII del propietario. Un string plano no es JSON válido (22P02): el escritor lo envuelve como escalar.';

ALTER TABLE tramites.quipux_submission_events ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.quipux_submission_events;
CREATE POLICY tenant_isolation ON tramites.quipux_submission_events
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- ============================================================================
-- admin.quipux_job_runs — bitácora por ejecución del worker
-- ============================================================================
-- Complementa a quipux_submission_events: aquel traza el trámite, este traza el job. Responde
-- "¿corrió el cron?", "¿cuánto tardó?", "¿cuántos falló?", que con las Lambdas de 1.0 no se podía
-- saber. Sin tenant_id (el job es global) ⇒ sin RLS.
CREATE TABLE IF NOT EXISTS admin.quipux_job_runs (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    job_name      varchar(40) NOT NULL CHECK (job_name IN ('quipux_register','quipux_status_poll')),
    started_at    timestamptz NOT NULL DEFAULT now(),
    finished_at   timestamptz NULL,
    claimed_count integer NOT NULL DEFAULT 0,
    ok_count      integer NOT NULL DEFAULT 0,
    error_count   integer NOT NULL DEFAULT 0,
    error_message varchar(1000) NULL
);

CREATE INDEX IF NOT EXISTS ix_quipux_job_runs_job_started
  ON admin.quipux_job_runs (job_name, started_at DESC);

-- Delata ciclos que murieron a medio camino: finished_at NULL y viejo.
CREATE INDEX IF NOT EXISTS ix_quipux_job_runs_en_curso
  ON admin.quipux_job_runs (started_at)
  WHERE finished_at IS NULL;

COMMENT ON COLUMN admin.quipux_job_runs.error_message IS 'Solo el error que aborta el ciclo entero. El fallo de un elemento va al evento de su submission.';
