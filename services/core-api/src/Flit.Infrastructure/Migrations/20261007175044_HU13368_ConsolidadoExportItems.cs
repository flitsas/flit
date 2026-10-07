using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13368 (Feature #13306, épica #13216, ADR-0070 adendas v3 y v4) — crea <c>tramites.consolidado_export_batch_parts</c>
    /// (partes ZIP cifradas, tenant heredado del lote por trigger), <c>tramites.consolidado_export_batch_items</c> (un
    /// trámite por fila, coherencia por estado y los diez códigos de omisión) y <c>tramites.consolidado_export_audit</c>
    /// (auditoría append-only Ley 1581), con los estados de cancelación de #13307 (parte <c>descartada</c>, ítem
    /// <c>cancelado</c>, <c>lote_cancelado</c> con conteos). DDL: <c>134-HU13368-consolidado-export-items.sql</c>.
    /// Descubrible al arrancar por el Designer.cs.
    /// </summary>
    public partial class HU13368_ConsolidadoExportItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("134-HU13368-consolidado-export-items.sql"));

        /// <inheritdoc />
        /// <remarks>
        /// <b>Revertir en QA/PDN destruye la auditoría Ley 1581 de los lotes</b> (<c>tramites.consolidado_export_audit</c>):
        /// antes, exportarla (lo decide el infra-agent), como en el Down del DDL 113. Orden inverso al Up: la auditoría y su
        /// función; los ítems antes que las partes (FK compuesta); la función de tenant de las partes al final. Los
        /// índices, las políticas RLS y los triggers caen con su tabla; <c>DROP TABLE</c> no dispara el trigger de
        /// inmutabilidad (es por fila sobre UPDATE/DELETE). Las tablas de #13367 no se tocan.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS tramites.trg_consolidado_export_audit_immutable();
                DROP TABLE IF EXISTS tramites.consolidado_export_batch_items;
                DROP TABLE IF EXISTS tramites.consolidado_export_batch_parts;
                DROP FUNCTION IF EXISTS tramites.trg_consolidado_export_batch_parts_tenant();
                """);
    }
}
