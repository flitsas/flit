using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14044 (Feature #14023, Épica #14021) — ancla de la validación de identidad del portal de Persona Natural: columnas
/// <c>portal_solicitud_ref</c> y <c>portal_parte</c> en <c>tramites.procedure_instance_biometric_validations</c>, CHECKs,
/// índice de lectura y unicidad en vuelo por (solicitud, rol, documento); la unicidad en vuelo por documento deja de contar
/// las filas del portal. Aditiva, idempotente, sin backfill. DDL: <c>160-HU14044-validacion-identidad-portal.sql</c>. Lleva
/// los atributos inline (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028120000_HU14044_ValidacionIdentidadPortal")]
public partial class HU14044_ValidacionIdentidadPortal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("160-HU14044-validacion-identidad-portal.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_portal;
            DROP INDEX IF EXISTS tramites.ix_biometric_validations_portal_solicitud;

            -- Las validaciones del portal no tienen representación sin las columnas: se retiran antes.
            DELETE FROM tramites.procedure_instance_biometric_validations WHERE portal_solicitud_ref IS NOT NULL;

            DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_doc_norm;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_biometric_validations_inflight_doc_norm
                ON tramites.procedure_instance_biometric_validations (
                    tenant_id, upper(btrim(document_type)), upper(btrim(document_number)))
                WHERE status IN ('pendiente_envio', 'enviado', 'en_proceso') AND deleted_at IS NULL AND mandate_signer_id IS NULL;

            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                ADD CONSTRAINT ck_biometric_validation_anchor
                CHECK (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL OR mandate_signer_id IS NOT NULL);
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP CONSTRAINT IF EXISTS ck_biometric_validations_portal_ref;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP COLUMN IF EXISTS portal_parte,
                DROP COLUMN IF EXISTS portal_solicitud_ref;
            """);
}
