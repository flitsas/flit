using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10919 (Feature #10918) — catálogo <c>tramites.vehicle_classification_fur</c> (clasificación →
    /// plantilla FUR AUTOMOTOR/MAQUINARIA/REMOLQUES) + seed de las 96 clasificaciones. Idempotente. DDL
    /// embebido: <c>39-HU10919-vehicle-classification-fur.sql</c>.
    /// ⚠️ Designer.cs omitido (patrón F08_SeedTiposConfigurados): la tabla NO es entidad EF (se consulta por
    /// SQL crudo desde el resolver), así que la migración solo ejecuta DDL/seed y no altera el snapshot.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260724000000_HU10919_VehicleClassificationFur")]
    public partial class HU10919_VehicleClassificationFur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-HU10919-vehicle-classification-fur.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS tramites.vehicle_classification_fur;");
    }
}
