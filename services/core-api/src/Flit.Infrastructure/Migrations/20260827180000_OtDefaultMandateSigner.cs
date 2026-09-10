using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>HU-L8 — default mandatario global en config de mandato del OT.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260827180000_OtDefaultMandateSigner")]
public partial class OtDefaultMandateSigner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("95-ot-default-mandate-signer.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.transit_office_mandate_config
              DROP CONSTRAINT IF EXISTS fk_tomc_default_mandate_signer;
            DROP INDEX IF EXISTS admin.ix_transit_office_mandate_config_default_signer;
            ALTER TABLE admin.transit_office_mandate_config
              DROP COLUMN IF EXISTS default_mandate_signer_id;
            """);
    }
}
