using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Declara que <c>CANCELACION_MATRICULA</c> no admite trámites complementarios —ni prenda ni
/// transformaciones—, aunque su familia (MATRICULAS) sí acumule: cancelar una matrícula y, sobre
/// ella, inscribir una limitación a la propiedad o declarar un cambio de color son contradicciones.
/// DDL: <c>93-cancelacion-sin-complementarios.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826140000_CancelacionSinComplementarios")]
public partial class CancelacionSinComplementarios : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("93-cancelacion-sin-complementarios.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Quita las llaves, con lo que el tipo vuelve a resolver por familia (la ausencia NO es
    /// <c>false</c>: es «lo que diga la familia», ver <c>ProcedureTypeGateProfile</c>).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET gate_profile = gate_profile
                                  - 'allowsComplementaryTransformations'
                                  - 'allowsComplementaryPrenda',
                   updated_at = now()
             WHERE code = 'CANCELACION_MATRICULA';
            """);
}
