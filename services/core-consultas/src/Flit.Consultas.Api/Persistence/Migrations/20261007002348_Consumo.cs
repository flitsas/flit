using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Consumo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumo",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fuente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proveedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    desde_cache = table.Column<bool>(type: "boolean", nullable: false),
                    latencia_ms = table.Column<int>(type: "integer", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumo_empresa_fecha",
                schema: "consultas",
                table: "consumo",
                columns: new[] { "tenant_id", "ocurrido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumo",
                schema: "consultas");
        }
    }
}
