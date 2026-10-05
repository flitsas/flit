using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12964 (Feature #12888, FLIT Suite, tarea B-04, decisión D1) — módulos y roles por producto, rol
/// <c>admin_tramites</c> con los permisos de Trámites de AdminCompany, un rol por usuario en cada producto
/// (<c>uq_ura_active_user_tenant_product</c>) y el espejo transitorio AdminCompany ⇒ admin_tramites. DDL en
/// <c>120-HU12964-rbac-por-producto.sql</c>, que es la fuente de verdad; esta migración solo lo ejecuta.
/// </summary>
public partial class HU12964_RbacPorProducto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("120-HU12964-rbac-por-producto.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Deja todo como antes de la B-04: AdminCompany recupera los permisos de Trámites, se retiran las
    /// asignaciones y el rol admin_tramites, y vuelve el índice de rol único por (usuario, empresa). Un
    /// usuario al que se le hubiera asignado admin_tramites solo (sin AdminCompany) queda sin rol: el índice
    /// antiguo no admite los dos, y ese estado no existía antes de la B-04.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_role_permissions_same_product ON security.role_permissions;
            DROP FUNCTION IF EXISTS security.trg_role_permissions_same_product();
            DROP TRIGGER IF EXISTS tr_ura_mirror_admin_tramites ON security.user_role_assignments;
            DROP FUNCTION IF EXISTS security.trg_ura_mirror_admin_tramites();

            INSERT INTO security.role_permissions (role_id, permission_id)
            SELECT ac.id, rp.permission_id
              FROM security.role_permissions rp
              JOIN security.roles at ON at.id = rp.role_id AND at.code = 'admin_tramites'
              JOIN security.roles ac ON ac.code = 'AdminCompany' AND ac.target_entity_type = 'COMPANY' AND ac.deleted_at IS NULL
            ON CONFLICT (role_id, permission_id) DO NOTHING;

            DELETE FROM security.user_role_assignments a
             USING security.roles at
             WHERE a.role_id = at.id AND at.code = 'admin_tramites';
            DELETE FROM security.role_permissions rp
             USING security.roles at
             WHERE rp.role_id = at.id AND at.code = 'admin_tramites';
            DELETE FROM security.roles WHERE code = 'admin_tramites';

            DROP INDEX IF EXISTS security.uq_ura_active_user_tenant_product;
            DROP TRIGGER IF EXISTS tr_ura_product_code ON security.user_role_assignments;
            DROP FUNCTION IF EXISTS security.trg_ura_product_code();
            ALTER TABLE security.user_role_assignments DROP COLUMN IF EXISTS product_code;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant
                ON security.user_role_assignments (user_id, tenant_id) WHERE deleted_at IS NULL;

            DROP INDEX IF EXISTS security.ix_roles_product_code;
            DROP INDEX IF EXISTS security.ix_modules_product_code;
            ALTER TABLE security.roles DROP CONSTRAINT IF EXISTS fk_roles_products;
            ALTER TABLE security.modules DROP CONSTRAINT IF EXISTS fk_modules_products;
            ALTER TABLE security.roles DROP COLUMN IF EXISTS product_code;
            ALTER TABLE security.modules DROP COLUMN IF EXISTS product_code;
            """);
}
