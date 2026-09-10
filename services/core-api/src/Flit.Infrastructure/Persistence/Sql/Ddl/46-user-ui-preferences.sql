-- 46-user-ui-preferences.sql — Preferencias de UI por usuario (base compartida entre criterios)
--
-- Tabla base para que cada usuario elija qué ve en la UI (p. ej. columnas visibles en las tablas
-- de trámites) y esa elección persista entre sesiones. El contrato (nombre de tabla, columnas,
-- unicidad y forma del JSON expuesto por el API) ya fue acordado con los equipos que consumen
-- este endpoint — NO renombrar columnas ni cambiar la unicidad sin coordinar el cambio.
--
-- scope es una lista blanca validada en la capa Application (Flit.Modules.Security.Application),
-- NUNCA en la base de datos: así un scope nuevo solo requiere desplegar código, sin migración.

CREATE TABLE admin.user_ui_preferences (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_ui_preferences PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    scope varchar(60) NOT NULL,
    value jsonb NOT NULL DEFAULT '{}',
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT uq_user_ui_preferences_tenant_user_scope UNIQUE (tenant_id, user_id, scope)
);

-- Índice de acceso (además del que ya cubre la UNIQUE): todas las lecturas del endpoint filtran
-- por tenant_id + user_id y luego por scope puntual, así que este índice cubre también el listado
-- (si en el futuro se agrega un GET de todas las preferencias del usuario).
CREATE INDEX ix_user_ui_preferences_tenant_user ON admin.user_ui_preferences(tenant_id, user_id);

-- RLS por tenant: mismo patrón que el resto de admin.* (aislamiento por app.current_tenant_id,
-- fijado por la app dentro de la transacción — ver TenantRlsScope).
ALTER TABLE admin.user_ui_preferences ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON admin.user_ui_preferences
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Triggers negocio (checklist A16): row_version por UPDATE (concurrencia optimista futura) +
-- bitácora de auditoría genérica (ambas funciones ya existen desde SchemaBootstrap).
DROP TRIGGER IF EXISTS tr_user_ui_preferences_row_version ON admin.user_ui_preferences;
CREATE TRIGGER tr_user_ui_preferences_row_version BEFORE UPDATE ON admin.user_ui_preferences
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_user_ui_preferences_audit ON admin.user_ui_preferences;
CREATE TRIGGER tr_user_ui_preferences_audit AFTER INSERT OR UPDATE OR DELETE ON admin.user_ui_preferences
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
