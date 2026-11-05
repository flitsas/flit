using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D1_Vehiculos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                },
                comment: "Llaves de ASP.NET Data Protection del servicio (HU #13735). Sin tenant.");

            migrationBuilder.CreateTable(
                name: "vehiculos",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote (la misma de la cabecera)."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cabecera del lote."),
                    identificador_original = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "@pii:low Identificador tal como lo escribió el cliente (placa o VIN)."),
                    identificador_norm = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false, comment: "@pii:low Identificador en mayúsculas y sin espacios; único en el lote."),
                    tipo_identificador = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA o VIN (R-19)."),
                    placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true, comment: "@pii:low Placa del vehículo."),
                    vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true, comment: "@pii:low VIN del vehículo."),
                    servicio = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Servicio normalizado (vehicle_service del RUNT)."),
                    organismo_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Organismo de tránsito (transit_office_name del RUNT)."),
                    departamento_codigo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true, comment: "Departamento derivado de la réplica de organismos."),
                    propietario_doc_tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true, comment: "Tipo de documento del propietario, hidratado del RUNT (D-02)."),
                    propietario_doc_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Documento del propietario cifrado (plan §9.3). El nombre no se guarda."),
                    locatario_doc_tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true, comment: "Tipo de documento del locatario."),
                    locatario_doc_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Documento del locatario cifrado, como el del propietario (R-3)."),
                    locatario_doc_origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, comment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (respaldo de la captura); null sin documento."),
                    datos_consolidados = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb", comment: "@pii:low Datos consolidados de las fuentes (objeto JSON), sin documentos en claro."),
                    runt_consultado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante de la última consulta al RUNT; reutilización en Trámites (R-14)."),
                    estado_consultas = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValueSql: "'PENDIENTE'", comment: "Estado derivado de las celdas del vehículo."),
                    orden = table.Column<int>(type: "integer", nullable: false, comment: "Posición del vehículo en el lote."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehiculos", x => x.id);
                    table.CheckConstraint("ck_locatario_origen", "(locatario_doc_cifrado IS NULL) = (locatario_doc_origen IS NULL)");
                    table.CheckConstraint("ck_vehiculos_datos_consolidados_objeto", "jsonb_typeof(datos_consolidados) = 'object'");
                    table.CheckConstraint("ck_vehiculos_identificador_norm", "identificador_norm = upper(identificador_norm) AND identificador_norm !~ '\\s'");
                    table.CheckConstraint("ck_vehiculos_locatario_doc_origen", "locatario_doc_origen IS NULL OR locatario_doc_origen IN ('DECLARADO_CLIENTE', 'PROVEEDOR')");
                    table.CheckConstraint("ck_vehiculos_tipo_identificador", "tipo_identificador IN ('PLACA_CARRO', 'PLACA_MOTO', 'PLACA_REMOLQUE', 'PLACA_MAQUINARIA', 'VIN')");
                    table.ForeignKey(
                        name: "fk_vehiculos_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Vehículos del lote (MOD02, HU #13735): identificador normalizado único por lote y documentos cifrados.");

            migrationBuilder.CreateIndex(
                name: "ux_vehiculo_por_lote",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "diagnostico_id", "identificador_norm" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "vehiculos",
                schema: "diagnostico");
        }
    }
}
