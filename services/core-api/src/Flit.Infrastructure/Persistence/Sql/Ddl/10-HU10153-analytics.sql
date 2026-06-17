-- HU #10153 — Analytics dashboard | Feature #10139

CREATE TABLE analytics.procedure_metrics_daily (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_metrics_daily PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    metric_date date NOT NULL,
    procedure_category varchar(20) NOT NULL,
    status varchar(20) NOT NULL,
    count integer NOT NULL DEFAULT 0,
    refreshed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_procedure_metrics_daily UNIQUE (tenant_id, metric_date, procedure_category, status)
);
CREATE INDEX ix_procedure_metrics_daily_tenant_id_metric_date
  ON analytics.procedure_metrics_daily(tenant_id, metric_date DESC);

CREATE TABLE analytics.user_productivity_daily (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_productivity_daily PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    metric_date date NOT NULL,
    submitted_count integer NOT NULL DEFAULT 0,
    approved_count integer NOT NULL DEFAULT 0,
    rejected_count integer NOT NULL DEFAULT 0,
    refreshed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_user_productivity_daily UNIQUE (tenant_id, user_id, metric_date)
);
CREATE INDEX ix_user_productivity_daily_tenant_id_metric_date
  ON analytics.user_productivity_daily(tenant_id, metric_date DESC);

ALTER TABLE analytics.procedure_metrics_daily ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON analytics.procedure_metrics_daily
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE analytics.user_productivity_daily ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON analytics.user_productivity_daily
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_procedure_metrics_daily_audit ON analytics.procedure_metrics_daily;
CREATE TRIGGER tr_procedure_metrics_daily_audit AFTER INSERT OR UPDATE OR DELETE ON analytics.procedure_metrics_daily
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_productivity_daily_audit ON analytics.user_productivity_daily;
CREATE TRIGGER tr_user_productivity_daily_audit AFTER INSERT OR UPDATE OR DELETE ON analytics.user_productivity_daily
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

