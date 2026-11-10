using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora de la Épica #14297 — lista de banners (hasta 6) de la marca del portal del organismo. Migra el banner único legacy a
/// <c>banners[0]</c> en borrador y publicado. DDL: <c>188-portal-marca-banners.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261109120000_PortalMarcaBanners")]
public partial class PortalMarcaBanners : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("188-portal-marca-banners.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        // Vuelve al banner único: conserva el PRIMER banner como "bannerId" y descarta el resto de la lista.
        migrationBuilder.Sql("""
            UPDATE admin.transit_office_portal_brandings
               SET draft = CASE
                     WHEN jsonb_typeof(draft -> 'banners') = 'array' AND jsonb_array_length(draft -> 'banners') > 0
                       THEN (draft - 'banners') || jsonb_build_object('bannerId', draft -> 'banners' -> 0 ->> 'imageId')
                     ELSE draft - 'banners' END
             WHERE transit_office_id IS NOT NULL AND jsonb_exists(draft, 'banners');
            UPDATE admin.transit_office_portal_brandings
               SET published = CASE
                     WHEN jsonb_typeof(published -> 'banners') = 'array' AND jsonb_array_length(published -> 'banners') > 0
                       THEN (published - 'banners') || jsonb_build_object('bannerId', published -> 'banners' -> 0 ->> 'imageId')
                     ELSE published - 'banners' END
             WHERE transit_office_id IS NOT NULL AND published IS NOT NULL AND jsonb_exists(published, 'banners');
            """);
}
