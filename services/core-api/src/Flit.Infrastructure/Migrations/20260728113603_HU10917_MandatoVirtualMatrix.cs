using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10917 (ADR-0036) — añade 'mandato' y 'tramite_virtual' a la matriz documental de
    /// Matrícula/Traspaso (procedure_document_requirements, no obligatorios) para que el OT los pueda
    /// REORDENAR en su prelación y el Consolidado maestro los ubique por la matriz. Seed idempotente
    /// (43-HU10917-mandato-virtual-matrix.sql, ON CONFLICT DO NOTHING).
    /// </remarks>
    public partial class HU10917_MandatoVirtualMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("43-HU10917-mandato-virtual-matrix.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                "DELETE FROM tramites.procedure_document_requirements r " +
                "USING tramites.document_types dt, tramites.procedure_types pt " +
                "WHERE r.document_type_id = dt.id AND r.procedure_type_id = pt.id " +
                "AND dt.code IN ('mandato', 'tramite_virtual') " +
                "AND pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD');");
    }
}
