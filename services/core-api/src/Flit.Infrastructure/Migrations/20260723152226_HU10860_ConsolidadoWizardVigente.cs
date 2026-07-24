using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10860 (Feature #10852, ADR-0032) — marca de vigencia del expediente derivado del wizard,
    /// espejo de <c>consolidado_maestro_vigente</c> (HU #10701). Agrega
    /// <c>tramites.procedure_instances.consolidado_wizard_vigente</c> (boolean, default false): la
    /// generación del consolidado del wizard la sube a true; cualquier transición de estado, la
    /// decisión del OT o el adjuntar la Licencia de Tránsito la baja a false, de modo que la próxima
    /// generación regenera EN CASCADA el FUR y sus documentos en caliente (con fecha vigente) antes de
    /// consolidar. La tabla está <c>ExcludeFromMigrations</c> (DDL por SQL crudo, HU #10150), por eso
    /// el diff EF queda vacío para esta columna y se agrega con SQL idempotente.
    ///
    /// El diff de <c>section_type</c> que EF propuso al generar esta migración correspondía a deriva
    /// del snapshot (la columna ya la creó F08, 20260721100000); se omite del Up/Down y el snapshot
    /// regenerado la incorpora, quedando modelo y snapshot consistentes.
    /// </remarks>
    public partial class HU10860_ConsolidadoWizardVigente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS consolidado_wizard_vigente boolean NOT NULL DEFAULT false;

                COMMENT ON COLUMN tramites.procedure_instances.consolidado_wizard_vigente IS
                  'HU #10860 (ADR-0032) — vigencia del expediente derivado del wizard (FUR + documentos en caliente + consolidado): true = el consolidado persistido refleja el expediente actual (se sirve sin regenerar); false = un cambio de estado, la decision del OT o la LT lo invalido y la proxima generacion regenera en cascada el FUR con fecha vigente antes de consolidar.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS consolidado_wizard_vigente;
                """);
        }
    }
}
