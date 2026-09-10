using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11302 (Feature #11301) — modelo canónico de certificaciones externas persistido: payload crudo
/// sanitizado, histórico de pólizas de SOAT, histórico de revisiones técnico-mecánicas y registro
/// mercantil por NIT. Materializa ADR-0041.
/// </summary>
/// <remarks>
/// El DDL va en crudo, como el resto de <c>tramites.*</c>, porque lleva índices únicos PARCIALES (una
/// sola fila <c>is_current</c> por trámite), CHECKs de vocabulario cerrado, RLS y los triggers de
/// <c>row_version</c> y bitácora. Nada de eso sabe expresarlo el generador de EF. Las entidades van
/// con <c>ExcludeFromMigrations()</c>, igual que <c>external_query_cache</c>. Detalle comentado en
/// <c>Persistence/Sql/Ddl/59-certificaciones-externas.sql</c>.
///
/// <para><b>Despliegue neutro</b>: las cuatro tablas nacen vacías y el lector documental cae al
/// camino anterior mientras no haya filas. Cero cambio visible hasta que empiecen a poblar las
/// consultas nuevas. El <c>Down</c> las elimina y deja el sistema exactamente como estaba, porque
/// <c>field_values</c> se sigue escribiendo en esta fase.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260808100000_CertificacionesExternas")]
public partial class CertificacionesExternas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("59-certificaciones-externas.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Orden inverso a la creación: las tres tablas de certificación referencian el payload.
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS tramites.company_registrations;
            DROP TABLE IF EXISTS tramites.vehicle_rtm_inspections;
            DROP TABLE IF EXISTS tramites.vehicle_soat_policies;
            DROP TABLE IF EXISTS tramites.external_query_payloads;
            """);
    }
}
