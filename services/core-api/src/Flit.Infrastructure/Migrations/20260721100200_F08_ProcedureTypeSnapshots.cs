using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 / Fase 2b — CREATE TABLE <c>tramites.procedure_type_snapshots</c> (CFD-01/AC#5).
    /// Snapshot liviano del tipo de trámite capturado al crear una instancia.
    /// Protege instancias en curso de cambios de configuración del tipo posteriores.
    /// Tiene <c>tenant_id</c> + RLS. Inmutable post-INSERT.
    /// DDL embebido: 37-F08-procedure-type-snapshots.sql. Down reversible.
    /// ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c> antes del merge.
    /// </remarks>
    public partial class F08_ProcedureTypeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("37-F08-procedure-type-snapshots.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropTable(
                name: "procedure_type_snapshots",
                schema: "tramites");
    }
}
