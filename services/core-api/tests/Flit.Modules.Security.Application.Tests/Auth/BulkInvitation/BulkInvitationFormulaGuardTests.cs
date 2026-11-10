using Flit.Modules.Security.Application.Auth.BulkInvitation;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.BulkInvitation;

/// <summary>HU #14422 — neutralización de inyección de fórmulas en celdas Excel/CSV.</summary>
public sealed class BulkInvitationFormulaGuardTests
{
    [Theory]
    [InlineData("=CMD()", true)]
    [InlineData("+1+1", true)]
    [InlineData("-1+1", true)]
    [InlineData("@SUM(A1)", true)]
    [InlineData("Radicador", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRisky_DetectsLeadingFormulaChars(string? value, bool expected) =>
        BulkInvitationFormulaGuard.IsRisky(value).Should().Be(expected);

    [Fact]
    public void Neutralize_PrefixesRiskyValuesWithApostrophe()
    {
        BulkInvitationFormulaGuard.Neutralize("=1+1").Should().Be("'=1+1");
        BulkInvitationFormulaGuard.Neutralize("Nombre").Should().Be("Nombre");
        BulkInvitationFormulaGuard.Neutralize(null).Should().BeNull();
    }
}
