using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13086 (Feature #13067, Épica #12737) — bitácora de accesos externos
/// <c>integrations.external_access_log</c>. DDL: <c>127-HU13086-integrations-external-access-log.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930150000_HU13086_ExternalAccessLog")]
public partial class HU13086_ExternalAccessLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("127-HU13086-integrations-external-access-log.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS integrations.external_access_log;");
}
