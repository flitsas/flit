using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D5_ConsultasPorBus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "republicaciones",
                schema: "diagnostico",
                table: "consultas",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                comment: "Republicaciones del reconciliador (ADR-0075, D3): 0 o 1 (ck_consultas_republicaciones); a la segunda huérfana la celda cierra en ERROR.");

            migrationBuilder.AddColumn<Guid>(
                name: "trabajo_id",
                schema: "diagnostico",
                table: "consultas",
                type: "uuid",
                nullable: true,
                comment: "Trabajo vigente de la consulta por RabbitMQ (ADR-0075): descarta trabajos y resultados viejos o duplicados y reconoce un resultado demorado; null sin trabajo publicado.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_consultas_republicaciones",
                schema: "diagnostico",
                table: "consultas",
                sql: "republicaciones BETWEEN 0 AND 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consultas_republicaciones",
                schema: "diagnostico",
                table: "consultas");

            migrationBuilder.DropColumn(
                name: "republicaciones",
                schema: "diagnostico",
                table: "consultas");

            migrationBuilder.DropColumn(
                name: "trabajo_id",
                schema: "diagnostico",
                table: "consultas");
        }
    }
}
