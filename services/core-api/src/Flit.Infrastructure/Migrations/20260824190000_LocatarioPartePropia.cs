using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// El arrendatario pasa a ser parte capturable: <c>requiresLessee</c> + paso propio en
/// <c>CAMBIO_LOCATARIO</c> y <c>MATRICULA_LEASING</c>. DDL: <c>88-locatario-parte-propia.sql</c>.
/// <para>Cierra un defecto silencioso: sin locatario real, <c>FurTramiteObservation</c> caía al
/// comprador y la matrícula por leasing emitía el FUR sin su observación obligatoria del párrafo 23.</para>
/// <para><c>TRASPASO_UNILATERAL</c> queda fuera: allí el adquirente ES el arrendatario.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824190000_LocatarioPartePropia")]
public partial class LocatarioPartePropia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("88-locatario-parte-propia.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira el paso y la capacidad. Los actores locatario ya persistidos NO se borran: son datos
    /// del expediente, no configuración, y el FUR vuelve solo a su fallback al comprador.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM tramites.procedure_steps ps
             USING tramites.procedure_types pt
             WHERE pt.id = ps.procedure_type_id
               AND ps.code = 'locatario'
               AND pt.code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING');

            UPDATE tramites.procedure_types
               SET gate_profile = gate_profile - 'requiresLessee',
                   updated_at = now()
             WHERE code IN ('CAMBIO_LOCATARIO', 'MATRICULA_LEASING');
            """);
}
