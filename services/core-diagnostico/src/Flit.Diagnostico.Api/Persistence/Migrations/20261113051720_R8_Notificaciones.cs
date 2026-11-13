using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R8_Notificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notificaciones",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote y de la notificación."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Destinatario: seudónimo uuid (claim sub); quien disparó el hecho."),
                    tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA o LOTE_TERMINADO (D8: sin DICTAMEN_AJUSTADO)."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lote al que se refiere el aviso."),
                    referencia_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO). Sin FK (polimórfica, ADR-0086)."),
                    datos = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb", comment: "@pii:none Solo contadores y códigos (p. ej. {\"creadas\":3,\"fallidas\":1}); el CHECK impide texto libre."),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante del hecho (timestamptz). Base de la retención."),
                    leida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante en que el destinatario la marcó leída; null si no. Se escribe GREATEST(ahora, creada_en)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificaciones", x => x.id);
                    table.CheckConstraint("ck_notificaciones_datos", "jsonb_typeof(datos) = 'object' AND NOT jsonb_path_exists(datos, 'strict $.* ? (@.type() == \"object\" || @.type() == \"array\" || (@.type() == \"string\" && !(@ like_regex \"^[A-Z0-9_]{1,32}$\")))') AND octet_length(datos::text) <= 512");
                    table.CheckConstraint("ck_notificaciones_leida", "leida_en IS NULL OR leida_en >= creada_en");
                    table.CheckConstraint("ck_notificaciones_tipo", "tipo IN ('ZIP_EVIDENCIAS_LISTO', 'ZIP_EVIDENCIAS_FALLIDO', 'DERIVACION_TERMINADA', 'LOTE_TERMINADO')");
                    table.ForeignKey(
                        name: "fk_notificaciones_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Avisos de la campana de Diagnóstico por usuario (clase (a) de ADR-0072 con excepción de retención de ADR-0086): sin texto, documentos ni PII; el mensaje se arma al leer. Purga física a los 90 días.");

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_bandeja",
                schema: "diagnostico",
                table: "notificaciones",
                columns: new[] { "tenant_id", "usuario_id", "creada_en", "id" },
                descending: new[] { false, false, true, true })
                .Annotation("Npgsql:IndexInclude", new[] { "leida_en" });

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_diagnostico",
                schema: "diagnostico",
                table: "notificaciones",
                columns: new[] { "tenant_id", "diagnostico_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_no_leidas",
                schema: "diagnostico",
                table: "notificaciones",
                columns: new[] { "tenant_id", "usuario_id" },
                filter: "leida_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_retencion",
                schema: "diagnostico",
                table: "notificaciones",
                column: "creada_en");

            migrationBuilder.CreateIndex(
                name: "ux_notificaciones_idempotencia",
                schema: "diagnostico",
                table: "notificaciones",
                columns: new[] { "tenant_id", "usuario_id", "tipo", "referencia_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificaciones",
                schema: "diagnostico");
        }
    }
}
