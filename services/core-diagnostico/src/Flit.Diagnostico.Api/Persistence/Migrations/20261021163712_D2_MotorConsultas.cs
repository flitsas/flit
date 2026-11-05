using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D2_MotorConsultas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "uq_vehiculos_tenant_id",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "consultas",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote (la misma del vehículo)."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cabecera del lote."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo de la celda."),
                    fuente = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Fuente de la celda (enum Fuente del dominio; sin FASECOLDA, 12659 #1)."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PENDIENTE, EN_CURSO, COMPLETADO, ERROR o NA (plan §3.5)."),
                    motivo_na = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "SERVICIO_PUBLICO, DEPARTAMENTO_NO_SOPORTADO o SIN_DOCUMENTO_LOCATARIO; solo con estado NA (R-9, R-3)."),
                    origen = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true, comment: "API o MANUAL; null mientras no hay dato."),
                    payload = table.Column<string>(type: "jsonb", nullable: true, comment: "@pii:medium Resultado normalizado de la fuente (objeto JSON), nunca el crudo del proveedor (R-13); sin documentos en claro."),
                    usuario_manual_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario del backoffice que la llenó a mano (claim sub); nunca en DTO de cliente."),
                    dato_obtenido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Fecha y hora del dato que muestra la celda (D-10); la de la celda origen si se reutilizó."),
                    reutilizada_de_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Celda de la misma empresa y del mismo día cuyo dato se reutilizó sin consumo (D-10)."),
                    disponible_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Cola: desde cuándo el procesador la puede tomar (backoff, D-12)."),
                    lease_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Cola: vencimiento del lease del procesador; null si nadie la tiene."),
                    intentos_auto = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Cola: intentos automáticos hechos (reintentos de errores transitorios, D-12)."),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Último cambio de la celda (timestamptz)."),
                    version_tablero = table.Column<long>(type: "bigint", nullable: false, comment: "Versión del tablero del lote en el último cambio (polling ?desde=)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultas", x => x.id);
                    table.UniqueConstraint("uq_consultas_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_consultas_estado", "estado IN ('PENDIENTE', 'EN_CURSO', 'COMPLETADO', 'ERROR', 'NA')");
                    table.CheckConstraint("ck_consultas_fuente", "fuente IN ('RUNT', 'RUNT_PROPIETARIO', 'RUNT_LOCATARIO', 'SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'AVALUO', 'IMPUESTO_DEPARTAMENTAL')");
                    table.CheckConstraint("ck_consultas_intentos_auto", "intentos_auto >= 0");
                    table.CheckConstraint("ck_consultas_motivo_na", "motivo_na IS NULL OR motivo_na IN ('SERVICIO_PUBLICO', 'DEPARTAMENTO_NO_SOPORTADO', 'SIN_DOCUMENTO_LOCATARIO')");
                    table.CheckConstraint("ck_consultas_origen", "origen IS NULL OR origen IN ('API', 'MANUAL')");
                    table.CheckConstraint("ck_consultas_payload_objeto", "payload IS NULL OR jsonb_typeof(payload) = 'object'");
                    table.CheckConstraint("ck_motivo_na", "(estado = 'NA') = (motivo_na IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_consultas_consultas_reutilizada_de",
                        columns: x => new { x.tenant_id, x.reutilizada_de_id },
                        principalSchema: "diagnostico",
                        principalTable: "consultas",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consultas_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consultas_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Celdas del semáforo por vehículo y fuente (MOD03/MOD04, HU #13723); unidad de trabajo de la cola durable.");

            migrationBuilder.CreateTable(
                name: "departamentos_impuesto",
                schema: "diagnostico",
                columns: table => new
                {
                    departamento_codigo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "Código del departamento (DIVIPOLA)."),
                    soportado = table.Column<bool>(type: "boolean", nullable: false, comment: "core-consultas soporta el impuesto del departamento (caché del rpc, R-23)."),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Último refresco desde core-consultas (timestamptz).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departamentos_impuesto", x => x.departamento_codigo);
                },
                comment: "Departamentos con impuesto vehicular soportado por core-consultas (R-9, HU #13723). Sin tenant.");

            migrationBuilder.CreateTable(
                name: "organismos",
                schema: "diagnostico",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Código del organismo de tránsito."),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Nombre del organismo."),
                    nombre_norm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Nombre normalizado para buscar por el transit_office_name del RUNT."),
                    departamento_codigo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "Código del departamento (DIVIPOLA)."),
                    departamento_nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Nombre del departamento.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organismos", x => x.codigo);
                },
                comment: "Réplica global de organismos de tránsito con su departamento (HU #13723). Sin tenant.");

            migrationBuilder.CreateTable(
                name: "intentos_consulta",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña de la celda."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cabecera del lote."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo de la celda."),
                    consulta_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Celda del intento."),
                    fuente = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Fuente consultada."),
                    servicio = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "Proveedor que respondió (12659 #2); null si no llegó a ninguno o fue manual."),
                    disparador = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "AUTO, REPROCESO o MANUAL (R-1)."),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inicio del intento (timestamptz)."),
                    fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Fin del intento (timestamptz)."),
                    estado = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "SUCCESS o ERROR."),
                    tipo_error = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Tipo de error de la fuente (patrón Comparendos); null si fue exitoso."),
                    mensaje_error = table.Column<string>(type: "text", nullable: true, comment: "@pii:low Mensaje de error de la fuente; sin documentos en claro."),
                    transaccion_proveedor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Transacción del proveedor; null en el manual (12659 #6)."),
                    desde_cache = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false, comment: "El dato salió de la reutilización del mismo día (D-10), sin consumo."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario del reproceso o del manual (claim sub); null si fue el procesador.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intentos_consulta", x => x.id);
                    table.CheckConstraint("ck_intentos_consulta_disparador", "disparador IN ('AUTO', 'REPROCESO', 'MANUAL')");
                    table.CheckConstraint("ck_intentos_consulta_estado", "estado IN ('SUCCESS', 'ERROR')");
                    table.CheckConstraint("ck_intentos_consulta_fuente", "fuente IN ('RUNT', 'RUNT_PROPIETARIO', 'RUNT_LOCATARIO', 'SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'AVALUO', 'IMPUESTO_DEPARTAMENTAL')");
                    table.CheckConstraint("ck_intentos_consulta_servicio", "servicio IS NULL OR servicio IN ('VERIFIK', 'KYVERUM', 'INTEMPO', 'SMARTCLOUD', 'FASECOLDA', 'MERCADOLIBRE', 'BASEGRAVABLE', 'SCRAPING', 'FLITFINES', 'OTRO')");
                    table.ForeignKey(
                        name: "fk_intentos_consulta_consultas",
                        columns: x => new { x.tenant_id, x.consulta_id },
                        principalSchema: "diagnostico",
                        principalTable: "consultas",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_intentos_consulta_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_intentos_consulta_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Intentos de consulta por celda: auditoría y log de negocio (R-1, HU #13723). Solo inserción.");

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_tenant_placa",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "placa" });

            migrationBuilder.CreateIndex(
                name: "ix_cola",
                schema: "diagnostico",
                table: "consultas",
                column: "disponible_desde",
                filter: "estado IN ('PENDIENTE', 'EN_CURSO')");

            migrationBuilder.CreateIndex(
                name: "ix_consultas_reutilizacion",
                schema: "diagnostico",
                table: "consultas",
                columns: new[] { "tenant_id", "fuente", "dato_obtenido_en" },
                filter: "estado = 'COMPLETADO'");

            migrationBuilder.CreateIndex(
                name: "ix_consultas_reutilizada_de",
                schema: "diagnostico",
                table: "consultas",
                columns: new[] { "tenant_id", "reutilizada_de_id" });

            migrationBuilder.CreateIndex(
                name: "ix_consultas_tablero",
                schema: "diagnostico",
                table: "consultas",
                columns: new[] { "tenant_id", "diagnostico_id", "version_tablero" });

            migrationBuilder.CreateIndex(
                name: "ux_consulta_vehiculo_fuente",
                schema: "diagnostico",
                table: "consultas",
                columns: new[] { "tenant_id", "vehiculo_id", "fuente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intentos_consulta_celda",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "consulta_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_intentos_consulta_diagnostico",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "diagnostico_id" });

            migrationBuilder.CreateIndex(
                name: "ix_intentos_consulta_vehiculo",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "vehiculo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_intentos_metricas",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "fuente", "servicio", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_organismos_nombre_norm",
                schema: "diagnostico",
                table: "organismos",
                column: "nombre_norm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "departamentos_impuesto",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "intentos_consulta",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "organismos",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "consultas",
                schema: "diagnostico");

            migrationBuilder.DropUniqueConstraint(
                name: "uq_vehiculos_tenant_id",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "ix_vehiculos_tenant_placa",
                schema: "diagnostico",
                table: "vehiculos");
        }
    }
}
