using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13246 (Feature #13245 F9, Épica #13090) — validación de identidad PROPIA del mandatario: columna
    /// <c>tramites.procedure_instance_biometric_validations.mandate_signer_id</c> (FK a <c>admin.mandate_signers</c>), party_role
    /// <c>mandatario</c> con CHECK de coherencia, ancla ampliada, índice por ficha y unicidad en vuelo por mandatario (la
    /// unicidad en vuelo por documento deja de contar las filas del mandatario). Aditiva, idempotente, sin backfill. DDL:
    /// <c>129-HU13246-mandatario-validacion-propia.sql</c>. Descubrible al arrancar por el Designer.cs.
    /// </summary>
    public partial class HU13246_MandatarioValidacionPropia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("129-HU13246-mandatario-validacion-propia.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_mandate_signer;
                DROP INDEX IF EXISTS tramites.ix_biometric_validations_mandate_signer;

                -- Las validaciones del mandatario no tienen representación sin la columna: se retiran antes.
                DELETE FROM tramites.procedure_instance_biometric_validations WHERE mandate_signer_id IS NOT NULL;

                DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_doc_norm;
                CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_doc_norm
                    ON tramites.procedure_instance_biometric_validations (
                        tenant_id, upper(btrim(document_type)), upper(btrim(document_number)))
                    WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso') AND deleted_at IS NULL;

                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    ADD CONSTRAINT ck_biometric_validation_anchor
                    CHECK (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL);
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validations_mandatario_ref;
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS fk_procedure_instance_biometric_validations_mandate_signers;
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP COLUMN IF EXISTS mandate_signer_id;
                """);
    }
}
