using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11308 (Feature #11301) — traslada al modelo canónico las certificaciones que ya estaban
/// guardadas en <c>field_values</c> y en el snapshot congelado del RUES.
/// </summary>
/// <remarks>
/// <b>Solo traslada</b> (D7): si una celda estaba vacía, sigue vacía. No consulta a ningún proveedor
/// y no repara nada — el fix aplica hacia adelante. Se hace igual porque no cuesta nada y deja a los
/// trámites vivos leyendo por el mismo camino que los nuevos, en vez de por el respaldo.
///
/// <para>Escribe en tablas NUEVAS, así que no dispara el trigger de inmutabilidad de
/// <c>field_values</c> y no hace falta <c>DISABLE TRIGGER USER</c>. Es idempotente
/// (<c>ON CONFLICT DO NOTHING</c> sobre la llave natural) y las filas se marcan con procedencia
/// <c>system</c>/<c>legacy</c>, la más débil, para que una consulta real siempre pueda mejorarlas.</para>
///
/// <para>El <c>Down</c> borra exactamente lo que este traslado insertó —las filas <c>legacy</c>— y
/// nada más: lo que haya escrito una consulta real se conserva.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260808110000_BackfillCertificaciones")]
public partial class BackfillCertificaciones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("60-backfill-certificaciones.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM tramites.vehicle_soat_policies    WHERE provider_key = 'legacy';
            DELETE FROM tramites.vehicle_rtm_inspections  WHERE provider_key = 'legacy';
            DELETE FROM tramites.company_registrations    WHERE provider_key = 'legacy';
            DROP FUNCTION IF EXISTS tramites.fn_backfill_parse_fecha(text);
            DROP FUNCTION IF EXISTS tramites.fn_backfill_vigencia(text);
            """);
    }
}
