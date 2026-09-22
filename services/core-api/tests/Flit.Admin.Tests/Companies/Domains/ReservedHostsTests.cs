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

    // HU #12761 — excepción exacta a la zona reservada para los tres hosts de prueba de marca blanca.
    private static readonly string[] Allowed =
    [
        "marcablancadev.flitsas.online",
        "marcablancaqa.flitsas.online",
        "marcablancapdn.flitsas.online",
    ];

    [Theory]
    [InlineData("marcablancadev.flitsas.online")]
    [InlineData("marcablancaqa.flitsas.online")]
    [InlineData("marcablancapdn.flitsas.online")]
    public void AC1_HostDePruebaPermitido_NoEsReservado(string host)
    {
        ReservedHosts.IsReserved(host, Patterns, Allowed).Should().BeFalse();
    }

    [Theory]
    [InlineData("app.flitsas.online")]
    [InlineData("dev.flitsas.online")]
    [InlineData("flitsas.online")]
    [InlineData("flitsas.com")]
    public void AC2_HostReservadoNoPermitido_SigueSiendoReservado(string host)
    {
        ReservedHosts.IsReserved(host, Patterns, Allowed).Should().BeTrue();
    }

    [Theory]
    [InlineData("sub.marcablancadev.flitsas.online")]
    [InlineData("a.b.marcablancaqa.flitsas.online")]
    public void AC3_SubdominioDeHostPermitido_SigueSiendoReservado(string host)
    {
        ReservedHosts.IsReserved(host, Patterns, Allowed).Should().BeTrue();
    }

    [Fact]
    public void AC3_HostQueSoloPrefijaAlPermitido_NoEsExceptuado()
    {
        // Fuera de la zona reservada: no es reservado, pero tampoco por la excepción (no coincide exacto).
        ReservedHosts.IsReserved("marcablancadev.flitsas.online.evil.com", Patterns, Allowed).Should().BeFalse();

        // Bajo un patrón reservado y solo prefijando al permitido ⇒ sigue reservado.
        ReservedHosts.IsReserved("marcablancadev.flitsas.online.flitsas.com", ["*.flitsas.com"], Allowed).Should().BeTrue();
    }

    [Fact]
    public void AC8_ComparacionInsensibleAMayusculas_ElHostPermitidoSeExceptua()
    {
        ReservedHosts.IsReserved("MarcaBlancaDev.FLITSAS.online", Patterns, Allowed).Should().BeFalse();
    }

    [Fact]
    public void AC2_ListaDePermitidosVacia_MantieneElComportamientoAnterior()
    {
        ReservedHosts.IsReserved("marcablancadev.flitsas.online", Patterns, []).Should().BeTrue();
        ReservedHosts.IsReserved("marcablancadev.flitsas.online", Patterns).Should().BeTrue();
    }
}
