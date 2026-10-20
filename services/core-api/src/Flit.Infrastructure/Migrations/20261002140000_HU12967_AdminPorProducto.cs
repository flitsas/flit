using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12967 (Epic #13217, FLIT Suite) — el Admin de Compañía entra a todo producto encendido para su empresa: roles
/// admin_comparendos y admin_diagnostico, asignados a los AdminCompany, y el espejo AdminCompany ⇒ admin de producto
/// para todos los productos. Sin cambio de esquema. DDL: <c>126-HU12967-admin-por-producto.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261002140000_HU12967_AdminPorProducto")]
public partial class HU12967_AdminPorProducto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("126-HU12967-admin-por-producto.sql"));

    /// <inheritdoc />
    /// <remarks>Vuelve al espejo solo de Trámites (DDL 120) y retira los roles nuevos con sus asignaciones.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_ura_mirror_product_admins ON security.user_role_assignments;
            DROP FUNCTION IF EXISTS security.trg_ura_mirror_product_admins();
            CREATE TRIGGER tr_ura_mirror_admin_tramites
                AFTER INSERT OR UPDATE OF role_id, deleted_at ON security.user_role_assignments
                FOR EACH ROW EXECUTE FUNCTION security.trg_ura_mirror_admin_tramites();
            DELETE FROM security.user_role_assignments
             WHERE role_id IN (SELECT id FROM security.roles WHERE code IN ('admin_comparendos', 'admin_diagnostico'));
            DELETE FROM security.roles WHERE code IN ('admin_comparendos', 'admin_diagnostico');
            """);
}
