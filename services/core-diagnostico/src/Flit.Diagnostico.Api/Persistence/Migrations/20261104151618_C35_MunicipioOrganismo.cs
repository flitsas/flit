using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C35_MunicipioOrganismo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "municipio",
                schema: "diagnostico",
                table: "organismos",
                type: "text",
                nullable: true,
                comment: "Municipio del organismo, de catalogos.proto. Lo exige la consulta de impuesto.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "municipio",
                schema: "diagnostico",
                table: "organismos");
        }
    }
}
