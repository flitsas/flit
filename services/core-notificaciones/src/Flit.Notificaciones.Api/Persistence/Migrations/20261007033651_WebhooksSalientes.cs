using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Notificaciones.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhooksSalientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhooks",
                schema: "notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo_http = table.Column<int>(type: "integer", nullable: true),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duracion_ms = table.Column<int>(type: "integer", nullable: false),
                    correlacion_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    trabajo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhooks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_empresa_fecha",
                schema: "notificaciones",
                table: "webhooks",
                columns: new[] { "tenant_id", "ocurrido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhooks",
                schema: "notificaciones");
        }
    }
}
