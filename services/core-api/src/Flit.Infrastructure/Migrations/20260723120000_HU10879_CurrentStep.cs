using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10879 — Persistir avance de borrador por pasos. Agrega
    /// <c>tramites.procedure_instances.current_step</c>: la <c>Key</c> del paso del wizard donde quedó
    /// el operador (autosave del avance). Al reabrir el borrador PRIMA como punto de retoma (AC2);
    /// mientras sea NULL el frontend cae al paso derivado de los gates (sin regresión). La tabla está
    /// <c>ExcludeFromMigrations</c> (DDL gestionado por SQL crudo, HU #10150), por eso el diff EF queda
    /// vacío y la columna se agrega con SQL idempotente. Se declaran <c>[DbContext]</c>/<c>[Migration]</c>
    /// inline (mismo patrón que N03_EstadosNegocioTramite) para que la migración se descubra y corra al
    /// arrancar sin depender del ModelSnapshot (que no rastrea esta tabla excluida).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260723120000_HU10879_CurrentStep")]
    public partial class HU10879_CurrentStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS current_step varchar(40) NULL;

                COMMENT ON COLUMN tramites.procedure_instances.current_step IS
                  'HU #10879 — paso actual persistido del wizard (Key del paso). Prima como punto de retoma al reabrir el borrador. NULL = caer al paso derivado de los gates.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS current_step;
                """);
        }
    }
}
