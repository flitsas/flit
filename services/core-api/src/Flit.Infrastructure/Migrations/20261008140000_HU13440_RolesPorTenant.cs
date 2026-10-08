using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13440 (Feature #13437, Epica #12750) - aislamiento por tenant en <c>security.roles</c>: <c>tenant_id</c> nullable
/// (NULL = catalogo FLIT), unicidad por tenant, bloqueo de codes que repiten un rol global y policy RLS nominal.
/// DDL: <c>134-HU13440-roles-por-tenant.sql</c>. Idempotente; los roles actuales conservan tenant_id NULL.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261008140000_HU13440_RolesPorTenant")]
public partial class HU13440_RolesPorTenant : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("134-HU13440-roles-por-tenant.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Los roles de tenant (tenant_id no nulo) no caben en el catalogo global: se borran sus asignaciones y permisos
    /// antes de quitar la columna. Es destructivo a proposito; el Down existe para entornos de desarrollo.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM security.user_role_assignments WHERE role_id IN (SELECT id FROM security.roles WHERE tenant_id IS NOT NULL);
            DELETE FROM security.role_permissions WHERE role_id IN (SELECT id FROM security.roles WHERE tenant_id IS NOT NULL);
            DELETE FROM security.roles WHERE tenant_id IS NOT NULL;
            DROP TRIGGER IF EXISTS tr_roles_tenant_code_not_global ON security.roles;
            DROP FUNCTION IF EXISTS security.trg_roles_tenant_code_not_global();
            DROP POLICY IF EXISTS tenant_isolation ON security.roles;
            ALTER TABLE security.roles DISABLE ROW LEVEL SECURITY;
            DROP INDEX IF EXISTS security.uq_roles_tenant_code;
            DROP INDEX IF EXISTS security.uq_roles_code_target_entity_type;
            CREATE UNIQUE INDEX uq_roles_code_target_entity_type ON security.roles (code, target_entity_type) WHERE deleted_at IS NULL;
            DROP INDEX IF EXISTS security.ix_roles_tenant_id;
            ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS fk_roles_tenants;
            ALTER TABLE security.roles DROP COLUMN IF EXISTS tenant_id;
            """);
}
