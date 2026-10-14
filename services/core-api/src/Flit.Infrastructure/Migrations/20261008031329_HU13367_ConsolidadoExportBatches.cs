using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13367 (Feature #13306, épica #13216, ADR-0070 adendas v3 y v5) — crea <c>tramites.consolidado_export_settings</c>
    /// (parámetros del motor de lotes, fila única global sembrada) y <c>tramites.consolidado_export_batches</c> (cabecera
    /// del lote, un activo por usuario, <c>tenant_id</c> NULL solo en origen superadmin y organismo solo y siempre en
    /// origen ot_bandeja). DDL: <c>133-HU13367-consolidado-export-batches.sql</c>. Descubrible al arrancar por el
    /// Designer.cs.
    /// </summary>
    public partial class HU13367_ConsolidadoExportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("133-HU13367-consolidado-export-batches.sql"));

        /// <inheritdoc />
        /// <remarks>
        /// Los índices, la política RLS y los triggers caen con su tabla. No toca tablas preexistentes; las filas que el
        /// trigger <c>trg_audit_log</c> de los parámetros dejó en <c>audit.audit_logs</c> se conservan (el lote no lo lleva:
        /// E6, ADR-0070 adenda v6). Revertir en QA/PDN pierde los
        /// lotes y la calibración de parámetros (el infra-agent decide si exportarlos antes).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS tramites.consolidado_export_batches;
                DROP TABLE IF EXISTS tramites.consolidado_export_settings;
                """);
    }
}
