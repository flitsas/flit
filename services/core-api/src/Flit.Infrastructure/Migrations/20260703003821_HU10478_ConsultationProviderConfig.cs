using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10478 — override por tenant de la cadena de proveedores de consulta RUNT. Añade
    /// <c>admin.tenant_operational_policies.consultation_provider_config</c> (jsonb NOT NULL DEFAULT
    /// <c>'{}'</c>). Forma:
    /// <c>{ "vehicle_vin": { "primary": "kyverum_runt", "fallback": ["verifik"] }, ... }</c>;
    /// <c>'{}'</c> = usar los defaults globales de <c>Consultations:DefaultChains</c>. Aditiva y
    /// backward-compatible: las filas existentes nacen con <c>'{}'</c>.
    /// </remarks>
    public partial class HU10478_ConsultationProviderConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "consultation_provider_config",
                schema: "admin",
                table: "tenant_operational_policies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consultation_provider_config",
                schema: "admin",
                table: "tenant_operational_policies");
        }
    }
}
