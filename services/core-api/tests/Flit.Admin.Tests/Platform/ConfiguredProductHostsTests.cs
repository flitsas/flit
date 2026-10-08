using Flit.Api.Platform;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Platform;

/// <summary>
/// Dónde vive cada producto (contrato v1 §1). Un producto «próximamente» (sin app desplegada en el ambiente) lleva a
/// su pantalla en el hub en vez de a un host que no responde.
/// </summary>
public sealed class ConfiguredProductHostsTests
{
    private static ConfiguredProductHosts Hosts(Action<SuiteHostsOptions> configure)
    {
        var options = new SuiteHostsOptions { Environment = "dev" };
        configure(options);
        var monitor = Substitute.For<IOptionsMonitor<SuiteHostsOptions>>();
        monitor.CurrentValue.Returns(options);
        return new ConfiguredProductHosts(monitor);
    }

    [Theory]
    [InlineData("plataforma", "https://dev.flitsas.online")]
    [InlineData("tramites", "https://dev.tramites.flitsas.online")]
    [InlineData("comparendos", "https://dev.comparendos.flitsas.online")]
    public void SinProximamente_CadaProductoEnSuHost(string code, string expected)
    {
        var hosts = Hosts(_ => { });
        hosts.UrlFor(code).Should().Be(expected);
        hosts.LinkFor(code).Should().Be(expected);
    }

    [Fact]
    public void ProductoProximamente_LlevaASuPantallaEnElHub()
    {
        var hosts = Hosts(o => o.ComingSoon = ["comparendos", "diagnostico"]);

        hosts.LinkFor("comparendos").Should().Be("https://dev.flitsas.online/proximamente/comparendos");
        hosts.LinkFor("diagnostico").Should().Be("https://dev.flitsas.online/proximamente/diagnostico");
        hosts.LinkFor("tramites").Should().Be("https://dev.tramites.flitsas.online");
        hosts.IsComingSoon("comparendos").Should().BeTrue();
        hosts.IsComingSoon("tramites").Should().BeFalse();
    }

    [Fact]
    public void ProductoProximamente_ConservaSuOrigenParaElLogin() =>
        Hosts(o => o.ComingSoon = ["comparendos"]).UrlFor("comparendos").Should().Be("https://dev.comparendos.flitsas.online");

    [Fact]
    public void ProductoProximamente_EnLocalUsaLaDireccionDelHub()
    {
        var hosts = Hosts(o =>
        {
            o.Overrides["plataforma"] = "http://127.0.0.1:4022/";
            o.ComingSoon = ["comparendos"];
        });

        hosts.LinkFor("comparendos").Should().Be("http://127.0.0.1:4022/proximamente/comparendos");
    }

    [Fact]
    public void UnReemplazoExplicito_GanaAProximamente()
    {
        var hosts = Hosts(o =>
        {
            o.Overrides["comparendos"] = "http://127.0.0.1:4023";
            o.ComingSoon = ["comparendos"];
        });

        hosts.LinkFor("comparendos").Should().Be("http://127.0.0.1:4023");
        hosts.IsComingSoon("comparendos").Should().BeFalse();
    }

    [Fact]
    public void Proximamente_NoCambiaQueProductoEsCadaHost() =>
        Hosts(o => o.ComingSoon = ["comparendos"]).ProductForHost("dev.comparendos.flitsas.online").Should().Be("comparendos");

    // ── Raíces alternativas (PDN: flitsas.online y, a la vez, app.flitsas.com + <producto>.flitsas.com) ──

    private static ConfiguredProductHosts Pdn(Action<SuiteHostsOptions>? configure = null) => Hosts(o =>
    {
        o.Environment = string.Empty;
        o.AlternateRoots = [new SuiteAlternateRoot { Hub = "app.flitsas.com", Products = "{product}.flitsas.com" }];
        configure?.Invoke(o);
    });

    [Theory]
    [InlineData("app.flitsas.com", "plataforma")]
    [InlineData("APP.flitsas.com:443", "plataforma")]
    [InlineData("tramites.flitsas.com", "tramites")]
    [InlineData("flitsas.online", "plataforma")]
    [InlineData("tramites.flitsas.online", "tramites")]
    public void RaizAlternativa_ReconoceSusHosts(string host, string expected) =>
        Pdn().ProductForHost(host).Should().Be(expected);

    [Theory]
    [InlineData("flitsas.com")]
    [InlineData("otro.example.com")]
    [InlineData("app.flitsas.com.evil.com")]
    public void RaizAlternativa_NoReconoceHostsAjenos(string host) =>
        Pdn().ProductForHost(host).Should().BeNull();

    [Theory]
    [InlineData("app.flitsas.com", "tramites", "https://tramites.flitsas.com")]
    [InlineData("tramites.flitsas.com", "plataforma", "https://app.flitsas.com")]
    [InlineData("flitsas.online", "tramites", "https://tramites.flitsas.online")]
    [InlineData(null, "tramites", "https://tramites.flitsas.online")]
    [InlineData("otro.example.com", "tramites", "https://tramites.flitsas.online")]
    public void RaizAlternativa_UrlEnLaRaizDeLaPeticion(string? host, string code, string expected) =>
        Pdn().UrlFor(code, host).Should().Be(expected);

    [Fact]
    public void RaizAlternativa_ProximamenteEnElHubDeEsaRaiz() =>
        Pdn(o => o.ComingSoon = ["comparendos"]).LinkFor("comparendos", "app.flitsas.com")
            .Should().Be("https://app.flitsas.com/proximamente/comparendos");

    [Fact]
    public void RaizAlternativa_TodasLasUrlsDelProducto()
    {
        Pdn().AllUrlsFor("tramites").Should().Equal("https://tramites.flitsas.online", "https://tramites.flitsas.com");
        Pdn().AllUrlsFor("plataforma").Should().Equal("https://flitsas.online", "https://app.flitsas.com");
    }

    [Fact]
    public void SinRaizAlternativa_UnaSolaUrl() =>
        Hosts(_ => { }).AllUrlsFor("tramites").Should().Equal("https://dev.tramites.flitsas.online");

    [Fact]
    public void RaizAlternativaVacia_SeIgnora()
    {
        // Así llega del compose en DEV/QA: Suite__Hosts__AlternateRoots__0__Hub y __Products vacías.
        var hosts = Hosts(o => o.AlternateRoots = [new SuiteAlternateRoot()]);
        hosts.AllUrlsFor("tramites").Should().Equal("https://dev.tramites.flitsas.online");
        hosts.ProductForHost("").Should().BeNull();
        hosts.UrlFor("tramites", "app.flitsas.com").Should().Be("https://dev.tramites.flitsas.online");
    }

    [Fact]
    public void ConReemplazo_SoloElReemplazo() =>
        Pdn(o => o.Overrides["tramites"] = "http://127.0.0.1:3000").AllUrlsFor("tramites").Should().Equal("http://127.0.0.1:3000");
}
