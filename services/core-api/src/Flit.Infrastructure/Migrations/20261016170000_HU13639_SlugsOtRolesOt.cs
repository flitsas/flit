using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13639 (Feature #13100 FA-3, Épica #12753) — concede los slugs OT 1-4 del Anexo A a los roles
/// <c>TRANSIT_OFFICE</c> que no son <c>ot_admin</c>. DDL: <c>138-HU13639-slugs-ot-roles-ot.sql</c>. Solo datos.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261016170000_HU13639_SlugsOtRolesOt")]
public partial class HU13639_SlugsOtRolesOt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("138-HU13639-slugs-ot-roles-ot.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions rp
             USING security.roles r, security.permissions p
             WHERE rp.role_id = r.id AND rp.permission_id = p.id
               AND r.target_entity_type = 'TRANSIT_OFFICE' AND r.code <> 'ot_admin'
               AND p.slug IN ('ot.tramites.manage', 'ot.improntas.manage', 'ot.reportes.manage', 'ot.mandatos.manage');
            """);
}
