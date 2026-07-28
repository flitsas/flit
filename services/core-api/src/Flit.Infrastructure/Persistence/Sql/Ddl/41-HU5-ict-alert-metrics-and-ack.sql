-- =============================================================================
-- HU5 / E1 — Observabilidad ICT en el subsistema de alertas de Reportes 2.0.
-- (A) Amplía el vocabulario de analytics.alert_rules.metric con las 4 métricas ICT
--     (evaluadas cross-schema sobre ict.* por AlertMetricsReadRepository).
-- (B) Agrega el reconocimiento (acknowledge) de disparos a analytics.alert_events.
-- Idempotente: DROP CONSTRAINT IF EXISTS + ADD, y ADD COLUMN IF NOT EXISTS.
-- =============================================================================

-- (A) CHECK de metric: los 4 originales + los 4 de ICT. El CHECK inline de la 31 se
--     auto-nombra alert_rules_metric_check; se reemplaza de forma idempotente.
ALTER TABLE analytics.alert_rules DROP CONSTRAINT IF EXISTS alert_rules_metric_check;
ALTER TABLE analytics.alert_rules
    ADD CONSTRAINT alert_rules_metric_check CHECK (metric IN (
        'rejection_rate_pct', 'stuck_count', 'external_api_errors', 'pending_identity_validations',
        'ict_stuck_in_validation', 'ict_novelty_rate_pct', 'ict_webhook_delivery_failures', 'ict_jobs_out_of_sla'
    ));

-- (B) Reconocimiento de disparos (set-once): quién y cuándo marcó el evento como visto.
ALTER TABLE analytics.alert_events
    ADD COLUMN IF NOT EXISTS acknowledged_at timestamptz,
    ADD COLUMN IF NOT EXISTS acknowledged_by uuid;

COMMENT ON COLUMN analytics.alert_events.acknowledged_at IS 'Momento en que un operador reconoció el disparo (null = sin reconocer).';
COMMENT ON COLUMN analytics.alert_events.acknowledged_by IS 'Usuario (claim sub) que reconoció el disparo.';
