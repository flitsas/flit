using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Platform.Domain.Access;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.Access;

/// <summary>
/// HU #12965 (B-05) — reglas del resolutor de acceso (contrato v1 §4, ADR-0063). Las lecturas reales
/// del almacén las prueba <c>ProductAccessStoreTests</c> contra Postgres.
/// </summary>
public sealed class ProductAccessResolverTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private readonly IProductAccessStore _store = Substitute.For<IProductAccessStore>();

    public ProductAccessResolverTests()
    {
        _store.GetTenantChainAsync(ChildId, Arg.Any<CancellationToken>()).Returns([ChildId, HeadId]);
        _store.GetUserGrantsAsync(UserId, ChildId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProductGrants([(RoleId, "Radicador")], ["tramites.read"]));
    }

    private ProductAccessResolver Resolver() => new(_store);

    private void Enabled(params Guid[] tenants) =>
        _store.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), "tramites", Arg.Any<CancellationToken>())
            .Returns(tenants.ToHashSet());

    [Fact]
    public async Task EncendidoEnLaEmpresaYEnSuCabeza_DaAccesoConRolYPermisos()
    {
        Enabled(ChildId, HeadId);

        var access = await Resolver().ResolveAsync(UserId, ChildId, "tramites", TestContext.Current.CancellationToken);

        access.ProductEnabled.Should().BeTrue();
        access.Roles.Should().ContainSingle(r => r.Id == RoleId && r.Code == "Radicador");
        access.Permissions.Should().Equal("tramites.read");
    }

    [Fact]
    public async Task CabezaApagada_LaHijaQuedaApagada()
    {
        Enabled(ChildId);

        var access = await Resolver().ResolveAsync(UserId, ChildId, "tramites", TestContext.Current.CancellationToken);

        access.ProductEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EmpresaInexistente_QuedaApagado()
    {
        _store.GetTenantChainAsync(ChildId, Arg.Any<CancellationToken>()).Returns([]);
        Enabled(ChildId, HeadId);

        var access = await Resolver().ResolveAsync(UserId, ChildId, "tramites", TestContext.Current.CancellationToken);

        access.ProductEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Plataforma_SiempreEncendida_SinConsultarLaHabilitacion()
    {
        var access = await Resolver().ResolveAsync(UserId, ChildId, "plataforma", TestContext.Current.CancellationToken);

        access.ProductEnabled.Should().BeTrue();
        await _store.DidNotReceiveWithAnyArgs().GetTenantsWithProductEnabledAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("flotas")]
    [InlineData("")]
    [InlineData("TRAMITES")]
    public async Task ProductoDesconocido_SeNiega(string productCode)
    {
        var access = await Resolver().ResolveAsync(UserId, ChildId, productCode, TestContext.Current.CancellationToken);

        access.ProductEnabled.Should().BeFalse();
        access.Roles.Should().BeEmpty();
        access.Permissions.Should().BeEmpty();
    }
}
