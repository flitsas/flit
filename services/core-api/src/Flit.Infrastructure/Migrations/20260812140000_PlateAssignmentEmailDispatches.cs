using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11484 (Feature #11482) — esquema de
/// <c>tramites.plate_assignment_email_dispatches</c> (DDL 71). Solo esquema;
/// escritura = HU #11485; consumo = HU #11487.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260812140000_PlateAssignmentEmailDispatches")]
public partial class PlateAssignmentEmailDispatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("71-plate-assignment-email-dispatch.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS tramites.plate_assignment_email_dispatches;
            """);
    }
}
