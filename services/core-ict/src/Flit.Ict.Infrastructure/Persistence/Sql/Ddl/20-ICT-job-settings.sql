-- =============================================================================
-- core-ict — Parámetros de CADENCIA, CONCURRENCIA y TAMAÑO DE LOTE de los jobs, configurables EN BD.
--
-- Hasta ahora los tiempos de los jobs (validación de negocio/externa, orquestador, envío, webhooks)
-- vivían solo en código (IctJobOptions) / appsettings; ajustarlos exigía recompilar o redesplegar.
-- Esta tabla los expone como CONFIGURACIÓN EDITABLE EN CALIENTE: el IctJobSettingsProvider la lee de
-- forma periódica (con throttle) y cada job toma de ahí su PollInterval, su concurrencia y su LIMIT de
-- lote en el siguiente ciclo. Así el operador afina el throughput (p.ej. subir batch/concurrency cuando
-- entran miles de pre-trámites) sin tocar el binario.
--
-- Tabla GLOBAL: sin tenant y SIN RLS (config de sistema, igual criterio que los catálogos). Fila ÚNICA
-- (id = 1, CHECK). Idempotente: el seed inserta la fila con los defaults actuales (paridad IctJobOptions)
-- y NO pisa ediciones manuales posteriores (ON CONFLICT DO NOTHING). Si la fila no existe o la lectura
-- falla, el provider cae a los defaults del código.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.job_settings (
    id                        smallint    NOT NULL DEFAULT 1,
    window_start_hour         smallint    NOT NULL DEFAULT 8,
    window_end_hour           smallint    NOT NULL DEFAULT 20,
    business_poll_seconds     integer     NOT NULL DEFAULT 45,
    external_poll_seconds     integer     NOT NULL DEFAULT 45,
    orchestrator_poll_seconds integer     NOT NULL DEFAULT 20,
    orchestrator_concurrency  integer     NOT NULL DEFAULT 10,
    orchestrator_batch_size   integer     NOT NULL DEFAULT 50,
    send_poll_seconds         integer     NOT NULL DEFAULT 20,
    send_concurrency          integer     NOT NULL DEFAULT 5,
    send_batch_size           integer     NOT NULL DEFAULT 50,
    webhook_poll_seconds      integer     NOT NULL DEFAULT 10,
    webhook_batch_size        integer     NOT NULL DEFAULT 50,
    updated_at                timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ict_job_settings PRIMARY KEY (id),
    CONSTRAINT ck_ict_job_settings_singleton CHECK (id = 1)
);

COMMENT ON TABLE ict.job_settings
    IS '@config Cadencia/concurrencia/tamaño de lote de los jobs ICT (fila única id=1). Editable en caliente; la lee IctJobSettingsProvider.';

-- Seed de la fila única con los defaults actuales. NO pisa cambios manuales (ON CONFLICT DO NOTHING):
-- las columnas toman su DEFAULT, que coincide con IctJobOptions.
INSERT INTO ict.job_settings (id) VALUES (1)
ON CONFLICT (id) DO NOTHING;
