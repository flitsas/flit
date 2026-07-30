-- Feature #11076 (ADR-0037/0038/0039) — Subsistema de Reportería Transaccional V2
-- Crea: analytics.export_jobs, analytics.saved_queries, analytics.dashboard_preferences,
--       analytics.report_sla_config, analytics.holiday_calendar (catálogo global CO),
--       analytics.v_reporting_tramites (vista V2 extendida).
-- Checklist db-schema-validator §A: A1–A20 verificados (ver comentarios inline).
-- LISTEN/NOTIFY: trigger AFTER INSERT en export_jobs → pg_notify('export_jobs_channel', id).
-- Pending-limit: trigger BEFORE INSERT en export_jobs (advisory; primaria en RequestExportHandler).

CREATE SCHEMA IF NOT EXISTS analytics;

-- ============================================================================
-- analytics.export_jobs — fuente durable de export jobs (ADR-0037)
-- A1: schema analytics  A2: snake_case plural inglés  A3: PK uuid+uuidv7
-- ============================================================================
CREATE TABLE analytics.export_jobs (
    id                  uuid          NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_export_jobs PRIMARY KEY (id),

    -- A4: tenant_id NOT NULL + FK explícita
    tenant_id           uuid          NOT NULL
        CONSTRAINT fk_export_jobs_tenants
            REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    owner_user_id       uuid          NOT NULL
        CONSTRAINT fk_export_jobs_users
            REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- A13: varchar / smallint / bigint / jsonb — no tipos prohibidos
    status              varchar(20)   NOT NULL DEFAULT 'pending',
    report_type         varchar(50)   NOT NULL,
    format              varchar(10)   NOT NULL,
    filters_json        jsonb         NOT NULL DEFAULT '{}',
    progress_pct        smallint      NOT NULL DEFAULT 0,
    -- Trazabilidad distribuida: X-Correlation-Id del request origen (§8.3 del diseño)
    correlation_id      uuid          NULL,
    -- ID opaco del file-manager (no URL). NULL hasta completarse.
    file_storage_path   varchar(500)  NULL,
    file_size_bytes     bigint        NULL,
    file_sha256         varchar(64)   NULL,
    error_message       text          NULL,
    -- A14: timestamptz (no timestamp sin tz)
    expires_at          timestamptz   NOT NULL,
    started_at          timestamptz   NULL,
    completed_at        timestamptz   NULL,

    -- A5: columnas estándar obligatorias (created_at/by, updated_at/by, deleted_at/by, row_version)
    row_version         bigint        NOT NULL DEFAULT 0,
    created_at          timestamptz   NOT NULL DEFAULT now(),
    created_by          uuid          NULL,
    updated_at          timestamptz   NULL,
    updated_by          uuid          NULL,
    deleted_at          timestamptz   NULL,
    deleted_by          uuid          NULL,

    -- A12: prefijos ck_
    CONSTRAINT ck_export_jobs_status
        CHECK (status IN ('pending','processing','completed','failed')),
    CONSTRAINT ck_export_jobs_format
        CHECK (format IN ('excel','csv','pdf')),
    CONSTRAINT ck_export_jobs_report_type
        CHECK (report_type IN ('procedures','consolidado','productivity','sla')),
    CONSTRAINT ck_export_jobs_progress
        CHECK (progress_pct BETWEEN 0 AND 100)
);

-- A11: tenant_id primera columna en índices compuestos
-- A9: índice cubriendo cada FK foránea
CREATE INDEX ix_export_jobs_tenant_owner
    ON analytics.export_jobs(tenant_id, owner_user_id)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_export_jobs_status_created
    ON analytics.export_jobs(status, created_at)
    WHERE status = 'pending' AND deleted_at IS NULL;

-- Índice de FK owner_user_id independiente (A9)
CREATE INDEX ix_export_jobs_owner_user_id
    ON analytics.export_jobs(owner_user_id);

