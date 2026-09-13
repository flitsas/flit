-- =============================================================================
-- Interruptores de la jerarquía de clientes + auditoría del vínculo padre-hija — HU #12323
-- (Feature #12254, Épica #12235). Migración: 20260910130000_HU12323_HierarchySwitchesAndLinkAudit
-- Database Agent.
--
-- ADR-0057 (Propuesto) §Notas: "los interruptores de alcance/herencia van en columnas/artefactos
-- propios, nunca reutilizar is_group_parent". La palanca de emergencia no puede ser el mismo bit que
-- el invariante de profundidad protege (107-HU12318): apagar un interruptor degrada a "la Concesión
-- no ve / no hereda", nunca a fuga ni a pérdida del vínculo. Por eso esta migración NO toca
-- identity.tenants.is_group_parent ni parent_tenant_id (AC2) y solo AGREGA artefactos.
--
-- 1. identity.hierarchy_switches — interruptores GLOBALES de plataforma (sin tenant_id, sin RLS:
--    misma excepción documentada que los catálogos §9.1, checklist A4/A10). Dos filas fijas:
--      group_read_scope        → apagado: toda cabeza de grupo resuelve TenantScope.Single (el
--                                DbTenantScopeResolver deja de ampliar la lectura a los hijos).
--      inherited_configuration → apagado: los hijos dejan de heredar la configuración del padre
--                                (lista de OT del padre acotada a los hijos; consumido por F#12256).
--    Sin despliegue: se conmutan con un UPDATE de is_enabled. PK id uuid (checklist A3) + UNIQUE
--    switch_key + CHECK con las dos claves; row_version y audit_log como toda tabla escribible.
--
-- 2. identity.tenant_hierarchy_audit — bitácora APPEND-ONLY de LINK/UNLINK del vínculo padre-hija.
--    EXCEPCIÓN DOCUMENTADA al checklist A7/A8/A9: parent_tenant_id y child_tenant_id NO llevan FK a
--    identity.tenants. El registro debe sobrevivir a desvínculos (el hijo deja de apuntar al padre) y
--    a cualquier borrado futuro de cualquiera de los dos clientes (AC3); una FK ON DELETE RESTRICT
--    impediría ese borrado y una ON DELETE CASCADE/SET NULL destruiría la traza. Sin soft delete ni
--    row_version (patrón append-only de imprint_signature_validations / notification_delivery_logs);
--    UPDATE y DELETE se rechazan con tr_tenant_hierarchy_audit_immutable. No lleva trg_audit_log:
--    la propia tabla es la auditoría, y el UPDATE sobre identity.tenants ya lo captura
--    tr_tenants_audit (01-HU10146). Sin tenant_id: la fila pertenece a DOS tenants a la vez y la
--    lee el SuperAdmin / la cabeza de grupo; el acceso lo gobierna la aplicación (checklist A4/A10,
--    excepción documentada como en hierarchy_switches).
--
-- 3. tr_tenants_hierarchy_audit (AFTER INSERT OR UPDATE OF parent_tenant_id ON identity.tenants):
--    cuando NEW.parent_tenant_id IS DISTINCT FROM OLD.parent_tenant_id (en INSERT, OLD no existe),
--    escribe UNLINK (parent = OLD.parent_tenant_id, child = NEW.id) si había padre y LINK
--    (parent = NEW.parent_tenant_id, child = NEW.id) si hay padre nuevo. Un cambio de padre produce
--    las dos filas, en ese orden. Corre DESPUÉS de tr_tenants_hierarchy (BEFORE), así que solo se
--    audita lo que la base aceptó. actor_user_id = COALESCE(app.current_user_id de la sesión,
--    NEW.updated_by, NEW.created_by): la sesión manda; si la app no la fija, cae al autor de la fila.
--
-- Aditiva, idempotente y reaplicable. Reversible: ver Down de la migración.
-- =============================================================================

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. identity.hierarchy_switches
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity.hierarchy_switches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_hierarchy_switches PRIMARY KEY (id),
    switch_key text NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL,
    row_version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_hierarchy_switches_switch_key UNIQUE (switch_key),
    CONSTRAINT ck_hierarchy_switches_switch_key
        CHECK (switch_key IN ('group_read_scope', 'inherited_configuration'))
);

DROP TRIGGER IF EXISTS tr_hierarchy_switches_row_version ON identity.hierarchy_switches;
CREATE TRIGGER tr_hierarchy_switches_row_version
    BEFORE UPDATE ON identity.hierarchy_switches
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_hierarchy_switches_audit ON identity.hierarchy_switches;
CREATE TRIGGER tr_hierarchy_switches_audit
    AFTER INSERT OR UPDATE OR DELETE ON identity.hierarchy_switches
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- Seed idempotente: ambos interruptores nacen ENCENDIDOS (el comportamiento diseñado es el
-- normal; apagar es la excepción operativa). Reaplicar no pisa un interruptor apagado a mano.
INSERT INTO identity.hierarchy_switches (switch_key, is_enabled)
VALUES
    ('group_read_scope', true),
    ('inherited_configuration', true)
ON CONFLICT (switch_key) DO NOTHING;

COMMENT ON TABLE identity.hierarchy_switches IS
    'HU #12323 (ADR-0057) — interruptores GLOBALES de la jerarquía de clientes, conmutables sin '
    'despliegue. group_read_scope: apagado ⇒ toda cabeza de grupo resuelve alcance Single (deja de '
    'leer a sus hijos). inherited_configuration: apagado ⇒ los hijos dejan de heredar la '
    'configuración del padre (lista de OT; Feature #12256). Ninguno reutiliza is_group_parent: el '
    'vínculo y el invariante de profundidad siguen intactos con los interruptores apagados. Sin '
    'tenant_id ni RLS por ser configuración de plataforma (excepción documentada checklist A4/A10).';

COMMENT ON COLUMN identity.hierarchy_switches.switch_key IS
    'Clave fija del interruptor: group_read_scope | inherited_configuration (ck_hierarchy_switches_switch_key).';

COMMENT ON COLUMN identity.hierarchy_switches.is_enabled IS
    'true = comportamiento de jerarquía activo. false = degradación segura (Single / sin herencia), nunca fuga.';

COMMENT ON COLUMN identity.hierarchy_switches.updated_by IS
    'Usuario (identity.users.id) que conmutó el interruptor por última vez. NULL = seed.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. identity.tenant_hierarchy_audit (append-only)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS identity.tenant_hierarchy_audit (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_hierarchy_audit PRIMARY KEY (id),
    parent_tenant_id uuid NOT NULL,
    child_tenant_id uuid NOT NULL,
    action text NOT NULL,
    actor_user_id uuid NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_tenant_hierarchy_audit_action CHECK (action IN ('LINK', 'UNLINK')),
    CONSTRAINT ck_tenant_hierarchy_audit_not_self CHECK (parent_tenant_id <> child_tenant_id)
);

