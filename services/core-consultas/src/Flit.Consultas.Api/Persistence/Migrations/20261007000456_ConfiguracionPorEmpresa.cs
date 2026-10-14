using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracionPorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuracion_empresa",
                schema: "consultas",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cadenas = table.Column<string>(type: "jsonb", nullable: true),
                    failover_timeout_ms = table.Column<int>(type: "integer", nullable: true),
                    fuente_multas = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    avaluos = table.Column<string>(type: "jsonb", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_empresa", x => x.tenant_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracion_empresa",
                schema: "consultas");
        }
    }
}
