using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU5 / E1 — Observabilidad ICT en el subsistema de alertas. Amplía el CHECK de
    /// <c>analytics.alert_rules.metric</c> con las 4 métricas ICT (evaluadas cross-schema sobre
    /// <c>ict.*</c>) y agrega el acknowledge (<c>acknowledged_at/by</c>) a
    /// <c>analytics.alert_events</c>. <c>Up</c> = DDL embebido idempotente (41-HU5-...); el CHECK no
    /// es concepto EF, por eso va como SQL crudo. Las 2 columnas entran al snapshot por esta migración.
    /// </remarks>
    public partial class HU5_IctAlertMetricsAndAck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("41-HU5-ict-alert-metrics-and-ack.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                ALTER TABLE analytics.alert_events DROP COLUMN IF EXISTS acknowledged_at;
                ALTER TABLE analytics.alert_events DROP COLUMN IF EXISTS acknowledged_by;
                ALTER TABLE analytics.alert_rules DROP CONSTRAINT IF EXISTS alert_rules_metric_check;
                ALTER TABLE analytics.alert_rules
                    ADD CONSTRAINT alert_rules_metric_check CHECK (metric IN (
                        'rejection_rate_pct', 'stuck_count', 'external_api_errors', 'pending_identity_validations'));
                """);
    }
}
