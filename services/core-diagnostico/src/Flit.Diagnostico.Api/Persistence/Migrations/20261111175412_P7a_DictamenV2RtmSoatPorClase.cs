using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P7a_DictamenV2RtmSoatPorClase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8807ebfe-94de-55f7-b0ea-2a75f1d3cc92"),
                column: "plantilla_texto",
                value: "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.");

            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f28ca1ae-7c69-51b6-9c93-f3833e4cd90f"),
                column: "plantilla_texto",
                value: "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.");

            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f723f921-6e82-55c1-82b9-5e51234d0472"),
                columns: new[] { "efecto", "plantilla_texto" },
                values: new object[] { "ADVIERTE", "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares). En el traspaso unilateral es una advertencia y el trámite sigue viable." });

            migrationBuilder.InsertData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                columns: new[] { "id", "catalogo_version", "codigo", "condicion", "efecto", "plantilla_texto", "tipo_tramite_codigo", "vigente" },
                values: new object[,]
                {
                    { new Guid("0573e03c-6239-58b0-ae95-878bc7e16270"), 2, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("09390584-97f3-56e9-b3e2-3165d74243b9"), 2, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_STANDARD", true },
                    { new Guid("40e1d86e-a45b-54b1-8307-c817c5f3d2b0"), 2, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_UNILATERAL", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("0573e03c-6239-58b0-ae95-878bc7e16270"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("09390584-97f3-56e9-b3e2-3165d74243b9"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("40e1d86e-a45b-54b1-8307-c817c5f3d2b0"));

            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8807ebfe-94de-55f7-b0ea-2a75f1d3cc92"),
                column: "plantilla_texto",
                value: "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.");

            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f28ca1ae-7c69-51b6-9c93-f3833e4cd90f"),
                column: "plantilla_texto",
                value: "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.");

            migrationBuilder.UpdateData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f723f921-6e82-55c1-82b9-5e51234d0472"),
                columns: new[] { "efecto", "plantilla_texto" },
                values: new object[] { "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado." });
        }
    }
}
