using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13195 (Feature #13116, Épica #13090, ADR-0066 D1 / D-1) — colapsa sin borrar los vínculos
/// mandatario-compañía activos que exceden uno por (organismo, compañía, grupo de origen) y crea el índice
/// único parcial por expresión <c>uq_mandate_signer_companies_one_per_origin</c>. Idempotente. La reversa
/// elimina SOLO el índice: no reactiva los vínculos colapsados. El índice por expresión no es representable en
/// EF Core, así que el snapshot no cambia. DDL: <c>128-HU13195-mandatario-uno-por-origen.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930180000_HU13195_MandatarioUnoPorOrigen")]
public partial class HU13195_MandatarioUnoPorOrigen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("128-HU13195-mandatario-uno-por-origen.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP INDEX IF EXISTS admin.uq_mandate_signer_companies_one_per_origin;");
}
