using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <summary>
    /// GDC punto 20: tope de 3 intentos de la consulta del conductor, con un intento por día. La columna guarda el día del
    /// último intento. Los comparendos que ya están en Error con 3 intentos o más pasan a Agotada: el trabajo diario nunca
    /// los iba a volver a tomar y la gestora tiene que verlos como «asignar a mano». Idempotente: solo toca esa combinación.
    /// </summary>
    public partial class GDC_TopeIntentosConsultaContraventor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ultimo_intento_consulta_contraventor",
                schema: "comparendos",
                table: "comparendo",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE comparendos.comparendo
                   SET estado_consulta_contraventor = 'Agotada'
                 WHERE estado_consulta_contraventor = 'Error'
                   AND intentos_consulta_contraventor >= 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El código anterior no conoce Agotada ni SinHora: vuelven a Error y a Pendiente.
            migrationBuilder.Sql("""
                UPDATE comparendos.comparendo SET estado_consulta_contraventor = 'Error' WHERE estado_consulta_contraventor = 'Agotada';
                UPDATE comparendos.comparendo SET estado_consulta_contraventor = 'Pendiente' WHERE estado_consulta_contraventor = 'SinHora';
                """);

            migrationBuilder.DropColumn(
                name: "ultimo_intento_consulta_contraventor",
                schema: "comparendos",
                table: "comparendo");
        }
    }
}
