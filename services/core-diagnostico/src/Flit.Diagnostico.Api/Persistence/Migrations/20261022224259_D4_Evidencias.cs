using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D4_Evidencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archivo_local",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación; la referencia es db:{id}."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del archivo."),
                    tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "PDF, XLSX o ZIP."),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Nombre del archivo."),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Tipo de contenido."),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:medium Bytes del archivo del cliente (documentos enmascarados, sin nombres de personas)."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante en que se guardó.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivo_local", x => x.id);
                },
                comment: "Archivos generados guardados en la base sin file-manager; solo pruebas automáticas (HU #13846).");

            migrationBuilder.CreateTable(
                name: "evidencias",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lote de la evidencia."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Vehículo del PDF; null en el Excel y el ZIP del lote."),
                    tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "PDF, XLSX o ZIP."),
                    huella = table.Column<string>(type: "char(64)", nullable: false, comment: "SHA-256 hex de las entradas del archivo (plantilla, cabecera, encabezado y celdas con su versión); si no cambia, no se regenera."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PENDIENTE, GENERANDO, LISTA o ERROR; PDF y XLSX nacen LISTA, el ZIP pasa por la cola (F07-H5)."),
                    archivo_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Referencia del archivo en el file-manager (o db: en pruebas); solo con estado LISTA."),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: true, comment: "Tamaño del archivo en bytes; obligatorio con estado LISTA."),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario que pidió el archivo (claim sub)."),
                    empresa_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Nombre de la empresa del claim company_name para el ZIP, que se genera sin sesión (F07-H5)."),
                    vigente = table.Column<bool>(type: "boolean", nullable: false, comment: "La que se entrega hoy para su clave; false = reemplazada y conservada (R-18)."),
                    intentos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Intentos de generación (cola del ZIP)."),
                    lease_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Cola del ZIP: vencimiento del lease del trabajador; null si nadie lo tiene."),
                    tipo_error = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Causa del fallo de generación; solo con estado ERROR."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante del pedido (timestamptz)."),
                    generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante en que el archivo quedó listo; null mientras no lo está."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidencias", x => x.id);
                    table.UniqueConstraint("uq_evidencias_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_evidencias_estado", "estado IN ('PENDIENTE', 'GENERANDO', 'LISTA', 'ERROR')");
                    table.CheckConstraint("ck_evidencias_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_evidencias_intentos", "intentos >= 0");
                    table.CheckConstraint("ck_evidencias_lista_archivo", "(estado = 'LISTA') = (archivo_ref IS NOT NULL) AND (estado <> 'LISTA' OR (tamano_bytes IS NOT NULL AND generado_en IS NOT NULL))");
                    table.CheckConstraint("ck_evidencias_pdf_vehiculo", "(tipo = 'PDF') = (vehiculo_id IS NOT NULL)");
                    table.CheckConstraint("ck_evidencias_tamano", "tamano_bytes IS NULL OR tamano_bytes >= 0");
                    table.CheckConstraint("ck_evidencias_tipo", "tipo IN ('PDF', 'XLSX', 'ZIP')");
                    table.CheckConstraint("ck_evidencias_tipo_error", "tipo_error IS NULL OR estado = 'ERROR'");
                    table.ForeignKey(
                        name: "fk_evidencias_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidencias_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "PDF, Excel y ZIP generados con la huella de sus entradas y su archivo en el file-manager (MOD07, HU #13846, R-18).");

            migrationBuilder.CreateIndex(
                name: "ix_archivo_local_tenant",
                schema: "diagnostico",
                table: "archivo_local",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_cola",
                schema: "diagnostico",
                table: "evidencias",
                columns: new[] { "estado", "lease_hasta" },
                filter: "tipo = 'ZIP' AND estado IN ('PENDIENTE', 'GENERANDO')");

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_diagnostico",
                schema: "diagnostico",
                table: "evidencias",
                columns: new[] { "tenant_id", "diagnostico_id", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_vehiculo",
                schema: "diagnostico",
                table: "evidencias",
                columns: new[] { "tenant_id", "vehiculo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_evidencias_vigente",
                schema: "diagnostico",
                table: "evidencias",
                columns: new[] { "tenant_id", "diagnostico_id", "vehiculo_id", "tipo" },
                unique: true,
                filter: "vigente")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archivo_local",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "evidencias",
                schema: "diagnostico");
        }
    }
}
