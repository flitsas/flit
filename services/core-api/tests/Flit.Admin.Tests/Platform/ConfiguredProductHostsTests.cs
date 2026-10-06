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
}
