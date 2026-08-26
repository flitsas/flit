using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11461 (Feature #11459) — esquema de
/// <c>tramites.procedure_state_change_email_dispatches</c> (DDL 69). Solo esquema;
/// escritura = HU #11465; consumo = HU #11467.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260812120000_ProcedureStateChangeEmailDispatches")]
public partial class ProcedureStateChangeEmailDispatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("69-tramite-state-email-dispatch.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS tramites.procedure_state_change_email_dispatches;
            """);
    }
}
