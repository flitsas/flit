using System.Text;
using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Biometrics;

public sealed class MockBiometricScorerTests
{
    private readonly MockBiometricScorer _scorer = new();

    private static byte[] Bytes(string s = "img") => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Score_ThreePhotos_ApprovesAboveThreshold()
    {
        var result = _scorer.Score(new BiometricPhotos(Bytes(), Bytes(), Bytes()));

        result.Aprobado.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(BiometricRules.ThresholdAprobacion);
    }

    [Fact]
    public void Score_MissingPhoto_RejectsWithMotivo()
    {
        var result = _scorer.Score(new BiometricPhotos(Bytes(), null, Bytes()));

        result.Aprobado.Should().BeFalse();
        result.Score.Should().Be(0);
        result.Motivo.Should().Contain("cedula_frontal");
    }

    [Fact]
    public void Score_EmptyPhoto_TreatedAsMissing()
    {
        var result = _scorer.Score(new BiometricPhotos(Bytes(), Bytes(), Array.Empty<byte>()));

        result.Aprobado.Should().BeFalse();
        result.Motivo.Should().Contain("cedula_reverso");
    }

    [Fact]
    public void Score_AllMissing_ListsAllThree()
    {
        var result = _scorer.Score(new BiometricPhotos(null, null, null));

        result.Aprobado.Should().BeFalse();
        result.Motivo.Should().Contain("rostro");
        result.Motivo.Should().Contain("cedula_frontal");
        result.Motivo.Should().Contain("cedula_reverso");
    }
}
