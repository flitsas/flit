-- =============================================================================
-- core-ict — Triggers de auditoría (plan §A.4 regla 4). Las tablas mutables de negocio
-- registran INSERT/UPDATE/DELETE en audit.audit_logs vía public.trg_audit_log() (definida por
-- core-api, que migra primero). La función NO requiere GUCs (usa NEW.id + to_jsonb), así que no
-- rompe los INSERT del pipeline. Idempotente (DROP TRIGGER IF EXISTS + CREATE).
--
-- Se EXCLUYE ict.integration_clients a propósito: cada login actualiza last_login_at (UPDATE) y
-- to_jsonb(NEW) volcaría el password_hash a audit_logs en cada acceso (volumen + credencial).
-- Los catálogos globales estáticos (allowed_documents, operation_type, …) tampoco se auditan.
--
-- ── 2026-08-13 · Se EXCLUYEN también external_integration_master y external_integration_actors ──
-- Ver ADR-0001-ict-sin-audit-generico-en-tablas-de-ingesta.md. Son tablas de INGESTA de máquina
-- (staging del pipeline), no ediciones humanas: reciben cientos de filas/s en el registro y decenas
-- de UPDATE por fila en el pipeline (validaciones/estados). El audit genérico por fila
-- (to_jsonb de ~48 columnas + INSERT en audit.audit_logs) medía ~60% del costo del registro
-- (EXPLAIN ANALYZE en DEV: 1557ms de 2607ms por 20k INSERT) y era el cuello del techo de ~25 req/s.
-- Quitarlo da ~2.5× en el registro. Es defendible porque:
--   (1) NO es la auditoría de cumplimiento (RNF01 lo cubre admin.tenant_config_audit_logs a nivel
--       de app: usuario/IP/operación/resultado). Este audit genérico ni siquiera llena changed_by.
--   (2) ICT ya tiene su propia trazabilidad de ciclo de vida: ict.pretramite_events (timeline
--       sanitizado) + ict.external_integration_process_status (historial de estados).
--   (3) Mismo criterio "excluir por costo/volumen" ya aplicado a integration_clients.
-- Se CONSERVAN los triggers de row_version (concurrencia optimista) de ambas tablas.
-- Cambio de POSTURA de auditoría → requiere visto bueno del Líder Técnico/seguridad (ver ADR).
-- El DROP sin CREATE es idempotente y auto-sana: en ambientes que YA tienen el trigger, el
-- bootstrapper lo elimina en el próximo arranque.
-- =============================================================================

-- Tablas de INGESTA de alto volumen: SIN audit genérico (ver cabecera + ADR). Solo DROP (idempotente).
DROP TRIGGER IF EXISTS tr_eim_audit ON ict.external_integration_master;
DROP TRIGGER IF EXISTS tr_eia_audit ON ict.external_integration_actors;

-- Tablas de bajo volumen / configuración: se MANTIENE el audit genérico (costo despreciable).
DROP TRIGGER IF EXISTS tr_eita_audit ON ict.external_integration_transaction_attachments;
CREATE TRIGGER tr_eita_audit AFTER INSERT OR UPDATE OR DELETE ON ict.external_integration_transaction_attachments
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ptm_audit ON ict.procedure_type_mapping;
CREATE TRIGGER tr_ptm_audit AFTER INSERT OR UPDATE OR DELETE ON ict.procedure_type_mapping
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
