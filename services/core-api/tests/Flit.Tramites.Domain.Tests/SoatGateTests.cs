using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>Gate duro de SOAT en la ruta de placa (R06, Feature #10587 · HU #10611).</summary>
public sealed class SoatGateTests
{
    [Theory]
    // HU #10611: bloquea SALVO que esté vigente (sin evidencia de SOAT el OT no aprueba).
    [InlineData("vigente", false)]  // única evidencia válida → no bloquea.
    [InlineData("VIGENTE", false)]  // case-insensitive.
    [InlineData("vencido", true)]   // vencido → bloquea.
    [InlineData("unknown", true)]   // sin registrar → bloquea.
    [InlineData(null, true)]        // ausente → bloquea.
    [InlineData("", true)]          // vacío → bloquea.
    public void BlocksApproval_BloqueaSalvoVigente(string? estado, bool bloquea)
    {
        SoatGate.BlocksApproval(estado).Should().Be(bloquea);
    }

    [Theory]
    [InlineData("vigente", true)]
    [InlineData("VIGENTE", true)]
    [InlineData("vencido", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void IsSatisfied_SoloVigente(string? estado, bool satisfecho)
    {
        SoatGate.IsSatisfied(estado).Should().Be(satisfecho);
    }
}
