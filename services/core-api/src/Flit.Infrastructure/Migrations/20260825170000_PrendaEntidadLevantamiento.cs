using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Añade <c>levantamiento_entidad</c> a la prenda: la entidad ante la que se extinguió el gravamen,
/// que el párrafo 23 del FUR declara en el trámite de levantamiento de prenda. El RUNT no la trae.
/// DDL: <c>91-prenda-entidad-levantamiento.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825170000_PrendaEntidadLevantamiento")]
public partial class PrendaEntidadLevantamiento : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("91-prenda-entidad-levantamiento.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE tramites.procedure_instance_prenda DROP COLUMN IF EXISTS levantamiento_entidad;");
}
