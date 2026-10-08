-- HU #13440 (Feature #13437, Epica #12750) - Roles por tenant: aislamiento en security.roles.
-- Migracion: 20261008140000_HU13440_RolesPorTenant.
--
-- security.roles es un catalogo GLOBAL desde la HU #10505 (ADR-0023). Para que un Admin de Compania pueda crear
-- roles propios sin que otra compania los vea ni los use, se agrega tenant_id NULLABLE:
--   * tenant_id NULL       = rol global del catalogo FLIT (todos los roles actuales quedan asi: AC1).
--   * tenant_id = <tenant> = rol propio de esa compania (HU #13441).
--
-- Unicidad (AC2):
--   * globales: UNIQUE (code, target_entity_type) entre filas vigentes con tenant_id NULL (igual que antes).
--   * de tenant: UNIQUE (tenant_id, code) entre filas vigentes. Dos companias pueden usar el mismo code.
--   * un tenant NO puede repetir el code de un rol global (cualquier target_entity_type): lo exige el trigger
--     tr_roles_tenant_code_not_global, porque un indice unico no puede comparar filas de dos subconjuntos.
--     Tambien impide crear despues un rol global con el code de un rol de tenant ya existente.
--
-- RLS (AC3/AC4): policy tenant_isolation = globales + propios, y bypass de SuperAdmin (app.is_superadmin).
-- Es DEFENSA EN PROFUNDIDAD NOMINAL: la app conecta como OWNER y ningun DDL usa FORCE ROW LEVEL SECURITY, asi que el
-- owner bypassa la policy (ver DDL 105). El aislamiento efectivo lo da el filtro del repositorio (RoleRepository).
-- security.role_permissions no tiene tenant_id desde la HU #10505; su aislamiento se hereda del rol (el repositorio solo
-- escribe permisos de roles visibles para el caller). Idempotente.

ALTER TABLE security.roles ADD COLUMN IF NOT EXISTS tenant_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_roles_tenants') THEN
        ALTER TABLE security.roles
            ADD CONSTRAINT fk_roles_tenants FOREIGN KEY (tenant_id)
            REFERENCES identity.tenants(id) ON UPDATE CASCADE ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_roles_tenant_id ON security.roles (tenant_id) WHERE tenant_id IS NOT NULL;

-- Unicidad: el indice global pasa a cubrir solo el catalogo (tenant_id NULL) y se agrega el de tenant.
DROP INDEX IF EXISTS security.uq_roles_code_target_entity_type;
CREATE UNIQUE INDEX IF NOT EXISTS uq_roles_code_target_entity_type
    ON security.roles (code, target_entity_type)
    WHERE deleted_at IS NULL AND tenant_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_roles_tenant_code
    ON security.roles (tenant_id, code)
    WHERE deleted_at IS NULL AND tenant_id IS NOT NULL;

CREATE OR REPLACE FUNCTION security.trg_roles_tenant_code_not_global()
RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF NEW.deleted_at IS NOT NULL THEN
        RETURN NEW;
    END IF;

    IF NEW.tenant_id IS NOT NULL THEN
        -- Rol de tenant: su code no puede coincidir con el de un rol global vigente.
        IF EXISTS (SELECT 1 FROM security.roles g
                    WHERE g.tenant_id IS NULL AND g.deleted_at IS NULL AND g.code = NEW.code) THEN
            RAISE EXCEPTION 'ROLE_CODE_DUPLICATE: el code % ya existe como rol global', NEW.code
                USING ERRCODE = '23505';
        END IF;
    ELSE
        -- Rol global nuevo: no puede pisar el code de un rol de tenant vigente.
        IF EXISTS (SELECT 1 FROM security.roles t
                    WHERE t.tenant_id IS NOT NULL AND t.deleted_at IS NULL AND t.code = NEW.code) THEN
            RAISE EXCEPTION 'ROLE_CODE_DUPLICATE: el code % ya existe como rol de una compania', NEW.code
                USING ERRCODE = '23505';
        END IF;
    END IF;
    RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS tr_roles_tenant_code_not_global ON security.roles;
CREATE TRIGGER tr_roles_tenant_code_not_global
    BEFORE INSERT OR UPDATE OF code, tenant_id, deleted_at ON security.roles
    FOR EACH ROW EXECUTE FUNCTION security.trg_roles_tenant_code_not_global();

ALTER TABLE security.roles ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON security.roles;
CREATE POLICY tenant_isolation ON security.roles
    USING (
        tenant_id IS NULL
        OR current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

COMMENT ON COLUMN security.roles.tenant_id IS
    'NULL = rol global del catalogo FLIT; con valor = rol propio de esa compania (HU #13440). FK identity.tenants.';
COMMENT ON INDEX security.uq_roles_code_target_entity_type IS
    'Unicidad del catalogo GLOBAL de roles (tenant_id NULL, solo filas vigentes): (code, target_entity_type).';
COMMENT ON INDEX security.uq_roles_tenant_code IS
    'Unicidad de los roles de una compania (solo vigentes): (tenant_id, code). Dos companias pueden repetir el code.';
