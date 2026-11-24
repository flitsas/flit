using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_ConsultasPersonalizadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consulta_guardada",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    de_fabrica = table.Column<bool>(type: "boolean", nullable: false),
                    definicion = table.Column<string>(type: "jsonb", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consulta_guardada", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consulta_programada",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consulta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    periodicidad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    dia_semana = table.Column<int>(type: "integer", nullable: true),
                    dia_mes = table.Column<int>(type: "integer", nullable: true),
                    hora = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    destinatarios = table.Column<List<string>>(type: "text[]", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    ultima_ejecucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proxima_ejecucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_resultado = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consulta_programada", x => x.id);
                    table.ForeignKey(
                        name: "fk_consulta_programada_consulta_guardada_consulta_id",
                        column: x => x.consulta_id,
                        principalSchema: "comparendos",
                        principalTable: "consulta_guardada",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consulta_guardada_tenant_id_usuario_id",
                schema: "comparendos",
                table: "consulta_guardada",
                columns: new[] { "tenant_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_consulta_programada_activa_proxima_ejecucion",
                schema: "comparendos",
                table: "consulta_programada",
                columns: new[] { "activa", "proxima_ejecucion" });

            migrationBuilder.CreateIndex(
                name: "ix_consulta_programada_consulta_id",
                schema: "comparendos",
                table: "consulta_programada",
                column: "consulta_id");

            migrationBuilder.CreateIndex(
                name: "ix_consulta_programada_tenant_id",
                schema: "comparendos",
                table: "consulta_programada",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consulta_programada",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "consulta_guardada",
                schema: "comparendos");
        }
    }
}
