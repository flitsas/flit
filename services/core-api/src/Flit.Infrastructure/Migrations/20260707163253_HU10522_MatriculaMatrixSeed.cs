using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10522 (RF17/RF22) — Matriz documental base de Matrícula Inicial. Siembra
    /// tramites.procedure_document_requirements para MATRICULA_NUEVA con los 9 documentos del
    /// catálogo vivo (misma obligatoriedad y orden), para que el gestor arranque a paridad con el
    /// checklist actual. Idempotente (ON CONFLICT DO NOTHING). Corre en todos los entornos
    /// (config de negocio, no dev-seed).
    /// </remarks>
    public partial class HU10522_MatriculaMatrixSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("24-HU10522-matricula-matrix-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.procedure_document_requirements r
                USING tramites.procedure_types pt, tramites.document_types dt
                WHERE r.procedure_type_id = pt.id
                  AND pt.code = 'MATRICULA_NUEVA'
                  AND r.document_type_id = dt.id
                  AND dt.code IN (
                      'factura','aduana','impronta','soat','certificado_ambiental',
                      'declaracion_aduana','acta_remate','oficio_judicial','otro'
                  );
                """);
        }
    }
}
