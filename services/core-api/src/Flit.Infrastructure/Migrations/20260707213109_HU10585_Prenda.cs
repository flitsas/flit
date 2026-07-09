using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10594 (Feature #10585) — dominio de prenda IT-3 (tramites.procedure_instance_prenda).
    /// El DDL embebido (24-HU10585-prenda.sql) crea la tabla con FK, índice único parcial (una vigente por
    /// instancia), CHECKs, RLS y triggers (row_version + audit), que el MigrationBuilder no modela. La entidad
    /// está ExcludeFromMigrations (igual que ProcedureInstance): el snapshot la refleja para el modelo EF.
    /// Mismo patrón que HU10150_InstancesEf / HU10545_OtRequirements.
    /// </remarks>
    public partial class HU10585_Prenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("24-HU10585-prenda.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS tramites.procedure_instance_prenda CASCADE;");
        }
    }
}
