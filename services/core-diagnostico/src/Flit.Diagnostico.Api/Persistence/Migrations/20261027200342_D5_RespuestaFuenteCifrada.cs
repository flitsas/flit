using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D5_RespuestaFuenteCifrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "respuesta_fuente_cifrada",
                schema: "diagnostico",
                table: "consultas",
                type: "bytea",
                nullable: true,
                comment: "@pii:high Respuesta completa de la fuente (proto-JSON del bus; en las fuentes de persona, objeto por orden) cifrada con Data Protection, propósito Flit.Diagnostico.RespuestaFuente.v1 (HU #14087 AC8). Null sin respuesta de la fuente. Solo la lee el detalle de la celda.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "respuesta_fuente_cifrada",
                schema: "diagnostico",
                table: "consultas");
        }
    }
}
