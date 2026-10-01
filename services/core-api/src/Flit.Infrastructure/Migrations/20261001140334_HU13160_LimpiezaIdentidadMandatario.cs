using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13160 (Feature #13120, Épica #13090) — limpieza DESTRUCTIVA del modelo de mandatarios: elimina la tabla
    /// <c>admin.admin_identity_validations</c> (sin uso desde ADR-0050) y las columnas
    /// <c>admin.mandate_signers.identity_validation_ref</c> y <c>admin.transit_office_mandate_config.custom_field_manifest</c>.
    /// NO toca <c>admin.company_legal_representatives.identity_validation_ref</c>. El DDL
    /// (<c>127-HU13160-limpieza-identidad-mandatario.sql</c>) aborta si encuentra datos en uso. La reversa recrea la
    /// estructura sin datos (DDL 40 y 74 más las dos columnas). La migración tiene Designer.cs con [DbContext] y
    /// [Migration], y el snapshot ya no incluye la entidad ni las propiedades retiradas.
    /// </remarks>
    public partial class HU13160_LimpiezaIdentidadMandatario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("127-HU13160-limpieza-identidad-mandatario.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("40-HU10907-admin-identity-validations.sql"));
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("74-HU11504-admin-identity-validations-attempts.sql"));
            migrationBuilder.Sql(
                "ALTER TABLE admin.mandate_signers ADD COLUMN IF NOT EXISTS identity_validation_ref uuid;");
            migrationBuilder.Sql(
                "ALTER TABLE admin.transit_office_mandate_config ADD COLUMN IF NOT EXISTS custom_field_manifest jsonb;");
        }
    }
}
