using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14072 (Feature J, Épica #14021) — lista blanca de tipos de trámite (<c>admin.persona_natural_enabled_types</c>) y
/// parámetros generales (<c>admin.persona_natural_settings</c>, fila única) del portal de Persona Natural. DDL:
/// <c>164-HU14072-configuracion-portal-persona-natural.sql</c>. La lista nace vacía. Lleva los atributos inline (sin
/// Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028140000_HU14072_ConfiguracionPortalPersonaNatural")]
public partial class HU14072_ConfiguracionPortalPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("164-HU14072-configuracion-portal-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS admin.persona_natural_settings;
            DROP TABLE IF EXISTS admin.persona_natural_enabled_types;
            """);
}
