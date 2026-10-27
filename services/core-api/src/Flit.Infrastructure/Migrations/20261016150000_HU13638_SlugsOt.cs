using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13638 (Feature #13100 FA-2, Épica #12753) — crea los 7 módulos RBAC y los 8 slugs <c>ot.*.manage</c> del
/// Anexo A de permisos OT y los concede a <c>ot_admin</c> (y SuperAdmin). DDL: <c>137-HU13638-slugs-ot.sql</c>.
/// Solo datos (sin cambio de modelo EF): lleva los atributos inline y no un Designer.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261016150000_HU13638_SlugsOt")]
public partial class HU13638_SlugsOt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("137-HU13638-slugs-ot.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Borra los grants, permisos y módulos creados por el Up (por code/slug; los grants caen primero por la FK).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions rp
             USING security.permissions p
             WHERE rp.permission_id = p.id
               AND p.slug IN ('ot.tramites.manage', 'ot.improntas.manage', 'ot.reportes.manage', 'ot.mandatos.manage',
                              'ot.mandatarios.manage', 'ot.documentos.manage', 'ot.usuarios.manage', 'ot.integracion.manage');
            DELETE FROM security.permissions
             WHERE slug IN ('ot.tramites.manage', 'ot.improntas.manage', 'ot.reportes.manage', 'ot.mandatos.manage',
                            'ot.mandatarios.manage', 'ot.documentos.manage', 'ot.usuarios.manage', 'ot.integracion.manage');
            DELETE FROM security.modules
             WHERE code IN ('ot-tramites', 'ot-improntas', 'ot-reportes', 'ot-mandatos', 'ot-documentos',
                            'ot-usuarios', 'ot-integracion');
            """);
}
