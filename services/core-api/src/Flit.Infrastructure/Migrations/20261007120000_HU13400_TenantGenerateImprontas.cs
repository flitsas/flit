using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13400 (Feature #13398, Épica #13039) — parámetro por compañía que habilita o deshabilita
    /// la generación automática de improntas: columna <c>generate_improntas</c> en
    /// <c>admin.tenant_operational_policies</c> (boolean NOT NULL DEFAULT true; las filas existentes
    /// quedan habilitadas). ALTER idempotente, mismo patrón que HU12250.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20261007120000_HU13400_TenantGenerateImprontas")]
    public partial class HU13400_TenantGenerateImprontas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS generate_improntas boolean NOT NULL DEFAULT true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS generate_improntas;
                """);
        }
    }
}
