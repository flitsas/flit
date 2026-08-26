using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Retira el paso propio «Decisión de prenda» de <c>LEVANTAMIENTO_PRENDA</c> y
/// <c>PRENDA_INSCRIPCION</c>: la prenda se captura dentro de Requisitos, igual que en traspaso y en
/// matrícula. DDL: <c>90-prenda-en-requisitos.sql</c>.
/// <para>No toca <c>LEVANTAR_INSCRIBIR_PRENDA</c> ni <c>CAMBIO_ACREEDOR</c>, que comparten el
/// recorrido pero ejecutan dos acciones sobre el gravamen y conservan su paso.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825160000_PrendaEnRequisitos")]
public partial class PrendaEnRequisitos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("90-prenda-en-requisitos.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restituye el paso al final del recorrido. No se intenta devolverlo a su posición original
    /// (era el 4 en uno y el 4 en otro, con vecinos distintos): reconstruir esa numeración exigiría
    /// recordar el recorrido previo de cada tipo, y el orden exacto de un paso que la reversión
    /// existe para descartar no justifica esa complejidad.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            WITH destino AS (
                SELECT pt.id AS type_id,
                       coalesce(max(st.sort_order), 0) + 1 AS siguiente
                  FROM tramites.procedure_types pt
                  LEFT JOIN tramites.procedure_steps st ON st.procedure_type_id = pt.id
                 WHERE pt.code IN ('LEVANTAMIENTO_PRENDA', 'PRENDA_INSCRIPCION')
                 GROUP BY pt.id
            ), nuevo AS (
                INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
                SELECT uuidv7(), d.type_id, 'prenda', 'Decisión de prenda', d.siguiente::smallint, true
                  FROM destino d
                 WHERE NOT EXISTS (
                     SELECT 1 FROM tramites.procedure_steps st
                      WHERE st.procedure_type_id = d.type_id AND st.code = 'prenda')
                RETURNING id
            )
            INSERT INTO tramites.procedure_sections
                (id, procedure_step_id, code, title, sort_order, layout, section_type)
            SELECT uuidv7(), n.id, 'prenda', 'Decisión de prenda', 1, 'single', 'prenda_decision'
              FROM nuevo n;
            """);
}
