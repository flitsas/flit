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
/// HU #14694 (plan §1.2 R1): los productos con aprovisionamiento suman el último estado reportado; los demás, <c>null</c>.
/// </summary>
public sealed class ListTenantProductsHandlerTests
{
    private readonly ITenantProductCredentialRepository _credentials = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductProvisioningStore _provisioning = Substitute.For<ITenantProductProvisioningStore>();

    public ListTenantProductsHandlerTests()
    {
        _provisioning.ListByTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private ListTenantProductsHandler Handler(IProductCatalog catalog, ITenantProductRepository repository) =>
        new(catalog, repository, _credentials, _provisioning);

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

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

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

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single(v => v.ProductCode == "diagnostico").ImpuestoApiKeyConfigurada.Should().Be(configurada);
        view.Single(v => v.ProductCode == "tramites").ImpuestoApiKeyConfigurada.Should().BeNull();
        await _credentials.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── HU #14694 — estado de aprovisionamiento (F1b-CF1) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Impuestos_SumaElUltimoEstadoReportado_YLosDemasProductosNull()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<IProductCatalog>();
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Product("diagnostico", "Diagnóstico", "gauge", ProductStatuses.Active),
            new Product("impuestos", "Impuestos", "receipt", ProductStatuses.Active),
            new Product("tramites", "Trámites", "file-text", ProductStatuses.Active),
        ]);
        var repository = Substitute.For<ITenantProductRepository>();
        repository.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(
            [new TenantProduct(tenantId, "impuestos", true, null, DateTimeOffset.UtcNow, null)]);
        var estado = new TenantProductProvisioning(tenantId, "impuestos", "LISTO", null, null, null, false, 4, DateTimeOffset.UnixEpoch);
        // Una fila de un producto sin aprovisionamiento no se muestra aunque exista. HU #14711: diagnostico ya tiene estado
        // propio (alta de la credencial del impuesto), así que la fila ajena es de otro producto.
        var ajena = estado with { ProductCode = "tramites", Estado = "ERROR" };
        _provisioning.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns([estado, ajena]);

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single(v => v.ProductCode == "impuestos").Provisioning.Should().Be(estado);
        view.Single(v => v.ProductCode == "diagnostico").Provisioning.Should().BeNull();
        view.Single(v => v.ProductCode == "tramites").Provisioning.Should().BeNull();
    }

    [Fact]
    public async Task Impuestos_SinReporteTodavia_ProvisioningNull()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<IProductCatalog>();
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns([new Product("impuestos", "Impuestos", "receipt", ProductStatuses.Active)]);
        var repository = Substitute.For<ITenantProductRepository>();
        repository.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns([]);

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single().Provisioning.Should().BeNull();
        await _provisioning.Received(1).ListByTenantAsync(tenantId, Arg.Any<CancellationToken>());
    }

    // ── HU #14711 — secreto del webhook y estado del alta de la credencial en diagnostico ──────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Diagnostico_DiceSiElSecretoDelWebhookEstaConfigurado_YLosDemasNull(bool configurado)
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
        _credentials.ExistsAsync(tenantId, "diagnostico", "impuesto_webhook_secret", Arg.Any<CancellationToken>()).Returns(configurado);

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single(v => v.ProductCode == "diagnostico").ImpuestoWebhookSecretConfigurada.Should().Be(configurado);
        view.Single(v => v.ProductCode == "tramites").ImpuestoWebhookSecretConfigurada.Should().BeNull();
    }

    [Fact]
    public async Task Diagnostico_SumaElEstadoDelAltaDeLaCredencial()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<IProductCatalog>();
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns([new Product("diagnostico", "Diagnóstico", "gauge", ProductStatuses.Active)]);
        var repository = Substitute.For<ITenantProductRepository>();
        repository.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(
            [new TenantProduct(tenantId, "diagnostico", true, null, DateTimeOffset.UtcNow, null)]);
        var estado = new TenantProductProvisioning(tenantId, "diagnostico", "REQUIERE_REVISION_NIT", "NIT_AUSENTE", "La compañía no tiene NIT registrado.", "CORREGIR_NIT", null, 2, DateTimeOffset.UnixEpoch);
        _provisioning.ListByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns([estado]);

        var view = await Handler(catalog, repository).HandleAsync(tenantId, TestContext.Current.CancellationToken);

        view.Single().Provisioning.Should().Be(estado);
    }
}
