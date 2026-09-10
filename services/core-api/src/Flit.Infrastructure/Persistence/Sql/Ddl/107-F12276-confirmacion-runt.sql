-- ─────────────────────────────────────────────────────────────────────────────
-- Feature #12276 (Epic #12234) — Confirmación RUNT: consulta periódica al RUNT para
-- confirmar que los trámites aprobados en FLIT quedaron registrados allá.
--
-- Cuatro piezas, todas en el schema tramites:
--   · runt_confirmation_settings  — configuración GLOBAL, fila única (HU #12277)
--   · runt_confirmation_runs      — bitácora por corrida (HU #12309)
--   · runt_confirmation_attempts  — un intento por trámite y corrida (HU #12308/#12309)
--   · procedure_instances.runt_*  — la marca que lee la columna «Confirmado en RUNT» (HU #12312)
--
-- GLOBAL DE PLATAFORMA (settings y runs): sin tenant_id y, en consecuencia, sin RLS — es una
-- consulta aislada al trámite que no hereda el proveedor de la empresa (decisión del PO), igual
-- que admin.quipux_settings / admin.quipux_job_runs. Los intentos SÍ llevan tenant_id y RLS:
-- son datos de un trámite de una compañía.
--
-- La marca vive en procedure_instances y NO en un estado: la corrida jamás cambia el status del
-- trámite (v1 sí lo hacía, y un timeout del proveedor lo dejaba «aprobado en RUNT»).
--
-- IDEMPOTENTE: CREATE ... IF NOT EXISTS, ADD COLUMN IF NOT EXISTS, DROP TRIGGER IF EXISTS antes
-- de CREATE TRIGGER, y el sembrado de la fila única es INSERT ... WHERE NOT EXISTS.
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- tramites.runt_confirmation_settings — configuración global (fila única)
-- ============================================================================
-- Fila única por índice único sobre una constante (uq_*_singleton), el patrón de
-- admin.notification_test_settings y admin.quipux_settings, y NO un CHECK (id = 1) con id entero:
-- public.trg_audit_log() lee NEW.id como uuid y un entero lo reventaría en el primer UPDATE.
CREATE TABLE IF NOT EXISTS tramites.runt_confirmation_settings (
    id                      uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_runt_confirmation_settings PRIMARY KEY (id),

    enabled                 boolean      NOT NULL DEFAULT false,
    run_at_local            varchar(5)   NOT NULL DEFAULT '02:00',
    provider_key            varchar(40)  NOT NULL DEFAULT 'kyverum_runt'
        CONSTRAINT ck_runt_confirmation_settings_provider CHECK (provider_key IN ('kyverum_runt', 'verifik')),
    grace_days              integer      NOT NULL DEFAULT 0
        CONSTRAINT ck_runt_confirmation_settings_grace CHECK (grace_days >= 0),
    discrepancy_after_runs  integer      NOT NULL DEFAULT 3
        CONSTRAINT ck_runt_confirmation_settings_discrepancy CHECK (discrepancy_after_runs >= 1),
    max_attempts            integer      NOT NULL DEFAULT 10
        CONSTRAINT ck_runt_confirmation_settings_max CHECK (max_attempts >= 1 AND max_attempts >= discrepancy_after_runs),

    row_version             bigint       NOT NULL DEFAULT 0,
    updated_at              timestamptz  NULL,
    updated_by              uuid         NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_runt_confirmation_settings_singleton
  ON tramites.runt_confirmation_settings ((true));

DROP TRIGGER IF EXISTS tr_runt_confirmation_settings_row_version ON tramites.runt_confirmation_settings;
CREATE TRIGGER tr_runt_confirmation_settings_row_version
  BEFORE UPDATE ON tramites.runt_confirmation_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- Histórico de la fila por trigger (audit.audit_logs); el detalle campo a campo con actor lo
-- escribe además la aplicación en admin.tenant_config_audit_logs (IAdminAuditWriter).
DROP TRIGGER IF EXISTS tr_runt_confirmation_settings_audit ON tramites.runt_confirmation_settings;
CREATE TRIGGER tr_runt_confirmation_settings_audit
  AFTER INSERT OR UPDATE OR DELETE ON tramites.runt_confirmation_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

INSERT INTO tramites.runt_confirmation_settings (enabled)
SELECT false
WHERE NOT EXISTS (SELECT 1 FROM tramites.runt_confirmation_settings);

COMMENT ON TABLE tramites.runt_confirmation_settings IS
  'Feature #12276 (HU #12277) — configuración GLOBAL de la Confirmación RUNT: interruptor, hora local de la corrida, proveedor, días de gracia, corridas antes de discrepancia y tope de intentos. Fila única (uq_runt_confirmation_settings_singleton). Sin tenant_id ni RLS: consulta aislada al trámite, no hereda el proveedor de la empresa.';
COMMENT ON COLUMN tramites.runt_confirmation_settings.run_at_local IS 'Hora local America/Bogota de la corrida diaria, HH:mm.';
COMMENT ON COLUMN tramites.runt_confirmation_settings.grace_days IS 'Días tras la aprobación antes de la primera consulta. 0 = de inmediato (no se hereda el 7 fijo de v1).';

-- ============================================================================
-- tramites.runt_confirmation_runs — bitácora por corrida
-- ============================================================================
CREATE TABLE IF NOT EXISTS tramites.runt_confirmation_runs (
    id              uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_runt_confirmation_runs PRIMARY KEY (id),

    trigger         varchar(20)  NOT NULL DEFAULT 'scheduled'
        CONSTRAINT ck_runt_confirmation_runs_trigger CHECK (trigger IN ('scheduled', 'manual')),
    started_at      timestamptz  NOT NULL DEFAULT now(),
    finished_at     timestamptz  NULL,
    provider_key    varchar(40)  NULL,
    skipped_reason  varchar(40)  NULL
        CONSTRAINT ck_runt_confirmation_runs_skipped CHECK (skipped_reason IS NULL OR skipped_reason IN ('disabled', 'already_running')),

    consulted       integer      NOT NULL DEFAULT 0,
    confirmed       integer      NOT NULL DEFAULT 0,
    pending         integer      NOT NULL DEFAULT 0,
    discrepancies   integer      NOT NULL DEFAULT 0,
    unverifiable    integer      NOT NULL DEFAULT 0,
    errors          integer      NOT NULL DEFAULT 0,
    provider_calls  integer      NOT NULL DEFAULT 0,
    error_message   varchar(1000) NULL
);

CREATE INDEX IF NOT EXISTS ix_runt_confirmation_runs_started
  ON tramites.runt_confirmation_runs (started_at DESC);

-- Delata corridas que murieron a medias: finished_at NULL y started_at viejo. También sostiene el
-- candado «una sola corrida a la vez».
CREATE INDEX IF NOT EXISTS ix_runt_confirmation_runs_en_curso
  ON tramites.runt_confirmation_runs (started_at)
  WHERE finished_at IS NULL;

COMMENT ON TABLE tramites.runt_confirmation_runs IS
  'Feature #12276 (HU #12309) — una fila por corrida de Confirmación RUNT (programada o manual): contadores por veredicto y llamadas reales al proveedor (un traspaso cuenta dos). Global: sin tenant_id ni RLS.';
COMMENT ON COLUMN tramites.runt_confirmation_runs.error_message IS 'Solo el error que abortó la corrida entera. El de un trámite va en su intento.';

-- ============================================================================
-- tramites.runt_confirmation_attempts — un intento por trámite
-- ============================================================================
CREATE TABLE IF NOT EXISTS tramites.runt_confirmation_attempts (
    id                          uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_runt_confirmation_attempts PRIMARY KEY (id),

    tenant_id                   uuid         NOT NULL
        CONSTRAINT fk_runt_confirmation_attempts_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id       uuid         NOT NULL
        CONSTRAINT fk_runt_confirmation_attempts_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    run_id                      uuid         NULL
        CONSTRAINT fk_runt_confirmation_attempts_run
        REFERENCES tramites.runt_confirmation_runs(id) ON DELETE SET NULL ON UPDATE CASCADE,

    attempt_no                  integer      NOT NULL CHECK (attempt_no >= 1),
    queried_at                  timestamptz  NOT NULL DEFAULT now(),
    provider_key                varchar(40)  NOT NULL,
    query_kind                  varchar(20)  NOT NULL
        CONSTRAINT ck_runt_confirmation_attempts_query_kind CHECK (query_kind IN ('vin', 'plate', 'plate_pair', 'reevaluation')),

    verdict                     varchar(20)  NOT NULL
        CONSTRAINT ck_runt_confirmation_attempts_verdict CHECK (verdict IN ('confirmed', 'pending', 'discrepancy', 'unverifiable', 'error')),
    reason_text                 varchar(2000) NOT NULL,
    rule_version                varchar(40)  NOT NULL,

    -- El crudo NO se copia: se referencia en external_query_payloads (mismo carril que las
    -- certificaciones, HU #11304). Es lo que permite re-evaluar sin volver a pagar la consulta.
    raw_payload_id              uuid         NULL
        CONSTRAINT fk_runt_confirmation_attempts_raw
        REFERENCES tramites.external_query_payloads(id) ON DELETE SET NULL ON UPDATE CASCADE,
    seller_raw_payload_id       uuid         NULL
        CONSTRAINT fk_runt_confirmation_attempts_seller_raw
        REFERENCES tramites.external_query_payloads(id) ON DELETE SET NULL ON UPDATE CASCADE,

    requested_by                uuid         NULL,
    reevaluated_from_attempt_id uuid         NULL
        CONSTRAINT fk_runt_confirmation_attempts_reevaluated_from
        REFERENCES tramites.runt_confirmation_attempts(id) ON DELETE SET NULL ON UPDATE CASCADE,
    flag_applied                varchar(20)  NULL
        CONSTRAINT ck_runt_confirmation_attempts_flag CHECK (flag_applied IS NULL OR flag_applied IN ('discrepancia', 'no_verificable', 'tope')),

    created_at                  timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_runt_confirmation_attempts_instance
  ON tramites.runt_confirmation_attempts (procedure_instance_id, queried_at DESC);

CREATE INDEX IF NOT EXISTS ix_runt_confirmation_attempts_run
  ON tramites.runt_confirmation_attempts (run_id);

CREATE INDEX IF NOT EXISTS ix_runt_confirmation_attempts_queried
  ON tramites.runt_confirmation_attempts (queried_at DESC);

CREATE INDEX IF NOT EXISTS ix_runt_confirmation_attempts_tenant_verdict
  ON tramites.runt_confirmation_attempts (tenant_id, verdict, queried_at DESC);

ALTER TABLE tramites.runt_confirmation_attempts ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.runt_confirmation_attempts;
CREATE POLICY tenant_isolation ON tramites.runt_confirmation_attempts
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada: sin soft-delete ni updated_*. Un intento es evidencia: no se edita ni se
-- «da de baja». Una re-evaluación es OTRA fila que apunta a la original (reevaluated_from_attempt_id).

COMMENT ON TABLE tramites.runt_confirmation_attempts IS
  'Feature #12276 (HU #12308/#12309) — un intento de confirmación de un trámite: proveedor, cómo se consultó, veredicto, motivo legible, versión de la regla y referencia al crudo. Es la fila del Historial interno. RLS por tenant.';
COMMENT ON COLUMN tramites.runt_confirmation_attempts.reason_text IS 'Motivo legible del veredicto: cita número de solicitud, fecha, estado y entidad. Es el log; uso interno, nunca se muestra al cliente.';
COMMENT ON COLUMN tramites.runt_confirmation_attempts.rule_version IS 'Versión de la regla con la que se decidió (RuntConfirmationRules.Version). Permite re-evaluar el crudo cuando la regla cambie.';

-- ============================================================================
-- tramites.procedure_instances — la marca «Confirmado en RUNT»
-- ============================================================================
ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS runt_confirmed_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS runt_attempts     integer     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS runt_flag         varchar(20) NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instances_runt_flag'
    ) THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT ck_procedure_instances_runt_flag
            CHECK (runt_flag IS NULL OR runt_flag IN ('discrepancia', 'no_verificable', 'tope'));
    END IF;
END $$;

-- Universo de la corrida: aprobados sin confirmar. Parcial para que no crezca con el resto.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_runt_universo
  ON tramites.procedure_instances (updated_at)
  WHERE status = 'aprobado' AND runt_confirmed_at IS NULL AND deleted_at IS NULL;

COMMENT ON COLUMN tramites.procedure_instances.runt_confirmed_at IS
  'Feature #12276 — momento en que la Confirmación RUNT dio Confirmado. NULL = no confirmado (la columna del gestor muestra NO si hay intentos, — si nunca se consultó). La corrida nunca toca status.';
COMMENT ON COLUMN tramites.procedure_instances.runt_attempts IS
  'Intentos con veredicto de negocio (Pendiente/Discrepancia). Un error del proveedor NO cuenta.';
COMMENT ON COLUMN tramites.procedure_instances.runt_flag IS
  'Marca interna: discrepancia | no_verificable | tope. Solo la ve el Historial; la columna del gestor solo muestra SÍ/NO/—.';
