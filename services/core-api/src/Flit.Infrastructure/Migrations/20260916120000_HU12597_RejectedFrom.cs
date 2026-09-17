using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12597 (Feature #12595, ADR-0059) — agrega <c>rejected_from</c> a <c>tramites.procedure_instances</c>:
/// el estado desde el que el OT rechazó por última vez, para que el gestor distinga un rechazo desde
/// <c>preasignacion</c>. Los estados <c>preasignacion</c>/<c>asignado</c> no requieren DDL (sin CHECK de
/// enum en <c>status</c>). DDL: <c>115-HU12597-rejected-from.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260916120000_HU12597_RejectedFrom")]
public partial class HU12597_RejectedFrom : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("115-HU12597-rejected-from.sql"));

    /// <inheritdoc />
    /// <remarks>Aditiva y reversible: la columna es nueva y sin backfill, así que borrarla no pierde nada previo.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE tramites.procedure_instances
                DROP COLUMN IF EXISTS rejected_from;
            """);
}
