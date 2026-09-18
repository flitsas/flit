using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// Uso de ejemplo: <c>ReservedHosts.IsReserved("app.flitsas.online", ["*.flitsas.online"])</c> → <c>true</c>.
/// HU #12416 AC3 — el dominio de FLIT (y subdominios) se rechaza; la lista real vive en
/// <c>Domains:Reserved</c> (<c>appsettings.json</c>), aquí se prueba la regla de comparación pura.
/// </summary>
public sealed class ReservedHostsTests
{
    private static readonly string[] Patterns = ["*.flitsas.online", "flitsas.com"];

    [Theory]
    [InlineData("app.flitsas.online")]
    [InlineData("flitsas.online")]
    [InlineData("sub.app.flitsas.online")]
    [InlineData("flitsas.com")]
    public void AC3_HostReservadoOSubdominio_EsRechazado(string host)
    {
        ReservedHosts.IsReserved(host, Patterns).Should().BeTrue();
    }

    [Theory]
    [InlineData("app.movilidadandina.com")]
    [InlineData("notflitsas.online")]
    [InlineData("flitsas.com.co")]
    public void AC3_HostAjeno_NoEsReservado(string host)
    {
        ReservedHosts.IsReserved(host, Patterns).Should().BeFalse();
    }

    [Fact]
    public void SinPatronesConfigurados_NadaEsReservado()
    {
        ReservedHosts.IsReserved("app.flitsas.online", []).Should().BeFalse();
    }
}