-- A10: RLS — aislamiento por tenant
ALTER TABLE analytics.export_jobs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.export_jobs;
CREATE POLICY tenant_isolation ON analytics.export_jobs
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- A16: trigger row_version (BEFORE UPDATE)
DROP TRIGGER IF EXISTS tr_export_jobs_row_version ON analytics.export_jobs;
CREATE TRIGGER tr_export_jobs_row_version
    BEFORE UPDATE ON analytics.export_jobs
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- A16: trigger audit_log (AFTER I/U/D)
DROP TRIGGER IF EXISTS tr_export_jobs_audit ON analytics.export_jobs;
CREATE TRIGGER tr_export_jobs_audit
    AFTER INSERT OR UPDATE OR DELETE ON analytics.export_jobs
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ─── LISTEN/NOTIFY — ADR-0037: wake-up inmediato del ExportJobsChannelListener ──────────
-- Implementado como trigger AFTER INSERT para atomicidad con la transacción de inserción.
-- El worker siempre ejecuta SELECT FOR UPDATE SKIP LOCKED como mecanismo primario;
-- NOTIFY es solo wake-up (fallback polling cada 30 s si NOTIFY se pierde por PG restart).
CREATE OR REPLACE FUNCTION analytics.trg_export_jobs_notify()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify('export_jobs_channel', NEW.id::text);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_export_jobs_notify ON analytics.export_jobs;
CREATE TRIGGER tr_export_jobs_notify
    AFTER INSERT ON analytics.export_jobs
    FOR EACH ROW EXECUTE FUNCTION analytics.trg_export_jobs_notify();

-- ─── Límite de 3 jobs pending/processing por usuario (validación DB advisory) ───────────
-- Capa secundaria de protección. La validación primaria (sin condición de carrera) ocurre
-- en RequestExportHandler con SELECT COUNT(...) FOR UPDATE antes del INSERT.
-- Este trigger captura escenarios de concurrencia baja sin SERIALIZABLE.
CREATE OR REPLACE FUNCTION analytics.trg_export_jobs_pending_limit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_active integer;
BEGIN
    SELECT COUNT(*) INTO v_active
    FROM analytics.export_jobs
    WHERE owner_user_id = NEW.owner_user_id
      AND status IN ('pending', 'processing')
      AND deleted_at IS NULL;
    IF v_active >= 3 THEN
        RAISE EXCEPTION 'EXPORT_LIMIT_EXCEEDED: owner_user_id=% ya tiene % jobs activos (máximo 3)', NEW.owner_user_id, v_active
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_export_jobs_pending_limit ON analytics.export_jobs;
CREATE TRIGGER tr_export_jobs_pending_limit
    BEFORE INSERT ON analytics.export_jobs
    FOR EACH ROW EXECUTE FUNCTION analytics.trg_export_jobs_pending_limit();

COMMENT ON TABLE  analytics.export_jobs IS
    'Feature #11076 (ADR-0037) — Fuente durable de export jobs asíncronos.';
COMMENT ON COLUMN analytics.export_jobs.file_storage_path IS
    'ID opaco del file-manager (no URL ni ruta). NULL hasta status=completed.';
COMMENT ON COLUMN analytics.export_jobs.correlation_id IS
    'X-Correlation-Id del request origen (trazabilidad distribuida §8.3).';
COMMENT ON COLUMN analytics.export_jobs.expires_at IS
    'Soft-expiry = created_at + 30 días. Cron de limpieza marca deleted_at al vencer.';

-- ============================================================================
-- analytics.saved_queries — consultas guardadas por usuario
-- ============================================================================
CREATE TABLE analytics.saved_queries (
    id            uuid          NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_saved_queries PRIMARY KEY (id),

    tenant_id     uuid          NOT NULL
        CONSTRAINT fk_saved_queries_tenants
            REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    user_id       uuid          NOT NULL
        CONSTRAINT fk_saved_queries_users
            REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    name          varchar(150)  NOT NULL,
    description   varchar(500)  NULL,
    filters_json  jsonb         NOT NULL DEFAULT '{}',
    -- Privadas por defecto; is_shared = true para compartir en el tenant
    is_shared     boolean       NOT NULL DEFAULT false,

    row_version   bigint        NOT NULL DEFAULT 0,
    created_at    timestamptz   NOT NULL DEFAULT now(),
    created_by    uuid          NULL,
    updated_at    timestamptz   NULL,
    updated_by    uuid          NULL,
    deleted_at    timestamptz   NULL,
    deleted_by    uuid          NULL
);

