using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10155_Documental : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("08-HU10155-documental.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Phase1DdlDown.Hu10155);
    }
}
