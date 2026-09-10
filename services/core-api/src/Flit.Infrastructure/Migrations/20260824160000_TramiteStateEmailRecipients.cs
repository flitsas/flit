using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

[DbContext(typeof(FlitDbContext))]
[Migration("20260824160000_TramiteStateEmailRecipients")]
public partial class TramiteStateEmailRecipients : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("72-tramite-state-email-recipients.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              ADD COLUMN IF NOT EXISTS tramite_state_emails_enabled boolean NOT NULL DEFAULT true;

            UPDATE admin.tenant_operational_policies
            SET tramite_state_emails_enabled =
              COALESCE(tramite_approved_emails_enabled, true)
              AND COALESCE(tramite_rejected_emails_enabled, true);

            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS tramite_approved_emails_enabled,
              DROP COLUMN IF EXISTS tramite_rejected_emails_enabled,
              DROP COLUMN IF EXISTS tramite_state_email_recipients;
            """);
    }
}
