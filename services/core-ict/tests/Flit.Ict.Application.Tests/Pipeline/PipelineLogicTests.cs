using Flit.Ict.Domain.Enums;
using Flit.Ict.Domain.Jobs;
using Flit.Ict.Domain.Validation;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Pipeline;

public sealed class ExternalSourceValidatorsTests
{
    private const int CurrentYear = 2026;

    [Fact]
    public void Soat_not_vigente_blocks()
    {
        var issues = ExternalSourceValidators.Validate(3, new ConsultationResult(SoatStatus: "VENCIDO"), CurrentYear);
        issues.Should().ContainSingle().Which.Should().Contain("SOAT");
    }

    [Fact]
    public void Rtm_expired_on_old_vehicle_blocks_except_unilateral_transfer()
    {
        var old = new ConsultationResult(SoatStatus: "VIGENTE", RtmStatus: "VENCIDO", VehicleModelYear: 2015);

        ExternalSourceValidators.Validate(3, old, CurrentYear).Should().Contain(i => i.Contains("RTM"));
        // Traspaso unilateral (tipo 4) solo advierte: no bloquea.
        ExternalSourceValidators.Validate(4, old, CurrentYear).Should().BeEmpty();
    }

    [Fact]
    public void Active_sanctions_block()
    {
        var issues = ExternalSourceValidators.Validate(3, new ConsultationResult(HasActiveSanctions: true), CurrentYear);
        issues.Should().Contain(i => i.Contains("sanciones"));
    }

    [Fact]
    public void All_valid_passes()
    {
        var ok = new ConsultationResult(SoatStatus: "VIGENTE", RtmStatus: "VIGENTE", VehicleModelYear: 2024, PazYSalvo: true);
        ExternalSourceValidators.Validate(3, ok, CurrentYear).Should().BeEmpty();
    }
}

public sealed class IctWindowEvaluatorTests
{
    [Theory]
    [InlineData("2026-07-22T14:00:00Z", true)]  // Bogotá 09:00 -> dentro
    [InlineData("2026-07-22T12:00:00Z", false)] // Bogotá 07:00 -> antes de las 08
    [InlineData("2026-07-23T02:00:00Z", false)] // Bogotá 21:00 -> después de las 20
    public void Window_08_20(string utc, bool expected)
    {
        var when = DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal);
        IctWindowEvaluator.IsWithinWindow(when, 8, 20).Should().Be(expected);
    }
}

public sealed class IctEstadoMapTests
{
    [Fact]
    public void Maps_internal_status_to_v2_vocabulary()
    {
        IctEstado.Map(1, hasProcedureInstance: false, businessValidated: false, externalStarted: false)
            .Should().Be(IctEstado.Recibido);
        IctEstado.Map(2, false, businessValidated: false, false).Should().Be(IctEstado.EnValidacionNegocio);
        IctEstado.Map(2, false, businessValidated: true, false).Should().Be(IctEstado.EnValidacionExterna);
        IctEstado.Map(4, false, true, false).Should().Be(IctEstado.ConNovedades);
        IctEstado.Map(3, hasProcedureInstance: true, true, false).Should().Be(IctEstado.BorradorCreado);
    }
}
