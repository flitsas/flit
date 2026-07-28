-- =============================================================================
-- core-ict — Auditoría de ejecuciones de jobs (HU5 / E1). Alimenta la métrica de
-- alerta ict_jobs_out_of_sla (submódulo ICT y analytics.alert_rules cross-schema).
-- Tabla GLOBAL de plataforma: los 5 BackgroundServices son cross-tenant, así que el
-- registro es operativo (no de negocio) y NO lleva RLS (como los catálogos globales).
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.job_runs (
    id             uuid NOT NULL DEFAULT uuidv7(),
    job_name       varchar(60) NOT NULL,
    started_at     timestamptz NOT NULL DEFAULT now(),
    finished_at    timestamptz,
    duration_ms    integer NOT NULL DEFAULT 0,
    outcome        varchar(20) NOT NULL DEFAULT 'ok',   -- ok|error
    breached_sla   boolean NOT NULL DEFAULT false,       -- ciclo con error o duración > su PollInterval
    error_message  text,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_job_runs PRIMARY KEY (id),
    CONSTRAINT ck_job_runs_outcome CHECK (outcome IN ('ok', 'error'))
);

CREATE INDEX IF NOT EXISTS ix_job_runs_name_date ON ict.job_runs (job_name, started_at DESC);
-- Índice parcial: la métrica de SLA solo cuenta corridas incumplidas dentro de la ventana.
CREATE INDEX IF NOT EXISTS ix_job_runs_breached_date ON ict.job_runs (started_at DESC) WHERE breached_sla;
