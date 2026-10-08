using Flit.Api.Identity;
using Flit.Api.Platform;
using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// Raíces alternativas de la suite (<c>Suite:Hosts:AlternateRoots</c>): en PDN el hub vive en <c>flitsas.online</c> y,
/// a la vez, en <c>app.flitsas.com</c>. Quien entra por una raíz no sale de ella durante el login: el emisor es el hub
/// de esa raíz y los clientes OIDC aceptan los retornos de ambas.
/// </summary>
public sealed class OidcAlternateRootTests
{
    private static ConfiguredProductHosts PdnHosts()
    {
        var options = new SuiteHostsOptions
        {
            AlternateRoots = [new SuiteAlternateRoot { Hub = "app.flitsas.com", Products = "{product}.flitsas.com" }],
        };
        var monitor = Substitute.For<IOptionsMonitor<SuiteHostsOptions>>();
        monitor.CurrentValue.Returns(options);
        return new ConfiguredProductHosts(monitor);
    }

    private static IDomainContextAccessor Sealed(string? host)
    {
        var domain = Substitute.For<IDomainContextAccessor>();
        domain.Kind.Returns(DomainKind.Flit);
        domain.Host.Returns(host);
        return domain;
    }

    [Theory]
    [InlineData("app.flitsas.com", "https://app.flitsas.com/")]
    [InlineData("flitsas.online", "https://flitsas.online/")]
    [InlineData("tramites.flitsas.com", "https://app.flitsas.com/")]
    [InlineData("tramites.flitsas.online", "https://flitsas.online/")]
    [InlineData(null, "https://flitsas.online/")]
    [InlineData("gateway", "https://flitsas.online/")]
    public void Emisor_EsElHubDeLaRaizDeLaPeticion(string? host, string expected) =>
        OidcIssuer.Resolve(Sealed(host), PdnHosts(), "https").ToString().Should().Be(expected);

    [Fact]
    public void ClienteDeProducto_AceptaLosRetornosDeAmbasRaices()
    {
        var descriptor = OidcClientSync.ProductClient("tramites", PdnHosts(), new OidcOptions());

        descriptor.RedirectUris.Select(u => u.ToString()).Should().BeEquivalentTo(
            "https://tramites.flitsas.online/auth/callback", "https://tramites.flitsas.com/auth/callback");
        descriptor.PostLogoutRedirectUris.Select(u => u.ToString()).Should().BeEquivalentTo(
            "https://tramites.flitsas.online/", "https://tramites.flitsas.com/");
    }

    [Fact]
    public void ClienteDelHub_AceptaLosRetornosDeAmbosHubs() =>
        OidcClientSync.ProductClient("plataforma", PdnHosts(), new OidcOptions())
            .RedirectUris.Select(u => u.ToString()).Should().BeEquivalentTo(
                "https://flitsas.online/auth/callback", "https://app.flitsas.com/auth/callback");
}
