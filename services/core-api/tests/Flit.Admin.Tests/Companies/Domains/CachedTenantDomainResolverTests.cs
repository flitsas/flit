using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Domains;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// Uso de ejemplo: <c>await new CachedTenantDomainResolver(repo, cache, logger).ResolveAsync("app.red.com")</c>.
/// HU #12416 AC4 — host activo devuelve la cabeza; desconocido/pendiente/fallido/cabeza inactiva
/// (indistinguibles desde la vista, <see cref="ITenantDomainRepository.FindActiveHeadTenantIdAsync"/>
/// ya solo consulta <c>v_active_network_domains</c>) y cualquier fallo devuelven "sin red" — y ambos
/// casos se cachean 60 s (una sola consulta al repositorio para dos resoluciones seguidas).
/// </summary>
public sealed class CachedTenantDomainResolverTests
{
    private static ITenantDomainRepository NewRepo() => Substitute.For<ITenantDomainRepository>();

    private static CachedTenantDomainResolver NewResolver(ITenantDomainRepository repo, IMemoryCache? cache = null) =>
        new(repo, cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<CachedTenantDomainResolver>.Instance);

    [Fact]
    public async Task AC4_HostActivo_DevuelveLaCabeza()
    {
        var headTenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var repo = NewRepo();
        repo.FindActiveHeadTenantIdAsync("red.example.com", Arg.Any<CancellationToken>()).Returns(headTenantId);

        var resolution = await NewResolver(repo).ResolveAsync("red.example.com", TestContext.Current.CancellationToken);

        resolution.IsNetwork.Should().BeTrue();
        resolution.HeadTenantId.Should().Be(headTenantId);
    }

    [Fact]
    public async Task AC4_HostDesconocidoPendienteFallidoOInactivo_DevuelveSinRed()
    {
        var repo = NewRepo();
        repo.FindActiveHeadTenantIdAsync("pendiente.example.com", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var resolution = await NewResolver(repo).ResolveAsync("pendiente.example.com", TestContext.Current.CancellationToken);

        resolution.Should().Be(NetworkResolution.None);
    }

    [Fact]
    public async Task AC4_FalloDelResolutor_DevuelveSinRedYNoPropagaLaExcepcion()
    {
        var repo = NewRepo();
        repo.FindActiveHeadTenantIdAsync("red.example.com", Arg.Any<CancellationToken>())
            .Returns<Guid?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var resolution = await NewResolver(repo).ResolveAsync("red.example.com", TestContext.Current.CancellationToken);

        resolution.Should().Be(NetworkResolution.None);
    }

    [Fact]
    public async Task NegativoYPositivo_SeCachean60sMismoCosto_UnaSolaConsultaParaDosResoluciones()
    {
        var headTenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var repo = NewRepo();
        repo.FindActiveHeadTenantIdAsync("red.example.com", Arg.Any<CancellationToken>()).Returns(headTenantId);
        repo.FindActiveHeadTenantIdAsync("desconocido.example.com", Arg.Any<CancellationToken>()).Returns((Guid?)null);
        var resolver = NewResolver(repo);

        await resolver.ResolveAsync("red.example.com", TestContext.Current.CancellationToken);
        await resolver.ResolveAsync("red.example.com", TestContext.Current.CancellationToken);
        await resolver.ResolveAsync("desconocido.example.com", TestContext.Current.CancellationToken);
        await resolver.ResolveAsync("desconocido.example.com", TestContext.Current.CancellationToken);

        await repo.Received(1).FindActiveHeadTenantIdAsync("red.example.com", Arg.Any<CancellationToken>());
        await repo.Received(1).FindActiveHeadTenantIdAsync("desconocido.example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListActiveHostsAsync_FalloDelResolutor_DevuelveListaVacia()
    {
        var repo = NewRepo();
        repo.ListActiveHostsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var hosts = await NewResolver(repo).ListActiveHostsAsync(TestContext.Current.CancellationToken);

        hosts.Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveHostsAsync_SeCachea_UnaSolaConsultaParaDosLlamadas()
    {
        var repo = NewRepo();
        repo.ListActiveHostsAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "red.example.com" });
        var resolver = NewResolver(repo);

        var first = await resolver.ListActiveHostsAsync(TestContext.Current.CancellationToken);
        var second = await resolver.ListActiveHostsAsync(TestContext.Current.CancellationToken);

        first.Should().BeEquivalentTo(second);
        await repo.Received(1).ListActiveHostsAsync(Arg.Any<CancellationToken>());
    }
}
