using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14216 (Épica #14186) — canal del punto de atención (<c>admin.persona_natural_point_channel</c>).
/// DDL: <c>168-HU14216-canal-punto-persona-natural.sql</c>. Independiente del grant del portal.
/// El 167 lo ocupa HU #14033 (empresa Persona Natural). Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261029120000_HU14216_CanalPuntoPersonaNatural")]
public partial class HU14216_CanalPuntoPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("168-HU14216-canal-punto-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.persona_natural_point_channel;");
}
