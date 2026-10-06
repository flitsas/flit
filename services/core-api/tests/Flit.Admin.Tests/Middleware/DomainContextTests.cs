using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Middleware;
using Flit.Modules.Platform.Application.Hosts;
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

    /// <summary>HU #12761 — hosts de prueba exceptuados de la zona reservada.</summary>
    private static DomainOptions NewOptionsConPermitidos() => new()
    {
        Reserved = ["*.flitsas.online", "*.flitsas.com"],
        Allowed = ["marcablancadev.flitsas.online", "marcablancaqa.flitsas.online", "marcablancapdn.flitsas.online"],
        EdgeTarget = "edge.flitsas.online",
    };

    [Fact]
    public async Task AC4_HostExceptuadoYRegistrado_ConsultaElResolutorYResuelveComoNetwork()
    {
        var headTenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var resolver = NewResolver();
        resolver.ResolveAsync("marcablancadev.flitsas.online", Arg.Any<CancellationToken>())
            .Returns(NetworkResolution.Head(headTenantId));

        var context = await DomainContextMiddleware.ResolveAsync(
            "MarcaBlancaDev.FLITSAS.online", resolver, NewOptionsConPermitidos(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Network);
        context.Host.Should().Be("marcablancadev.flitsas.online");
        context.HeadTenantId.Should().Be(headTenantId);
        await resolver.Received(1).ResolveAsync("marcablancadev.flitsas.online", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_HostReservadoNoExceptuado_ResuelveComoFlitSinConsultarElResolutor()
    {
        var resolver = NewResolver();

        var context = await DomainContextMiddleware.ResolveAsync(
            "dev.flitsas.online", resolver, NewOptionsConPermitidos(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
        context.Host.Should().Be("dev.flitsas.online");
        context.HeadTenantId.Should().BeNull();
        await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_SubdominioDeHostExceptuado_ResuelveComoFlitSinConsultarElResolutor()
    {
        var resolver = NewResolver();

        var context = await DomainContextMiddleware.ResolveAsync(
            "sub.marcablancadev.flitsas.online", resolver, NewOptionsConPermitidos(), TestContext.Current.CancellationToken);

        context.Kind.Should().Be(DomainKind.Flit);
        await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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

    // ── HU #12968 (B-08): DomainContext con producto ──────────────────────────────

    private static IProductHosts NewHosts()
    {
        var hosts = Substitute.For<IProductHosts>();
        hosts.ProductForHost(Arg.Any<string?>()).Returns((string?)null);
        hosts.ProductForHost("dev.tramites.flitsas.online").Returns("tramites");
        hosts.ProductForHost("dev.flitsas.online").Returns("plataforma");
        return hosts;
    }

    [Theory]
    [InlineData("dev.tramites.flitsas.online", "tramites")]
    [InlineData("dev.flitsas.online", "plataforma")]
    [InlineData("otro.flitsas.online", "plataforma")]
    public async Task HostFlit_DeduceElProductoDelHost(string host, string expected)
    {
        var context = await DomainContextMiddleware.ResolveAsync(
            host, NewResolver(), NewOptions(), TestContext.Current.CancellationToken, NewHosts());

        context.Kind.Should().Be(DomainKind.Flit);
        context.ProductCode.Should().Be(expected);
    }

    [Fact]
    public async Task DominioDeRed_UsaElProductoDelDominio()
    {
        var headTenantId = Guid.NewGuid();
        var resolver = NewResolver();
        resolver.ResolveAsync("comparendos.red.com", Arg.Any<CancellationToken>())
            .Returns(NetworkResolution.Head(headTenantId, "comparendos"));

        var context = await DomainContextMiddleware.ResolveAsync(
            "comparendos.red.com", resolver, NewOptions(), TestContext.Current.CancellationToken, NewHosts());

        context.Kind.Should().Be(DomainKind.Network);
        context.HeadTenantId.Should().Be(headTenantId);
        context.ProductCode.Should().Be("comparendos");
    }

    [Fact]
    public async Task SinSello_EsPlataforma()
    {
        var context = await DomainContextMiddleware.ResolveAsync(
            null, NewResolver(), NewOptions(), TestContext.Current.CancellationToken, NewHosts());

        context.ProductCode.Should().Be("plataforma");
    }
}
