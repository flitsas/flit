using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13076 (Feature #13066, Épica #12737) — índices del recorrido del feed por (transacción, versión)
/// y del arranque por fecha. DDL: <c>126-HU13076-sync-cursor.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260929210000_HU13076_SyncCursor")]
public partial class HU13076_SyncCursor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("126-HU13076-sync-cursor.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS tramites.ix_procedure_instances_sync_changed;
            DROP INDEX IF EXISTS tramites.ix_procedure_instances_sync_cursor;
            """);
}
