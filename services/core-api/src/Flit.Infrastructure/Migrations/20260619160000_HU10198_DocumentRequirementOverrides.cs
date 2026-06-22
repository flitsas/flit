using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #10198 — crea <c>tramites.document_requirement_overrides</c>: la obligatoriedad
    /// documental por Organismo de Tránsito (3 estados: REQUIRED / OPTIONAL / NOT_APPLICABLE).
    /// Esquema aplicado vía DDL crudo embebido, igual que el resto del linaje documental
    /// (HU #10155). Migración hand-authored: las entidades admin se mantienen fuera del
    /// snapshot EF (se aplican por SQL), por eso los atributos van inline y no hay
    /// <c>BuildTargetModel</c> — el snapshot autoritativo sigue siendo
    /// <c>FlitDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260619160000_HU10198_DocumentRequirementOverrides")]
    public partial class HU10198_DocumentRequirementOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("12-HU10198-document-requirement-overrides.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS tramites.document_requirement_overrides CASCADE;");
    }
}
