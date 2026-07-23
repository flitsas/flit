using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 / Fase 2b — CREATE TABLE <c>tramites.procedure_type_sources</c> (CFD-04).
    /// Fuentes de datos externas habilitadas por tipo de trámite.
    /// Catálogo global sin <c>tenant_id</c>: excepción A4/A20 documentada en ADR-0019.
    /// PK compuesta (procedure_type_id, external_data_source_id).
    /// DDL embebido: 36-F08-procedure-type-sources.sql. Down reversible.
    /// ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c> antes del merge.
    /// </remarks>
    // Atributos inline (patrón HU10774): sin ellos EF no descubre la migración y el CREATE TABLE
    // nunca corre en DEV. Ver F08_ConformationProfile para el detalle.
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260721100100_F08_ProcedureTypeSources")]
    public partial class F08_ProcedureTypeSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("36-F08-procedure-type-sources.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropTable(
                name: "procedure_type_sources",
                schema: "tramites");
    }
}
