using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Catálogo persistido de colores de vehículo (RUNT) + seed desde CSV de negocio.
/// Sustituye el placeholder VEHICLE_COLOR_CATALOG del frontend (ADR-0029 deuda).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260804120000_VehicleColorsCatalog")]
public partial class VehicleColorsCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("55-vehicle-colors-catalog.sql"));
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("55-vehicle-colors-catalog-seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS catalogs.vehicle_colors;");
    }
}
