using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <summary>
    /// GDC-EVID §10: «Asociar los N» en segundo plano. Dos tablas nuevas (el lote y sus documentos, que son la cola del trabajador)
    /// y el contador de documentos por asociar del paquete. Reversible: el Down devuelve a Duplicado los que estuvieran PorAsociar.
    /// </summary>
    public partial class GDC_AsociacionLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cantidad_por_asociar",
                schema: "comparendos",
                table: "paquete_ocr",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "asociacion_lote",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    solicitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asociacion_lote", x => x.id);
                    table.ForeignKey(
                        name: "fk_asociacion_lote_paquete_ocr_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "comparendos",
                        principalTable: "paquete_ocr",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asociacion_lote_documento",
                schema: "comparendos",
                columns: table => new
                {
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    correo_estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    correo_motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<short>(type: "smallint", nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asociacion_lote_documento", x => new { x.lote_id, x.documento_id });
                    table.ForeignKey(
                        name: "fk_asociacion_lote_documento_asociacion_lote_lote_id",
                        column: x => x.lote_id,
                        principalSchema: "comparendos",
                        principalTable: "asociacion_lote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asociacion_lote_documento_documento_ocr_documento_id",
                        column: x => x.documento_id,
                        principalSchema: "comparendos",
                        principalTable: "documento_ocr",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asociacion_lote_paquete_id_solicitado_en",
                schema: "comparendos",
                table: "asociacion_lote",
                columns: new[] { "paquete_id", "solicitado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_asociacion_lote_documento_cola",
                schema: "comparendos",
                table: "asociacion_lote_documento",
                column: "proximo_intento_en",
                filter: "resultado = 'PorAsociar'");

            migrationBuilder.CreateIndex(
                name: "ix_asociacion_lote_documento_documento_id",
                schema: "comparendos",
                table: "asociacion_lote_documento",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ux_asociacion_lote_documento_activo",
                schema: "comparendos",
                table: "asociacion_lote_documento",
                column: "documento_id",
                unique: true,
                filter: "resultado = 'PorAsociar'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // GDC-EVID §10: el código anterior no conoce el estado PorAsociar (fallaría al leer el enum): vuelven a Duplicado.
            migrationBuilder.Sql("UPDATE comparendos.documento_ocr SET estado = 'Duplicado', proximo_intento_en = NULL WHERE estado = 'PorAsociar';");
            migrationBuilder.Sql("UPDATE comparendos.paquete_ocr SET cantidad_duplicados = cantidad_duplicados + cantidad_por_asociar WHERE cantidad_por_asociar > 0;");

            migrationBuilder.DropTable(
                name: "asociacion_lote_documento",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "asociacion_lote",
                schema: "comparendos");

            migrationBuilder.DropColumn(
                name: "cantidad_por_asociar",
                schema: "comparendos",
                table: "paquete_ocr");
        }
    }
}
