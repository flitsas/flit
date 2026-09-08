using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Tabla append-only <c>tramites.imprint_signature_validations</c> — log OT de validación
    /// de firma digital de impronta manual (HU #12148).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908040000_ImprintSignatureValidations")]
    public partial class ImprintSignatureValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("100-imprint-signature-validations.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_imprint_signature_validations_audit ON tramites.imprint_signature_validations;
                DROP POLICY IF EXISTS tenant_isolation ON tramites.imprint_signature_validations;
                DROP TABLE IF EXISTS tramites.imprint_signature_validations;
                """);
        }
    }
}
