using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Catálogo global (ADR-0019) de tipos de servicio del vehículo — sección 18 del FUR. 6 valores
/// cerrados (PARTICULAR, PUBLICO, DIPLOMATICO, OFICIAL, ESPECIAL, OTROS), contrato con
/// <c>FurFieldMapper.MarkServicio</c>. Mismo patrón que <c>VehicleColorsCatalog</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811150000_VehicleServiceTypesCatalog")]
public partial class VehicleServiceTypesCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("69-vehicle-service-types-catalog.sql"));
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("69-vehicle-service-types-catalog-seed.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS catalogs.vehicle_service_types;");
    }
}
