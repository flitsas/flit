using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13661 (Feature #13105 F-E, Épica #12753) — marca <c>improntas.read</c> e <c>improntas.generate</c> como
/// <c>enforced = 'excluded'</c> (rutas solo de SuperAdmin: configurarlos por usuario no tendría efecto). DDL: <c>142-HU13661-enforced-improntas.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019120000_HU13661_EnforcedImprontas")]
public partial class HU13661_EnforcedImprontas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("142-HU13661-enforced-improntas.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("UPDATE security.permissions SET enforced = 'false' WHERE slug IN ('improntas.read', 'improntas.generate');");
}
