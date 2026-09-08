using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12153 (Feature #12150) — orden numérico del radicado.
    /// DDL en <c>Persistence/Sql/Ddl/104-HU12153-orden-numerico-radicado.sql</c>.
    ///
    /// Va en migración aparte de la #12151 porque endurece un CHECK que aquella acaba de crear;
    /// separarlas deja legible en el historial qué cambio pidió cada invariante.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908130000_HU12153_OrdenNumericoRadicado")]
    public partial class HU12153_OrdenNumericoRadicado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("104-HU12153-orden-numerico-radicado.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Devuelve el CHECK a la forma laxa de la #12151 y retira el índice de apoyo.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_reference_orden;

                ALTER TABLE tramites.procedure_instances
                    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico;

                ALTER TABLE tramites.procedure_instances
                    ADD CONSTRAINT ck_procedure_instances_reference_numerico
                    CHECK (reference_number ~ '^[0-9]+$');
                """);
        }
    }
}
