using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Declara que <c>TRASLADO_CUENTA</c> captura un organismo de DESTINO además del suyo. Su organismo
/// sigue siendo el del RUNT —el traslado lo expide el de origen, que valida el paz y salvo—; el
/// destino solo se declara para el párrafo 23 del FUR. Espejo del radicado (DDL 92), donde el destino
/// ES el organismo del trámite. DDL: <c>93-traslado-cuenta-secretaria-destino.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825190000_TrasladoCuentaSecretariaDestino")]
public partial class TrasladoCuentaSecretariaDestino : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("93-traslado-cuenta-secretaria-destino.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira la llave. Los <c>transit_office_destino_*</c> ya capturados se conservan: son datos del
    /// expediente, y borrarlos perdería el destino que el gestor declaró en trámites en curso. Sin la
    /// llave dejan de pedirse y de imprimirse, que es lo que la reversión busca.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET gate_profile = gate_profile - 'requiresDestinationTransitOffice',
                   updated_at = now()
             WHERE code = 'TRASLADO_CUENTA';
            """);
}
