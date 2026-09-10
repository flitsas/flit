using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11469 (Feature #11460) — columna
/// <c>admin.tenant_operational_policies.tramite_state_emails_enabled</c>
/// (DEFAULT true). Solo esquema; el worker (HU #11467) la consume.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260812130000_TramiteStateEmailsEnabled")]
public partial class TramiteStateEmailsEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("70-tramite-state-emails-enabled.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS tramite_state_emails_enabled;
            """);
    }
}
