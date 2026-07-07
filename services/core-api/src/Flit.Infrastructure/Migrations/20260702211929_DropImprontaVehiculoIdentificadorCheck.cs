using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropImprontaVehiculoIdentificadorCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_impronta_generations_identificador_vehiculo",
                schema: "admin",
                table: "impronta_generations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_impronta_generations_identificador_vehiculo",
                schema: "admin",
                table: "impronta_generations",
                sql: "num_motor IS NOT NULL OR num_chasis IS NOT NULL OR num_serie IS NOT NULL");
        }
    }
}
