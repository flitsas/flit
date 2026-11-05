using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D8_FuentesObligatorias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "definiciones_fuentes_obligatorias",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "UUID v5 en la semilla y UUID v7 si el PUT crea la cabecera. Sin DEFAULT uuidv7()."),
                    familia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Familia del trámite. Nula si la definición es de un tipo."),
                    tipo_tramite_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Código del tipo de trámite. Nulo si la definición es de una familia."),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante del último guardado (timestamptz). En la semilla, 2026-11-04T00:00:00Z."),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Seudónimo uuid del claim sub de quien guardó la definición; sin nombre, correo ni documento. NULL en la semilla."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definiciones_fuentes_obligatorias", x => x.id);
                    table.CheckConstraint("ck_definicion_un_alcance", "(familia IS NOT NULL AND tipo_tramite_codigo IS NULL AND familia ~ '^[A-Z][A-Z0-9_]{0,63}$') OR (familia IS NULL AND tipo_tramite_codigo IS NOT NULL AND tipo_tramite_codigo ~ '^[A-Z][A-Z0-9_]{0,63}$')");
                },
                comment: "Catálogo global de definiciones de fuentes obligatorias de FLIT (ADR-0078). Sin tenant_id: excepción de catálogo de ADR-0072. La cabecera existe o no existe; cero líneas es la lista vacía explícita.");

            migrationBuilder.CreateTable(
                name: "fuentes_definicion_obligatoria",
                schema: "diagnostico",
                columns: table => new
                {
                    definicion_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cabecera dueña de la línea."),
                    fuente = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Fuente configurable (enum Fuente menos RUNT).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuentes_definicion_obligatoria", x => new { x.definicion_id, x.fuente });
                    table.CheckConstraint("ck_fuentes_definicion_obligatoria_fuente", "fuente IN ('RUNT_PROPIETARIO', 'RUNT_LOCATARIO', 'SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'AVALUO', 'IMPUESTO_DEPARTAMENTAL')");
                    table.ForeignKey(
                        name: "fk_fuentes_definicion_obligatoria_definicion",
                        column: x => x.definicion_id,
                        principalSchema: "diagnostico",
                        principalTable: "definiciones_fuentes_obligatorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Fuentes de una definición obligatoria (ADR-0078). Sin tenant_id: excepción de catálogo de ADR-0072. Cero filas de una cabecera es la lista vacía explícita. RUNT no se guarda.");

            migrationBuilder.InsertData(
                schema: "diagnostico",
                table: "definiciones_fuentes_obligatorias",
                columns: new[] { "id", "actualizado_en", "actualizado_por", "familia", "tipo_tramite_codigo" },
                values: new object[,]
                {
                    { new Guid("1e9d3f5a-5fcb-57f8-8c24-302e5d8c5de7"), new DateTimeOffset(new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "TRASPASO_STANDARD" },
                    { new Guid("5ecf004e-e332-5ba7-9523-51014ec4cf3c"), new DateTimeOffset(new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "TRASPASO_TRANSFERENCIA_DE_DOMINIO" },
                    { new Guid("adb98fbb-4f43-5e77-8735-135635b678ca"), new DateTimeOffset(new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "TRASPASO", null },
                    { new Guid("caaaba1c-e41d-5981-b2b5-b140b0983789"), new DateTimeOffset(new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "TRASPASO_UNILATERAL" }
                });

            migrationBuilder.InsertData(
                schema: "diagnostico",
                table: "fuentes_definicion_obligatoria",
                columns: new[] { "definicion_id", "fuente" },
                values: new object[,]
                {
                    { new Guid("1e9d3f5a-5fcb-57f8-8c24-302e5d8c5de7"), "SIMIT_PROPIETARIO" },
                    { new Guid("5ecf004e-e332-5ba7-9523-51014ec4cf3c"), "SIMIT_PROPIETARIO" },
                    { new Guid("adb98fbb-4f43-5e77-8735-135635b678ca"), "SIMIT_PROPIETARIO" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_definicion_familia",
                schema: "diagnostico",
                table: "definiciones_fuentes_obligatorias",
                column: "familia",
                unique: true,
                filter: "familia IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_definicion_tipo",
                schema: "diagnostico",
                table: "definiciones_fuentes_obligatorias",
                column: "tipo_tramite_codigo",
                unique: true,
                filter: "tipo_tramite_codigo IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuentes_definicion_obligatoria",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "definiciones_fuentes_obligatorias",
                schema: "diagnostico");
        }
    }
}
