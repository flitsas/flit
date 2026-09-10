using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// La impronta deja de contar como documento generado por el sistema y vuelve al checklist de
/// carga del gestor: se puede adjuntar y —con <c>improntaSource</c> distinto de <c>MANUAL</c>—
/// generar desde su propia tarjeta. Sigue siendo requisito obligatorio del tipo y conserva su
/// orden en el consolidado. DDL: <c>95-impronta-carga-gestor.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260827120000_ImprontaCargaGestor")]
public partial class ImprontaCargaGestor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("95-impronta-carga-gestor.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.document_types
               SET is_system_generated = true,
                   generated_sort_order = COALESCE(generated_sort_order, 12::smallint),
                   updated_at = now()
             WHERE code = 'impronta';
            """);
}
