using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Tabla <c>tramites.vehicle_signature_imprints</c>: auditoría de impronta manual firmada
    /// (paridad legacy) al estampar en consolidado OT.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908010000_VehicleSignatureImprints")]
    public partial class VehicleSignatureImprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("97-vehicle-signature-imprints.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_vehicle_signature_imprints_audit ON tramites.vehicle_signature_imprints;
                DROP TRIGGER IF EXISTS tr_vehicle_signature_imprints_row_version ON tramites.vehicle_signature_imprints;
                DROP POLICY IF EXISTS tenant_isolation ON tramites.vehicle_signature_imprints;
                DROP TABLE IF EXISTS tramites.vehicle_signature_imprints;
                """);
        }
    }
}
