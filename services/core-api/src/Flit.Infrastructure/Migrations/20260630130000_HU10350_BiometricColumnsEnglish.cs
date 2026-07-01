using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10350 — Renombra a INGLÉS los nombres de columna (no los valores) de
    /// <c>tramites.procedure_instance_biometric_validations</c> ya desplegadas en español. Solo afecta
    /// NOMBRES de columna; los datos (estado='aprobado'/'rechazado'/..., parte='comprador'/'vendedor')
    /// se preservan intactos por <c>RENAME COLUMN</c>. La columna <c>valid_until</c> NO se renombra aquí:
    /// ya se crea con ese nombre en la migración previa (HU10350_VigenciaPersisted).
    ///
    /// Cada rename es IDEMPOTENTE (se aplica solo si la columna en español aún existe) para tolerar BDs
    /// donde la columna ya fue renombrada manualmente (p.ej. el entorno local).
    /// </remarks>
    public partial class HU10350_BiometricColumnsEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RenameSql(
                ("parte", "party_role"),
                ("nombre", "name"),
                ("tipo_doc", "document_type"),
                ("documento", "document_number"),
                ("estado", "status"),
                ("intentos", "attempts"),
                ("max_intentos", "max_attempts"),
                ("detalle", "detail"),
                ("foto_rostro_path", "face_photo_path"),
                ("foto_cedula_frontal_path", "id_front_photo_path"),
                ("foto_cedula_reverso_path", "id_back_photo_path"),
                ("validado_at", "validated_at")));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RenameSql(
                ("party_role", "parte"),
                ("name", "nombre"),
                ("document_type", "tipo_doc"),
                ("document_number", "documento"),
                ("status", "estado"),
                ("attempts", "intentos"),
                ("max_attempts", "max_intentos"),
                ("detail", "detalle"),
                ("face_photo_path", "foto_rostro_path"),
                ("id_front_photo_path", "foto_cedula_frontal_path"),
                ("id_back_photo_path", "foto_cedula_reverso_path"),
                ("validated_at", "validado_at")));
        }

        /// <summary>
        /// Genera un bloque PL/pgSQL idempotente que renombra cada columna solo si la de origen existe.
        /// </summary>
        private static string RenameSql(params (string From, string To)[] renames)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("DO $$ BEGIN");
            foreach (var (from, to) in renames)
            {
                sb.AppendLine(
                    "  IF EXISTS (SELECT 1 FROM information_schema.columns " +
                    "WHERE table_schema='tramites' AND table_name='procedure_instance_biometric_validations' " +
                    $"AND column_name='{from}') THEN");
                sb.AppendLine(
                    "    ALTER TABLE tramites.procedure_instance_biometric_validations " +
                    $"RENAME COLUMN {from} TO {to};");
                sb.AppendLine("  END IF;");
            }
            sb.AppendLine("END $$;");
            return sb.ToString();
        }
    }
}
