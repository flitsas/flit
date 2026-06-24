using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10146_IdentitySecurityAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SchemaBootstrap.Up);
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("01-HU10146-identity-security-auth.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Phase1DdlDown.Hu10146);
            migrationBuilder.Sql(SchemaBootstrap.Down);
        }
    }
}
