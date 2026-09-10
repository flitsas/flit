using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Catálogo persistido de carrocerías por clase de vehículo + seed desde CLASE CARROCERIA.xlsx.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826200000_VehicleBodyworksCatalog")]
public partial class VehicleBodyworksCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("82-vehicle-bodyworks-catalog.sql"));
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("82-vehicle-bodyworks-catalog-seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS catalogs.vehicle_bodyworks;");
    }
}
