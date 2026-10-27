using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13662 (Feature #13105 F-E, Épica #12753) — <c>tramites.create</c> pasa a enforced, sin backfill: lo conservan los roles
/// que ya lo tenían (la UI lo exigía para crear). DDL: <c>143-HU13662-enforced-tramites-create.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019130000_HU13662_EnforcedTramitesCreate")]
public partial class HU13662_EnforcedTramitesCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("143-HU13662-enforced-tramites-create.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.permissions SET enforced = 'false' WHERE slug = 'tramites.create';
            """);
}
