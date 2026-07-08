using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10522 (RF17/RF22) — Matriz documental base de Traspaso. Siembra
    /// tramites.procedure_document_requirements para TRASPASO_STANDARD con los documentos del
    /// catálogo vivo (misma obligatoriedad y orden), para que el gestor arranque a paridad con el
    /// checklist de traspaso. Idempotente (ON CONFLICT DO NOTHING). Corre en todos los entornos.
    /// </remarks>
    public partial class HU10522_TraspasoMatrixSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("25-HU10522-traspaso-matrix-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.procedure_document_requirements r
                USING tramites.procedure_types pt, tramites.document_types dt
                WHERE r.procedure_type_id = pt.id
                  AND pt.code = 'TRASPASO_STANDARD'
                  AND r.document_type_id = dt.id
                  AND dt.code IN (
                      'compraventa','impronta','soat','rtm','paz_salvo','cedulas','cert_tradicion'
                  );
                """);
        }
    }
}
