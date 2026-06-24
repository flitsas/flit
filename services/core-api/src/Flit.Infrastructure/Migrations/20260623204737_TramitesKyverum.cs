using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10233 — Integrar Kyverum Verify (fase 1) + contratos de eventos (fase 2).
    /// El DDL (17-tramites-kyverum.sql) extiende procedure_instance_biometric_validations con los campos
    /// del proveedor (provider, kyverum_verification_id, capture_url, webhook_secret_encrypted,
    /// provider_status, provider_payload) + índice único parcial, y crea
    /// tramites.identity_validation_outbox con RLS e índices (incl. único parcial 'completed'). El
    /// snapshot/Designer reconcilian el modelo EF (no se emiten AddColumn/CreateTable desde el
    /// MigrationBuilder, para no arrastrar tablas creadas por SQL crudo como security.user_invitations).
    /// </remarks>
    public partial class TramitesKyverum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("17-tramites-kyverum.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.identity_validation_outbox;
DROP INDEX IF EXISTS tramites.uq_procedure_instance_biometric_validations_kyverum_verification_id;
ALTER TABLE tramites.procedure_instance_biometric_validations
  DROP COLUMN IF EXISTS provider,
  DROP COLUMN IF EXISTS kyverum_verification_id,
  DROP COLUMN IF EXISTS capture_url,
  DROP COLUMN IF EXISTS webhook_secret_encrypted,
  DROP COLUMN IF EXISTS provider_status,
  DROP COLUMN IF EXISTS provider_payload;
");
        }
    }
}
