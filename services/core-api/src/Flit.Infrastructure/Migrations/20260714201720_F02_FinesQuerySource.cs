using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE 02 — fuente de comparendos: columna aditiva <c>fines_query_source</c>
    /// (internal | external, default 'external') + CHECK en admin.tenant_operational_policies.
    /// El DDL embebido (32-F02-fines-query-source.sql) es idempotente (ADD COLUMN IF NOT EXISTS +
    /// guard del constraint); el snapshot/Designer reconcilian el modelo EF. Mismo patrón que
    /// ADR0024_AuditHardening.
    /// </remarks>
    public partial class F02_FinesQuerySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("32-F02-fines-query-source.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fines_query_source",
                schema: "admin",
                table: "tenant_operational_policies");
        }
    }
}
