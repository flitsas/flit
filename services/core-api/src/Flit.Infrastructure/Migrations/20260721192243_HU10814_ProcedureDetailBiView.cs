using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10814 (Feature #10813) — vista BI en vivo <c>analytics.v_procedure_detail_report</c> que
    /// alimenta el reporte detallado de trámites. El DDL embebido (35-HU10814-procedure-detail-bi-view.sql)
    /// crea el schema <c>analytics</c> (si falta), la vista y un índice auxiliar sobre
    /// <c>tramites.procedure_instance_field_values</c> — piezas que EF no modela (la vista no es una
    /// entidad). El commit original de la HU añadió el .sql como recurso embebido pero omitió esta
    /// migración, por lo que la vista nunca se creaba y el endpoint fallaba con 42P01.
    /// </remarks>
    public partial class HU10814_ProcedureDetailBiView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("35-HU10814-procedure-detail-bi-view.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS analytics.v_procedure_detail_report;");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS tramites.ix_procedure_instance_field_values_instance_key;");
        }
    }
}
