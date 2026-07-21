using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 / HU-BE-07 (CFD-10) — Seeds de los 4 tipos de referencia como configuraciones
    /// completas: <c>MATRICULA_INICIAL</c>, <c>TRASPASO_SIMPLE</c>, <c>PRENDA_INSCRIPCION</c> y
    /// <c>CAMBIO_LOCATARIO</c> (gate_profile + steps/sections con section_type + conformation_rules +
    /// procedure_type_sources + document_requirements). Idempotente. DDL embebido:
    /// 38-F08-seeds-tipos-configurados.sql. El flag F08_DynamicProcedures se activa por tenant en DEV
    /// (fuera de esta migración). ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c>.
    /// </remarks>
    public partial class F08_SeedTiposConfigurados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("38-F08-seeds-tipos-configurados.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DELETE FROM tramites.procedure_types
                WHERE code IN ('MATRICULA_INICIAL', 'TRASPASO_SIMPLE', 'PRENDA_INSCRIPCION', 'CAMBIO_LOCATARIO');
                """);
    }
}
