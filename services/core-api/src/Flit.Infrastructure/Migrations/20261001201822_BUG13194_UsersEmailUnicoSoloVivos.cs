using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BUG13194_UsersEmailUnicoSoloVivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true);
        }
    }
}
