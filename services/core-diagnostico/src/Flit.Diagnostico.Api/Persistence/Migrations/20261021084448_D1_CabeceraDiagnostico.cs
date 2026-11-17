using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D1_CabeceraDiagnostico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diagnosticos",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote."),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Nombre del lote dado por el usuario."),
                    modo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Plan comercial: SIMPLE o CON_ANALISIS (Con Análisis IA, R-2)."),
                    tipo_tramite_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Código de tramites.procedure_types; obligatorio en CON_ANALISIS."),
                    es_express = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false", comment: "Creado por la captura express."),
                    alcance = table.Column<string>(type: "jsonb", nullable: false, comment: "Fuentes a consultar (arreglo de códigos de Fuente); RUNT siempre."),
                    configuracion = table.Column<string>(type: "jsonb", nullable: false, comment: "Copia inmutable de la configuración de la compañía al guardar."),
                    estimado_consultas = table.Column<string>(type: "jsonb", nullable: true, comment: "Estimado de consultas por fuente al confirmar (R-11); null hasta confirmar."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Estado del lote: CREADO, EN_PROCESO, COMPLETADO o INCOMPLETO (plan §3.5)."),
                    version_tablero = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0", comment: "Sube con cada cambio de celda; polling del tablero (?desde=)."),
                    usuario_creador_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario que creó el lote (claim sub, seudónimo)."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante de creación (timestamptz)."),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante de cierre (COMPLETADO o INCOMPLETO); null mientras sigue abierto."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnosticos", x => x.id);
                    table.UniqueConstraint("uq_diagnosticos_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_diagnosticos_alcance_valido", "jsonb_typeof(alcance) = 'array' AND alcance <@ '[\"RUNT\",\"RUNT_PROPIETARIO\",\"RUNT_LOCATARIO\",\"SIMIT_VEHICULO\",\"SIMIT_PROPIETARIO\",\"SIMIT_LOCATARIO\",\"AVALUO\",\"IMPUESTO_DEPARTAMENTAL\"]'::jsonb AND alcance @> '[\"RUNT\"]'::jsonb");
                    table.CheckConstraint("ck_diagnosticos_configuracion_objeto", "jsonb_typeof(configuracion) = 'object'");
                    table.CheckConstraint("ck_diagnosticos_estado", "estado IN ('CREADO', 'EN_PROCESO', 'COMPLETADO', 'INCOMPLETO')");
                    table.CheckConstraint("ck_diagnosticos_estimado_consultas_objeto", "estimado_consultas IS NULL OR jsonb_typeof(estimado_consultas) = 'object'");
                    table.CheckConstraint("ck_diagnosticos_modo", "modo IN ('SIMPLE', 'CON_ANALISIS')");
                    table.CheckConstraint("ck_tipo_con_analisis", "modo = 'SIMPLE' OR tipo_tramite_codigo IS NOT NULL");
                },
                comment: "Cabecera del diagnóstico (MOD01, HU #13738): lote con su alcance y su configuración inmutables.");

            migrationBuilder.CreateTable(
                name: "historial_estado_diagnostico",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote (la misma de la cabecera)."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cabecera del lote."),
                    estado_anterior = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "Estado de partida; null en la creación."),
                    estado_nuevo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Estado de llegada."),
                    disparador = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "AUTO, CIERRE_PANEL, REPROCESO o MANUAL."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario que lo provocó (claim sub, seudónimo); null si fue el procesador."),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante de la transición (timestamptz).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historial_estado_diagnostico", x => x.id);
                    table.CheckConstraint("ck_historial_estado_diagnostico_disparador", "disparador IN ('AUTO', 'CIERRE_PANEL', 'REPROCESO', 'MANUAL')");
                    table.CheckConstraint("ck_historial_estado_diagnostico_estado_anterior", "estado_anterior IS NULL OR estado_anterior IN ('CREADO', 'EN_PROCESO', 'COMPLETADO', 'INCOMPLETO')");
                    table.CheckConstraint("ck_historial_estado_diagnostico_estado_nuevo", "estado_nuevo IN ('CREADO', 'EN_PROCESO', 'COMPLETADO', 'INCOMPLETO')");
                    table.ForeignKey(
                        name: "fk_historial_estado_diagnostico_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Transiciones de estado del lote (R-8, HU #13738). Solo inserción.");

            migrationBuilder.CreateIndex(
                name: "ix_diag_tenant_creado",
                schema: "diagnostico",
                table: "diagnosticos",
                columns: new[] { "tenant_id", "creado_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_historial_diagnostico",
                schema: "diagnostico",
                table: "historial_estado_diagnostico",
                columns: new[] { "tenant_id", "diagnostico_id", "en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historial_estado_diagnostico",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "diagnosticos",
                schema: "diagnostico");
        }
    }
}
