using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>Default mandatario persona en regla compañía×OT.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260806240000_CompanyOtDefaultMandateSigner")]
public partial class CompanyOtDefaultMandateSigner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("62-company-ot-default-mandate-signer.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.company_ot_mandate_rules
              DROP CONSTRAINT IF EXISTS fk_comr_default_mandate_signer;
            DROP INDEX IF EXISTS admin.ix_company_ot_mandate_rules_default_signer;
            ALTER TABLE admin.company_ot_mandate_rules
              DROP COLUMN IF EXISTS default_mandate_signer_id;
            """);
    }
}
