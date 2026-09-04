using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FUR §1 CIUDAD — <c>catalogs.transit_offices.city_name</c> y <c>department_name</c>.
    /// DDL idempotente + backfill en <c>Persistence/Sql/Ddl/34-transit-offices-city-department-names.sql</c>.
    /// Bases donde el SQL 34 ya corrió a mano: ADD COLUMN IF NOT EXISTS + UPDATE son no-op seguros.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260904120000_TransitOfficesCityDepartmentNames")]
    public partial class TransitOfficesCityDepartmentNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El script 34 no debe traer BEGIN/COMMIT: choca con la transacción de EF
            // ("Transaction is already completed" en Apply migrations del CI).
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("34-transit-offices-city-department-names.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE catalogs.transit_offices
                    DROP COLUMN IF EXISTS city_name;

                ALTER TABLE catalogs.transit_offices
                    DROP COLUMN IF EXISTS department_name;
                """);
        }
    }
}
