using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserDocumentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_document_number",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "document_number",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_number",
                schema: "identity",
                table: "users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_document_number",
                schema: "identity",
                table: "users",
                column: "document_number");
        }
    }
}
