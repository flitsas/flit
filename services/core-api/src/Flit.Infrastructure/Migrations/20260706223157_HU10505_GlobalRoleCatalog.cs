using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10505 — Catálogo GLOBAL de roles por tipo de entidad (<c>COMPANY</c> |
    /// <c>TRANSIT_OFFICE</c>). ADR-0023 (excepción a checklist A4/A20, mismo patrón que
    /// ADR-0019): <c>security.roles</c> / <c>security.role_permissions</c> dejan de ser
    /// tenant-scoped — se elimina <c>tenant_id</c>, RLS y <c>UNIQUE(tenant_id, code)</c>; se
    /// agrega <c>target_entity_type</c> + <c>is_active</c> y <c>UNIQUE(code, target_entity_type)</c>.
    /// La protección de escritura pasa de RLS a RBAC (SuperAdmin).
    ///
    /// Up, en orden:
    /// 1. Consolida datos heredados: hoy pueden existir varias filas con el mismo <c>code</c>
    ///    de rol de sistema (una por tenant, patrón anterior a esta HU). Se fusionan en una
    ///    sola fila canónica por <c>code</c> con UNIÓN de permisos (nunca reduce acceso) y
    ///    reasignación de <c>user_role_assignments.role_id</c> de las filas descartadas a la
    ///    canónica (soft-delete de la sobrante). Corre ANTES de tocar el esquema porque usa
    ///    <c>tenant_id</c>/RLS todavía vigentes.
    /// 2. Elimina RLS, policy, FK, índice y UNIQUE ligados a <c>tenant_id</c> (artefactos de
    ///    BD que EF no modela — igual que RLS/triggers en el resto del repo).
    /// 3. EF-generado: DROP COLUMN <c>tenant_id</c> (ambas tablas) + ADD COLUMN
    ///    <c>is_active</c> / <c>target_entity_type</c> (con <c>DEFAULT 'COMPANY'</c>).
    /// 4. Backfill de <c>target_entity_type</c>: <c>ot_admin</c> → <c>TRANSIT_OFFICE</c>; el
    ///    resto (incluido <c>SuperAdmin</c>, transversal a todos los tenants) → <c>COMPANY</c>
    ///    como default, porque el enum no tiene un tercer valor "global/transversal"
    ///    (decisión documentada en ADR-0023; SuperAdmin no se expone en las pantallas de
    ///    gestión de roles por tipo de entidad).
    /// 5. CHECK + UNIQUE(code, target_entity_type) + índice, idempotentes (drop-if-exists + add).
    ///
    /// Down: revierte la estructura (best-effort). La consolidación de datos (fusión de filas
    /// duplicadas de roles de sistema) es IRREVERSIBLE — igual que
    /// 20260630180000_RestrictTenantTypeCatalog, no se reconstruye el estado previo a la
    /// fusión. <c>tenant_id</c> se restaura NULLABLE (no hay un único tenant correcto para una
    /// fila que, tras la fusión, puede corresponder a asignaciones de varios tenants).
    /// </remarks>
    public partial class HU10505_GlobalRoleCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Consolidación de datos (usa tenant_id/RLS vigentes — corre antes del DDL).
            migrationBuilder.Sql(
                """
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
                    -- Canónica: preferir la fila con asignaciones activas (no perder el acceso
                    -- de un usuario ya asignado); en empate, la más antigua.
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
                """);

            // 2. Elimina RLS/policy/FK/índice/UNIQUE ligados a tenant_id (artefactos de BD, no modelados por EF).
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON security.roles;
                ALTER TABLE security.roles DISABLE ROW LEVEL SECURITY;
                DROP INDEX IF EXISTS security.ix_roles_tenant_id;
                ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS uq_roles_tenant_id_code;
                ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS roles_tenant_id_fkey;

                DROP POLICY IF EXISTS tenant_isolation ON security.role_permissions;
                ALTER TABLE security.role_permissions DISABLE ROW LEVEL SECURITY;
                DROP INDEX IF EXISTS security.ix_role_permissions_tenant_id;
                ALTER TABLE security.role_permissions DROP CONSTRAINT IF EXISTS role_permissions_tenant_id_fkey;
                """);

            // 3. EF-generado: DROP COLUMN tenant_id (ambas tablas) + ADD COLUMN is_active/target_entity_type.
            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "security",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "security",
                table: "role_permissions");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "security",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "target_entity_type",
                schema: "security",
                table: "roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "COMPANY");

            // 4 + 5. Backfill + CHECK + UNIQUE(code, target_entity_type) + índice (idempotente).
            //
            // La unicidad (code, target_entity_type) se implementa como ÍNDICE ÚNICO PARCIAL
            // (WHERE deleted_at IS NULL), NO como CONSTRAINT UNIQUE plano: la fila fusionada por
            // el paso 1 queda soft-deleted (deleted_at seteado) con el MISMO (code,
            // target_entity_type) que la canónica, y un CONSTRAINT UNIQUE de Postgres valida
            // TODAS las filas físicas sin importar deleted_at -- rompería aquí mismo. Mismo
            // patrón que 20260624193655_Fix_UserRoleAssignment_UniqueConstraint
            // (uq_ura_active_user_tenant) para el mismo problema en user_role_assignments.
            migrationBuilder.Sql(
                """
                UPDATE security.roles
                   SET target_entity_type = CASE WHEN code = 'ot_admin' THEN 'TRANSIT_OFFICE' ELSE 'COMPANY' END,
                       is_active = true;

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
                  'Catalogo global de roles por tipo de entidad (HU #10505 / ADR-0023): COMPANY | TRANSIT_OFFICE. Sin tenant_id, sin RLS -- protegido por RBAC SuperAdmin.';
                COMMENT ON INDEX security.uq_roles_code_target_entity_type IS
                  'Unicidad de negocio del catalogo global de roles (solo filas activas): un code no se repite dentro del mismo target_entity_type.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS security.uq_roles_code_target_entity_type;
                DROP INDEX IF EXISTS security.ix_roles_target_entity_type;
                ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS ck_roles_target_entity_type;
                """);

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "security",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "target_entity_type",
                schema: "security",
                table: "roles");

            // Restaura tenant_id (best-effort, NULLABLE): la consolidación de datos del Up es
            // irreversible -- no hay un único tenant correcto para una fila fusionada.
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "security",
                table: "roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "security",
                table: "role_permissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_roles_tenant_id ON security.roles(tenant_id);
                ALTER TABLE security.roles
                  ADD CONSTRAINT roles_tenant_id_fkey FOREIGN KEY (tenant_id)
                  REFERENCES identity.tenants(id) ON UPDATE CASCADE ON DELETE RESTRICT;
                ALTER TABLE security.roles ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON security.roles;
                CREATE POLICY tenant_isolation ON security.roles
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                CREATE INDEX IF NOT EXISTS ix_role_permissions_tenant_id ON security.role_permissions(tenant_id);
                ALTER TABLE security.role_permissions
                  ADD CONSTRAINT role_permissions_tenant_id_fkey FOREIGN KEY (tenant_id)
                  REFERENCES identity.tenants(id) ON UPDATE CASCADE ON DELETE RESTRICT;
                ALTER TABLE security.role_permissions ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON security.role_permissions;
                CREATE POLICY tenant_isolation ON security.role_permissions
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);
        }
    }
}
