-- Reportes 2.0 — HU-D (HU #10624): informes programados + alertas por umbral.
-- Tablas EXACTAS del §8 de docs/contratos-reportes-v2.md. El scheduler
-- (AnalyticsSchedulerProcessor) evalúa report_schedules/alert_rules cada 60 s en hora
-- America/Bogota, sella last_sent_at/last_triggered_at con FOR UPDATE SKIP LOCKED
-- (multi-réplica) y registra los disparos en alert_events.

CREATE SCHEMA IF NOT EXISTS analytics;

-- ============================================================================
-- report_schedules — informes programados por correo
-- ============================================================================
CREATE TABLE IF NOT EXISTS analytics.report_schedules (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id     uuid NOT NULL,
    name          varchar(120) NOT NULL,
    report_type   varchar(20)  NOT NULL CHECK (report_type IN ('resumen','operacion','ot','uso','productividad')),
    frequency     varchar(10)  NOT NULL CHECK (frequency IN ('daily','weekly','monthly')),
    day_of_week   smallint NULL CHECK (day_of_week BETWEEN 0 AND 6),
    day_of_month  smallint NULL CHECK (day_of_month BETWEEN 1 AND 28),
    send_hour     smallint NOT NULL DEFAULT 7 CHECK (send_hour BETWEEN 0 AND 23),
    format        varchar(10)  NOT NULL CHECK (format IN ('excel','pdf')),
    recipients    jsonb NOT NULL DEFAULT '[]',
    is_active     boolean NOT NULL DEFAULT true,
    last_sent_at  timestamptz NULL,
    created_by    uuid NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NULL,
    deleted_at    timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_report_schedules_tenant_id
  ON analytics.report_schedules (tenant_id);

COMMENT ON COLUMN analytics.report_schedules.send_hour IS 'Hora local America/Bogota (0-23)';
COMMENT ON COLUMN analytics.report_schedules.last_sent_at IS 'Sellado ANTES de enviar (idempotencia por ventana: día / semana ISO / mes)';

-- ============================================================================
-- alert_rules — reglas de alerta por umbral
-- ============================================================================
CREATE TABLE IF NOT EXISTS analytics.alert_rules (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id          uuid NOT NULL,
    name               varchar(120) NOT NULL,
    metric             varchar(40) NOT NULL CHECK (metric IN ('rejection_rate_pct','stuck_count','external_api_errors','pending_identity_validations')),
    operator           varchar(4)  NOT NULL CHECK (operator IN ('gt','gte','lt','lte')),
    threshold          numeric(12,2) NOT NULL,
    window_minutes     integer NOT NULL DEFAULT 1440 CHECK (window_minutes BETWEEN 5 AND 43200),
    cooldown_minutes   integer NOT NULL DEFAULT 240  CHECK (cooldown_minutes BETWEEN 5 AND 10080),
    recipients         jsonb NOT NULL DEFAULT '[]',
    is_active          boolean NOT NULL DEFAULT true,
    last_triggered_at  timestamptz NULL,
    created_by         uuid NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NULL,
    deleted_at         timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_alert_rules_tenant_id
  ON analytics.alert_rules (tenant_id);

COMMENT ON COLUMN analytics.alert_rules.last_triggered_at IS 'Ancla del cooldown: no re-disparar dentro de cooldown_minutes';

-- ============================================================================
-- alert_events — historial de disparos
-- ============================================================================
CREATE TABLE IF NOT EXISTS analytics.alert_events (
    id             uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id      uuid NOT NULL,
    alert_rule_id  uuid NOT NULL REFERENCES analytics.alert_rules(id) ON DELETE CASCADE,
    triggered_at   timestamptz NOT NULL DEFAULT now(),
    metric_value   numeric(12,2) NOT NULL,
    threshold      numeric(12,2) NOT NULL,
    notified       boolean NOT NULL DEFAULT false,
    recipients     jsonb NOT NULL DEFAULT '[]',
    message        text NULL
);

CREATE INDEX IF NOT EXISTS ix_alert_events_tenant_id_triggered_at
  ON analytics.alert_events (tenant_id, triggered_at);

CREATE INDEX IF NOT EXISTS ix_alert_events_alert_rule_id_triggered_at
  ON analytics.alert_events (alert_rule_id, triggered_at);

-- ============================================================================
-- RLS — aislamiento por tenant (patrón §0 del contrato)
-- ============================================================================
ALTER TABLE analytics.report_schedules ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.report_schedules;
CREATE POLICY tenant_isolation ON analytics.report_schedules
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

ALTER TABLE analytics.alert_rules ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.alert_rules;
CREATE POLICY tenant_isolation ON analytics.alert_rules
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

ALTER TABLE analytics.alert_events ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.alert_events;
CREATE POLICY tenant_isolation ON analytics.alert_events
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
