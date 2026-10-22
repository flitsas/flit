using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13580 (Feature #13579, Épica #13271) — los permisos de Reportes Detallados pasan al módulo
/// <c>reportes</c> y el módulo <c>reportes-detallados</c> se retira con borrado lógico.
/// DDL: <c>135-HU13580-reportes-detallados-al-modulo-reportes.sql</c>.
/// Solo datos (sin cambio de modelo EF), por eso lleva los atributos inline y no un Designer.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261015120000_HU13580_ReportesDetalladosAlModuloReportes")]
public partial class HU13580_ReportesDetalladosAlModuloReportes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("135-HU13580-reportes-detallados-al-modulo-reportes.sql"));

    /// <inheritdoc />
    /// <remarks>Revive el módulo y le devuelve sus dos permisos; los grants no se tocaron en el Up.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.modules
               SET is_active = true, deleted_at = NULL, updated_at = now()
             WHERE code = 'reportes-detallados';

            UPDATE security.permissions p
               SET module_id = m.id, updated_at = now()
              FROM security.modules m
             WHERE m.code = 'reportes-detallados'
               AND p.slug IN ('reportes.detallados.read', 'reportes.detallados.export');
            """);
}
