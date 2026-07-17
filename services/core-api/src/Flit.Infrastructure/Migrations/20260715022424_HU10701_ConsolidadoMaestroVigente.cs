using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10701 / HU #10706 — marca de vigencia del consolidado maestro. Agrega
    /// <c>tramites.procedure_instances.consolidado_maestro_vigente</c> (boolean, default false):
    /// la generación la sube a true; cualquier transición de estado o el adjuntar la Licencia de
    /// Tránsito la baja a false, de modo que el botón único "Ver consolidado" regenera el PDF solo
    /// cuando el expediente cambió. La tabla está <c>ExcludeFromMigrations</c> (DDL por SQL crudo,
    /// HU #10150), por eso el diff EF queda vacío y la columna se agrega con SQL idempotente.
    /// </remarks>
    public partial class HU10701_ConsolidadoMaestroVigente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS consolidado_maestro_vigente boolean NOT NULL DEFAULT false;

                COMMENT ON COLUMN tramites.procedure_instances.consolidado_maestro_vigente IS
                  'Feature #10701 — vigencia del consolidado maestro: true = el PDF persistido refleja el expediente actual (se muestra sin regenerar); false = un cambio de estado o la LT lo invalidó y se regenera al próximo Ver consolidado.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS consolidado_maestro_vigente;
                """);
        }
    }
}
