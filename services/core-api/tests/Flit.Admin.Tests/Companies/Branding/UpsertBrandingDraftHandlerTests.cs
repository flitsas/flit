using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new UpsertBrandingDraftHandler(repo, new PermissiveBrandAssetValidator());
/// var result = await handler.HandleAsync(new UpsertBrandingDraftCommand { TenantId = id, Request = req });
/// HU #12412 AC1 (configuración inicial) / AC2 (solo MARCA_BLANCA) / AC4 (borrador no toca lo publicado).
/// </summary>
public sealed class UpsertBrandingDraftHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Operator = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task AC1_PrimerPut_CreaElBorrador()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantBranding?)null);
        repo.UpsertDraftAsync(TenantId, Arg.Any<BrandingDraft>(), Operator, Arg.Any<CancellationToken>())
            .Returns(ci => new TenantBranding { TenantId = TenantId, Draft = ci.Arg<BrandingDraft>(), RowVersion = 1 });

        var handler = new UpsertBrandingDraftHandler(repo, new PermissiveBrandAssetValidator());
        var request = new UpsertBrandingDraftRequest(
            "Movilidad Andina",
            new UpsertBrandingDraftColorsRequest("#0b3d91", "#1fa2ff", "#ffffff"),
            null,
            null);

        var result = await handler.HandleAsync(new UpsertBrandingDraftCommand
        {
            TenantId = TenantId,
            Request = request,
            ChangedBy = Operator,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Saved);
        result.Branding!.Draft.PlatformName.Should().Be("Movilidad Andina");
        // Los colores se normalizan a mayúsculas.
        result.Branding.Draft.Colors!.Primary.Should().Be("#0B3D91");
    }

    [Fact]
    public async Task AC2_TenantNoMarcaBlanca_RepositorioRechaza_SeTraduceAConflicto()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantBranding?)null);
        repo.UpsertDraftAsync(TenantId, Arg.Any<BrandingDraft>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns<TenantBranding>(_ => throw new BrandingTenantNotMarcaBlancaException(TenantId));

        var handler = new UpsertBrandingDraftHandler(repo, new PermissiveBrandAssetValidator());
        var result = await handler.HandleAsync(new UpsertBrandingDraftCommand
        {
            TenantId = TenantId,
            Request = new UpsertBrandingDraftRequest("Otra red", null, null, null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.TenantNotMarcaBlanca);
    }

    [Fact]
    public async Task AC4_RowVersionDesactualizada_Conflicto409_NoLlamaAlRepositorioDeEscritura()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, RowVersion = 5 });

        var handler = new UpsertBrandingDraftHandler(repo, new PermissiveBrandAssetValidator());
        var result = await handler.HandleAsync(new UpsertBrandingDraftCommand
        {
            TenantId = TenantId,
            Request = new UpsertBrandingDraftRequest("Movilidad Andina", null, null, RowVersion: 4),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Conflict);
        await repo.DidNotReceiveWithAnyArgs()
            .UpsertDraftAsync(default, default!, default, default);
    }
}
