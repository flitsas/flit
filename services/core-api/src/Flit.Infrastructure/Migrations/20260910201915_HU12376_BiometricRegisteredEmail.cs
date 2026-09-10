using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU12376_BiometricRegisteredEmail : Migration
    {
        /// <inheritdoc />
        // NOTA — esta migración se generó con `dotnet ef migrations add`, que también propuso re-agregar
        // signature_image_path/signature_image_sha256: esas columnas YA existen en BD desde
        // 20260902160000_IdentitySignatureImage (ADR-0054, SQL crudo vía EmbeddedDdl), migración que no
        // pasó por `migrations add` y por eso nunca actualizó el ModelSnapshot — drift preexistente,
        // ajeno al Bug #12376. Se recortan aquí para no reintentar un ADD COLUMN que ya existe (fallaría
        // en DEV/QA/PDN); el snapshot regenerado por esta migración ya las incluye correctamente, así que
        // el próximo `migrations add` no debería volver a proponerlas.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "registered_email",
                schema: "tramites",
                table: "procedure_instance_biometric_validations",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "");

            // Bug #12376, defecto 3 — backfill de mejor esfuerzo para filas existentes: el correo
            // "original" que teníamos era el operativo (no hay forma de recuperar el histórico real).
            migrationBuilder.Sql(
                """
                UPDATE tramites.procedure_instance_biometric_validations
                SET registered_email = email
                WHERE registered_email = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registered_email",
                schema: "tramites",
                table: "procedure_instance_biometric_validations");
        }
    }
}
