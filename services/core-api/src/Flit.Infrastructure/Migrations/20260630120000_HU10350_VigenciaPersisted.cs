using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10350 — Persistir la fecha de fin de vigencia de la identidad aprobada como UNA columna real en
    /// <c>tramites.procedure_instance_biometric_validations</c>:
    ///   - <c>valid_until timestamptz NULL</c> — medianoche (hora Colombia) del día
    ///     <c>(validado_at AT TIME ZONE 'America/Bogota')::date + 30</c>. NULL si validado_at es NULL.
    ///
    /// La columna la ESTAMPA el código de la aplicación al aprobar (método
    /// <c>ProcedureInstanceBiometricValidation.Approve(now)</c>), NO la base de datos — es un valor absoluto
    /// que solo depende de <c>validado_at</c>. Los "días restantes" NO se persisten: se calculan al leer
    /// (siempre frescos), así que no hay columna ni job para ellos.
    ///
    /// El backfill aquí recalcula <c>valid_until</c> para las filas YA aprobadas (las nuevas las estampa
    /// la app). El trigger AFTER de auditoría existente no se toca. El resto de columnas en español de esta
    /// tabla se pasan a inglés en la migración siguiente (HU10350_BiometricColumnsEnglish), por lo que este
    /// Up todavía lee <c>validado_at</c> (aún sin renombrar en ese punto).
    /// </remarks>
    public partial class HU10350_VigenciaPersisted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- 1. Columna de fin de vigencia (idempotente). La estampa la app al aprobar.
                ALTER TABLE tramites.procedure_instance_biometric_validations
                  ADD COLUMN IF NOT EXISTS valid_until timestamptz NULL;

                COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.valid_until IS
                  'HU #10350 — Fin de vigencia de la identidad aprobada: medianoche (hora Colombia, UTC-5) del día validado_at_colombia::date + 30. NULL si validado_at es NULL. La estampa el código al aprobar (Approve(now)); los días restantes se calculan al leer (no se persisten).';

                -- 2. Backfill de las filas ya aprobadas (medianoche Colombia del día validado_at + 30).
                UPDATE tramites.procedure_instance_biometric_validations
                  SET valid_until = (
                    ((validado_at AT TIME ZONE 'America/Bogota')::date + 30)::timestamp
                    AT TIME ZONE 'America/Bogota'
                  )
                  WHERE validado_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instance_biometric_validations
                  DROP COLUMN IF EXISTS valid_until;
                """);
        }
    }
}
