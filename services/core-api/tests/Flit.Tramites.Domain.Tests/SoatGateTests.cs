using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>Gate duro de SOAT en la ruta de placa (R06, Feature #10587).</summary>
public sealed class SoatGateTests
{
    [Theory]
    [InlineData("vencido", true)]
    [InlineData("VENCIDO", true)]
    [InlineData("vigente", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void BlocksApproval_SoloVencidoBloquea(string? estado, bool bloquea)
    {
        SoatGate.BlocksApproval(estado).Should().Be(bloquea);
    }
}
