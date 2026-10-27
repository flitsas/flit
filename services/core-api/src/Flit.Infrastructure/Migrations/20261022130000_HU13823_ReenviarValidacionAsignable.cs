using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13823 (Feature #13819, Épica #13817) — <c>AdminTramiteReenviarValidacion</c> en los roles administradores
/// de producto Trámites (<c>admin_tramites</c>, <c>ot_admin</c>) para que deje de ser «Solo SA» y se pueda asignar.
/// DDL: <c>150-HU13823-reenviar-validacion-asignable.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261022130000_HU13823_ReenviarValidacionAsignable")]
public partial class HU13823_ReenviarValidacionAsignable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("150-HU13823-reenviar-validacion-asignable.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions rp
             USING security.roles r, security.permissions p
             WHERE rp.role_id = r.id
               AND rp.permission_id = p.id
               AND p.slug = 'AdminTramiteReenviarValidacion'
               AND r.code IN ('admin_tramites', 'ot_admin')
               AND r.tenant_id IS NULL;
            """);
}
