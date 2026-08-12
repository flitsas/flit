using Flit.Tramites.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Notifications;

/// <summary>HU #11463 — mapa estado → plantilla.</summary>
public sealed class TramiteStateChangeTemplateMapTests
{
    [Theory]
    [InlineData("aprobado", "tramites.aprobado")]
    [InlineData("APROBADO", "tramites.aprobado")]
    [InlineData("rechazado", "tramites.rechazado")]
    [InlineData("Rechazado", "tramites.rechazado")]
    public void SoloAprobadoYRechazadoTienenPlantilla(string status, string expected)
    {
        TramiteStateChangeTemplateMap.ResolveTemplateKey(status).Should().Be(expected);
    }

    [Theory]
    [InlineData("anulado")]
    [InlineData("entregado")]
    [InlineData("preparado")]
    [InlineData("")]
    [InlineData("   ")]
    public void TransicionSinPlantilla_NoInventaNiLanza(string status)
    {
        var act = () => TramiteStateChangeTemplateMap.ResolveTemplateKey(status);
        act.Should().NotThrow();
        act().Should().BeNull();
    }
}
