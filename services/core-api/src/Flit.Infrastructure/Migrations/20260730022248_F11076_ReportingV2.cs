using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #11076 (ADR-0037/0038/0039) — Subsistema de Reportería Transaccional V2.
    ///
    /// Cambios en esta migración:
    ///
    /// 1. DDL principal — <c>45-F11076-reporting-v2.sql</c>:
    ///    - <c>analytics.export_jobs</c>: fuente durable de export jobs asíncronos,
    ///      trigger NOTIFY 'export_jobs_channel' (wake-up ExportJobsChannelListener),
    ///      trigger advisory de límite 3 pending/processing por usuario.
    ///    - <c>analytics.saved_queries</c>: consultas guardadas por usuario.
    ///    - <c>analytics.dashboard_preferences</c>: preferencias KPI por usuario (1 fila).
    ///    - <c>analytics.report_sla_config</c>: SLA configurable por tipo de trámite y OT.
    ///    - <c>analytics.holiday_calendar</c>: catálogo mixto festivos CO 2025/2026
    ///      (tenant_id NULL = global; NOT NULL = festivos propios del tenant).
    ///    - <c>analytics.v_reporting_tramites</c>: vista V2 extendida (plate, vin,
    ///      transit_office_name, company_name, elapsed_hours_total).
    ///    - Índice auxiliar <c>ix_procedure_instances_tenant_created_reporting</c>.
    ///    - Todas las tablas nuevas incluyen: RLS tenant_isolation, triggers row_version +
    ///      audit_log, índices con tenant_id como primera columna, FKs con ON DELETE/UPDATE.
    ///
    /// 2. Auditoría enriquecida — <c>46-F11076-status-history-audit.sql</c> (G3):
    ///    - ALTER <c>tramites.procedure_instance_status_history</c>: agrega columnas nullable
    ///      role_id_at_time, organization_id_at_time, organization_type_at_time.
    ///    - Backfill NULL (historial previo sin enriquecimiento).
    ///    - Decisión aprobada determinista: SIEMPRE ALTER TABLE (PG17 O(1) para nullable/NULL).
    ///
    /// 3. Admin — <c>admin.tenant_operational_policies.plate_flow_skip_to_terminado</c>:
    ///    - La columna fue creada por DDL raw en <c>20260729140000_PlateFlowTerminado</c>
    ///      (<c>ADD COLUMN IF NOT EXISTS</c>). Esta migración NO la vuelve a crear: el
    ///      snapshot EF ya la registra (correcto), pero el Up/Down omite la operación
    ///      duplicada para no fallar al intentar <c>ADD COLUMN</c> sobre una columna existente.
    ///
    /// Mecanismo de aplicación: <c>Database:AutoMigrate=true</c> en <c>Program.cs</c>
    /// invoca <c>db.Database.Migrate()</c> al arrancar la app. El DDL SQL se carga desde
    /// recursos embebidos (<c>Flit.Infrastructure.Persistence.Sql.Ddl.*</c>).
    /// </remarks>
    public partial class F11076_ReportingV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Tablas analytics + vista + índice auxiliar ─────────────────────────
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("45-F11076-reporting-v2.sql"));

            // ── 2. Auditoría enriquecida status_history (G3 — determinista) ──────────
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("46-F11076-status-history-audit.sql"));

            // ── 3. Admin: plate_flow_skip_to_terminado ────────────────────────────────
            // OMITIDA intencionalmente: ya creada por raw SQL en PlateFlowTerminado
            // (20260729140000). El snapshot EF la registra correctamente. Si se añadiera
            // migrationBuilder.AddColumn aquí, el runtime lanzaría 42701 (column exists).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Reversa analytics ─────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DROP VIEW  IF EXISTS analytics.v_reporting_tramites;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_created_reporting;
                DROP TABLE IF EXISTS analytics.holiday_calendar     CASCADE;
                DROP TABLE IF EXISTS analytics.report_sla_config    CASCADE;
                DROP TABLE IF EXISTS analytics.dashboard_preferences CASCADE;
                DROP TABLE IF EXISTS analytics.saved_queries         CASCADE;
                DROP TABLE IF EXISTS analytics.export_jobs           CASCADE;
                DROP FUNCTION IF EXISTS analytics.trg_export_jobs_notify();
                DROP FUNCTION IF EXISTS analytics.trg_export_jobs_pending_limit();
                """);

            // ── Reversa auditoría status_history ─────────────────────────────────────
            migrationBuilder.DropColumn(
                name: "role_id_at_time",
                schema: "tramites",
                table: "procedure_instance_status_history");

            migrationBuilder.DropColumn(
                name: "organization_id_at_time",
                schema: "tramites",
                table: "procedure_instance_status_history");

            migrationBuilder.DropColumn(
                name: "organization_type_at_time",
                schema: "tramites",
                table: "procedure_instance_status_history");

            // ── Admin: plate_flow_skip_to_terminado — OMITIDA ─────────────────────────
            // PlateFlowTerminado.Down() ya la elimina; no repetir aquí.
        }
    }
}
