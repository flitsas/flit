using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14319 (Feature #14316, Épica #14296) — recrea <c>analytics.v_procedure_detail_report</c> con el canal
/// efectivo (<c>COALESCE(channel, 'tramites')</c>), el organismo del canal y su nombre. DDL:
/// <c>174-HU14319-canal-en-reporte-detallado.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105140000_HU14319_CanalEnReporteDetallado")]
public partial class HU14319_CanalEnReporteDetallado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("174-HU14319-canal-en-reporte-detallado.sql"));

    /// <inheritdoc />
    /// <remarks>Vuelve a la definición anterior (DDL 90, idéntica a la que dejó el DDL 132).</remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS analytics.v_procedure_detail_report;");
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("90-bi-view-traspaso-solo-en-familia-traspaso.sql"));
    }
}
