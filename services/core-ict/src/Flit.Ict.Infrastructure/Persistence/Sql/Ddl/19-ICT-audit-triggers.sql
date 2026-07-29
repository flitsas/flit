-- =============================================================================
-- core-ict — Triggers de auditoría (plan §A.4 regla 4). Las tablas mutables de negocio
-- registran INSERT/UPDATE/DELETE en audit.audit_logs vía public.trg_audit_log() (definida por
-- core-api, que migra primero). La función NO requiere GUCs (usa NEW.id + to_jsonb), así que no
-- rompe los INSERT del pipeline. Idempotente (DROP TRIGGER IF EXISTS + CREATE).
--
-- Se EXCLUYE ict.integration_clients a propósito: cada login actualiza last_login_at (UPDATE) y
-- to_jsonb(NEW) volcaría el password_hash a audit_logs en cada acceso (volumen + credencial).
-- Los catálogos globales estáticos (allowed_documents, operation_type, …) tampoco se auditan.
-- =============================================================================

DROP TRIGGER IF EXISTS tr_eim_audit ON ict.external_integration_master;
CREATE TRIGGER tr_eim_audit AFTER INSERT OR UPDATE OR DELETE ON ict.external_integration_master
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_eia_audit ON ict.external_integration_actors;
CREATE TRIGGER tr_eia_audit AFTER INSERT OR UPDATE OR DELETE ON ict.external_integration_actors
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_eita_audit ON ict.external_integration_transaction_attachments;
CREATE TRIGGER tr_eita_audit AFTER INSERT OR UPDATE OR DELETE ON ict.external_integration_transaction_attachments
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ptm_audit ON ict.procedure_type_mapping;
CREATE TRIGGER tr_ptm_audit AFTER INSERT OR UPDATE OR DELETE ON ict.procedure_type_mapping
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