CREATE INDEX IF NOT EXISTS ix_tenant_hierarchy_audit_parent_occurred_at
    ON identity.tenant_hierarchy_audit (parent_tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_tenant_hierarchy_audit_child_occurred_at
    ON identity.tenant_hierarchy_audit (child_tenant_id, occurred_at DESC);

-- Append-only forzado por la base: ni la aplicación ni un UPDATE manual reescriben la historia.
CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_audit_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'identity.tenant_hierarchy_audit es append-only: no se permite % (id=%)', TG_OP, OLD.id
        USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_tenant_hierarchy_audit_immutable ON identity.tenant_hierarchy_audit;
CREATE TRIGGER tr_tenant_hierarchy_audit_immutable
    BEFORE UPDATE OR DELETE ON identity.tenant_hierarchy_audit
    FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_audit_immutable();

COMMENT ON TABLE identity.tenant_hierarchy_audit IS
    'HU #12323 (ADR-0057) — bitácora append-only de LINK/UNLINK del vínculo padre-hija de '
    'identity.tenants, escrita solo por tr_tenants_hierarchy_audit. SIN FK a identity.tenants '
    '(excepción documentada checklist A7/A8/A9): la fila debe sobrevivir al desvínculo y a cualquier '
    'borrado futuro de padre o hijo. UPDATE/DELETE rechazados por tr_tenant_hierarchy_audit_immutable. '
    'Sin tenant_id ni RLS: la fila pertenece a dos tenants; el acceso lo gobierna la aplicación.';

COMMENT ON COLUMN identity.tenant_hierarchy_audit.parent_tenant_id IS
    'Cabeza de grupo del vínculo (identity.tenants.id, sin FK a propósito). En UNLINK es el padre que se deja.';

COMMENT ON COLUMN identity.tenant_hierarchy_audit.child_tenant_id IS
    'Cliente hijo del vínculo (identity.tenants.id, sin FK a propósito).';

COMMENT ON COLUMN identity.tenant_hierarchy_audit.action IS
    'LINK = el hijo quedó colgado del padre. UNLINK = el hijo dejó de colgar del padre. Un cambio de padre produce UNLINK + LINK.';

COMMENT ON COLUMN identity.tenant_hierarchy_audit.actor_user_id IS
    'Usuario que ejecutó la operación: app.current_user_id de la sesión, o en su defecto updated_by/created_by de la fila de tenants. NULL = origen sin identidad (script).';

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. tr_tenants_hierarchy_audit sobre identity.tenants
-- ─────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_audit() RETURNS trigger AS $$
DECLARE
    v_old_parent uuid;
    v_actor uuid;
BEGIN
    -- En INSERT no existe OLD: el padre anterior es NULL.
    v_old_parent := CASE WHEN TG_OP = 'UPDATE' THEN OLD.parent_tenant_id ELSE NULL END;

    IF NEW.parent_tenant_id IS NOT DISTINCT FROM v_old_parent THEN
        RETURN NULL; -- sin cambio de vínculo, nada que auditar
    END IF;

    v_actor := COALESCE(
        NULLIF(current_setting('app.current_user_id', true), '')::uuid,
        NEW.updated_by,
        NEW.created_by);

    IF v_old_parent IS NOT NULL THEN
        INSERT INTO identity.tenant_hierarchy_audit (parent_tenant_id, child_tenant_id, action, actor_user_id)
        VALUES (v_old_parent, NEW.id, 'UNLINK', v_actor);
    END IF;

    IF NEW.parent_tenant_id IS NOT NULL THEN
        INSERT INTO identity.tenant_hierarchy_audit (parent_tenant_id, child_tenant_id, action, actor_user_id)
        VALUES (NEW.parent_tenant_id, NEW.id, 'LINK', v_actor);
    END IF;

    RETURN NULL; -- AFTER trigger: el valor de retorno se ignora
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_tenants_hierarchy_audit ON identity.tenants;
CREATE TRIGGER tr_tenants_hierarchy_audit
    AFTER INSERT OR UPDATE OF parent_tenant_id ON identity.tenants
    FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_audit();
