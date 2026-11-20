using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14401 (Feature #14400, Épica #14297) — ámbito web en las tablas de marca del portal (DDL 180).
/// DDL: <c>184-HU14401-portal-marca-web.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261106120000_HU14401_PortalMarcaWeb")]
public partial class HU14401_PortalMarcaWeb : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("184-HU14401-portal-marca-web.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM admin.transit_office_portal_brand_images WHERE scope = 'web';
            DELETE FROM admin.transit_office_portal_brandings WHERE scope = 'web';
            DROP INDEX IF EXISTS admin.uq_transit_office_portal_brand_images_web_version;
            DROP INDEX IF EXISTS admin.uq_transit_office_portal_brandings_web;
            ALTER TABLE admin.transit_office_portal_brand_images DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brand_images_scope;
            ALTER TABLE admin.transit_office_portal_brandings DROP CONSTRAINT IF EXISTS ck_transit_office_portal_brandings_scope;
            ALTER TABLE admin.transit_office_portal_brand_images ALTER COLUMN transit_office_id SET NOT NULL;
            ALTER TABLE admin.transit_office_portal_brandings ALTER COLUMN transit_office_id SET NOT NULL;
            ALTER TABLE admin.transit_office_portal_brand_images DROP COLUMN IF EXISTS scope;
            ALTER TABLE admin.transit_office_portal_brandings DROP COLUMN IF EXISTS scope;
            """);
}
