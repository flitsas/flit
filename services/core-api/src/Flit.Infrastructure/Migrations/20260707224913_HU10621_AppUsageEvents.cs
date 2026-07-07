using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Reportes 2.0 · HU-A — Telemetría de uso (docs/contratos-reportes-v2.md §7).
    /// Crea <c>analytics.app_usage_events</c> (eventos de wizard y uso de módulos) con sus
    /// índices y política RLS <c>tenant_isolation</c> desde el DDL embebido, siguiendo
    /// ADR-0018 (migración por HU + DDL embebido). Las tablas de HU-D (schedules/alertas)
    /// van en la migración HU10624_AnalyticsSchedulesAlerts.
    /// </remarks>
    public partial class HU10621_AppUsageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "analytics");
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("30-HU10621-app-usage-events.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS analytics.app_usage_events CASCADE;");
        }
    }
}
