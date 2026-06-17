using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10148_Rbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("02-HU10148-rbac.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Phase1DdlDown.Hu10148);
    }
}
