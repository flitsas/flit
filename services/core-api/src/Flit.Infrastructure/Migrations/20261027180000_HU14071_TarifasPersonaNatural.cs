using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14071 (Feature J, Épica #14021) — tarifas versionadas del portal de Persona Natural
/// (<c>admin.persona_natural_tariffs</c>). DDL: <c>153-HU14071-tarifas-persona-natural.sql</c>. Siembra vacía.
/// Lleva los atributos inline (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261027180000_HU14071_TarifasPersonaNatural")]
public partial class HU14071_TarifasPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("153-HU14071-tarifas-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP FUNCTION IF EXISTS admin.persona_natural_tariff_new_version(varchar, uuid, uuid, bigint, date, uuid);
            DROP TABLE IF EXISTS admin.persona_natural_tariffs;
            DROP FUNCTION IF EXISTS admin.trg_persona_natural_tariffs_guard();
            """);
}