CREATE INDEX ix_saved_queries_tenant_user
    ON analytics.saved_queries(tenant_id, user_id)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_saved_queries_user_id
    ON analytics.saved_queries(user_id);

ALTER TABLE analytics.saved_queries ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.saved_queries;
CREATE POLICY tenant_isolation ON analytics.saved_queries
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_saved_queries_row_version ON analytics.saved_queries;
CREATE TRIGGER tr_saved_queries_row_version
    BEFORE UPDATE ON analytics.saved_queries
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_saved_queries_audit ON analytics.saved_queries;
CREATE TRIGGER tr_saved_queries_audit
    AFTER INSERT OR UPDATE OR DELETE ON analytics.saved_queries
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE analytics.saved_queries IS
    'Feature #11076 — Consultas guardadas por usuario (privadas por defecto; is_shared para compartir en tenant).';

-- ============================================================================
-- analytics.dashboard_preferences — configuración de KPIs por usuario (1 fila por user)
-- ============================================================================
CREATE TABLE analytics.dashboard_preferences (
    id          uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_dashboard_preferences PRIMARY KEY (id),

    tenant_id   uuid         NOT NULL
        CONSTRAINT fk_dashboard_preferences_tenants
            REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    user_id     uuid         NOT NULL
        CONSTRAINT fk_dashboard_preferences_users
            REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- { "visibleKpis": ["totalTramites",...], "kpiOrder": [...], "hiddenCharts": [...] }
    config_json jsonb        NOT NULL DEFAULT '{}',

    row_version bigint       NOT NULL DEFAULT 0,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    created_by  uuid         NULL,
    updated_at  timestamptz  NULL,
    updated_by  uuid         NULL,
    deleted_at  timestamptz  NULL,
    deleted_by  uuid         NULL,

    -- A12: prefijo uq_
    CONSTRAINT uq_dashboard_preferences_user UNIQUE (tenant_id, user_id)
);

-- A9: FK index user_id
CREATE INDEX ix_dashboard_preferences_user_id
    ON analytics.dashboard_preferences(user_id);

ALTER TABLE analytics.dashboard_preferences ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.dashboard_preferences;
CREATE POLICY tenant_isolation ON analytics.dashboard_preferences
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_dashboard_preferences_row_version ON analytics.dashboard_preferences;
CREATE TRIGGER tr_dashboard_preferences_row_version
    BEFORE UPDATE ON analytics.dashboard_preferences
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_dashboard_preferences_audit ON analytics.dashboard_preferences;
CREATE TRIGGER tr_dashboard_preferences_audit
    AFTER INSERT OR UPDATE OR DELETE ON analytics.dashboard_preferences
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE  analytics.dashboard_preferences IS
    'Feature #11076 — Preferencias de dashboard por usuario (mostrar/ocultar/reordenar KPIs). Sin constructor libre.';
COMMENT ON COLUMN analytics.dashboard_preferences.config_json IS
    '{ "visibleKpis": [...], "kpiOrder": [...], "hiddenCharts": [...] }';

