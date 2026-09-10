using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// En <c>TRASPASO_UNILATERAL</c> la parte entrante es el LOCATARIO del leasing, no un comprador: el
/// paso se rotula como tal. Solo cambia el <c>title</c>; el rol persistido sigue siendo
/// <c>comprador</c>. DDL: <c>99-traspaso-unilateral-rotulo-locatario.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260828160000_TraspasoUnilateralRotuloLocatario")]
public partial class TraspasoUnilateralRotuloLocatario : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("99-traspaso-unilateral-rotulo-locatario.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_steps st
               SET title = 'Comprador',
                   updated_at = now()
              FROM tramites.procedure_types pt
             WHERE st.procedure_type_id = pt.id
               AND pt.code = 'TRASPASO_UNILATERAL'
               AND st.code = 'comprador';
            """);
}
