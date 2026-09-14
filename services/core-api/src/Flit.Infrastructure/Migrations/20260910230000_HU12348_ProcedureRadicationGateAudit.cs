using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>HU #12348 / #12409 — auditoría de rechazos en creación de trámite.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910230000_HU12348_ProcedureRadicationGateAudit")]
public partial class HU12348_ProcedureRadicationGateAudit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("112-HU12348-procedure-radication-gate-audit.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS tramites.procedure_radication_gate_denials;
            """);
}
