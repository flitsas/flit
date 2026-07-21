using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-05 (CFD-08) — gate puro del paso de solicitud de placa.
/// Cubre BE-05-AC-02 (aplica si el flag está activo), AC-03 (se omite si false) y AC-04 (bloquea
/// hasta completar). El wiring en WizardStateQuery es HU-BE-06.
/// </summary>
public sealed class PlateRequestGateTests
{
    private static ProcedureTypeGateProfile Profile(bool requiresPlate) =>
        new() { RequiresPlateRequest = requiresPlate };

    [Fact]
    public void AppliesTo_TrueWhenFlagOn()
    {
        // BE-05-AC-02
        PlateRequestGate.AppliesTo(Profile(true)).Should().BeTrue();
    }

    [Fact]
    public void AppliesTo_FalseWhenFlagOff()
    {
        // BE-05-AC-03: el paso se omite.
        PlateRequestGate.AppliesTo(Profile(false)).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FlagOnAndNotCompleted_BlocksWithPending()
    {
        // BE-05-AC-04
        var result = PlateRequestGate.Evaluate(Profile(true), plateRequestCompleted: false);

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(PlateRequestGate.PlateRequestPending);
    }

    [Fact]
    public void Evaluate_FlagOnAndCompleted_Allows()
    {
        PlateRequestGate.Evaluate(Profile(true), plateRequestCompleted: true).Ok.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FlagOff_AlwaysAllows()
    {
        PlateRequestGate.Evaluate(Profile(false), plateRequestCompleted: false).Ok.Should().BeTrue();
    }
}
