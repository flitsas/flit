using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Reemplaza UNIQUE global de <c>document_hash</c> por índice único parcial
    /// (<c>WHERE deleted_at IS NULL</c>) para permitir re-firma tras soft-delete de auditoría.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908030000_VehicleSignatureImprintsPartialUniqueHash")]
    public partial class VehicleSignatureImprintsPartialUniqueHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("99-vehicle-signature-imprints-partial-unique-hash.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS tramites.uq_vehicle_signature_imprints_document_hash_active;

                CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_document_hash
                  ON tramites.vehicle_signature_imprints (document_hash);

                ALTER TABLE tramites.vehicle_signature_imprints
                  ADD CONSTRAINT uq_vehicle_signature_imprints_document_hash UNIQUE (document_hash);
                """);
        }
    }
}
