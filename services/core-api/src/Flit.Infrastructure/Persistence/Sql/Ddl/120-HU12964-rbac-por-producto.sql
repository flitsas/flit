-- HU #12964 (Feature #12888, FLIT Suite, tarea B-04) — módulos y roles por producto, y un rol por usuario en
-- cada producto (decisión D1 del inventario B-01, docs/suite/frentes/b-inventario-plataforma-vs-tramites.md).
-- Migración: 20260925181833_HU12964_RbacPorProducto · ADR-0063 · contrato de plataforma v1, §2 y §4. Requiere el DDL 119
-- (platform.products).
--
-- Qué hace:
--   1. security.modules.product_code y security.roles.product_code (FK a platform.products), rellenados por
--      code con la sección 5 del inventario B-01. El seeder solo corre en Development, así que el backfill no
--      puede depender de él. Un código desconocido (creado a mano en RBAC Admin) queda en tramites y se
--      informa con RAISE NOTICE.
--   2. Rol admin_tramites (COMPANY, producto tramites) con los permisos de Trámites que tenía AdminCompany.
--      AdminCompany se queda solo con los de plataforma.
--   3. security.user_role_assignments.product_code, copiado del rol por disparador, y el índice de rol único
--      pasa de (usuario, empresa) a (usuario, empresa, producto).
--   4. A cada AdminCompany activo se le asigna admin_tramites en la misma empresa: nadie pierde acceso. El
--      login suma los permisos de todas las asignaciones activas (AuthUserRepository), así que el conjunto de
--      permisos de esos usuarios queda igual.
--   5. Espejo transitorio (hasta B-12, cuando el hub asigne un rol por producto): asignar AdminCompany crea
--      admin_tramites y quitar AdminCompany lo cierra. Cubre invitación, activación, asignación y seeder.
--   6. Un rol solo puede tener permisos de módulos de su producto (contrato §4). SuperAdmin queda exento:
--      conserva el bypass en todos los productos (contrato §2.1).
--
-- Idempotente y reversible (Down completo en la migración).

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. product_code en módulos y roles
-- ─────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS product_code varchar(40);
ALTER TABLE security.roles   ADD COLUMN IF NOT EXISTS product_code varchar(40);

UPDATE security.modules
   SET product_code = CASE WHEN code IN ('auth', 'usuarios', 'rbac', 'banners') THEN 'plataforma' ELSE 'tramites' END
 WHERE product_code IS NULL;

UPDATE security.roles
   SET product_code = CASE WHEN code IN ('SuperAdmin', 'AdminCompany') THEN 'plataforma' ELSE 'tramites' END
 WHERE product_code IS NULL;

DO $$
DECLARE
    v_unknown text;
BEGIN
    SELECT string_agg(code, ', ' ORDER BY code) INTO v_unknown
      FROM security.modules
     WHERE deleted_at IS NULL
       AND code NOT IN ('auth', 'usuarios', 'rbac', 'banners', 'dashboard', 'tramites', 'reportes',
                        'reportes-detallados', 'validaciones', 'improntas', 'historial-placa', 'logqx',
                        'ict-logs', 'ict-clients', 'generacion-documental', 'confirmacion-runt',
                        'admin-tramites-avanzado');
    IF v_unknown IS NOT NULL THEN
        RAISE NOTICE 'HU #12964: módulos fuera del inventario B-01, quedan en tramites: %', v_unknown;
    END IF;

    SELECT string_agg(code, ', ' ORDER BY code) INTO v_unknown
      FROM security.roles
     WHERE deleted_at IS NULL
       AND code NOT IN ('SuperAdmin', 'AdminCompany', 'admin_tramites', 'ot_admin', 'Radicador');
    IF v_unknown IS NOT NULL THEN
        RAISE NOTICE 'HU #12964: roles fuera del inventario B-01, quedan en tramites: %', v_unknown;
    END IF;
END $$;

ALTER TABLE security.modules ALTER COLUMN product_code SET DEFAULT 'tramites';
ALTER TABLE security.modules ALTER COLUMN product_code SET NOT NULL;
ALTER TABLE security.roles   ALTER COLUMN product_code SET DEFAULT 'tramites';
ALTER TABLE security.roles   ALTER COLUMN product_code SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_modules_products') THEN
        ALTER TABLE security.modules ADD CONSTRAINT fk_modules_products FOREIGN KEY (product_code)
            REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_roles_products') THEN
        ALTER TABLE security.roles ADD CONSTRAINT fk_roles_products FOREIGN KEY (product_code)
            REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_modules_product_code ON security.modules (product_code);
