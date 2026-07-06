-- HU #10505 — Roles y Permisos — Catálogo global de roles por tipo de entidad | Feature #10504
-- ADR-0023 (excepción a checklist A4/A20, mismo patrón que ADR-0019: catálogo global sin
-- tenant_id, protegido por RBAC SuperAdmin en vez de RLS por tenant).
--
-- security.roles / security.role_permissions dejan de ser tenant-scoped: se elimina
-- tenant_id, RLS y UNIQUE(tenant_id, code); se agrega target_entity_type (COMPANY |
-- TRANSIT_OFFICE) + is_active y UNIQUE(code, target_entity_type).

-- 1. Consolidación de datos heredados: hoy pueden existir varias filas con el mismo code de
--    rol de sistema (una por tenant, patrón anterior a esta HU). Se fusionan en una sola fila
--    canónica por code con UNIÓN de permisos (nunca reduce acceso) y reasignación de
--    user_role_assignments.role_id de las filas descartadas a la canónica (soft-delete de la
--    sobrante). Corre ANTES de tocar el esquema porque usa tenant_id/RLS todavía vigentes.
DO $$
DECLARE
  grp RECORD;
  canonical_id uuid;
  canonical_tenant_id uuid;
  dup RECORD;
BEGIN
  FOR grp IN
    SELECT code
    FROM security.roles
    WHERE deleted_at IS NULL
    GROUP BY code
    HAVING count(*) > 1
  LOOP
    -- Canónica: preferir la fila con asignaciones activas (no perder el acceso de un usuario
    -- ya asignado); en empate, la más antigua.
    SELECT r.id, r.tenant_id INTO canonical_id, canonical_tenant_id
    FROM security.roles r
    WHERE r.code = grp.code AND r.deleted_at IS NULL
    ORDER BY
      EXISTS (
        SELECT 1 FROM security.user_role_assignments ura
        WHERE ura.role_id = r.id AND ura.deleted_at IS NULL
      ) DESC,
      r.created_at ASC
    LIMIT 1;

    FOR dup IN
      SELECT id FROM security.roles
      WHERE code = grp.code AND deleted_at IS NULL AND id <> canonical_id
    LOOP
      -- Unión de permisos: agrega a la canónica los que aún no tenga (nunca reduce acceso).
      INSERT INTO security.role_permissions (id, tenant_id, role_id, permission_id, created_at)
      SELECT uuidv7(), canonical_tenant_id, canonical_id, rp.permission_id, now()
      FROM security.role_permissions rp
      WHERE rp.role_id = dup.id
        AND NOT EXISTS (
          SELECT 1 FROM security.role_permissions rp2
          WHERE rp2.role_id = canonical_id AND rp2.permission_id = rp.permission_id
        );

      -- Reasigna usuarios de la fila descartada a la canónica (no se pierde ninguna asignación).
      UPDATE security.user_role_assignments
         SET role_id = canonical_id
       WHERE role_id = dup.id;

      -- Soft-delete de la fila sobrante.
      UPDATE security.roles
         SET deleted_at = now()
       WHERE id = dup.id;
    END LOOP;
  END LOOP;
END $$;

-- 2. Elimina RLS/policy/FK/índice/UNIQUE ligados a tenant_id.
DROP POLICY IF EXISTS tenant_isolation ON security.roles;
ALTER TABLE security.roles DISABLE ROW LEVEL SECURITY;
DROP INDEX IF EXISTS security.ix_roles_tenant_id;
ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS uq_roles_tenant_id_code;
ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS roles_tenant_id_fkey;

DROP POLICY IF EXISTS tenant_isolation ON security.role_permissions;
ALTER TABLE security.role_permissions DISABLE ROW LEVEL SECURITY;
DROP INDEX IF EXISTS security.ix_role_permissions_tenant_id;
ALTER TABLE security.role_permissions DROP CONSTRAINT IF EXISTS role_permissions_tenant_id_fkey;

ALTER TABLE security.roles DROP COLUMN IF EXISTS tenant_id;
ALTER TABLE security.role_permissions DROP COLUMN IF EXISTS tenant_id;

-- 3. Columnas nuevas del catálogo global.
ALTER TABLE security.roles
  ADD COLUMN IF NOT EXISTS target_entity_type varchar(20) NOT NULL DEFAULT 'COMPANY';

ALTER TABLE security.roles
  ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

-- 4. Backfill: ot_admin -> TRANSIT_OFFICE; el resto (incluido SuperAdmin, transversal a todos
--    los tenants) -> COMPANY como default -- el enum no tiene un tercer valor
--    "global/transversal" (decisión documentada en ADR-0023; SuperAdmin no se expone en las
--    pantallas de gestión de roles por tipo de entidad).
UPDATE security.roles
   SET target_entity_type = CASE WHEN code = 'ot_admin' THEN 'TRANSIT_OFFICE' ELSE 'COMPANY' END,
       is_active = true;

-- 5. CHECK + UNIQUE(code, target_entity_type) + índice (idempotente).
--
-- La unicidad (code, target_entity_type) se implementa como ÍNDICE ÚNICO PARCIAL
-- (WHERE deleted_at IS NULL), NO como CONSTRAINT UNIQUE plano: la fila fusionada por el paso
-- 1 queda soft-deleted (deleted_at seteado) con el MISMO (code, target_entity_type) que la
-- canónica, y un CONSTRAINT UNIQUE de Postgres valida TODAS las filas físicas sin importar
-- deleted_at -- rompería aquí mismo. Mismo patrón que
-- 20260624193655_Fix_UserRoleAssignment_UniqueConstraint (uq_ura_active_user_tenant) para el
-- mismo problema en user_role_assignments.
ALTER TABLE security.roles
  DROP CONSTRAINT IF EXISTS ck_roles_target_entity_type;
ALTER TABLE security.roles
  ADD CONSTRAINT ck_roles_target_entity_type
  CHECK (target_entity_type IN ('COMPANY', 'TRANSIT_OFFICE'));

ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS uq_roles_code_target_entity_type;
DROP INDEX IF EXISTS security.uq_roles_code_target_entity_type;
CREATE UNIQUE INDEX IF NOT EXISTS uq_roles_code_target_entity_type
  ON security.roles(code, target_entity_type)
  WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_roles_target_entity_type ON security.roles(target_entity_type);

COMMENT ON COLUMN security.roles.target_entity_type IS
  'Catálogo global de roles por tipo de entidad (HU #10505 / ADR-0023): COMPANY | TRANSIT_OFFICE. Sin tenant_id, sin RLS -- protegido por RBAC SuperAdmin.';
COMMENT ON INDEX security.uq_roles_code_target_entity_type IS
  'Unicidad de negocio del catálogo global de roles (solo filas activas): un code no se repite dentro del mismo target_entity_type.';
