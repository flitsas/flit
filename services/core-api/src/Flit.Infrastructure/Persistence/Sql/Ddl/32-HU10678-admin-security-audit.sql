-- HU #10678 — Registro de operaciones administrativas y de seguridad.
-- Generaliza la tabla existente admin.tenant_config_audit_logs (HU #10190/ADR-0024) a rastro
-- único de auditoría: además de cambios de configuración OT, registra CUALQUIER operación
-- administrativa/seguridad (usuarios, roles, permisos, autenticación). Cambios aditivos e
-- idempotentes; sin backfill (las filas históricas quedan con las columnas nuevas en NULL).

-- tenant_id pasa a NULLABLE: eventos de autenticación sin tenant resoluble (p. ej. login
-- fallido con email inexistente) se auditan igual.
ALTER TABLE admin.tenant_config_audit_logs
    ALTER COLUMN tenant_id DROP NOT NULL;

ALTER TABLE admin.tenant_config_audit_logs
    ADD COLUMN IF NOT EXISTS tenant_type varchar(20),
    ADD COLUMN IF NOT EXISTS module varchar(20),
    ADD COLUMN IF NOT EXISTS target_entity_type varchar(40),
    ADD COLUMN IF NOT EXISTS target_entity_id uuid,
    ADD COLUMN IF NOT EXISTS user_agent text;

COMMENT ON COLUMN admin.tenant_config_audit_logs.tenant_type IS
  'Tipo de tenant denormalizado: COMPANY | TRANSIT_OFFICE (HU #10678)';
COMMENT ON COLUMN admin.tenant_config_audit_logs.module IS
  'Categoría transversal: users | roles | permissions | authentication | security | config (HU #10678)';
COMMENT ON COLUMN admin.tenant_config_audit_logs.target_entity_type IS
  'Tipo de la entidad afectada: USER | ROLE | PERMISSION | INVITATION | SESSION | … (HU #10678)';
COMMENT ON COLUMN admin.tenant_config_audit_logs.target_entity_id IS
  'Id de la entidad afectada (usuario/rol/invitación objetivo de la operación) (HU #10678)';

-- ============================================================================
-- Índices para las consultas globales del SuperAdmin (rastro unificado, HU #10678)
-- ============================================================================
CREATE INDEX IF NOT EXISTS ix_audit_logs_changed_at
  ON admin.tenant_config_audit_logs (changed_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_module_changed_at
  ON admin.tenant_config_audit_logs (module, changed_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_target_changed_at
  ON admin.tenant_config_audit_logs (target_entity_id, changed_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_changed_by_changed_at
  ON admin.tenant_config_audit_logs (changed_by, changed_at DESC);

-- ============================================================================
-- RLS — bypass SuperAdmin + filas sin tenant (rastro unificado, HU #10678)
-- ============================================================================
-- La policy original (07-HU10154-admin-tenants.sql / 11-schema-conformance-patch.sql) solo
-- permitía filas del tenant actual. Se amplía para admitir: (a) sesiones marcadas como
-- SuperAdmin (lectura cross-tenant del historial global), (b) filas sin tenant (eventos de
-- autenticación sin tenant resoluble). Sin FOR/WITH CHECK explícito: Postgres reusa el mismo
-- predicado como WITH CHECK, así que también habilita el INSERT de filas tenant_id IS NULL.
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_config_audit_logs;
CREATE POLICY tenant_isolation ON admin.tenant_config_audit_logs
  USING (
    current_setting('app.is_superadmin', true) = 'true'
    OR tenant_id IS NULL
    OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
  );
