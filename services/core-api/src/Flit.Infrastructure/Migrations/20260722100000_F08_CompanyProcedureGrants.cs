using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 — CREATE TABLE <c>admin.company_procedure_type_grants</c>. Habilitación de tipos de
    /// trámite por compañía (grant model): fila = habilitado. El selector del operador filtra por estos
    /// grants. Tiene <c>tenant_id</c> + RLS. DDL embebido: 39-F08-company-procedure-grants.sql. Down reversible.
    /// ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c> antes del merge.
    /// </remarks>
    public partial class F08_CompanyProcedureGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-F08-company-procedure-grants.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropTable(
                name: "company_procedure_type_grants",
                schema: "admin");
    }
}
