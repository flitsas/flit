using Flit.Admin.Application.Companies.Branding.RetireBranding;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo: var result = await new RetireBrandingHandler(repo).HandleAsync(command);
/// HU #12412 AC5/AC6 — el retiro conserva el dato; solo deja de resolverse (fuera del alcance de este
/// handler: la resolución pública la implementa #12418).
/// </summary>
public sealed class RetireBrandingHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC6_MarcaExistente_SeRetiraYSeConserva()
    {
        var current = new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, RowVersion = 1 };
        var retired = new TenantBranding
        {
            TenantId = TenantId,
            Draft = current.Draft,
            DeletedAt = DateTimeOffset.UtcNow,
            RowVersion = 2,
        };

        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);
        repo.RetireAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(retired);

        var handler = new RetireBrandingHandler(repo);
        var result = await handler.HandleAsync(new RetireBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RetireBrandingOutcome.Retired);
        result.Branding!.IsRetired.Should().BeTrue();
    }

    [Fact]
    public async Task SinConfiguracionInicial_404()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantBranding?)null);

        var handler = new RetireBrandingHandler(repo);
        var result = await handler.HandleAsync(new RetireBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RetireBrandingOutcome.NotFound);
    }
}
