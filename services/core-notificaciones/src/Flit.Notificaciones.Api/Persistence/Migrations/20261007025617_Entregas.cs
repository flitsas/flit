using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Notificaciones.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Entregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entregas",
                schema: "notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plantilla = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    canal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    destinatario = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    desenlace = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo_fallo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duracion_ms = table.Column<int>(type: "integer", nullable: false),
                    desviado = table.Column<bool>(type: "boolean", nullable: false),
                    tema = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    tema_version = table.Column<int>(type: "integer", nullable: true),
                    remitente_nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trabajo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entregas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_empresa_fecha",
                schema: "notificaciones",
                table: "entregas",
                columns: new[] { "tenant_id", "ocurrido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entregas",
                schema: "notificaciones");
        }
    }
}
