using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S2_AccesosReporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accesos_reporte",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación; se imprime en el PDF y en la hoja Aviso del Excel."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Compañía del lector (la del token), también en el backoffice."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario que vio o descargó (claim sub, seudónimo)."),
                    alcance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "COMPANIA, BACKOFFICE_EMPRESA o BACKOFFICE_GLOBAL."),
                    tipo_reporte = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "vehiculos, consultas, saneamientos, derivaciones o consumo."),
                    formato = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "PANTALLA, XLSX o PDF."),
                    desde = table.Column<DateOnly>(type: "date", nullable: false, comment: "Inicio del rango pedido (día de Colombia, inclusive)."),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false, comment: "Fin del rango pedido (día de Colombia, inclusive)."),
                    filtros = table.Column<string>(type: "jsonb", nullable: false, comment: "Filtros de la lista blanca con valores de enum; la placa se guarda como placa_filtrada: true, nunca su valor."),
                    empresa_consultada = table.Column<Guid>(type: "uuid", nullable: true, comment: "Compañía consultada; solo en BACKOFFICE_EMPRESA."),
                    filas = table.Column<int>(type: "integer", nullable: false, comment: "Filas entregadas (la página o el archivo)."),
                    empresas = table.Column<int>(type: "integer", nullable: false, comment: "Compañías distintas entregadas."),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Momento del acceso.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accesos_reporte", x => x.id);
                    table.CheckConstraint("ck_accesos_reporte_alcance", "alcance IN ('COMPANIA', 'BACKOFFICE_EMPRESA', 'BACKOFFICE_GLOBAL')");
                    table.CheckConstraint("ck_accesos_reporte_empresa_consultada", "(alcance = 'BACKOFFICE_EMPRESA') = (empresa_consultada IS NOT NULL)");
                    table.CheckConstraint("ck_accesos_reporte_empresas", "empresas >= 0");
                    table.CheckConstraint("ck_accesos_reporte_filas", "filas >= 0");
                    table.CheckConstraint("ck_accesos_reporte_filtros", "jsonb_typeof(filtros) = 'object' AND octet_length(filtros::text) <= 1024 AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (!(@.key like_regex \"^[a-z_]{1,32}$\"))', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (@.key == \"placa_filtrada\" && !(@.value.type() == \"boolean\" && @.value == true))', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.keyvalue() ? (@.key != \"placa_filtrada\" && @.value.type() != \"array\")', '{}', true) AND NOT jsonb_path_exists(filtros, 'strict $.* ? (@.type() == \"array\")[*] ? (@.type() != \"string\" || !(@ like_regex \"^[A-Z0-9_]{1,32}$\"))', '{}', true)");
                    table.CheckConstraint("ck_accesos_reporte_formato", "formato IN ('PANTALLA', 'XLSX', 'PDF')");
                    table.CheckConstraint("ck_accesos_reporte_rango", "desde <= hasta");
                    table.CheckConstraint("ck_accesos_reporte_tipo", "tipo_reporte IN ('vehiculos', 'consultas', 'saneamientos', 'derivaciones', 'consumo')");
                },
                comment: "Registro de cada visualización y descarga del módulo de reportes (ADR-0088, Ley 1581 art. 17 lit. d). Sin PII. Retención: la de lecturas_backoffice (pendiente LT). Solo inserción.");

            migrationBuilder.CreateIndex(
                name: "ix_accesos_reporte_tenant_fecha",
                schema: "diagnostico",
                table: "accesos_reporte",
                columns: new[] { "tenant_id", "fecha" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accesos_reporte",
                schema: "diagnostico");
        }
    }
}
