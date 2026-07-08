-- Analytics — Reportes 2.0 (HU-A / HU10621): telemetría de uso del aplicativo.
-- Eventos de uso (module_view, api_module_access, wizard_*) escritos por el writer
-- asíncrono (UsageEventWriterProcessor) y leídos por IUsageMetricsReadRepository.
-- Taxonomía cerrada (contrato docs/contratos-reportes-v2.md §7). Sin PII en metadata
-- (prohibido: nombres, documentos, emails, placas, VIN). Retención: el writer borra
-- eventos con occurred_at < now() - AnalyticsTelemetry:RetentionDays (default 90 días).

CREATE SCHEMA IF NOT EXISTS analytics;

-- ============================================================================
-- app_usage_events
-- ============================================================================
CREATE TABLE IF NOT EXISTS analytics.app_usage_events (
    id                     uuid PRIMARY KEY DEFAULT uuidv7(),
    tenant_id              uuid NOT NULL,
    user_id                uuid NULL,
    event_type             varchar(40)  NOT NULL,
    module                 varchar(40)  NULL,
    step_key               varchar(40)  NULL,
    procedure_instance_id  uuid NULL,
    duration_ms            integer NULL CHECK (duration_ms IS NULL OR duration_ms >= 0),
    metadata               jsonb NOT NULL DEFAULT '{}',
    occurred_at            timestamptz NOT NULL,
    created_at             timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_app_usage_events_tenant_occurred
  ON analytics.app_usage_events (tenant_id, occurred_at);
CREATE INDEX IF NOT EXISTS ix_app_usage_events_tenant_type_occurred
  ON analytics.app_usage_events (tenant_id, event_type, occurred_at);
CREATE INDEX IF NOT EXISTS ix_app_usage_events_instance
  ON analytics.app_usage_events (procedure_instance_id) WHERE procedure_instance_id IS NOT NULL;

COMMENT ON COLUMN analytics.app_usage_events.event_type IS
  'Taxonomía cerrada: module_view | api_module_access | wizard_server_view | wizard_step_view | wizard_step_complete | wizard_step_exit | wizard_abandon | wizard_complete';
COMMENT ON COLUMN analytics.app_usage_events.metadata IS
  'Contexto adicional (jsonb). SIN PII: prohibido nombres, documentos, emails, placas, VIN';

-- ============================================================================
-- RLS — aislamiento por tenant (patrón del repo, ver 22-n03-procedure-state-change-outbox.sql)
-- ============================================================================
ALTER TABLE analytics.app_usage_events ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON analytics.app_usage_events;
CREATE POLICY tenant_isolation ON analytics.app_usage_events
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
