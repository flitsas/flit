using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10147_Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("03-HU10147-invitations.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(Phase1DdlDown.Hu10147);
    }
}
