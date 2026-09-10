using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 — la vista BI deja de producir la categoría <c>vehicular</c>, que ninguna fila podía
/// tener desde que el CHECK del DDL 79 restringió <c>procedure_types.family</c> a tres valores.
/// DDL: <c>84-bi-view-sin-categoria-vehicular.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824123000_BiViewSinCategoriaVehicular")]
public partial class BiViewSinCategoriaVehicular : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("84-bi-view-sin-categoria-vehicular.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura la definición original reaplicando el DDL 35, que la trae con la rama
    /// <c>VEHICULAR</c>. Es idempotente (<c>CREATE OR REPLACE</c> + <c>CREATE INDEX IF NOT EXISTS</c>).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("35-HU10814-procedure-detail-bi-view.sql"));
}
