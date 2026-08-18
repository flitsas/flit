using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Informes programados y alertas por umbral, alcance Organismo de Tránsito (Reportes 2.0, HU-D,
/// tercera ola): ensancha los CHECK de <c>report_type</c> (+ <c>ot_operativo</c>) y <c>metric</c>
/// (+ <c>ot_rejection_rate_pct</c>, <c>ot_stuck_count</c>). Sin cambios de esquema — ver el detalle
/// comentado en <c>Persistence/Sql/Ddl/76-reportes-programados-alertas-ot.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260818130000_ReportesProgramadosAlertasOt")]
public partial class ReportesProgramadosAlertasOt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("76-reportes-programados-alertas-ot.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE analytics.report_schedules " +
            "DROP CONSTRAINT IF EXISTS report_schedules_report_type_check;");
        migrationBuilder.Sql(
            "ALTER TABLE analytics.report_schedules ADD CONSTRAINT report_schedules_report_type_check " +
            "CHECK (report_type IN ('resumen','operacion','ot','uso','productividad','consulta'));");

        migrationBuilder.Sql(
            "ALTER TABLE analytics.alert_rules DROP CONSTRAINT IF EXISTS alert_rules_metric_check;");
        migrationBuilder.Sql(
            "ALTER TABLE analytics.alert_rules ADD CONSTRAINT alert_rules_metric_check " +
            "CHECK (metric IN (" +
            "'rejection_rate_pct','stuck_count','external_api_errors','pending_identity_validations'," +
            "'ict_stuck_in_validation','ict_novelty_rate_pct','ict_webhook_delivery_failures','ict_jobs_out_of_sla'" +
            "));");
    }
}
