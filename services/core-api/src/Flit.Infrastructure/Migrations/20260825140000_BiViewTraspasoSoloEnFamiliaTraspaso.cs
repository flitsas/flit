using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// <c>transfer_type</c> deja de calcularse fuera de la familia TRASPASO, y dentro de ella lo decide
/// el tipo antes que el indicio. DDL: <c>90-bi-view-traspaso-solo-en-familia-traspaso.sql</c>.
/// <para>Solo redefine una vista: no toca datos y es idempotente.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825140000_BiViewTraspasoSoloEnFamiliaTraspaso")]
public partial class BiViewTraspasoSoloEnFamiliaTraspaso : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("90-bi-view-traspaso-solo-en-familia-traspaso.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reaplica el DDL 84, que es la definición inmediatamente anterior. Volver más atrás
    /// resucitaría la categoría <c>vehicular</c>, que es un problema distinto y ya resuelto.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("84-bi-view-sin-categoria-vehicular.sql"));
}
