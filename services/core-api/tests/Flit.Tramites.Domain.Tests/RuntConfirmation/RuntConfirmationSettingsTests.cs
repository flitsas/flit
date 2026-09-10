using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.RuntConfirmation;

/// <summary>HU #12277 — valores por defecto (AC1) y validación de rangos (AC2) de la configuración global.</summary>
public sealed class RuntConfirmationSettingsTests
{
    [Fact]
    public void Defaults_SonLosDelContrato()
    {
        var s = RuntConfirmationSettings.Defaults();

        s.Enabled.Should().BeFalse();
        s.RunAtLocal.Should().Be("02:00");
        s.ProviderKey.Should().Be("kyverum_runt");
        s.GraceDays.Should().Be(0, "no se hereda el default de 7 días de v1");
        s.DiscrepancyAfterRuns.Should().Be(3);
        s.MaxAttempts.Should().Be(10);
        s.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, 3, 10, "kyverum_runt", "02:00", "graceDays")]
    [InlineData(0, 0, 10, "kyverum_runt", "02:00", "discrepancyAfterRuns")]
    [InlineData(0, 5, 3, "kyverum_runt", "02:00", "maxAttempts")]
    [InlineData(0, 3, 10, "otro", "02:00", "providerKey")]
    [InlineData(0, 3, 10, "verifik", "25:00", "runAtLocal")]
    [InlineData(0, 3, 10, "verifik", "2am", "runAtLocal")]
    public void Validate_IdentificaElCampoInvalido(int grace, int discrepancy, int max, string provider, string runAt, string field)
    {
        var s = new RuntConfirmationSettings
        {
            GraceDays = grace,
            DiscrepancyAfterRuns = discrepancy,
            MaxAttempts = max,
            ProviderKey = provider,
            RunAtLocal = runAt,
        };

        s.Validate().Should().ContainSingle().Which.Field.Should().Be(field);
    }

    [Fact]
    public void Validate_AceptaVerifikYLimites()
    {
        new RuntConfirmationSettings { ProviderKey = "verifik", RunAtLocal = "23:59", DiscrepancyAfterRuns = 1, MaxAttempts = 1 }
            .Validate().Should().BeEmpty();
    }
}
