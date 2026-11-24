using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8_CatalogoDictamenV3ImpuestoValidacion : Migration
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
                    { new Guid("08b2c41c-e9c0-5a5f-a4dd-1c54be96b3cc"), 3, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "BLOQUEA", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("0dbf2365-889c-5b4a-8b24-684239574f1b"), 3, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("22ed9dc1-d2a4-5653-a4b3-ca83a4905299"), 3, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_STANDARD", true },
                    { new Guid("24688da0-b6ba-56d8-8b71-31f6937b2cd0"), 3, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("293d5a4a-7565-5150-af4e-dea609e91191"), 3, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "BLOQUEA", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. El traspaso queda bloqueado hasta completar la consulta.", "TRASPASO_STANDARD", true },
                    { new Guid("30a7ab62-ddbf-596d-be5d-cb379b6752b1"), 3, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "BLOQUEA", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("32d74e23-d64e-5029-9959-95fc3a612716"), 3, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("33f71947-298d-5222-9018-c3a5784e236b"), 3, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_STANDARD", true },
                    { new Guid("38f928d5-f46f-533d-b55e-fc93f358ff9e"), 3, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_UNILATERAL", true },
                    { new Guid("3dd294f5-5af7-5b0a-b72d-e651e2ffe711"), 3, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("43f4c657-229e-5bf1-a7a2-8f2da61801bd"), 3, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_UNILATERAL", true },
                    { new Guid("458d8a63-6964-528b-bf77-6d99e346cadb"), 3, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_STANDARD", true },
                    { new Guid("4b120a1c-3b06-5da0-a409-0616eae6d09d"), 3, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("4db40dc0-2052-5b15-b907-9c1dee09d439"), 3, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "BLOQUEA", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("58a2252d-0520-5a19-9f91-c67a07a278d8"), 3, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "ADVIERTE", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("597b2bdb-e036-5a4c-9b1c-ae2fc0bc67a1"), 3, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("5eb78d1e-a69a-5cb1-8c8f-05500eeecb69"), 3, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("5ee2cabc-51ce-5ca4-8d74-7e22b11c9fcd"), 3, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("6297fc0c-24c4-5aec-85d4-ce6ac9612bc9"), 3, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_UNILATERAL", true },
                    { new Guid("630cf390-acaa-5f8b-8880-7c7ddd46d193"), 3, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("6ae593f4-0841-5680-b032-b8b1db40cb89"), 3, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("721b6386-f2d4-5d2d-886b-29781797b3a3"), 3, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_UNILATERAL", true },
                    { new Guid("75a5dd4c-da6c-520a-9ccc-17c4b18ff7d2"), 3, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("7bf15f53-45a6-5bce-bed5-9e94c7e75f3b"), 3, "SOAT_MAQUINARIA", "{\"codigo\":\"SOAT_MAQUINARIA\"}", "ADVIERTE", "La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("7ed05f1b-2179-5aa7-8dad-a72ddf076fa1"), 3, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("7f574044-8d8a-5cce-a779-54e4fc406340"), 3, "IMPUESTO_VALIDACION", "{\"codigo\":\"IMPUESTO_VALIDACION\"}", "ADVIERTE", "La fuente del impuesto informó datos del vehículo que no coinciden con el portal departamental. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("8a8bc20c-d669-5ac8-8309-63ed72c5bc05"), 3, "IMPUESTO_VALIDACION", "{\"codigo\":\"IMPUESTO_VALIDACION\"}", "ADVIERTE", "La fuente del impuesto informó datos del vehículo que no coinciden con el portal departamental. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("8b250492-e4f2-57c6-b6f7-fba5da9813ad"), 3, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_UNILATERAL", true },
                    { new Guid("996dcbde-21ff-58eb-b2bc-f10194a23b72"), 3, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "BLOQUEA", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día.", "TRASPASO_STANDARD", true },
                    { new Guid("9eb86d21-e19b-54fd-ab92-f6f26f9dc943"), 3, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("9f750af4-438a-5d35-b22a-28ab1ea71e46"), 3, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "BLOQUEA", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_STANDARD", true },
                    { new Guid("a54ded2e-6de9-5fc8-9309-e51bd28a1944"), 3, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "ADVIERTE", "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares). En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("b23fa69d-fbab-56a0-aeeb-0423ca50ddaa"), 3, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("b4bcb318-a2e1-56a9-86f1-297986795a56"), 3, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "BLOQUEA", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día.", "TRASPASO_STANDARD", true },
                    { new Guid("b7a47733-334d-5a55-b46c-4a93b2f92a56"), 3, "SIMIT_SIN_VERIFICAR", "{\"codigo\":\"SIMIT_SIN_VERIFICAR\"}", "BLOQUEA", "No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. El traspaso queda bloqueado hasta completar la consulta.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("ba14a4a6-602c-59fa-962c-f5b3a507a00c"), 3, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("bb3aea4b-ebfc-5578-83d6-bedaf456e532"), 3, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "ADVIERTE", "El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("bd0d0fa0-a08b-5a67-bc2b-fe5670ca7071"), 3, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "ADVIERTE", "El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("bf60310a-0c9b-59d7-8420-4a245f131772"), 3, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "ADVIERTE", "El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("c006900e-c3da-5419-9030-15ba159f19f7"), 3, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("c4a55b3d-8b97-5aa2-9ae3-b2badab50c9a"), 3, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("c8aed8e8-2e56-55ea-a44c-f0343be176a9"), 3, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("cf76be19-748a-5283-ab98-296467feebf2"), 3, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("d52a165f-c14e-562e-a418-6bd6eb9b3095"), 3, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_STANDARD", true },
                    { new Guid("d582223f-d167-5c10-98bb-1bf07ef16f96"), 3, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("de23dc51-1871-5382-a629-4a91a993762c"), 3, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("e128f72a-c497-5e8a-97e5-7c671848ecaa"), 3, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("e1fc45e7-7552-563b-be05-3cf56731fda1"), 3, "SIMIT_LICENCIA", "{\"codigo\":\"SIMIT_LICENCIA\"}", "ADVIERTE", "El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto.", "TRASPASO_STANDARD", true },
                    { new Guid("e3285246-4f8e-58bb-a845-169d88d351a7"), 3, "IMPUESTO_VALIDACION", "{\"codigo\":\"IMPUESTO_VALIDACION\"}", "ADVIERTE", "La fuente del impuesto informó datos del vehículo que no coinciden con el portal departamental. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("e3393842-828e-5274-bacb-a4cc69e98035"), 3, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("e7b2597b-4fe2-5af7-b9d9-35f59935c5b0"), 3, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("e8383d6f-7928-5516-ac6c-304590673dcb"), 3, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("e8b569c5-81f3-5872-a90b-1eab8896e484"), 3, "SIMIT_ACUERDO_PAGO", "{\"codigo\":\"SIMIT_ACUERDO_PAGO\"}", "ADVIERTE", "Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto.", "TRASPASO_STANDARD", true },
                    { new Guid("ef3cae20-0ca5-5824-bd3c-098ee7489c92"), 3, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("efd1634d-f858-5051-80ef-7cb9c18e916a"), 3, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("f1f17813-f2c6-51e2-83f9-08f548908fb2"), 3, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("f5b51449-3359-512e-bd4d-944fe65446de"), 3, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_STANDARD", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("08b2c41c-e9c0-5a5f-a4dd-1c54be96b3cc"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("0dbf2365-889c-5b4a-8b24-684239574f1b"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("22ed9dc1-d2a4-5653-a4b3-ca83a4905299"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("24688da0-b6ba-56d8-8b71-31f6937b2cd0"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("293d5a4a-7565-5150-af4e-dea609e91191"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("30a7ab62-ddbf-596d-be5d-cb379b6752b1"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("32d74e23-d64e-5029-9959-95fc3a612716"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("33f71947-298d-5222-9018-c3a5784e236b"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("38f928d5-f46f-533d-b55e-fc93f358ff9e"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("3dd294f5-5af7-5b0a-b72d-e651e2ffe711"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("43f4c657-229e-5bf1-a7a2-8f2da61801bd"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("458d8a63-6964-528b-bf77-6d99e346cadb"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("4b120a1c-3b06-5da0-a409-0616eae6d09d"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("4db40dc0-2052-5b15-b907-9c1dee09d439"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("58a2252d-0520-5a19-9f91-c67a07a278d8"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("597b2bdb-e036-5a4c-9b1c-ae2fc0bc67a1"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("5eb78d1e-a69a-5cb1-8c8f-05500eeecb69"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("5ee2cabc-51ce-5ca4-8d74-7e22b11c9fcd"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("6297fc0c-24c4-5aec-85d4-ce6ac9612bc9"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("630cf390-acaa-5f8b-8880-7c7ddd46d193"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("6ae593f4-0841-5680-b032-b8b1db40cb89"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("721b6386-f2d4-5d2d-886b-29781797b3a3"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("75a5dd4c-da6c-520a-9ccc-17c4b18ff7d2"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("7bf15f53-45a6-5bce-bed5-9e94c7e75f3b"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("7ed05f1b-2179-5aa7-8dad-a72ddf076fa1"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("7f574044-8d8a-5cce-a779-54e4fc406340"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8a8bc20c-d669-5ac8-8309-63ed72c5bc05"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("8b250492-e4f2-57c6-b6f7-fba5da9813ad"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("996dcbde-21ff-58eb-b2bc-f10194a23b72"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9eb86d21-e19b-54fd-ab92-f6f26f9dc943"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("9f750af4-438a-5d35-b22a-28ab1ea71e46"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("a54ded2e-6de9-5fc8-9309-e51bd28a1944"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("b23fa69d-fbab-56a0-aeeb-0423ca50ddaa"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("b4bcb318-a2e1-56a9-86f1-297986795a56"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("b7a47733-334d-5a55-b46c-4a93b2f92a56"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("ba14a4a6-602c-59fa-962c-f5b3a507a00c"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("bb3aea4b-ebfc-5578-83d6-bedaf456e532"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("bd0d0fa0-a08b-5a67-bc2b-fe5670ca7071"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("bf60310a-0c9b-59d7-8420-4a245f131772"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c006900e-c3da-5419-9030-15ba159f19f7"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c4a55b3d-8b97-5aa2-9ae3-b2badab50c9a"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("c8aed8e8-2e56-55ea-a44c-f0343be176a9"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("cf76be19-748a-5283-ab98-296467feebf2"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("d52a165f-c14e-562e-a418-6bd6eb9b3095"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("d582223f-d167-5c10-98bb-1bf07ef16f96"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("de23dc51-1871-5382-a629-4a91a993762c"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e128f72a-c497-5e8a-97e5-7c671848ecaa"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e1fc45e7-7552-563b-be05-3cf56731fda1"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e3285246-4f8e-58bb-a845-169d88d351a7"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e3393842-828e-5274-bacb-a4cc69e98035"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e7b2597b-4fe2-5af7-b9d9-35f59935c5b0"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e8383d6f-7928-5516-ac6c-304590673dcb"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("e8b569c5-81f3-5872-a90b-1eab8896e484"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("ef3cae20-0ca5-5824-bd3c-098ee7489c92"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("efd1634d-f858-5051-80ef-7cb9c18e916a"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f1f17813-f2c6-51e2-83f9-08f548908fb2"));

            migrationBuilder.DeleteData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                keyColumn: "id",
                keyValue: new Guid("f5b51449-3359-512e-bd4d-944fe65446de"));
        }
    }
}
