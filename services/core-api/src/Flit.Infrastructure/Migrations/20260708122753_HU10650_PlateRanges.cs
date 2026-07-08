using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10650 (Feature #10587) — inventario de preasignación de placa. El DDL embebido
    /// (33-HU10650-plate-ranges.sql) crea admin.plate_ranges y admin.plate_range_details con FKs,
    /// CHECKs, RLS y triggers (row_version + audit) que el MigrationBuilder no modela; el
    /// snapshot/Designer reconcilian el modelo EF. Mismo patrón que HU10545_OtRequirements.
    /// Auto-aplicable en el arranque (Program.cs Database.Migrate).
    /// </remarks>
    public partial class HU10650_PlateRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("33-HU10650-plate-ranges.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.plate_range_details CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.plate_ranges CASCADE;");
        }
    }
}
