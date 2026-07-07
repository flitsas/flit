using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10545 (Feature #10535) — requisitos configurables por OT (admin.ot_requirements).
    /// El DDL embebido (23-HU10545-ot-requirements.sql) crea la tabla con FKs, RLS y triggers
    /// (row_version + audit), que el MigrationBuilder no modela. El snapshot/Designer reconcilian
    /// el modelo EF. Mismo patrón que N03_ProcedureStateChangeOutbox / HU10152_OtAdmin.
    /// </remarks>
    public partial class HU10545_OtRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("23-HU10545-ot-requirements.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.ot_requirements CASCADE;");
        }
    }
}
