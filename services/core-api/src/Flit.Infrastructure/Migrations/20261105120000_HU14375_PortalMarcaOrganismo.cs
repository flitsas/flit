using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14375 / #14376 (Feature #14374, Épica #14297) — marca del portal Persona Natural por organismo
/// (<c>admin.transit_office_portal_brandings</c> y <c>admin.transit_office_portal_brand_images</c>).
/// DDL: <c>180-HU14375-portal-marca-organismo.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14375_PortalMarcaOrganismo")]
public partial class HU14375_PortalMarcaOrganismo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("180-HU14375-portal-marca-organismo.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS admin.transit_office_portal_brand_images;
            DROP TABLE IF EXISTS admin.transit_office_portal_brandings;
            """);
}
