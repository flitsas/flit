using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10912 (ADR-0036) — configuración de mandato por Organismo de Tránsito. El DDL embebido
    /// (41-HU10912-transit-office-mandate-config.sql) crea admin.transit_office_mandate_config (UNIQUE
    /// por transit_office_id, CHECK de template_code, FK al catálogo de OT, trigger row_version) y
    /// siembra Sabaneta/Bello. La entidad está ExcludeFromMigrations (mismo patrón que HU10642/HU10900):
    /// el DDL crudo lleva el esquema y el snapshot solo refleja el modelo EF.
    /// </remarks>
    public partial class HU10912_TransitOfficeMandateConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("41-HU10912-transit-office-mandate-config.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.transit_office_mandate_config CASCADE;");
    }
}
