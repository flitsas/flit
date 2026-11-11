using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14397 (Feature #14396, Épica #14297) — índice parcial del reporte de los trámites del portal. DDL:
/// <c>185-HU14397-indice-reporte-portal.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106130000_HU14397_IndiceReportePortal")]
public partial class HU14397_IndiceReportePortal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("185-HU14397-indice-reporte-portal.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP INDEX IF EXISTS tramites.ix_procedure_instances_portal_channels_created_at;");
}
