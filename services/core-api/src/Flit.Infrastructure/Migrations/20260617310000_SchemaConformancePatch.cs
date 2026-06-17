using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchemaConformancePatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("11-schema-conformance-patch.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SchemaConformancePatchSql.Down);
    }
}
