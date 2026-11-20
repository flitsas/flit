using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_CatalogoDictamenV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                columns: new[] { "id", "catalogo_version", "codigo", "condicion", "efecto", "plantilla_texto", "tipo_tramite_codigo", "vigente" },
                values: new object[,]
                {
                    { new Guid("05819e81-3811-537f-9b87-b3906e5118c6"), 2, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "ADVIERTE", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("07ef8030-c000-5a28-8a56-36b2678568fc"), 2, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("0f15e299-f7d0-55bc-a2f4-b14ea2d23852"), 2, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_STANDARD", true },
                    { new Guid("10edd35a-1116-51f1-90fb-e717a3204694"), 2, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("120c745d-6e51-50c4-86d2-0e50ea862222"), 2, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("151f02ce-0e95-589e-82ce-58e866b03415"), 2, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("19ed6bb7-022d-56e1-9ab5-a2b823c27951"), 2, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("358b3948-0ecf-5ed8-aa26-5c1431584e25"), 2, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_UNILATERAL", true },
                    { new Guid("3f31c25d-3c25-5577-bab6-7e4903050824"), 2, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_UNILATERAL", true },
                    { new Guid("40f48ba9-52b4-5e9d-8d5c-9280c92738ec"), 2, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_UNILATERAL", true },
                    { new Guid("41632168-d509-5154-96f3-7a26261b9c48"), 2, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("45e9c3e7-c3f8-5fb0-a736-768f11be8a54"), 2, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "ADVIERTE", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("46a35c3f-10b1-57bf-9667-b083ca0c21e1"), 2, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("524600ad-438f-58ba-a637-ebb2e0529381"), 2, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "BLOQUEA", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("5853b614-4621-5074-91e9-4a7810dcc2b0"), 2, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("65d39996-95e6-56e1-bf2f-33aa74b66976"), 2, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "BLOQUEA", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. El traspaso queda bloqueado hasta completar la consulta.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("66cbe0ff-91cc-581b-8c5c-e0e9cae1543e"), 2, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("78645093-9bfb-5ebd-947f-546cc85809da"), 2, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("7961f7d1-686f-5b94-9789-92a9b8952ae5"), 2, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "ADVIERTE", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("800a5ecd-fcb6-5e48-b0a8-84aeb262099b"), 2, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_UNILATERAL", true },
                    { new Guid("802f2eb0-4ac8-5ece-add1-0b089567d02a"), 2, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("8403e8eb-8fba-5b23-a448-2ddfe856adeb"), 2, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("87ec78c9-4dbe-580e-be33-8eeb6b034692"), 2, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("8807ebfe-94de-55f7-b0ea-2a75f1d3cc92"), 2, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("902a35c2-bbcd-534e-8912-6ae2f6082206"), 2, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_STANDARD", true },
                    { new Guid("9397c48a-0c22-507a-aafd-fdae7cf37c25"), 2, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "BLOQUEA", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_STANDARD", true },
                    { new Guid("9912f466-b596-5ae8-a5ac-e0f38eeff0bc"), 2, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("9f74b3be-4414-5718-b881-c0f70ddde30b"), 2, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "BLOQUEA", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. El traspaso queda bloqueado hasta completar la consulta.", "TRASPASO_STANDARD", true },
                    { new Guid("9fc7fdd2-ed27-5f24-a27a-6f1c9d6c87c4"), 2, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("a0bd4ecc-79f3-528f-bf95-085f6d41af92"), 2, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_STANDARD", true },
                    { new Guid("a2d2e03b-2e68-5b2d-84b0-84c695bfb7a6"), 2, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "ADVIERTE", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("a33870e5-846c-56dd-bbc9-85e2a174f012"), 2, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("a4c12367-b0d1-5c66-85ac-b4bd0e8ca0c6"), 2, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("c3d09a54-4ece-5bb5-ad31-d6a457583ac4"), 2, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("c3d54ec5-f899-59b3-9ef4-00b89d4db033"), 2, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "BLOQUEA", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("c5b801aa-6b9d-5807-b39d-2510dfcfb053"), 2, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("d009dccf-c777-5ce6-bbde-ccf5ee70a20a"), 2, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_UNILATERAL", true },
                    { new Guid("d8291b0f-5f69-5755-b14d-055ce73c84ad"), 2, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_STANDARD", true },
                    { new Guid("dac6c1c5-6f4f-5fc2-afe4-6cfd5de36db4"), 2, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("e4831343-6ddb-58e2-8773-5a768bcef3d5"), 2, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "BLOQUEA", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_STANDARD", true },
                    { new Guid("e7a50020-71b5-56ce-94bf-68afa6b27a54"), 2, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("e825d85e-e0da-5e8f-ad7f-ece615cb4935"), 2, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("ea107048-75e5-5254-9208-e4cac25c4f99"), 2, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("eff7957e-b0fb-5f4c-92f7-a8676516db93"), 2, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("f10342c5-ddb9-581f-92cb-3b791d011734"), 2, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("f28ca1ae-7c69-51b6-9c93-f3833e4cd90f"), 2, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("f723f921-6e82-55c1-82b9-5e51234d0472"), 2, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_UNILATERAL", true },
                    { new Guid("f7502f2a-5a35-5838-ac46-2e03c4138aab"), 2, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "BLOQUEA", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("fe9b3a30-4b2f-50eb-8454-18e50549fa23"), 2, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "BLOQUEA", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.", "TRASPASO_STANDARD", true },
                    { new Guid("ff7022a7-28bf-51d1-b77b-043c27aac994"), 2, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_STANDARD", true },
                    { new Guid("ffa1995b-f144-5975-8698-9f02504f8939"), 2, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("05819e81-3811-537f-9b87-b3906e5118c6"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("07ef8030-c000-5a28-8a56-36b2678568fc"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("0f15e299-f7d0-55bc-a2f4-b14ea2d23852"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("10edd35a-1116-51f1-90fb-e717a3204694"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("120c745d-6e51-50c4-86d2-0e50ea862222"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("151f02ce-0e95-589e-82ce-58e866b03415"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("19ed6bb7-022d-56e1-9ab5-a2b823c27951"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("358b3948-0ecf-5ed8-aa26-5c1431584e25"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("3f31c25d-3c25-5577-bab6-7e4903050824"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("40f48ba9-52b4-5e9d-8d5c-9280c92738ec"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("41632168-d509-5154-96f3-7a26261b9c48"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("45e9c3e7-c3f8-5fb0-a736-768f11be8a54"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("46a35c3f-10b1-57bf-9667-b083ca0c21e1"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("524600ad-438f-58ba-a637-ebb2e0529381"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("5853b614-4621-5074-91e9-4a7810dcc2b0"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("65d39996-95e6-56e1-bf2f-33aa74b66976"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("66cbe0ff-91cc-581b-8c5c-e0e9cae1543e"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("78645093-9bfb-5ebd-947f-546cc85809da"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("7961f7d1-686f-5b94-9789-92a9b8952ae5"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("800a5ecd-fcb6-5e48-b0a8-84aeb262099b"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("802f2eb0-4ac8-5ece-add1-0b089567d02a"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8403e8eb-8fba-5b23-a448-2ddfe856adeb"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("87ec78c9-4dbe-580e-be33-8eeb6b034692"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8807ebfe-94de-55f7-b0ea-2a75f1d3cc92"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("902a35c2-bbcd-534e-8912-6ae2f6082206"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9397c48a-0c22-507a-aafd-fdae7cf37c25"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9912f466-b596-5ae8-a5ac-e0f38eeff0bc"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9f74b3be-4414-5718-b881-c0f70ddde30b"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9fc7fdd2-ed27-5f24-a27a-6f1c9d6c87c4"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("a0bd4ecc-79f3-528f-bf95-085f6d41af92"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("a2d2e03b-2e68-5b2d-84b0-84c695bfb7a6"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("a33870e5-846c-56dd-bbc9-85e2a174f012"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("a4c12367-b0d1-5c66-85ac-b4bd0e8ca0c6"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c3d09a54-4ece-5bb5-ad31-d6a457583ac4"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c3d54ec5-f899-59b3-9ef4-00b89d4db033"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c5b801aa-6b9d-5807-b39d-2510dfcfb053"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("d009dccf-c777-5ce6-bbde-ccf5ee70a20a"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("d8291b0f-5f69-5755-b14d-055ce73c84ad"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("dac6c1c5-6f4f-5fc2-afe4-6cfd5de36db4"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e4831343-6ddb-58e2-8773-5a768bcef3d5"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e7a50020-71b5-56ce-94bf-68afa6b27a54"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e825d85e-e0da-5e8f-ad7f-ece615cb4935"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("ea107048-75e5-5254-9208-e4cac25c4f99"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("eff7957e-b0fb-5f4c-92f7-a8676516db93"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f10342c5-ddb9-581f-92cb-3b791d011734"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f28ca1ae-7c69-51b6-9c93-f3833e4cd90f"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f723f921-6e82-55c1-82b9-5e51234d0472"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f7502f2a-5a35-5838-ac46-2e03c4138aab"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("fe9b3a30-4b2f-50eb-8454-18e50549fa23"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("ff7022a7-28bf-51d1-b77b-043c27aac994"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("ffa1995b-f144-5975-8698-9f02504f8939"));
        }
    }
}
