using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14393 (Feature #14392, Épica #14297) — banderas de medios de pago y switches de cobro del portal de Persona Natural.
/// DDL: <c>182-HU14393-medios-cobro-persona-natural.sql</c> (dos tablas no mapeadas en EF). Lleva los atributos inline
/// (sin Designer): sin ellos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106100000_HU14393_MediosCobroPersonaNatural")]
public partial class HU14393_MediosCobroPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("182-HU14393-medios-cobro-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS admin.persona_natural_charge_switches;
            DROP TABLE IF EXISTS admin.persona_natural_payment_methods;
            """);
}
