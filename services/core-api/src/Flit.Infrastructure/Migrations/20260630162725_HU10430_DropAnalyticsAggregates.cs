using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10430 — Analítica · Conectar reportes con datos reales de trámites | Feature #10139.
    /// ADR-0021 (Opción C aceptada): la analítica pasa a LECTURA EN VIVO desde <c>tramites.*</c>
    /// (ver <see cref="Persistence.Repositories.AnalyticsReadRepository"/>), por lo que los agregados
    /// materializados dejan de usarse. Esta migración los ELIMINA para tener una sola fuente de verdad:
    ///   - función <c>analytics.refresh_procedure_aggregates(uuid, date, date)</c> (HU #10240),
    ///   - tablas <c>analytics.user_productivity_daily</c> y <c>analytics.procedure_metrics_daily</c>
    ///     (HU #10153) con sus políticas RLS y triggers de auditoría (DROP ... CASCADE).
    /// En prod las tablas estaban vacías (el refresh nunca se ejecutaba en runtime). El schema
    /// <c>analytics</c> se conserva (vacío) por si aloja artefactos futuros.
    /// Down recrea tablas + función desde el DDL embebido original (no restaura datos).
    /// </remarks>
    public partial class HU10430_DropAnalyticsAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP FUNCTION IF EXISTS analytics.refresh_procedure_aggregates(uuid, date, date);
DROP TABLE IF EXISTS analytics.user_productivity_daily CASCADE;
DROP TABLE IF EXISTS analytics.procedure_metrics_daily CASCADE;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recrea los agregados y la función de refresh desde el DDL original (sin datos).
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("10-HU10153-analytics.sql"));
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("20-HU10240-analytics-refresh.sql"));
        }
    }
}