CREATE INDEX IF NOT EXISTS ix_roles_product_code ON security.roles (product_code);

COMMENT ON COLUMN security.modules.product_code IS
    'HU #12964 (ADR-0063) · Producto del módulo: plataforma o tramites. Un rol solo tiene permisos de módulos de su producto (tr_role_permissions_same_product).';
COMMENT ON COLUMN security.roles.product_code IS
    'HU #12964 (ADR-0063, decisión D1) · Producto del rol. Un usuario tiene un rol por producto en cada empresa (uq_ura_active_user_tenant_product).';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. admin_tramites con los permisos de Trámites de AdminCompany
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO security.roles (code, name, description, is_system, is_active, target_entity_type, product_code)
SELECT 'admin_tramites', 'Administrador de Trámites',
       'Administra Trámites en la empresa. Mientras el hub no asigne un rol por producto, acompaña siempre a AdminCompany (HU #12964).',
       true, true, 'COMPANY', 'tramites'
 WHERE NOT EXISTS (
       SELECT 1 FROM security.roles WHERE code = 'admin_tramites' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL);

INSERT INTO security.role_permissions (role_id, permission_id)
SELECT at.id, rp.permission_id
  FROM security.role_permissions rp
  JOIN security.roles ac ON ac.id = rp.role_id AND ac.code = 'AdminCompany' AND ac.target_entity_type = 'COMPANY' AND ac.deleted_at IS NULL
  JOIN security.permissions p ON p.id = rp.permission_id
  JOIN security.modules m ON m.id = p.module_id AND m.product_code = 'tramites'
 CROSS JOIN LATERAL (
       SELECT id FROM security.roles
        WHERE code = 'admin_tramites' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL
        LIMIT 1) at
ON CONFLICT (role_id, permission_id) DO NOTHING;

DELETE FROM security.role_permissions rp
 USING security.roles ac, security.permissions p, security.modules m
 WHERE rp.role_id = ac.id AND ac.code = 'AdminCompany'
   AND p.id = rp.permission_id AND m.id = p.module_id AND m.product_code = 'tramites';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3. Un rol por usuario en cada producto
-- ─────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE security.user_role_assignments ADD COLUMN IF NOT EXISTS product_code varchar(40);

UPDATE security.user_role_assignments a
   SET product_code = r.product_code
  FROM security.roles r
 WHERE r.id = a.role_id AND a.product_code IS DISTINCT FROM r.product_code;

ALTER TABLE security.user_role_assignments ALTER COLUMN product_code SET NOT NULL;

-- La aplicación no escribe la columna: la copia este disparador desde el rol.
CREATE OR REPLACE FUNCTION security.trg_ura_product_code()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    SELECT r.product_code INTO NEW.product_code FROM security.roles r WHERE r.id = NEW.role_id;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_ura_product_code ON security.user_role_assignments;
CREATE TRIGGER tr_ura_product_code
    BEFORE INSERT OR UPDATE OF role_id ON security.user_role_assignments
    FOR EACH ROW EXECUTE FUNCTION security.trg_ura_product_code();

-- 4. AdminCompany activo ⇒ admin_tramites en la misma empresa (antes del índice nuevo no hay choque: un
--    usuario con AdminCompany no tiene otro rol activo en ese tenant, lo garantizaba uq_ura_active_user_tenant).
DROP INDEX IF EXISTS security.uq_ura_active_user_tenant;

INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at, product_code)
SELECT a.tenant_id, a.user_id, at.id, now(), 'tramites'
  FROM security.user_role_assignments a
  JOIN security.roles ac ON ac.id = a.role_id AND ac.code = 'AdminCompany'
 CROSS JOIN LATERAL (
       SELECT id FROM security.roles
        WHERE code = 'admin_tramites' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL
        LIMIT 1) at
 WHERE a.deleted_at IS NULL
   AND NOT EXISTS (
       SELECT 1 FROM security.user_role_assignments x
        WHERE x.user_id = a.user_id AND x.tenant_id = a.tenant_id
          AND x.product_code = 'tramites' AND x.deleted_at IS NULL);

CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant_product
    ON security.user_role_assignments (user_id, tenant_id, product_code)
 WHERE deleted_at IS NULL;

