using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora de la Épica #14297 — carrusel de imágenes de la marca del portal del organismo: el tipo de imagen <c>carousel</c>.
/// DDL: <c>187-portal-marca-carrusel.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261107120000_PortalMarcaCarrusel")]
public partial class PortalMarcaCarrusel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("187-portal-marca-carrusel.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        // Las imágenes de carrusel dejan de ser válidas: se quitan del borrador y del publicado antes de restituir el CHECK.
        migrationBuilder.Sql("""
            UPDATE admin.transit_office_portal_brandings
               SET draft = draft - 'carousel',
                   published = CASE WHEN published IS NULL THEN NULL ELSE published - 'carousel' END
             WHERE jsonb_exists(draft, 'carousel') OR (published IS NOT NULL AND jsonb_exists(published, 'carousel'));
            DELETE FROM admin.transit_office_portal_brand_images WHERE kind = 'carousel';
            ALTER TABLE admin.transit_office_portal_brand_images DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brand_images_kind;
            ALTER TABLE admin.transit_office_portal_brand_images
                ADD CONSTRAINT ck_transit_office_portal_brand_images_kind CHECK (kind IN ('logo', 'banner'));
            """);
}
