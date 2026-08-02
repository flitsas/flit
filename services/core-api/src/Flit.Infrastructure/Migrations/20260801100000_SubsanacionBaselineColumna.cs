using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// El baseline de campos que usa el diff de re-radicación viajaba dentro del <c>metadata</c> de
    /// una fila de <c>procedure_instance_status_history</c> con <c>rechazado → rechazado</c>, escrita
    /// al activar la subsanación. Esa fila no era una transición real: solo transportaba el snapshot,
    /// pero el timeline del trámite la pintaba como un segundo "Rechazado".
    ///
    /// <para>Aquí el baseline pasa a una columna propia de la instancia. El backfill copia el
    /// snapshot más reciente del historial a la columna para los expedientes que ya tienen la
    /// subsanación activa, de modo que ninguno pierda su punto de comparación.</para>
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260801100000_SubsanacionBaselineColumna")]
    public partial class SubsanacionBaselineColumna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS subsanacion_baseline jsonb;

                COMMENT ON COLUMN tramites.procedure_instances.subsanacion_baseline IS
                  'Snapshot de field_values capturado al activar la subsanación. Es el baseline del diff que decide qué gates se re-evalúan al re-radicar. Antes viajaba en el metadata de una fila rechazado→rechazado del historial, que ensuciaba el timeline.';
                """);

            // Backfill: el snapshot más reciente del historial de cada expediente con subsanación
            // activa. Solo se consideran filas cuyo metadata trae fieldSnapshot (las de rechazo del
            // OT/Quipux también lo traen, así que sirven de origen igual que la fila fantasma).
            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;

                WITH ultimo AS (
                    SELECT DISTINCT ON (h.procedure_instance_id)
                           h.procedure_instance_id,
                           h.metadata
                      FROM tramites.procedure_instance_status_history h
                      JOIN tramites.procedure_instances i ON i.id = h.procedure_instance_id
                     WHERE i.subsanacion_activa = true
                       AND h.metadata IS NOT NULL
                       AND h.metadata::text LIKE '%fieldSnapshot%'
                     ORDER BY h.procedure_instance_id, h.changed_at DESC, h.id DESC
                )
                UPDATE tramites.procedure_instances i
                   SET subsanacion_baseline = u.metadata::jsonb
                  FROM ultimo u
                 WHERE i.id = u.procedure_instance_id
                   AND i.subsanacion_baseline IS NULL;
                """);

            // Las filas fantasma ya escritas se retiran del historial: no representan ninguna
            // transición y su único contenido útil acaba de copiarse a la columna.
            migrationBuilder.Sql(
                """
                SET LOCAL row_security = off;

                DELETE FROM tramites.procedure_instance_status_history
                 WHERE from_status = 'rechazado'
                   AND to_status = 'rechazado';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las filas fantasma no se recrean: eran el defecto que esta migración corrige y el
            // baseline sigue disponible en la columna mientras exista.
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS subsanacion_baseline;
                """);
        }
    }
}
