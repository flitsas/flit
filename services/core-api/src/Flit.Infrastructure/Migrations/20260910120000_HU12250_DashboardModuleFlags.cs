using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12250 (Feature #12249 — [DASHBOARD] Estadísticas de Trámites por servicios activos del
    /// cliente). Agrega 3 flags de módulos del dashboard a
    /// <c>admin.tenant_operational_policies</c> (patrón <c>signature_vault_enabled</c> /
    /// <c>plate_preassign_enabled</c>): <c>tramites_module_enabled</c> (default true — módulo
    /// histórico, ya en uso por todos los tenants), <c>comparendos_module_enabled</c> y
    /// <c>resoluciones_module_enabled</c> (default false — opt-in por tenant). ALTER idempotente:
    /// bases nuevas la reciben del DDL base (CreateTable de EF es no-op sobre esta tabla) y este
    /// ALTER es no-op; bases existentes la reciben aquí. Auto-aplicable en el arranque
    /// (Program.cs Database.Migrate). Sin tabla ni catálogo nuevo (AC2: ninguna fila existente
    /// falla por ser NOT NULL sin required, porque las 3 columnas traen DEFAULT).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260910120000_HU12250_DashboardModuleFlags")]
    public partial class HU12250_DashboardModuleFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS tramites_module_enabled boolean NOT NULL DEFAULT true;
                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS comparendos_module_enabled boolean NOT NULL DEFAULT false;
                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS resoluciones_module_enabled boolean NOT NULL DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS resoluciones_module_enabled;
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS comparendos_module_enabled;
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS tramites_module_enabled;
                """);
        }
    }
}
