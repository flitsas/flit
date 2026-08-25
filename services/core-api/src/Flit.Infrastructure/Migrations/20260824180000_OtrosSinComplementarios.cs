using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 — declara en el <c>gate_profile</c> que la familia OTROS no acumula trámites
/// complementarios, y alinea los recorridos de <c>PRENDA_INSCRIPCION</c> y <c>CAMBIO_LOCATARIO</c>
/// con la regla «un titular = el propietario inscrito». DDL: <c>87-otros-sin-complementarios.sql</c>.
/// <para>Matrícula y traspaso NO se tocan: conservan prenda y transformaciones adicionales del
/// art. 5.1.8, y el propio DDL aborta si alguna quedara marcada.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824180000_OtrosSinComplementarios")]
public partial class OtrosSinComplementarios : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("87-otros-sin-complementarios.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Solo retira las dos llaves. Sin ellas el perfil vuelve a resolverse por familia, que da el
    /// MISMO resultado —OTROS sigue sin complementarios— porque la regla vive también en el dominio.
    /// Los recorridos reescritos no se revierten: el del seed 38 dejaba tipos sin titular exigido y
    /// restaurarlo sería reintroducir el defecto, no deshacer un cambio.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET gate_profile = gate_profile
                                  - 'allowsComplementaryTransformations'
                                  - 'allowsComplementaryPrenda',
                   updated_at = now()
             WHERE family = 'OTROS';
            """);
}
