using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>HU #12958 (B-03) — vista de productos de una empresa para el SuperAdmin.</summary>
public sealed class ListTenantProductsHandlerTests
{
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

        var view = await new ListTenantProductsHandler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Select(v => (v.ProductCode, v.Enabled)).Should().Equal(("tramites", true), ("comparendos", false));
        view.Single(v => v.ProductCode == "comparendos").UpdatedAt.Should().BeNull();
    }
}
