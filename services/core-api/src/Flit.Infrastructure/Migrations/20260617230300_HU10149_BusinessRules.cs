using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10149_BusinessRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("05-HU10149-business-rules.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Phase1DdlDown.Hu10149);
    }
}