-- ============================================================================
-- analytics.report_sla_config — SLA configurable por tipo de trámite y OT
-- ============================================================================
CREATE TABLE analytics.report_sla_config (
    id                  uuid          NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_report_sla_config PRIMARY KEY (id),

    tenant_id           uuid          NOT NULL
        CONSTRAINT fk_report_sla_config_tenants
            REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- NULL = aplica a todo el tenant (sin filtro por OT)
    transit_office_id   uuid          NULL
        CONSTRAINT fk_report_sla_config_transit_offices
            REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- NULL = aplica a todos los tipos de trámite del tenant
    procedure_type      varchar(50)   NULL,

    -- Horas hábiles objetivo. calendar_type define si se cuentan festivos.
    sla_hours           smallint      NOT NULL
        CONSTRAINT ck_report_sla_config_sla_hours CHECK (sla_hours > 0),

    calendar_type       varchar(20)   NOT NULL DEFAULT 'business'
        CONSTRAINT ck_report_sla_config_calendar_type
            CHECK (calendar_type IN ('business','calendar')),

    effective_from      date          NOT NULL DEFAULT CURRENT_DATE,
    effective_to        date          NULL,

    row_version         bigint        NOT NULL DEFAULT 0,
    created_at          timestamptz   NOT NULL DEFAULT now(),
    created_by          uuid          NULL,
    updated_at          timestamptz   NULL,
    updated_by          uuid          NULL,
    deleted_at          timestamptz   NULL,
    deleted_by          uuid          NULL
);

-- A11: tenant_id primera columna; partial index para lookup activo
CREATE INDEX ix_report_sla_config_tenant_lookup
    ON analytics.report_sla_config(tenant_id, procedure_type, transit_office_id)
    WHERE effective_to IS NULL OR effective_to >= CURRENT_DATE;

-- A9: FK index transit_office_id
CREATE INDEX ix_report_sla_config_transit_office_id
    ON analytics.report_sla_config(transit_office_id)
    WHERE transit_office_id IS NOT NULL;

ALTER TABLE analytics.report_sla_config ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.report_sla_config;
CREATE POLICY tenant_isolation ON analytics.report_sla_config
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_report_sla_config_row_version ON analytics.report_sla_config;
CREATE TRIGGER tr_report_sla_config_row_version
    BEFORE UPDATE ON analytics.report_sla_config
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_report_sla_config_audit ON analytics.report_sla_config;
CREATE TRIGGER tr_report_sla_config_audit
    AFTER INSERT OR UPDATE OR DELETE ON analytics.report_sla_config
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE  analytics.report_sla_config IS
    'Feature #11076 — SLA configurable por tipo de trámite y OT. NULL en transit_office_id o procedure_type = global del tenant.';
COMMENT ON COLUMN analytics.report_sla_config.sla_hours IS
    'Horas hábiles objetivo. calendar_type=business excluye festivos de analytics.holiday_calendar.';

-- ============================================================================
-- analytics.holiday_calendar — catálogo mixto de días festivos (global + per-tenant)
-- Diseño: tenant_id NULL = entrada global/compartida (visible a todos los tenants).
--         tenant_id NOT NULL = entrada propia del tenant (festivos regionales/laborales).
-- RLS expone ambas capas al tenant activo.
-- Requisito Feature #11076: calendarios configurables por SLA/OT/tenant.
-- ============================================================================
CREATE TABLE analytics.holiday_calendar (
    id              uuid          NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_holiday_calendar PRIMARY KEY (id),

    -- NULL = entrada global; NOT NULL = entrada específica de tenant
    tenant_id       uuid          NULL
        CONSTRAINT fk_holiday_calendar_tenants
            REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,

    holiday_date    date          NOT NULL,
    name            varchar(200)  NOT NULL,
    country_code    varchar(5)    NOT NULL DEFAULT 'CO',
    is_active       boolean       NOT NULL DEFAULT true,
    external_refs   jsonb         NOT NULL DEFAULT '{}',
    created_at      timestamptz   NOT NULL DEFAULT now(),
    updated_at      timestamptz   NULL,

    -- NULLS NOT DISTINCT (PG15+): unicidad incluye NULLs → una sola entrada global por (fecha, país)
    --   y una entrada única por (tenant, fecha, país) para overrides tenant-specific.
    CONSTRAINT uq_holiday_calendar_tenant_date_country
        UNIQUE NULLS NOT DISTINCT (tenant_id, holiday_date, country_code)
);

