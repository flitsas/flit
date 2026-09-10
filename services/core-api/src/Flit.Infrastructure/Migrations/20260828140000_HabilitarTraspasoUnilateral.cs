using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Enciende <c>wizard_enabled</c> de <c>TRASPASO_UNILATERAL</c>: con la parametrización de ADR-0051
/// ya validada, el interruptor era lo único que impedía crear el trámite desde el modal.
/// DDL: <c>98-habilitar-traspaso-unilateral.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260828140000_HabilitarTraspasoUnilateral")]
public partial class HabilitarTraspasoUnilateral : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("98-habilitar-traspaso-unilateral.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_types
               SET wizard_enabled = false,
                   updated_at = now()
             WHERE code = 'TRASPASO_UNILATERAL';
            """);
}
