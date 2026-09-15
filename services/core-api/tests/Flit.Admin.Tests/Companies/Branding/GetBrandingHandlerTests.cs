using Flit.Admin.Application.Companies.Branding.GetBranding;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo: var branding = await new GetBrandingHandler(repo).HandleAsync(tenantId);
/// HU #12412 AC1/AC3 — lectura pura, sin regla de negocio.
/// </summary>
public sealed class GetBrandingHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task DevuelveNull_CuandoNoHayConfiguracionInicial()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantBranding?)null);

        var handler = new GetBrandingHandler(repo);
        var result = await handler.HandleAsync(TenantId, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DevuelveLaMarca_CuandoExiste()
    {
        var branding = new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, RowVersion = 1 };
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);

        var handler = new GetBrandingHandler(repo);
        var result = await handler.HandleAsync(TenantId, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(branding);
    }
}
