using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

public sealed class TramiteStateEmailRecipientsSchemaTests
{
    [Fact]
    public void InterruptoresYDestinatariosConDefaultEncendido()
    {
        var sql = EmbeddedDdl.LoadUp("72-tramite-state-email-recipients.sql");

        sql.Should().Contain("tramite_approved_emails_enabled boolean NOT NULL DEFAULT true");
        sql.Should().Contain("tramite_rejected_emails_enabled boolean NOT NULL DEFAULT true");
        sql.Should().Contain("tramite_state_email_recipients jsonb NOT NULL");
        sql.Should().Contain("DROP COLUMN IF EXISTS tramite_state_emails_enabled");
        sql.Should().Contain("Ley 1581");
        sql.Should().Contain("extraEmail");
    }
}
