using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10648 (Feature #10587). Agrega el flag por compañía <c>plate_preassign_enabled</c> a
    /// <c>admin.tenant_operational_policies</c> (patrón <c>signature_vault_enabled</c>). La tabla la
    /// crea el DDL crudo 07-HU10154 (el CreateTable de EF es no-op), así que la columna se agrega con
    /// un ALTER idempotente: bases nuevas la reciben del DDL 07 y este ALTER es no-op; bases existentes
    /// la reciben aquí. Auto-aplicable en el arranque (Program.cs Database.Migrate).
    /// </remarks>
    public partial class HU10648_PlatePreassignEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS plate_preassign_enabled boolean NOT NULL DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS plate_preassign_enabled;
                """);
        }
    }
}
