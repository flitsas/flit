using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13128 (Feature #13114, Épica #13090, ADR-0061) — modelo, forma de firma, vigencia propia y baja lógica
/// del mandatario (<c>admin.mandate_signers</c>) y origen de la configuración (<c>configured_by_scope</c>) en
/// reglas por compañía, configuración del OT y vínculos mandatario-compañía. Aditiva e idempotente.
/// DDL: <c>122-HU13128-mandatario-modelo-vigencia-origen.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930120000_HU13128_MandatarioModeloVigenciaOrigen")]
public partial class HU13128_MandatarioModeloVigenciaOrigen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("122-HU13128-mandatario-modelo-vigencia-origen.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS admin.ix_mandate_signers_alive;

            ALTER TABLE admin.mandate_signer_companies
                DROP CONSTRAINT IF EXISTS ck_mandate_signer_companies_configured_by_scope,
                DROP COLUMN IF EXISTS configured_by_scope;
            ALTER TABLE admin.transit_office_mandate_config
                DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_configured_by_scope,
                DROP COLUMN IF EXISTS configured_by_scope;
            ALTER TABLE admin.company_ot_mandate_rules
                DROP CONSTRAINT IF EXISTS ck_company_ot_mandate_rules_configured_by_scope,
                DROP COLUMN IF EXISTS configured_by_scope;

            ALTER TABLE admin.mandate_signers
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_document_required,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_model_coherence,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_validity_fixed,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_validity_range,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_validity_kind,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_signature_method,
                DROP CONSTRAINT IF EXISTS ck_mandate_signers_signer_model;

            -- Restaura NOT NULL de document_number: las filas sin documento (formato_blanco) quedan con 'N/A'.
            UPDATE admin.mandate_signers SET document_number = 'N/A' WHERE document_number IS NULL;
            ALTER TABLE admin.mandate_signers ALTER COLUMN document_number SET NOT NULL;

            ALTER TABLE admin.mandate_signers
                DROP COLUMN IF EXISTS deleted_by,
                DROP COLUMN IF EXISTS deleted_at,
                DROP COLUMN IF EXISTS valid_to,
                DROP COLUMN IF EXISTS valid_from,
                DROP COLUMN IF EXISTS validity_kind,
                DROP COLUMN IF EXISTS signature_method,
                DROP COLUMN IF EXISTS signer_model;
            """);
}
