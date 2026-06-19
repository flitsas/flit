using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10203_UserDocumentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HU #10203 — identificador alterno (número de documento) para "recordar usuario".
            // Las tablas del schema admin (HU #10189+) se gestionan por su propio mecanismo;
            // esta migración solo añade la columna nueva.
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
