using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Platform.Domain.Access;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.Access;

/// <summary>
/// HU #13334 (Epic #13316) — productos encendidos de una empresa con la regla fail-closed de la jerarquía (ADR-0057),
/// la misma de <see cref="ProductAccessResolver"/>.
/// </summary>
public sealed class ListEnabledProductsHandlerTests
{
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();

    private readonly IProductAccessStore _store = Substitute.For<IProductAccessStore>();

    public ListEnabledProductsHandlerTests()
    {
        _store.GetTenantChainAsync(ChildId, Arg.Any<CancellationToken>()).Returns([ChildId, HeadId]);
        _store.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
    }

    private void Enabled(string product, params Guid[] tenants) =>
        _store.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), product, Arg.Any<CancellationToken>())
            .Returns(tenants.ToHashSet());

    private Task<IReadOnlyList<string>> Handle(Guid tenantId) =>
        new ListEnabledProductsHandler(_store).HandleAsync(tenantId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EncendidoEnLaEmpresaYEnSuCabeza_ApareceJuntoConPlataforma_Ordenados()
    {
        Enabled("tramites", ChildId, HeadId);
        Enabled("diagnostico", ChildId, HeadId);

        (await Handle(ChildId)).Should().Equal("diagnostico", "plataforma", "tramites");
    }

    [Fact]
    public async Task CabezaApagada_LaHijaNoLoTiene()
    {
        Enabled("tramites", ChildId);

        (await Handle(ChildId)).Should().Equal("plataforma");
    }

    [Fact]
    public async Task EmpresaInexistente_SoloPlataforma_SinPreguntarPorProductos()
    {
        var unknown = Guid.NewGuid();
        _store.GetTenantChainAsync(unknown, Arg.Any<CancellationToken>()).Returns([]);

        (await Handle(unknown)).Should().Equal("plataforma");
        await _store.DidNotReceive().GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
