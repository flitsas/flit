-- HU #12967 (Epic #13217, FLIT Suite) — el Admin de Compañía entra a todo producto encendido para su empresa.
-- Migración: 20261002140000_HU12967_AdminPorProducto. Decisión de Samuel Cardenas, 2026-10-02 (opción a).
--
-- Para abrir un producto hacen falta dos cosas (contrato v1 §4): que esté encendido para la empresa y que el usuario
-- tenga un rol en él. Comparendos y Diagnóstico no tenían ningún rol, así que encenderlos no servía: nadie salvo el
-- SuperAdmin los veía. Qué hace:
--   1. Roles de sistema admin_comparendos y admin_diagnostico (COMPANY), iguales a admin_tramites. Sin permisos por
--      ahora: cada producto traerá sus módulos y permisos con su manifiesto.
--   2. A cada AdminCompany activo se le asigna el admin de cada producto que le falte.
--   3. El espejo del DDL 120 (AdminCompany ⇒ admin_tramites) pasa a cubrir todos los productos: asignar AdminCompany
--      crea admin_<producto> para cada producto con rol admin_<producto> de sistema, y quitarlo los cierra. Un producto
--      nuevo solo tiene que sembrar su rol admin_<código> y asignarlo a los AdminCompany existentes.
--
-- El acceso sigue exigiendo que el producto esté encendido (ProductAccessResolver): tener el rol no basta. Idempotente
-- y reversible (Down en la migración).

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. Rol admin de cada producto nuevo
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO security.roles (code, name, description, is_system, is_active, target_entity_type, product_code)
SELECT v.code, v.name, v.description, true, true, 'COMPANY', v.product_code
  FROM (VALUES
        ('admin_comparendos', 'Administrador de Comparendos',
         'Administra Comparendos en la empresa. Acompaña siempre a AdminCompany (HU #12967).', 'comparendos'),
        ('admin_diagnostico', 'Administrador de Diagnóstico',
         'Administra Diagnóstico en la empresa. Acompaña siempre a AdminCompany (HU #12967).', 'diagnostico')
       ) AS v(code, name, description, product_code)
 WHERE NOT EXISTS (
       SELECT 1 FROM security.roles r
        WHERE r.code = v.code AND r.target_entity_type = 'COMPANY' AND r.deleted_at IS NULL);

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. AdminCompany activo ⇒ admin de cada producto (los que falten)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at)
SELECT a.tenant_id, a.user_id, pa.id, now()
  FROM security.user_role_assignments a
  JOIN security.roles ac ON ac.id = a.role_id AND ac.code = 'AdminCompany' AND ac.target_entity_type = 'COMPANY'
  JOIN platform.products p ON p.code <> 'plataforma'
  JOIN security.roles pa ON pa.code = 'admin_' || p.code AND pa.target_entity_type = 'COMPANY'
                         AND pa.is_system AND pa.deleted_at IS NULL
 WHERE a.deleted_at IS NULL
   AND NOT EXISTS (
       SELECT 1 FROM security.user_role_assignments x
        WHERE x.user_id = a.user_id AND x.tenant_id = a.tenant_id
          AND x.product_code = p.code AND x.deleted_at IS NULL);

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3. Espejo AdminCompany ⇒ admin de cada producto (reemplaza tr_ura_mirror_admin_tramites)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION security.trg_ura_mirror_product_admins()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_admin_company uuid;
    v_was_active boolean;
    v_is_active boolean;
BEGIN
    SELECT id INTO v_admin_company FROM security.roles
     WHERE code = 'AdminCompany' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL LIMIT 1;
    IF v_admin_company IS NULL THEN
        RETURN NULL;
    END IF;

    v_is_active  := NEW.deleted_at IS NULL AND NEW.role_id = v_admin_company;
    v_was_active := TG_OP = 'UPDATE' AND OLD.deleted_at IS NULL AND OLD.role_id = v_admin_company;

    IF v_is_active AND NOT v_was_active THEN
        -- Se asigna AdminCompany: el admin de cada producto en el que el usuario todavía no tenga rol.
        INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at, assigned_by, created_by)
        SELECT NEW.tenant_id, NEW.user_id, pa.id, now(), NEW.assigned_by, NEW.created_by
          FROM platform.products p
          JOIN security.roles pa ON pa.code = 'admin_' || p.code AND pa.target_entity_type = 'COMPANY'
                                 AND pa.is_system AND pa.deleted_at IS NULL
         WHERE p.code <> 'plataforma'
           AND NOT EXISTS (
               SELECT 1 FROM security.user_role_assignments x
                WHERE x.user_id = NEW.user_id AND x.tenant_id = NEW.tenant_id
                  AND x.product_code = p.code AND x.deleted_at IS NULL);
    ELSIF v_was_active AND NOT v_is_active THEN
        -- Se quita AdminCompany: se cierran los admin de producto que lo acompañaban.
        UPDATE security.user_role_assignments u
           SET deleted_at = COALESCE(NEW.deleted_at, now()), deleted_by = NEW.deleted_by
          FROM security.roles pa
          JOIN platform.products p ON pa.code = 'admin_' || p.code AND p.code <> 'plataforma'
         WHERE u.role_id = pa.id AND pa.is_system AND pa.target_entity_type = 'COMPANY'
           AND u.user_id = NEW.user_id AND u.tenant_id = NEW.tenant_id AND u.deleted_at IS NULL;
    END IF;

    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION security.trg_ura_mirror_product_admins() IS
    'HU #12967 · El admin de cada producto (admin_<código>, de sistema) acompaña a AdminCompany: se crea al asignarlo y se cierra al quitarlo. Reemplaza al espejo solo de Trámites del DDL 120.';

DROP TRIGGER IF EXISTS tr_ura_mirror_admin_tramites ON security.user_role_assignments;
DROP TRIGGER IF EXISTS tr_ura_mirror_product_admins ON security.user_role_assignments;
CREATE TRIGGER tr_ura_mirror_product_admins
    AFTER INSERT OR UPDATE OF role_id, deleted_at ON security.user_role_assignments
    FOR EACH ROW EXECUTE FUNCTION security.trg_ura_mirror_product_admins();
