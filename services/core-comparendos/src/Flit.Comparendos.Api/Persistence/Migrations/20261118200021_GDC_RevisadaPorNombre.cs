using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_RevisadaPorNombre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revisada_juridicamente_por_nombre",
                schema: "comparendos",
                table: "plantilla_version",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revisada_juridicamente_por_nombre",
                schema: "comparendos",
                table: "plantilla_version");
        }
    }
}
