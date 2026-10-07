using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13283 (Feature #13280 A1, Épica #13202 Identidad manual) — amplía
    /// <c>tramites.procedure_instance_biometric_validations</c>: <c>status</c> a varchar(40), CHECKs de estado/proveedor/origen
    /// (estados <c>manual_activo</c> y <c>pendiente_revision_manual</c>, proveedor <c>manual</c>), columnas de activación,
    /// consentimiento y revisión, backfill <c>approval_origin='automatica'</c> en lo ya aprobado e índice parcial de la
    /// pestaña de manuales. Aditiva, idempotente. DDL: <c>130-HU13283-identidad-manual.sql</c>. Descubrible al arrancar por
    /// el Designer.cs.
    /// </summary>
    public partial class HU13283_IdentidadManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("130-HU13283-identidad-manual.sql"));

        /// <inheritdoc />
        /// <remarks>
        /// Las filas con estado/proveedor manual no tienen representación sin los CHECKs ampliados ni en varchar(20): se
        /// retiran antes de volver al esquema anterior.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.ix_biometric_validations_manual_tab;

                DELETE FROM tramites.procedure_instance_biometric_validations
                 WHERE provider = 'manual' OR status IN ('manual_activo', 'pendiente_revision_manual');

                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validations_approval_origin;
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validations_provider;
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validations_status;

                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP COLUMN IF EXISTS rejection_reason_code,
                    DROP COLUMN IF EXISTS reviewed_at,
                    DROP COLUMN IF EXISTS reviewed_by,
                    DROP COLUMN IF EXISTS consent_text_version,
                    DROP COLUMN IF EXISTS consent_ip,
                    DROP COLUMN IF EXISTS consent_at,
                    DROP COLUMN IF EXISTS manual_activated_at,
                    DROP COLUMN IF EXISTS manual_activated_by,
                    DROP COLUMN IF EXISTS approval_origin;

                ALTER TABLE tramites.procedure_instance_biometric_validations
                    ALTER COLUMN status TYPE varchar(20);

                COMMENT ON COLUMN tramites.procedure_instance_biometric_validations.status IS
                    'enviado|en_proceso|aprobado|rechazado|expirado';
                """);
    }
}
