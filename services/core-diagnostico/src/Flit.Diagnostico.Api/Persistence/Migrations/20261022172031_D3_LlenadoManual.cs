using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D3_LlenadoManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_consultas_origen_manual",
                schema: "diagnostico",
                table: "consultas",
                sql: "(COALESCE(origen, '') = 'MANUAL') = (usuario_manual_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consultas_origen_manual",
                schema: "diagnostico",
                table: "consultas");
        }
    }
}