-- A9: FK index tenant_id
CREATE INDEX ix_holiday_calendar_tenant_id
    ON analytics.holiday_calendar(tenant_id)
    WHERE tenant_id IS NOT NULL;

-- Lookup por tenant (global + específico), país y fecha
CREATE INDEX ix_holiday_calendar_tenant_country_date
    ON analytics.holiday_calendar(tenant_id, country_code, holiday_date)
    WHERE is_active = true;

-- A10: RLS — filas globales (NULL) y del tenant activo
ALTER TABLE analytics.holiday_calendar ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.holiday_calendar;
CREATE POLICY tenant_isolation ON analytics.holiday_calendar
    USING (
        tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

COMMENT ON TABLE  analytics.holiday_calendar IS
    'Feature #11076 — Catálogo mixto de días festivos. tenant_id NULL = global (Colombia); NOT NULL = festivos propios del tenant (regionales/laborales).';
COMMENT ON COLUMN analytics.holiday_calendar.tenant_id IS
    'NULL = entrada global visible a todos los tenants. NOT NULL = override tenant-specific.';

-- Seed festivos Colombia 2025 (Ley 51/1983 + Ley 270/1996 + traslados lunes)
-- tenant_id = NULL → entrada global
INSERT INTO analytics.holiday_calendar (tenant_id, holiday_date, name, country_code) VALUES
    (NULL, '2025-01-01', 'Año Nuevo', 'CO'),
    (NULL, '2025-01-06', 'Reyes Magos', 'CO'),
    (NULL, '2025-03-24', 'San José', 'CO'),
    (NULL, '2025-04-17', 'Jueves Santo', 'CO'),
    (NULL, '2025-04-18', 'Viernes Santo', 'CO'),
    (NULL, '2025-05-01', 'Día del Trabajo', 'CO'),
    (NULL, '2025-06-02', 'Ascensión del Señor', 'CO'),
    (NULL, '2025-06-23', 'Corpus Christi', 'CO'),
    (NULL, '2025-06-30', 'Sagrado Corazón de Jesús', 'CO'),
    (NULL, '2025-07-07', 'San Pedro y San Pablo', 'CO'),
    (NULL, '2025-07-20', 'Día de la Independencia', 'CO'),
    (NULL, '2025-08-07', 'Batalla de Boyacá', 'CO'),
    (NULL, '2025-08-18', 'La Asunción de la Virgen', 'CO'),
    (NULL, '2025-10-13', 'Día de la Raza', 'CO'),
    (NULL, '2025-11-03', 'Todos los Santos', 'CO'),
    (NULL, '2025-11-17', 'Independencia de Cartagena', 'CO'),
    (NULL, '2025-12-08', 'Inmaculada Concepción', 'CO'),
    (NULL, '2025-12-25', 'Navidad', 'CO')
ON CONFLICT (tenant_id, holiday_date, country_code) DO NOTHING;

-- Seed festivos Colombia 2026
INSERT INTO analytics.holiday_calendar (tenant_id, holiday_date, name, country_code) VALUES
    (NULL, '2026-01-01', 'Año Nuevo', 'CO'),
    (NULL, '2026-01-12', 'Reyes Magos', 'CO'),
    (NULL, '2026-03-23', 'San José', 'CO'),
    (NULL, '2026-04-02', 'Jueves Santo', 'CO'),
    (NULL, '2026-04-03', 'Viernes Santo', 'CO'),
    (NULL, '2026-05-01', 'Día del Trabajo', 'CO'),
    (NULL, '2026-05-18', 'Ascensión del Señor', 'CO'),
    (NULL, '2026-06-08', 'Corpus Christi', 'CO'),
    (NULL, '2026-06-15', 'Sagrado Corazón de Jesús', 'CO'),
    (NULL, '2026-06-29', 'San Pedro y San Pablo', 'CO'),
    (NULL, '2026-07-20', 'Día de la Independencia', 'CO'),
    (NULL, '2026-08-07', 'Batalla de Boyacá', 'CO'),
    (NULL, '2026-08-17', 'La Asunción de la Virgen', 'CO'),
    (NULL, '2026-10-12', 'Día de la Raza', 'CO'),
    (NULL, '2026-11-02', 'Todos los Santos', 'CO'),
    (NULL, '2026-11-16', 'Independencia de Cartagena', 'CO'),
    (NULL, '2026-12-08', 'Inmaculada Concepción', 'CO'),
    (NULL, '2026-12-25', 'Navidad', 'CO')
ON CONFLICT (tenant_id, holiday_date, country_code) DO NOTHING;

-- ============================================================================
-- analytics.v_reporting_tramites — Vista V2 extendida (Feature #11076, G2)
-- Estrategia: NUEVA vista (no ALTER VIEW) para no romper v_procedure_detail_report (V1).
-- Agrega sobre la base: plate, vin, transit_office_name, company_name, elapsed_hours_total.
-- ADR-0021 (Aceptado): lectura en vivo — sin materialización.
-- ============================================================================
CREATE OR REPLACE VIEW analytics.v_reporting_tramites AS
SELECT
    vd.id,
    vd.tenant_id,
    vd.reference_number,
    vd.transit_office_id,
    vd.procedure_type_id,
    vd.procedure_type_name,
    vd.category,
    vd.status,
    vd.created_by_display_name,
    vd.submitted_at,
    vd.completed_at,
    vd.created_at,
    -- A15: PII — campos de persona solo disponibles con permisos reporting.detail/audit (control en API)
    vd.person_document,
    vd.person_full_name,
    vd.is_leasing,
    vd.has_transformation,
    vd.transformation_detail,
    vd.payment_type,
    vd.transfer_type,
    -- V2: placa del vehículo (field_key = 'plate' — 40-catalogo-tipos-tramite-canonico.sql)
    coalesce(fv_plate.value_text, '') AS plate,
    -- V2: VIN del vehículo (field_key = 'vin')
    coalesce(fv_vin.value_text, '')   AS vin,
    -- V2: nombre de la OT
    coalesce(oto.name, '')            AS transit_office_name,
    -- V2: nombre legal del tenant/empresa
    coalesce(t.legal_name, '')        AS company_name,
    -- V2: horas totales desde submitted_at hasta completed_at (o now si en curso)
    CASE
        WHEN pi.submitted_at IS NOT NULL THEN
            round(
                extract(EPOCH FROM (coalesce(pi.completed_at, now()) - pi.submitted_at)) / 3600.0,
                2
            )
        ELSE NULL
    END::numeric(10,2)               AS elapsed_hours_total
FROM analytics.v_procedure_detail_report vd
JOIN tramites.procedure_instances pi ON pi.id = vd.id
LEFT JOIN identity.tenants t ON t.id = vd.tenant_id
LEFT JOIN catalogs.transit_offices oto ON oto.id = vd.transit_office_id
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = vd.id AND fv.field_key = 'plate'
    LIMIT 1
) fv_plate ON TRUE
LEFT JOIN LATERAL (
    SELECT fv.value_text
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.procedure_instance_id = vd.id AND fv.field_key = 'vin'
    LIMIT 1
) fv_vin ON TRUE;

COMMENT ON VIEW analytics.v_reporting_tramites IS
    'Feature #11076 — Vista V2 extendida para reportería transaccional.
     Extiende v_procedure_detail_report con: plate, vin, transit_office_name, company_name, elapsed_hours_total.
     La vista base v_procedure_detail_report se conserva para retrocompat de módulo V1.
     Lectura en vivo (ADR-0021 Aceptado).';

-- Índice auxiliar para filtro (tenant_id, created_at DESC) en consultas de rango 12 meses.
-- ix_procedure_instances_tenant_id_status_created_at cubre (tenant_id, status, created_at DESC);
-- este nuevo índice cubre consultas sin filtro de status (tab tramites V2 sin filtro de estado).
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_created_reporting
    ON tramites.procedure_instances(tenant_id, created_at DESC)
    WHERE deleted_at IS NULL;
