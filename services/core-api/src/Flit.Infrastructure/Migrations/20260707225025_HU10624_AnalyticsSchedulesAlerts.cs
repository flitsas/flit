using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Reportes 2.0 · HU-D — Informes programados y alertas por umbral
    /// (docs/contratos-reportes-v2.md §8). Crea <c>analytics.report_schedules</c>,
    /// <c>analytics.alert_rules</c> y <c>analytics.alert_events</c> con índices por tenant
    /// y políticas RLS <c>tenant_isolation</c> desde el DDL embebido (ADR-0018). El modelo
    /// EF de las tres entidades entró al snapshot en HU10621_AppUsageEvents (misma tanda
    /// Reportes 2.0), por eso esta migración es solo SQL.
    /// </remarks>
    public partial class HU10624_AnalyticsSchedulesAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("31-HU10624-analytics-schedules-alerts.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS analytics.alert_events CASCADE;
DROP TABLE IF EXISTS analytics.alert_rules CASCADE;
DROP TABLE IF EXISTS analytics.report_schedules CASCADE;
");
        }
    }
}
