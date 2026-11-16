using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Reportes de la empresa — recrea <c>analytics.v_procedure_detail_report</c> con la columna <c>actors</c> (jsonb):
/// la validación de identidad de cada persona del trámite. DDL: <c>189-reportes-identidad-por-actor.sql</c>.
/// Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261111120000_ReportesIdentidadPorActor")]
public partial class ReportesIdentidadPorActor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("189-reportes-identidad-por-actor.sql"));

    /// <inheritdoc />
    /// <remarks>Vuelve a la definición anterior (DDL 174, canal en el reporte detallado).</remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS analytics.v_procedure_detail_report;");
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("174-HU14319-canal-en-reporte-detallado.sql"));
    }
}
