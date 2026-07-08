-- ADR-0024 (RNF01) — Endurecimiento de la auditoría mínima de configuración OT.
-- Columnas aditivas nullable a admin.tenant_config_audit_logs: IP, resultado, operación y
-- código de error. Sin backfill (las filas históricas quedan con NULL). Índice adicional
-- para consultas por resultado. Idempotente (IF NOT EXISTS) para re-aplicación segura.

ALTER TABLE admin.tenant_config_audit_logs
    ADD COLUMN IF NOT EXISTS client_ip text,
    ADD COLUMN IF NOT EXISTS result text,
    ADD COLUMN IF NOT EXISTS operation text,
    ADD COLUMN IF NOT EXISTS error_code text;

CREATE INDEX IF NOT EXISTS ix_tenant_config_audit_logs_tenant_id_result_changed_at
    ON admin.tenant_config_audit_logs (tenant_id, result, changed_at DESC);
