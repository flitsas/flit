using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 — parametrización Quipux de MATRICULA_LEASING y TRASPASO_UNILATERAL, que en FLIT 1.0
/// eran una casilla dentro del trámite canónico y ahora son tipos propios del catálogo.
/// DDL: <c>83-quipux-refs-variantes-propias.sql</c>.
/// <para>Sin bloque <c>quipux</c> propio, esos dos tipos quedan no elegibles y sus trámites no se
/// radican. Los códigos son los que la <c>variante</c> del seed 33 ya declaraba (13/MIL, 213/TRU);
/// no se inventa ninguno.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824120000_QuipuxRefsVariantesPropias")]
public partial class QuipuxRefsVariantesPropias : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("83-quipux-refs-variantes-propias.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira el bloque <c>quipux</c> de los dos tipos, que es lo que había antes: ninguno estaba
    /// parametrizado. El resto de <c>external_refs</c> queda intacto.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET external_refs = external_refs - 'quipux', updated_at = now()
             WHERE code IN ('MATRICULA_LEASING', 'TRASPASO_UNILATERAL');
            """);
}
