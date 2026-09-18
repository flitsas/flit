using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Middleware;
using Flit.Modules.Security.Application.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Middleware;

/// <summary>
/// HU #12417 AC2 — <see cref="DomainContextMiddleware.ResolveAsync"/> (misma lógica que el
/// middleware, sin levantar el pipeline HTTP). Uso de ejemplo:
/// <code>
/// var ctx = await DomainContextMiddleware.ResolveAsync("app.red.com", resolver, options, ct);
/// </code>
/// </summary>
public sealed class DomainContextTests
{
    private static ITenantDomainResolver NewResolver() => Substitute.For<ITenantDomainResolver>();

    private static DomainOptions NewOptions() => new()
    {
        Reserved = ["*.flitsas.online", "*.flitsas.com"],
        EdgeTarget = "edge.flitsas.online",
    };

    [Fact]
    public async Task SinSello_ResuelveComoFlit()
    {
        var context = await DomainContextMiddleware.ResolveAsync(
            null, NewResolver(), NewOptions(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
        context.Host.Should().BeNull();
        context.HeadTenantId.Should().BeNull();
    }

    [Fact]
    public async Task SelloVacio_ResuelveComoFlit()
    {
        var context = await DomainContextMiddleware.ResolveAsync(
            "   ", NewResolver(), NewOptions(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
    }

    [Fact]
    public async Task HostReservadoDeFlit_ResuelveComoFlitSinConsultarElResolutor()
    {
        var resolver = NewResolver();

        var context = await DomainContextMiddleware.ResolveAsync(
            "dev.flitsas.online", resolver, NewOptions(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
        context.Host.Should().Be("dev.flitsas.online");
        await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HostDeRedActiva_ResuelveComoNetworkConLaCabeza()
    {
        var headTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var resolver = NewResolver();
        resolver.ResolveAsync("cliente.movilidadandina.com", Arg.Any<CancellationToken>())
            .Returns(NetworkResolution.Head(headTenantId));

        var context = await DomainContextMiddleware.ResolveAsync(
            "Cliente.MovilidadAndina.com", resolver, NewOptions(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Network);
        context.Host.Should().Be("cliente.movilidadandina.com");
        context.HeadTenantId.Should().Be(headTenantId);
    }

    [Fact]
    public async Task HostSinRed_ResuelveComoFlit()
    {
        var resolver = NewResolver();
        resolver.ResolveAsync("desconocido.com", Arg.Any<CancellationToken>())
            .Returns(NetworkResolution.None);

        var context = await DomainContextMiddleware.ResolveAsync(
            "desconocido.com", resolver, NewOptions(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
        context.Host.Should().Be("desconocido.com");
    }
}
