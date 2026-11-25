using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #12958 (B-03) — vista de productos de una empresa para el SuperAdmin. ADR-0092: en <c>diagnostico</c> se suma si la
/// API key del impuesto está configurada (solo un booleano; nunca el valor), <c>null</c> en los demás productos.
/// </summary>
public sealed class ListTenantProductsHandlerTests
{
    private readonly ITenantProductCredentialRepository _credentials = Substitute.For<ITenantProductCredentialRepository>();

    [Fact]
    public async Task SinFila_SaleApagado_YPlataformaNoAparece()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<IProductCatalog>();
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Product("plataforma", "Plataforma", "layout-grid", ProductStatuses.Active),
            new Product("tramites", "Trámites", "file-text", ProductStatuses.Active),
            new Product("comparendos", "Comparendos", "ticket", ProductStatuses.Active),
        ]);
        var repository = Substitute.For<ITenantProductRepository>();
        repository.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(
        [
            new TenantProduct(tenantId, "tramites", true, null, DateTimeOffset.UtcNow, null),
        ]);

        var view = await new ListTenantProductsHandler(catalog, repository, _credentials).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Select(v => (v.ProductCode, v.Enabled)).Should().Equal(("tramites", true), ("comparendos", false));
        view.Single(v => v.ProductCode == "comparendos").UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Diagnostico_DiceSiLaLlaveDelImpuestoEstaConfigurada_YLosDemasNull(bool configurada)
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<IProductCatalog>();
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Product("tramites", "Trámites", "file-text", ProductStatuses.Active),
            new Product("diagnostico", "Diagnóstico", "gauge", ProductStatuses.Active),
        ]);
        var repository = Substitute.For<ITenantProductRepository>();
        repository.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns([]);
        _credentials.ExistsAsync(tenantId, "diagnostico", "impuesto_api_key", Arg.Any<CancellationToken>()).Returns(configurada);

        var view = await new ListTenantProductsHandler(catalog, repository, _credentials).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single(v => v.ProductCode == "diagnostico").ImpuestoApiKeyConfigurada.Should().Be(configurada);
        view.Single(v => v.ProductCode == "tramites").ImpuestoApiKeyConfigurada.Should().BeNull();
        await _credentials.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
