using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13660 (Feature #13105 F-E, Épica #12753) — marca enforced los permisos de Reportes detallados y completa los grants de admin_tramites.
/// DDL: <c>141-HU13660-enforced-reportes-detallados.sql</c>. Solo datos, atributos inline.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019110000_HU13660_EnforcedReportesDetallados")]
public partial class HU13660_EnforcedReportesDetallados : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("141-HU13660-enforced-reportes-detallados.sql"));

    /// <inheritdoc />
    /// <remarks>Devuelve el marcador a 'false'; los grants del backfill se conservan (no se sabe cuáles existían antes).</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.permissions SET enforced = 'false'
             WHERE slug IN ('reportes.detallados.read', 'reportes.detallados.export');
            """);
}
