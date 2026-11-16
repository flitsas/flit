using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #14319 (Épica #14296) AC2 — el canal efectivo: un trámite sin canal (histórico, incluido el migrado) se lee
/// como Trámites; un canal con dato se conserva tal cual (ICT nunca se disfraza).
/// Uso de ejemplo: <c>TramiteCanal.Efectivo(null)</c> devuelve <c>"tramites"</c>.
/// </summary>
public sealed class TramiteCanalEfectivoTests
{
    [Fact]
    public void AC2_SinDatoSeLeeComoTramites() => TramiteCanal.Efectivo(null).Should().Be(TramiteCanal.Tramites);

    [Theory]
    [InlineData("tramites")]
    [InlineData("pn_web")]
    [InlineData("pn_punto")]
    [InlineData("integracion")]
    [InlineData("aplicacion")]
    public void AC2_ConDatoSeConserva(string codigo) => TramiteCanal.Efectivo(codigo).Should().Be(codigo);
}
