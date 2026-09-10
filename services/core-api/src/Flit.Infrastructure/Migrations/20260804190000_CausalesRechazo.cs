using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Causales de rechazo: catálogo global administrado por SuperAdmin + causales marcadas por el
/// organismo en cada evento de rechazo. Habilita el reporte de motivos (A.3 del organismo y B.1 de
/// la empresa), hoy imposible porque el motivo es texto libre.
/// La siembra porta las 18 causales de FLIT 1 (unificando el duplicado «No tiene improntas»).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260804190000_CausalesRechazo")]
public partial class CausalesRechazo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("56-causales-rechazo.sql"));
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("56-causales-rechazo-seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TABLE IF EXISTS tramites.procedure_instance_rejection_reasons;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS catalogs.rejection_reasons;");
    }
}
