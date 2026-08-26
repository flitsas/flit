using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Extiende <c>only_own_vehicles</c> a las tres familias de trámite (MATRICULAS / TRASPASO / OTROS).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260805210000_OnlyOwnVehiclesByFamily")]
public partial class OnlyOwnVehiclesByFamily : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("57-only-own-vehicles-by-family.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS only_own_vehicles_matriculas,
              DROP COLUMN IF EXISTS only_own_vehicles_otros;
            """);
    }
}
