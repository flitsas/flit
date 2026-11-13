using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora ICT/QA (sin HU) — copia de la firma de baúl al radicar: columna <c>firma_baul_copiada_at</c> en
/// <c>tramites.procedure_instances</c> y tabla <c>tramites.procedure_instance_signature_vault_copies</c>.
/// DDL: <c>195-firma-baul-copia-al-radicar.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261111120000_FirmaBaulCopiaAlRadicar")]
public partial class FirmaBaulCopiaAlRadicar : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("195-firma-baul-copia-al-radicar.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS tramites.procedure_instance_signature_vault_copies;
            ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS firma_baul_copiada_at;
            """);
}
