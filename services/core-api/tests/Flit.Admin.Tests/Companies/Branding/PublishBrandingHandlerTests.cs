using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.PublishBranding;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new PublishBrandingHandler(repo, new PermissiveBrandAssetValidator());
/// var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = id });
/// HU #12412 AC4 — publicar es una operación explícita y separada del borrador.
/// </summary>
public sealed class PublishBrandingHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    [Fact]
    public async Task AC4_ConBorradorExistente_Publica()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        var current = new TenantBranding { TenantId = TenantId, Draft = draft, RowVersion = 1 };
        var published = new TenantBranding
        {
            TenantId = TenantId,
            Draft = draft,
            Published = draft,
            PublishedVersion = 1,
            PublishedAt = DateTimeOffset.UtcNow,
            RowVersion = 2,
        };

        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);
        repo.PublishAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(published);

        var handler = new PublishBrandingHandler(repo, new PermissiveBrandAssetValidator());
        var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.Published);
        result.Branding!.PublishedVersion.Should().Be(1);
    }

    [Fact]
    public async Task AC4_SinConfiguracionInicial_404()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantBranding?)null);

        var handler = new PublishBrandingHandler(repo, new PermissiveBrandAssetValidator());
        var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.NotFound);
    }

    [Fact]
    public async Task RowVersionDesactualizada_Conflicto409()
    {
        var current = new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, RowVersion = 3 };
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);

        var handler = new PublishBrandingHandler(repo, new PermissiveBrandAssetValidator());
        var result = await handler.HandleAsync(
            new PublishBrandingCommand { TenantId = TenantId, RowVersion = 1 },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.Conflict);
        await repo.DidNotReceiveWithAnyArgs().PublishAsync(default, default, default);
    }

    // ---- HU #12413 AC6 — publicar exige marca completa ----

    [Fact]
    public async Task AC6_BorradorIncompleto_SeRechazaConDetalleDeQueFalta()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, null); // falta el logo
        var current = new TenantBranding { TenantId = TenantId, Draft = draft, RowVersion = 1 };

        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);

        var handler = new PublishBrandingHandler(repo, new BrandAssetValidator(new BrandingOptions()));
        var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.Incomplete);
        result.Missing.Should().BeEquivalentTo(["logo"]);
        await repo.DidNotReceiveWithAnyArgs().PublishAsync(default, default, default);
    }

    [Fact]
    public async Task AC6_BorradorVacio_ReportaTodosLosCamposFaltantes()
    {
        var current = new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, RowVersion = 1 };
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);

        var handler = new PublishBrandingHandler(repo, new BrandAssetValidator(new BrandingOptions()));
        var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.Incomplete);
        result.Missing.Should().BeEquivalentTo(
            ["platformName", "colors.primary", "colors.secondary", "colors.onPrimary", "logo"]);
    }

    [Fact]
    public async Task AC6_GuardarBorradorIncompletoSiguePermitido_SoloPublicarLoRechaza()
    {
        // Nota funcional (AC6, segunda mitad): UpsertBrandingDraftHandler no exige completitud
        // (cubierto en UpsertBrandingDraftHandlerTests); aquí solo se confirma que publicar SÍ la exige.
        var draft = new BrandingDraft(null, null, null);
        var current = new TenantBranding { TenantId = TenantId, Draft = draft, RowVersion = 1 };
        var repo = Substitute.For<ITenantBrandingRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);

        var handler = new PublishBrandingHandler(repo, new BrandAssetValidator(new BrandingOptions()));
        var result = await handler.HandleAsync(new PublishBrandingCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PublishBrandingOutcome.Incomplete);
    }
}
