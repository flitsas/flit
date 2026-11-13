using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R7_AjusteDictamen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Precondición (schema.md §2.3): falla con un mensaje claro en vez de una violación genérica de CHECK.
            migrationBuilder.Sql(@"DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM diagnostico.dictamenes
             WHERE version < 1
                OR (usuario_override_id IS NULL) <> (override_en IS NULL)
                OR analisis_override <> (usuario_override_id IS NOT NULL)) THEN
    RAISE EXCEPTION 'R7_AjusteDictamen: hay dictamenes con override incoherente o versión < 1; revisar diagnostico.dictamenes antes de migrar.';
  END IF;
END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_redactado_por",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_redaccion",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.AlterColumn<string>(
                name: "redactado_por",
                schema: "diagnostico",
                table: "dictamenes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "PLANTILLA, LLM o MANUAL (textos editados por FLIT; el LLM no los reescribe).",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "PLANTILLA o LLM.");

            migrationBuilder.AddColumn<int>(
                name: "ajustado_desde_version",
                schema: "diagnostico",
                table: "dictamenes",
                type: "integer",
                nullable: true,
                comment: "Versión vigente que reemplazó el override o ajuste FLIT (= version - 1). NULL en las automáticas. [ADR-0086]");

            migrationBuilder.AddColumn<string>(
                name: "ajuste_motivo",
                schema: "diagnostico",
                table: "dictamenes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "@pii:low Motivo del ajuste FLIT (texto libre de soporte, 10..500). Solo backoffice; nunca en DTO de compañía, eventos ni logs. [ADR-0086]");

            // Backfill exacto (schema.md F7): toda fila con override sale de la vigente anterior, que siempre es version - 1.
            migrationBuilder.Sql("UPDATE diagnostico.dictamenes SET ajustado_desde_version = version - 1 WHERE analisis_override;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_ajuste",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "analisis_override = (ajustado_desde_version IS NOT NULL) AND (ajustado_desde_version IS NULL OR ajustado_desde_version = version - 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_ajuste_motivo",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "ajuste_motivo IS NULL OR (analisis_override AND char_length(btrim(ajuste_motivo)) BETWEEN 10 AND 500)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_manual",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "redactado_por <> 'MANUAL' OR (analisis_override AND ajuste_motivo IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_override",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "(usuario_override_id IS NULL) = (override_en IS NULL) AND analisis_override = (usuario_override_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_redactado_por",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "redactado_por IN ('PLANTILLA', 'LLM', 'MANUAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_version",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_redaccion",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "(estado_redaccion = 'COMPLETADA') = (redactado_por IN ('LLM', 'MANUAL'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Guarda del rollback (schema.md §2.3): el CHECK anterior no admite ajustes FLIT y bajar la columna borraría auditoría.
            migrationBuilder.Sql(@"DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM diagnostico.dictamenes WHERE redactado_por = 'MANUAL' OR ajuste_motivo IS NOT NULL) THEN
    RAISE EXCEPTION 'Rollback de R7_AjusteDictamen bloqueado: hay ajustes FLIT (redactado_por MANUAL o ajuste_motivo). Exportar y decidir con el LT antes del rollback.';
  END IF;
END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_ajuste",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_ajuste_motivo",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_manual",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_override",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_redactado_por",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_version",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_redaccion",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropColumn(
                name: "ajustado_desde_version",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropColumn(
                name: "ajuste_motivo",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.AlterColumn<string>(
                name: "redactado_por",
                schema: "diagnostico",
                table: "dictamenes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "PLANTILLA o LLM.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "PLANTILLA, LLM o MANUAL (textos editados por FLIT; el LLM no los reescribe).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_redactado_por",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "redactado_por IN ('PLANTILLA', 'LLM')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_redaccion",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "(estado_redaccion = 'COMPLETADA') = (redactado_por = 'LLM')");
        }
    }
}
