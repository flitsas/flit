using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Columna <c>field_to_fill</c> + reseed del catálogo (Excel PINTAR FUR).
/// Designer.cs omitido (mismo patrón que HU #10919): la tabla no es entidad EF.
/// DDL: <c>97-vehicle-classification-fur-field-to-fill.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260831120000_VehicleClassificationFurFieldToFill")]
public partial class VehicleClassificationFurFieldToFill : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("97-vehicle-classification-fur-field-to-fill.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE tramites.vehicle_classification_fur
                DROP COLUMN IF EXISTS field_to_fill;
            """);
}
