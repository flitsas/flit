using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Soft-delete + snapshot de PDF firmado: <c>attachment_id</c> nullable / SET NULL
    /// y columnas <c>signed_*</c> para conservar auditoría al reemplazar la impronta.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908020000_VehicleSignatureImprintsAuditSnapshot")]
    public partial class VehicleSignatureImprintsAuditSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("98-vehicle-signature-imprints-audit-snapshot.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE tramites.vehicle_signature_imprints
                  DROP CONSTRAINT IF EXISTS fk_vehicle_signature_imprints_attachment;

                DELETE FROM tramites.vehicle_signature_imprints WHERE attachment_id IS NULL;

                ALTER TABLE tramites.vehicle_signature_imprints
                  ALTER COLUMN attachment_id SET NOT NULL;

                ALTER TABLE tramites.vehicle_signature_imprints
                  ADD CONSTRAINT fk_vehicle_signature_imprints_attachment
                  FOREIGN KEY (attachment_id)
                  REFERENCES tramites.procedure_instance_attachments(id)
                  ON DELETE RESTRICT ON UPDATE CASCADE;

                ALTER TABLE tramites.vehicle_signature_imprints DROP COLUMN IF EXISTS signed_filename;
                ALTER TABLE tramites.vehicle_signature_imprints DROP COLUMN IF EXISTS signed_size_bytes;
                ALTER TABLE tramites.vehicle_signature_imprints DROP COLUMN IF EXISTS signed_sha256;
                ALTER TABLE tramites.vehicle_signature_imprints DROP COLUMN IF EXISTS signed_storage_path;

                DROP INDEX IF EXISTS tramites.ix_vehicle_signature_imprints_attachment;
                CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_attachment
                  ON tramites.vehicle_signature_imprints (attachment_id);
                """);
        }
    }
}
