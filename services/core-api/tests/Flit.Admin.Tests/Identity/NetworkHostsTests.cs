using Flit.Api.Endpoints.Platform;
using Flit.Modules.Platform.Application.Apps;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// Marca Blanca con la suite — el menú de productos visto desde un dominio de red enlaza los dominios de esa red.
/// </summary>
public sealed class NetworkHostsTests
{
    private static readonly IReadOnlyList<MyAppView> Apps =
    [
        new("plataforma", "Inicio", "home", "https://dev.flitsas.online", false, false),
        new("tramites", "Trámites", "file", "https://dev.tramites.flitsas.online", false, false),
        new("comparendos", "Comparendos", "car", "https://dev.flitsas.online/proximamente/comparendos", false, true),
        new("diagnostico", "Diagnóstico", "chart", "https://dev.diagnostico.flitsas.online", false, false),
    ];

    private static readonly Dictionary<string, string> Red = new()
    {
        ["HUB"] = "marca.cliente.com",
        ["tramites"] = "tramites.cliente.com",
    };

    [Fact]
    public void ProductosConDominioEnLaRed_SeAbrenEnEl_YMarcaElActual()
    {
        var apps = NetworkHosts.Rebase(Apps, Red, "marca.cliente.com").ToDictionary(a => a.Code);

        apps["plataforma"].Url.Should().Be("https://marca.cliente.com");
        apps["plataforma"].Current.Should().BeTrue();
        apps["tramites"].Url.Should().Be("https://tramites.cliente.com");
        apps["tramites"].Current.Should().BeFalse();
    }

    [Fact]
    public void Proximamente_VaAlHubDeLaRed_YSinDominioConservaElDeFlit()
    {
        var apps = NetworkHosts.Rebase(Apps, Red, "tramites.cliente.com").ToDictionary(a => a.Code);

        apps["comparendos"].Url.Should().Be("https://marca.cliente.com/proximamente/comparendos");
        apps["diagnostico"].Url.Should().Be("https://dev.diagnostico.flitsas.online");
        apps["tramites"].Current.Should().BeTrue();
    }

    [Fact]
    public void RedSinDominios_NoCambiaNada()
    {
        NetworkHosts.Rebase(Apps, new Dictionary<string, string>(), "marca.cliente.com").Should().Equal(Apps);
        NetworkHosts.HubUrl(new Dictionary<string, string>()).Should().BeNull();
    }
}
