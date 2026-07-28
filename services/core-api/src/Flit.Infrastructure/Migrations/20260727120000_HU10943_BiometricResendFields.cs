using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10943 (Feature #10864, CF-03) — Editar prevalidación y reenviar validación de identidad.
    ///
    /// El DDL embebido (42-HU10943-biometric-resend.sql) agrega a
    /// <c>tramites.procedure_instance_biometric_validations</c>:
    /// - <c>resend_count</c> int NOT NULL DEFAULT 0 (D10: tope de 3 reenvíos).
    /// - <c>last_resent_at</c> timestamptz NULL (D10: cooldown de 5 minutos entre reenvíos).
    ///
    /// Mismo patrón que HU10865/HU10900: entidad gestionada por EF, DDL vía SQL crudo
    /// (<c>migrationBuilder.Sql</c>), con atributos inline <c>[DbContext]</c>/<c>[Migration]</c>
    /// (sin <c>.Designer.cs</c>) — la migración SÍ corre al arrancar porque el runtime la descubre por
    /// estos atributos, no por el snapshot.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260727120000_HU10943_BiometricResendFields")]
    public partial class HU10943_BiometricResendFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("42-HU10943-biometric-resend.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP COLUMN IF EXISTS resend_count,
                    DROP COLUMN IF EXISTS last_resent_at;
                """);
        }
    }
}
