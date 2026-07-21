using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10642 (Feature #10641, ADR-0025) — baúl de firmas (admin.signature_vault). El DDL
    /// embebido (32-HU10642-signature-vault.sql) crea la tabla con FK opcional al mandatario,
    /// índice único parcial (una firma 'activa' por (tenant, NIT, documento)), CHECKs (estado +
    /// vigencia), RLS por tenant y triggers (row_version + audit), que el MigrationBuilder no
    /// modela. La entidad está ExcludeFromMigrations (igual que ProcedureInstancePrenda): el
    /// snapshot la refleja para el modelo EF. Mismo patrón que HU10585_Prenda / HU10545_OtRequirements.
    /// </remarks>
    public partial class HU10642_SignatureVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("32-HU10642-signature-vault.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.signature_vault CASCADE;");
        }
    }
}
