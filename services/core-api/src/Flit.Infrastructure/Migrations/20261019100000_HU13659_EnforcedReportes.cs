using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13659 (Feature #13105 F-E, Épica #12753) — los 8 slugs de Reportes pasan a <c>enforced = 'true'</c>.
/// DDL: <c>140-HU13659-enforced-reportes.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019100000_HU13659_EnforcedReportes")]
public partial class HU13659_EnforcedReportes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("140-HU13659-enforced-reportes.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.permissions SET enforced = 'false'
             WHERE slug IN ('reportes.read', 'reportes.resumen.read', 'reportes.operacion.read', 'reportes.ot.read',
                            'reportes.uso.read', 'reportes.productividad.read', 'reportes.consultas.read',
                            'reportes.programacion.manage');
            """);
}
