using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FurCopropiedadObservationTests
{
    [Fact]
    public void Compose_SingleActor_ReturnsNull()
    {
        FurCopropiedadObservation.Compose(
            [new DocumentParte("comprador", "SOLO UNO", "1", null, OwnershipPercentage: 100m)])
            .Should().BeNull();
    }

    [Fact]
    public void Compose_TwoBuyers_EmitsPercentLiteral()
    {
        var text = FurCopropiedadObservation.Compose(
        [
            new DocumentParte("comprador", "EUGENIA MARIA CARDENAS TORRES", "1", null, Ordinal: 1, OwnershipPercentage: 50m),
            new DocumentParte("comprador", "JULIO MARIO FONNEGRA SUCERQUIA", "2", null, Ordinal: 2, OwnershipPercentage: 50m),
        ]);

        text.Should().Be(
            "EUGENIA MARIA CARDENAS TORRES es el propietario del 50%. JULIO MARIO FONNEGRA SUCERQUIA es el propietario del 50%.");
    }

    [Theory]
    [InlineData(33.3, "33.30")]
    [InlineData(50, "50")]
    public void FormatoPorcentaje_RoundsTwoDecimals(decimal value, string expected) =>
        FurCopropiedadObservation.FormatoPorcentaje(value).Should().Be(expected);
}
