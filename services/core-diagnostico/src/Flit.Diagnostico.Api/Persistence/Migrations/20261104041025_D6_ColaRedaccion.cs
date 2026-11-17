using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D6_ColaRedaccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "redaccion_intentos",
                schema: "diagnostico",
                table: "dictamenes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Intentos de la cola de redacción. La toma entre empresas los suma.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0,
                oldComment: "Intentos de la cola de redacción. El índice entre empresas no se crea en este corte.");

            migrationBuilder.CreateIndex(
                name: "ix_redaccion_pendiente",
                schema: "diagnostico",
                table: "dictamenes",
                column: "redaccion_disponible_desde",
                filter: "estado_redaccion = 'PENDIENTE' AND vigente");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_redaccion_pendiente",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.AlterColumn<int>(
                name: "redaccion_intentos",
                schema: "diagnostico",
                table: "dictamenes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Intentos de la cola de redacción. El índice entre empresas no se crea en este corte.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0,
                oldComment: "Intentos de la cola de redacción. La toma entre empresas los suma.");
        }
    }
}
