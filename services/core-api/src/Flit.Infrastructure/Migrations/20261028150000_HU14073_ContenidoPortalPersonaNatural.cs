using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — contenido administrable del portal de Persona Natural: preguntas frecuentes,
/// banners, enlaces y versiones publicadas (<c>admin.persona_natural_faqs/_banners/_links/_content_versions</c>). DDL:
/// <c>165-HU14073-contenido-portal-persona-natural.sql</c>. Nace vacío. Lleva los atributos inline (sin Designer): sin ellos
/// la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028150000_HU14073_ContenidoPortalPersonaNatural")]
public partial class HU14073_ContenidoPortalPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("165-HU14073-contenido-portal-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS admin.persona_natural_content_versions;
            DROP TABLE IF EXISTS admin.persona_natural_links;
            DROP TABLE IF EXISTS admin.persona_natural_banners;
            DROP TABLE IF EXISTS admin.persona_natural_faqs;
            DROP FUNCTION IF EXISTS admin.trg_persona_natural_content_versions_guard();
            """);
}
