using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D6_TablasDictamen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dictamenes",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del dictamen (la misma del vehículo)."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo dictaminado."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Versión de negocio del dictamen de ese vehículo. No es el token xmin."),
                    vigente = table.Column<bool>(type: "boolean", nullable: false, comment: "El que se entrega hoy; false conserva el anterior (no se borra)."),
                    veredicto = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "VIABLE o BLOQUEADO. Lo calcula el evaluador."),
                    alertamientos = table.Column<string>(type: "jsonb", nullable: false, comment: "Arreglo JSON: codigo, texto, efecto y fuente. Sin documentos ni nombres."),
                    catalogo_version = table.Column<int>(type: "integer", nullable: false, comment: "Versión del catálogo con la que se dictaminó. Entero de recuerdo, no FK."),
                    redactado_por = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PLANTILLA o LLM."),
                    estado_redaccion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PENDIENTE o COMPLETADA."),
                    redaccion_intentos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Intentos de la cola de redacción. El índice entre empresas no se crea en este corte."),
                    redaccion_disponible_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Desde cuándo la cola puede tomar la redacción; null si no espera."),
                    redaccion_lease_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Vencimiento del lease de redacción; null si nadie la tiene."),
                    analisis_override = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false", comment: "True si un analista reemplazó el veredicto. Ningún endpoint lo usa en este corte."),
                    usuario_override_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Seudónimo uuid de quien hizo el override; sin documento ni nombre."),
                    override_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante del override (timestamptz); null si no hubo."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante en que se guardó el dictamen (timestamptz)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictamenes", x => x.id);
                    table.UniqueConstraint("uq_dictamenes_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_dictamenes_alertamientos", "jsonb_typeof(alertamientos) = 'array'");
                    table.CheckConstraint("ck_dictamenes_estado_redaccion", "estado_redaccion IN ('PENDIENTE', 'COMPLETADA')");
                    table.CheckConstraint("ck_dictamenes_redactado_por", "redactado_por IN ('PLANTILLA', 'LLM')");
                    table.CheckConstraint("ck_dictamenes_veredicto", "veredicto IN ('VIABLE', 'BLOQUEADO')");
                    table.CheckConstraint("ck_redaccion", "(estado_redaccion = 'COMPLETADA') = (redactado_por = 'LLM')");
                    table.ForeignKey(
                        name: "fk_dictamenes_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Dictamen de un vehículo (clase (a) de ADR-0072, ADR-0077): tenant_id, xmin y FK compuesta al vehículo. Nace vacía, sin borrado lógico.");

            migrationBuilder.CreateTable(
                name: "reglas_dictamen",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "UUID v5 del proyector (catalogo_version, tipo_tramite_codigo, codigo). Sin DEFAULT uuidv7()."),
                    catalogo_version = table.Column<int>(type: "integer", nullable: false, comment: "Versión del catálogo. La v1 es 1."),
                    tipo_tramite_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Código de trámite de la familia (varchar 64)."),
                    codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Código de la regla. La condición guarda el mismo valor."),
                    condicion = table.Column<string>(type: "jsonb", nullable: false, comment: "Objeto {\"codigo\":\"<codigo>\"}. No es un motor de reglas."),
                    efecto = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "BLOQUEA o ADVIERTE, el de CatalogoReglasDictamen.Filas."),
                    plantilla_texto = table.Column<string>(type: "text", nullable: false, comment: "Texto de la plantilla, el de CatalogoReglasDictamen.Filas."),
                    vigente = table.Column<bool>(type: "boolean", nullable: false, comment: "La v1 nace en true. El único update previsto es false al publicar otra versión."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reglas_dictamen", x => x.id);
                    table.CheckConstraint("ck_reglas_dictamen_condicion", "jsonb_typeof(condicion) = 'object' AND condicion->>'codigo' = codigo");
                    table.CheckConstraint("ck_reglas_dictamen_efecto", "efecto IN ('BLOQUEA', 'ADVIERTE')");
                },
                comment: "Catálogo global de reglas de dictamen de FLIT (ADR-0077). Sin tenant_id: excepción de catálogo de ADR-0072. No se edita a mano; condicion repite el código de la fila y no es un motor.");

            migrationBuilder.InsertData(
                schema: "diagnostico",
                table: "reglas_dictamen",
                columns: new[] { "id", "catalogo_version", "codigo", "condicion", "efecto", "plantilla_texto", "tipo_tramite_codigo", "vigente" },
                values: new object[,]
                {
                    { new Guid("0fe37cbf-801e-5c34-973e-7c15c8805217"), 1, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("15604da8-20f4-54ea-9335-824c22ae8d0f"), 1, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("17a1a24a-1ce3-5db5-b50e-88b5ec3b698b"), 1, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("1d3a5f04-f7e8-5752-814c-f495d0659a90"), 1, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_STANDARD", true },
                    { new Guid("1f8e7def-9d1c-50c3-9462-9cdf3fb0999b"), 1, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("22229c2f-7a50-51a1-b193-c90dd2e9a406"), 1, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_STANDARD", true },
                    { new Guid("24ad7c86-d40d-5162-8732-c3c3f7930051"), 1, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("299b149a-eaad-5276-a6d5-8431a71715f3"), 1, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "ADVIERTE", "El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("2c83a005-cc4a-57c7-939e-92a5032ca0b8"), 1, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "ADVIERTE", "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("342a46bc-0d50-5fb7-b5fd-be5d6f209347"), 1, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("3e2166b5-40b5-5923-bc5b-9cc38a61946f"), 1, "ESTADO_VEHICULO", "{\"codigo\":\"ESTADO_VEHICULO\"}", "ADVIERTE", "El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable.", "TRASPASO_UNILATERAL", true },
                    { new Guid("40072083-940e-5447-b638-cf6160369e4c"), 1, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_UNILATERAL", true },
                    { new Guid("43e0ed4a-d031-5dfa-b5a6-bcc0ea10052f"), 1, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "ADVIERTE", "El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, no se pudo consultar su deuda.", "TRASPASO_UNILATERAL", true },
                    { new Guid("4eb0f804-22e1-5cf3-8ab2-6fc09314aabd"), 1, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "ADVIERTE", "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("521e5df7-c5c7-53dc-a306-85ada06f0c34"), 1, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("55df451f-b41d-5ff2-bb55-2c494518bb7a"), 1, "LICENCIA_CONDUCTOR", "{\"codigo\":\"LICENCIA_CONDUCTOR\"}", "ADVIERTE", "La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("5dfcbb96-f6ba-526a-b387-e9d2f76015b8"), 1, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("6414a735-dd51-5199-92fe-b20187a7b526"), 1, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("65440b8b-713d-5321-9010-fc024db11d6b"), 1, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "ADVIERTE", "El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("7f10f78f-f6e8-57e2-9d15-f6f6c2d159df"), 1, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("81143b0e-770d-5a75-8b98-947d2091fd94"), 1, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_UNILATERAL", true },
                    { new Guid("81d7fa62-673b-5b25-b480-1a0e76aa9d76"), 1, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("85e6b488-7bf4-5d0d-b843-e92067b79c92"), 1, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("9eadea8c-cf53-58f9-8e75-2da10372924f"), 1, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_UNILATERAL", true },
                    { new Guid("a367e833-8821-5e4f-992e-be727e0aed40"), 1, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("a613c1e1-30f0-5f8c-8b91-b859ec9c91f3"), 1, "GARANTIA_MOBILIARIA", "{\"codigo\":\"GARANTIA_MOBILIARIA\"}", "ADVIERTE", "El vehículo tiene una garantía mobiliaria registrada. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("a978390a-3e7b-5187-b4cd-2705cebb103b"), 1, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "ADVIERTE", "El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, no se pudo consultar su deuda.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("b0d0d2af-8a64-5198-8208-afe102514fc7"), 1, "PRENDA", "{\"codigo\":\"PRENDA\"}", "ADVIERTE", "El vehículo tiene una prenda registrada. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("c1b6d234-94d7-5460-9932-1928ddfcccdc"), 1, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("ca245f58-0893-5eb6-8136-000f38582755"), 1, "RTM_NO_VIGENTE", "{\"codigo\":\"RTM_NO_VIGENTE\"}", "BLOQUEA", "La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("cbe2d044-ee98-5917-b9f4-63aea051786d"), 1, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("cda18963-5b11-5ca1-84ee-f5145784f84c"), 1, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_UNILATERAL", true },
                    { new Guid("d8b7a707-305c-5d3a-adce-87e764e8961d"), 1, "SIMIT_VEHICULO", "{\"codigo\":\"SIMIT_VEHICULO\"}", "ADVIERTE", "El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("d981f4f1-ad6f-590f-9fe7-5578c1db7471"), 1, "SOAT_NO_VIGENTE", "{\"codigo\":\"SOAT_NO_VIGENTE\"}", "BLOQUEA", "El SOAT no está vigente, así que el traspaso queda bloqueado.", "TRASPASO_STANDARD", true },
                    { new Guid("e170b56e-fd41-596f-95b1-3c2133f37a7c"), 1, "GRAVAMEN", "{\"codigo\":\"GRAVAMEN\"}", "ADVIERTE", "El vehículo tiene un gravamen registrado. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("e2e7279d-4047-59b6-9ba8-5c81ee97858e"), 1, "SIMIT_LOCATARIO", "{\"codigo\":\"SIMIT_LOCATARIO\"}", "ADVIERTE", "El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, no se pudo consultar su deuda.", "TRASPASO_STANDARD", true },
                    { new Guid("edc5c707-7b7d-5886-b4bb-acd479df9a9e"), 1, "FUENTE_PENDIENTE", "{\"codigo\":\"FUENTE_PENDIENTE\"}", "ADVIERTE", "Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true },
                    { new Guid("ee0a5b6e-5f95-50b7-8e4e-0a4a6bd2f6f7"), 1, "INFRACCION_RUNT", "{\"codigo\":\"INFRACCION_RUNT\"}", "ADVIERTE", "El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT.", "TRASPASO_STANDARD", true },
                    { new Guid("f389e9dd-fe63-5836-bc76-b98095a3a72f"), 1, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("f798bb57-9009-5ff0-8e98-3a0fdce72409"), 1, "LIMITACION", "{\"codigo\":\"LIMITACION\"}", "ADVIERTE", "El vehículo tiene una limitación a la propiedad. Es una advertencia.", "TRASPASO_STANDARD", true },
                    { new Guid("f8fe7a99-11bd-5aaa-a2da-a5d7a18ba175"), 1, "IMPUESTO", "{\"codigo\":\"IMPUESTO\"}", "ADVIERTE", "El vehículo tiene impuesto departamental pendiente. Es una advertencia.", "TRASPASO_UNILATERAL", true },
                    { new Guid("fe3ead9a-c293-5b5f-8b21-18e0020a7632"), 1, "SIMIT_PROPIETARIO", "{\"codigo\":\"SIMIT_PROPIETARIO\"}", "ADVIERTE", "El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia.", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", true }
                });

            migrationBuilder.CreateIndex(
                name: "ux_dictamen_version",
                schema: "diagnostico",
                table: "dictamenes",
                columns: new[] { "tenant_id", "vehiculo_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_dictamen_vigente",
                schema: "diagnostico",
                table: "dictamenes",
                columns: new[] { "tenant_id", "vehiculo_id" },
                unique: true,
                filter: "vigente");

            migrationBuilder.CreateIndex(
                name: "ux_regla",
                schema: "diagnostico",
                table: "reglas_dictamen",
                columns: new[] { "catalogo_version", "tipo_tramite_codigo", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dictamenes",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "reglas_dictamen",
                schema: "diagnostico");
        }
    }
}
