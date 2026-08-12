using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>HU #11469 — DDL del interruptor operativo de avisos de cambio de estado.</summary>
public sealed class TramiteStateEmailsEnabledSchemaTests
{
    [Fact]
    public void ColumnaConDefaultTrueEIdempotente()
    {
        var sql = EmbeddedDdl.LoadUp("70-tramite-state-emails-enabled.sql");

        sql.Should().Contain(
            "ADD COLUMN IF NOT EXISTS tramite_state_emails_enabled boolean NOT NULL DEFAULT true");
        sql.Should().Contain("COMMENT ON COLUMN admin.tenant_operational_policies.tramite_state_emails_enabled");
        sql.Should().NotContain("DEFAULT false");
    }
}
