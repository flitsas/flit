using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Los tipos generados/apalancados (SOAT, RTM, cédulas, más los ya marcados) dejan de pedirse
/// en Requisitos y de bloquear radicación. Siguen asociados para el consolidado.
/// DDL: <c>94-gestor-carga-excluye-generados.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826230000_GestorCargaExcluyeGenerados")]
public partial class GestorCargaExcluyeGenerados : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("94-gestor-carga-excluye-generados.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.document_types
               SET is_system_generated = false,
                   generated_sort_order = NULL,
                   updated_at = now()
             WHERE code IN ('soat', 'rtm', 'cedulas');

            COMMENT ON COLUMN tramites.document_types.is_system_generated
              IS 'HU #11181 — el documento lo produce FLIT (FUR, certificados, mandato, escrituras). Entra en la lista ordenable del OT; NO implica exclusión del checklist del gestor.';
            """);
}
