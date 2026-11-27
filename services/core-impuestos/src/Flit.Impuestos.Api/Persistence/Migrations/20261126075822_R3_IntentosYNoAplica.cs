using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Impuestos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R3_IntentosYNoAplica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidacion_items_estado",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.AddColumn<int>(
                name: "c_no_aplica",
                schema: "impuestos",
                table: "liquidaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "posible_duplicado",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Solo filas anteriores a la 1.3.0: desde entonces relanzar abre un nuevo intento del mismo ítem (R3) y no se marca.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "numero_intento",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Intento en curso (1..n). Los cerrados están en liquidacion_item_intentos (R3).");

            migrationBuilder.CreateTable(
                name: "liquidacion_item_intentos",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    error_clase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    error_mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    campo_faltante = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    total_a_pagar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, comment: "NULL no es 0 (F2-CF14)."),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    relanzado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidacion_item_intentos", x => x.id);
                    table.CheckConstraint("ck_liquidacion_item_intentos_error_clase", "error_clase IS NULL OR error_clase IN ('CORREGIR_DATOS', 'DEPENDE', 'REINTENTABLE')");
                    table.CheckConstraint("ck_liquidacion_item_intentos_estado", "estado IN ('PENDIENTE', 'CONSULTANDO_RUNT', 'REINTENTO_PROGRAMADO', 'ENVIANDO', 'ENVIADO', 'INCIERTO', 'INCIERTO_REQUIERE_DECISION', 'DATOS_INCOMPLETOS', 'DEPARTAMENTO_NO_HABILITADO', 'EXITOSO', 'ERROR', 'SIN_RESULTADO_LOTE_CERRADO', 'NO_APLICA')");
                    table.CheckConstraint("ck_liquidacion_item_intentos_numero", "numero >= 1");
                    table.ForeignKey(
                        name: "fk_liquidacion_item_intentos_liquidacion_items",
                        column: x => x.item_id,
                        principalSchema: "impuestos",
                        principalTable: "liquidacion_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Intentos cerrados de cada ítem al relanzarlo (R3). Sin PII del propietario ni resultado cifrado.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidacion_items_estado",
                schema: "impuestos",
                table: "liquidacion_items",
                sql: "estado IN ('PENDIENTE', 'CONSULTANDO_RUNT', 'REINTENTO_PROGRAMADO', 'ENVIANDO', 'ENVIADO', 'INCIERTO', 'INCIERTO_REQUIERE_DECISION', 'DATOS_INCOMPLETOS', 'DEPARTAMENTO_NO_HABILITADO', 'EXITOSO', 'ERROR', 'SIN_RESULTADO_LOTE_CERRADO', 'NO_APLICA')");

            migrationBuilder.CreateIndex(
                name: "ux_liquidacion_item_intentos_item_numero",
                schema: "impuestos",
                table: "liquidacion_item_intentos",
                columns: new[] { "item_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "liquidacion_item_intentos",
                schema: "impuestos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidacion_items_estado",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.DropColumn(
                name: "c_no_aplica",
                schema: "impuestos",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "numero_intento",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.AlterColumn<bool>(
                name: "posible_duplicado",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Solo filas anteriores a la 1.3.0: desde entonces relanzar abre un nuevo intento del mismo ítem (R3) y no se marca.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidacion_items_estado",
                schema: "impuestos",
                table: "liquidacion_items",
                sql: "estado IN ('PENDIENTE', 'CONSULTANDO_RUNT', 'REINTENTO_PROGRAMADO', 'ENVIANDO', 'ENVIADO', 'INCIERTO', 'INCIERTO_REQUIERE_DECISION', 'DATOS_INCOMPLETOS', 'DEPARTAMENTO_NO_HABILITADO', 'EXITOSO', 'ERROR', 'SIN_RESULTADO_LOTE_CERRADO')");
        }
    }
}
