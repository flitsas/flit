using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Network;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.Network;

/// <summary>
/// HU #12423 — <see cref="NetworkUrlBaseResolver"/>.
/// Uso de ejemplo:
/// var resolver = new NetworkUrlBaseResolver(networkMembership);
/// var activateUrlBase = await resolver.ForTenantAsync(tenantId, "http://localhost:3000/invite/activate", ct);
/// </summary>
public sealed class NetworkUrlBaseResolverTests
{
    private readonly ITenantNetworkMembership _networkMembership = Substitute.For<ITenantNetworkMembership>();
    private readonly NetworkUrlBaseResolver _resolver;

    public NetworkUrlBaseResolverTests()
    {
        _resolver = new NetworkUrlBaseResolver(_networkMembership);
    }

    // AC4 — compañía sin red: el enlace es LITERALMENTE el mismo que hoy (igualdad de referencia).
    [Fact]
    public async Task ForTenantAsync_NoNetwork_ReturnsSameStringInstance()
    {
        var tenantId = Guid.NewGuid();
        var configuredBase = "http://localhost:3000/invite/activate";
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);

        var result = await _resolver.ForTenantAsync(tenantId, configuredBase, CancellationToken.None);

        ReferenceEquals(result, configuredBase).Should().BeTrue();
        result.Should().Be(configuredBase);
    }

    // AC4 — misma igualdad literal cuando la red existe pero no tiene dominio activo.
    [Fact]
    public async Task ForTenantAsync_NetworkWithoutActiveHost_ReturnsConfiguredBaseLiteral()
    {
        var tenantId = Guid.NewGuid();
        var configuredBase = "http://localhost:3000/invite/activate";
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(Guid.NewGuid(), IsMarcaBlancaNetwork: true, ActiveHost: null));

        var result = await _resolver.ForTenantAsync(tenantId, configuredBase, CancellationToken.None);

        ReferenceEquals(result, configuredBase).Should().BeTrue();
    }

    // AC1 — red con dominio activo: usa ese dominio conservando el path configurado.
    [Fact]
    public async Task ForTenantAsync_NetworkWithActiveHost_UsesHostAndKeepsConfiguredPath()
    {
        var tenantId = Guid.NewGuid();
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(Guid.NewGuid(), IsMarcaBlancaNetwork: true, ActiveHost: "app.movilidadandina.com"));

        var result = await _resolver.ForTenantAsync(
            tenantId, "http://localhost:3000/invite/activate", CancellationToken.None);

        result.Should().Be("https://app.movilidadandina.com/invite/activate");
    }

    // Host con mayúsculas mezcladas y espacios accidentales → normalizado a minúsculas.
    [Fact]
    public async Task ForTenantAsync_HostWithMixedCase_IsNormalizedToLowercase()
    {
        var tenantId = Guid.NewGuid();
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(Guid.NewGuid(), IsMarcaBlancaNetwork: true, ActiveHost: " App.MovilidadAndina.COM "));

        var result = await _resolver.ForTenantAsync(
            tenantId, "http://localhost:3000/invite/activate", CancellationToken.None);

        result.Should().Be("https://app.movilidadandina.com/invite/activate");
    }

    // Host con puerto: se conserva tal cual (solo se normaliza el casing), no se le quita el puerto.
    [Fact]
    public async Task ForTenantAsync_HostWithPort_KeepsPortAndNormalizesCase()
    {
        var tenantId = Guid.NewGuid();
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(Guid.NewGuid(), IsMarcaBlancaNetwork: true, ActiveHost: "APP.LOCAL:8443"));

        var result = await _resolver.ForTenantAsync(
            tenantId, "http://localhost:3000/invite/activate", CancellationToken.None);

        result.Should().Be("https://app.local:8443/invite/activate");
    }

    // AC1/AC2 — dominio vigente: dos tenants distintos con distinta red producen enlaces distintos,
    // y el mismo tenant recalcula si el dominio activo cambió entre dos llamadas (reenvío, AC2).
    [Fact]
    public async Task ForTenantAsync_DomainChangedBetweenCalls_RecalculatesLink()
    {
        var tenantId = Guid.NewGuid();
        var headTenantId = Guid.NewGuid();
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(
                new NetworkMembership(headTenantId, IsMarcaBlancaNetwork: true, ActiveHost: "old.flitsas.online"),
                new NetworkMembership(headTenantId, IsMarcaBlancaNetwork: true, ActiveHost: "new.flitsas.online"));

        var first = await _resolver.ForTenantAsync(tenantId, "http://localhost:3000/invite/activate", CancellationToken.None);
        var second = await _resolver.ForTenantAsync(tenantId, "http://localhost:3000/invite/activate", CancellationToken.None);

        first.Should().Be("https://old.flitsas.online/invite/activate");
        second.Should().Be("https://new.flitsas.online/invite/activate");
    }

    // Query string en la base configurada: se conserva junto con el path.
    [Fact]
    public async Task ForTenantAsync_ConfiguredBaseWithQuery_PreservesPathAndQuery()
    {
        var tenantId = Guid.NewGuid();
        _networkMembership.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(Guid.NewGuid(), IsMarcaBlancaNetwork: true, ActiveHost: "app.movilidadandina.com"));

        var result = await _resolver.ForTenantAsync(
            tenantId, "http://localhost:3000/invite/activate?ref=email", CancellationToken.None);

        result.Should().Be("https://app.movilidadandina.com/invite/activate?ref=email");
    }

    // AC3 — dominio de la solicitud (recuperación): dominio de red ⇒ usa ese host + path configurado.
    [Fact]
    public void ForRequestDomain_NetworkDomain_UsesHostAndKeepsConfiguredPath()
    {
        var domainContext = FakeDomainContext(DomainKind.Network, "app.movilidadandina.com", Guid.NewGuid());

        var result = _resolver.ForRequestDomain(domainContext, "http://localhost:4001/reset-password");

        result.Should().Be("https://app.movilidadandina.com/reset-password");
    }

    // AC3 — una solicitud por el dominio de FLIT genera el enlace de FLIT (literal).
    [Fact]
    public void ForRequestDomain_FlitDomain_ReturnsConfiguredBaseLiteral()
    {
        var domainContext = FakeDomainContext(DomainKind.Flit, "app.flit.test", null);
        var configuredBase = "http://localhost:4001/reset-password";

        var result = _resolver.ForRequestDomain(domainContext, configuredBase);

        ReferenceEquals(result, configuredBase).Should().BeTrue();
    }

    // Sin sello (fail-closed a FLIT) ⇒ literal también.
    [Fact]
    public void ForRequestDomain_NoSeal_ReturnsConfiguredBaseLiteral()
    {
        var domainContext = FakeDomainContext(DomainKind.Flit, null, null);
        var configuredBase = "http://localhost:4001/reset-password";

        var result = _resolver.ForRequestDomain(domainContext, configuredBase);

        ReferenceEquals(result, configuredBase).Should().BeTrue();
    }

    /// <summary>
    /// Application no puede referenciar <c>Flit.Api.Authorization.DomainContext</c> (capas): se
    /// simula el contrato mínimo con un doble de <see cref="IDomainContextAccessor"/>.
    /// </summary>
    private static IDomainContextAccessor FakeDomainContext(DomainKind kind, string? host, Guid? headTenantId)
    {
        var accessor = Substitute.For<IDomainContextAccessor>();
        accessor.Kind.Returns(kind);
        accessor.Host.Returns(host);
        accessor.HeadTenantId.Returns(headTenantId);
        return accessor;
    }
}
