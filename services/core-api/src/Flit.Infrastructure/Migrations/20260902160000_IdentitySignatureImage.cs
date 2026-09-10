using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0054 — path + hash del recorte de la rúbrica Kyverum en
/// <c>tramites.procedure_instance_biometric_validations</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260902160000_IdentitySignatureImage")]
public partial class IdentitySignatureImage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("101-identity-signature-image.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP CONSTRAINT IF EXISTS ck_biometric_validations_signature_image;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP COLUMN IF EXISTS signature_image_path,
                DROP COLUMN IF EXISTS signature_image_sha256;
            """);
}
