using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D1_ConfiguracionEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuracion_empresa",
                schema: "diagnostico",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña de la configuración (una fila por empresa)."),
                    fuentes_habilitadas = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[\"RUNT\"]'::jsonb", comment: "Contractual: fuentes contratadas (arreglo de códigos de Fuente)."),
                    limite_vehiculos_lote = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "500", comment: "Contractual: máximo de vehículos por lote (> 0)."),
                    contrato_actualizado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario del backoffice del último cambio contractual (seudónimo)."),
                    contrato_actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Momento del último cambio contractual."),
                    redaccion_ia_suspendida = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false", comment: "Interruptor de emergencia del backoffice para la redacción IA (R-2). No se muestra al cliente."),
                    visibilidad_usuario = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValueSql: "'EMPRESA'", comment: "Operativa: lotes que ve cada usuario (PROPIOS o EMPRESA, D-01)."),
                    fuentes_por_defecto = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[\"RUNT\"]'::jsonb", comment: "Operativa: fuentes premarcadas al crear un diagnóstico (subconjunto de las habilitadas)."),
                    estimado_consultas = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValueSql: "'NO_MOSTRAR'", comment: "Operativa: cuándo se muestra el estimado de consultas (R-11, R-11b)."),
                    operativa_actualizada_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Admin de la compañía del último cambio operativo (seudónimo)."),
                    operativa_actualizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Momento del último cambio operativo."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_empresa", x => x.tenant_id);
                    table.CheckConstraint("ck_configuracion_empresa_estimado_consultas", "estimado_consultas IN ('NO_MOSTRAR', 'ANTES_DE_LANZAR', 'AL_FINALIZAR')");
                    table.CheckConstraint("ck_configuracion_empresa_fuentes_habilitadas_validas", "jsonb_typeof(fuentes_habilitadas) = 'array' AND fuentes_habilitadas <@ '[\"RUNT\",\"RUNT_PROPIETARIO\",\"RUNT_LOCATARIO\",\"SIMIT_VEHICULO\",\"SIMIT_PROPIETARIO\",\"SIMIT_LOCATARIO\",\"AVALUO\",\"IMPUESTO_DEPARTAMENTAL\"]'::jsonb");
                    table.CheckConstraint("ck_configuracion_empresa_fuentes_por_defecto_validas", "jsonb_typeof(fuentes_por_defecto) = 'array' AND fuentes_por_defecto <@ '[\"RUNT\",\"RUNT_PROPIETARIO\",\"RUNT_LOCATARIO\",\"SIMIT_VEHICULO\",\"SIMIT_PROPIETARIO\",\"SIMIT_LOCATARIO\",\"AVALUO\",\"IMPUESTO_DEPARTAMENTAL\"]'::jsonb");
                    table.CheckConstraint("ck_configuracion_empresa_limite_positivo", "limite_vehiculos_lote > 0");
                    table.CheckConstraint("ck_configuracion_empresa_visibilidad_usuario", "visibilidad_usuario IN ('PROPIOS', 'EMPRESA')");
                    table.CheckConstraint("ck_por_defecto_subconjunto", "fuentes_habilitadas @> fuentes_por_defecto");
                },
                comment: "Configuración de Diagnóstico por compañía (D-04: nivel contractual y operativo). Sin retención ni purga (R-18).");

            migrationBuilder.CreateTable(
                name: "cambios_contrato",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa cuya configuración cambió."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario del backoffice que hizo el cambio (claim sub, seudónimo)."),
                    campo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Columna contractual cambiada (p. ej. fuentes_habilitadas)."),
                    valor_anterior = table.Column<string>(type: "jsonb", nullable: true, comment: "Valor anterior; null si no había."),
                    valor_nuevo = table.Column<string>(type: "jsonb", nullable: false, comment: "Valor nuevo."),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()", comment: "Momento del cambio.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cambios_contrato", x => x.id);
                    table.ForeignKey(
                        name: "fk_cambios_contrato_configuracion_empresa",
                        column: x => x.tenant_id,
                        principalSchema: "diagnostico",
                        principalTable: "configuracion_empresa",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Cambios del nivel contractual de la configuración por compañía (HU #13736). Solo inserción.");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_contrato_tenant_fecha",
                schema: "diagnostico",
                table: "cambios_contrato",
                columns: new[] { "tenant_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cambios_contrato",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "configuracion_empresa",
                schema: "diagnostico");
        }
    }
}