COMMENT ON COLUMN security.user_role_assignments.product_code IS
    'HU #12964 (decisión D1) · Copia del producto del rol (tr_ura_product_code). Un rol activo por usuario, empresa y producto (uq_ura_active_user_tenant_product).';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 5. Espejo AdminCompany ⇒ admin_tramites (transitorio hasta B-12)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION security.trg_ura_mirror_admin_tramites()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_admin_company uuid;
    v_admin_tramites uuid;
    v_was_active boolean;
    v_is_active boolean;
BEGIN
    SELECT id INTO v_admin_company FROM security.roles
     WHERE code = 'AdminCompany' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL LIMIT 1;
    SELECT id INTO v_admin_tramites FROM security.roles
     WHERE code = 'admin_tramites' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL LIMIT 1;
    IF v_admin_company IS NULL OR v_admin_tramites IS NULL THEN
        RETURN NULL;
    END IF;

    v_is_active  := NEW.deleted_at IS NULL AND NEW.role_id = v_admin_company;
    v_was_active := TG_OP = 'UPDATE' AND OLD.deleted_at IS NULL AND OLD.role_id = v_admin_company;

    IF v_is_active AND NOT v_was_active THEN
        -- Se asigna AdminCompany: se agrega admin_tramites si el usuario no tiene ya un rol de Trámites.
        INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at, assigned_by, created_by)
        SELECT NEW.tenant_id, NEW.user_id, v_admin_tramites, now(), NEW.assigned_by, NEW.created_by
         WHERE NOT EXISTS (
               SELECT 1 FROM security.user_role_assignments x
                WHERE x.user_id = NEW.user_id AND x.tenant_id = NEW.tenant_id
                  AND x.product_code = 'tramites' AND x.deleted_at IS NULL);
    ELSIF v_was_active AND NOT v_is_active THEN
        -- Se quita AdminCompany: se cierra el admin_tramites que lo acompañaba.
        UPDATE security.user_role_assignments
           SET deleted_at = COALESCE(NEW.deleted_at, now()), deleted_by = NEW.deleted_by
         WHERE user_id = NEW.user_id AND tenant_id = NEW.tenant_id
           AND role_id = v_admin_tramites AND deleted_at IS NULL;
    END IF;

    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION security.trg_ura_mirror_admin_tramites() IS
    'HU #12964 · Transitorio hasta B-12: admin_tramites acompaña a AdminCompany (se crea al asignarlo y se cierra al quitarlo), para que las pantallas de un solo rol sigan funcionando igual.';

DROP TRIGGER IF EXISTS tr_ura_mirror_admin_tramites ON security.user_role_assignments;
CREATE TRIGGER tr_ura_mirror_admin_tramites
    AFTER INSERT OR UPDATE OF role_id, deleted_at ON security.user_role_assignments
    FOR EACH ROW EXECUTE FUNCTION security.trg_ura_mirror_admin_tramites();

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 6. Un rol solo tiene permisos de módulos de su producto (SuperAdmin exento)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION security.trg_role_permissions_same_product()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_role_code text;
    v_role_product text;
    v_module_product text;
BEGIN
    SELECT r.code, r.product_code INTO v_role_code, v_role_product FROM security.roles r WHERE r.id = NEW.role_id;
    IF v_role_code = 'SuperAdmin' THEN
        RETURN NEW;
    END IF;

    SELECT m.product_code INTO v_module_product
      FROM security.permissions p JOIN security.modules m ON m.id = p.module_id
     WHERE p.id = NEW.permission_id;

    IF v_module_product IS DISTINCT FROM v_role_product THEN
        RAISE EXCEPTION 'El rol % es de % y el permiso es de un módulo de %', v_role_code, v_role_product, v_module_product
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_role_permissions_same_product';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_role_permissions_same_product ON security.role_permissions;
CREATE TRIGGER tr_role_permissions_same_product
    BEFORE INSERT OR UPDATE ON security.role_permissions
    FOR EACH ROW EXECUTE FUNCTION security.trg_role_permissions_same_product();

-- No se siembra security.users.reset_password.all (que AdminResetPasswordHandler consulta): daría a un rol de
-- empresa el reseteo de contraseñas de cualquier empresa si se concediera por error desde RBAC Admin. Hoy
-- solo lo ejerce el SuperAdmin por su bypass de rol; se decide aparte.
