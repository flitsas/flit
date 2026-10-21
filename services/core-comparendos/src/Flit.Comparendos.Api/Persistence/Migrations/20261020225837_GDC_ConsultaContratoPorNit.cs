using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_ConsultaContratoPorNit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "consulta_contrato_api",
                schema: "comparendos",
                table: "identificacion_monitoreada",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consulta_contrato_api",
                schema: "comparendos",
                table: "identificacion_monitoreada");
        }
    }
}
