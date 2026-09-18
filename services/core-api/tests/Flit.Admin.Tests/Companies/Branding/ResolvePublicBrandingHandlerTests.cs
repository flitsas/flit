using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Domains;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new ResolvePublicBrandingHandler(brandingRepo, tenantLookup, cache, logger);
/// var response = await handler.HandleAsync(isNetworkDomain: true, headTenantId: id, ct);
/// HU #12418 AC1-AC3, AC6-AC9 — resolución pública de marca por dominio, antes de sesión.
/// </summary>
public sealed class ResolvePublicBrandingHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    private static ResolvePublicBrandingHandler NewHandler(
        ITenantBrandingRepository? brandingRepository = null,
        IBrandingTenantLookup? tenantLookup = null,
        IPublicBrandingCache? cache = null) =>
        new(
            brandingRepository ?? Substitute.For<ITenantBrandingRepository>(),
            tenantLookup ?? Substitute.For<IBrandingTenantLookup>(),
            cache ?? new MemoryPublicBrandingCache(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<ResolvePublicBrandingHandler>.Instance);

    private static BrandingTenantSnapshot ActiveMarcaBlancaHead(Guid tenantId) =>
        new(tenantId, HeadTenantTypes.MarcaBlanca, IsGroupParent: true, IsActive: true, ParentTenantId: null);

    // ── AC8 — dominio de FLIT: cero acceso a repositorio ────────────────────────────

    [Fact]
    public async Task AC8_DominioFlit_DevuelveIdentidadFlitSinTocarRepositorioNiTenantLookup()
    {
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();

        var response = await NewHandler(brandingRepo, tenantLookup)
            .HandleAsync(isNetworkDomain: false, headTenantId: null, TestContext.Current.CancellationToken);

        response.PlatformName.Should().Be(BrandIdentity.Flit.PlatformName);
        response.Version.Should().Be(0);
        response.LogoUrl.Should().BeNull();
        await brandingRepo.DidNotReceiveWithAnyArgs().GetByTenantIdAsync(default, TestContext.Current.CancellationToken);
        await tenantLookup.DidNotReceiveWithAnyArgs().GetAsync(default, TestContext.Current.CancellationToken);
    }

    // ── AC1 — marca publicada ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_CabezaMarcaBlancaActivaConMarcaPublicada_DevuelveLaMarca()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        var branding = new TenantBranding
        {
            TenantId = TenantId,
            Draft = draft,
            Published = draft,
            PublishedVersion = 3,
            RowVersion = 1,
        };

        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));

        var response = await NewHandler(brandingRepo, tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        response.PlatformName.Should().Be("Movilidad Andina");
        response.Version.Should().Be(3);
        response.LogoUrl.Should().Be($"/api/v1/public/branding/logos/{draft.LogoId}");
        response.Colors.Primary.Should().Be("#0B3D91");
    }

    // ── AC2 — matriz de negativos: misma respuesta que FLIT, byte a byte ────────────

    [Fact]
    public async Task AC2_CabezaInexistente_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns((BrandingTenantSnapshot?)null);

        var response = await NewHandler(tenantLookup: tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC2_CabezaNoEsMarcaBlanca_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(TenantId, CompanyTenantTypes.Concesion, true, true, null));

        var response = await NewHandler(tenantLookup: tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC2_CabezaInactiva_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(TenantId, HeadTenantTypes.MarcaBlanca, true, false, null));

        var response = await NewHandler(tenantLookup: tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC2_YaNoEsCabezaDeGrupo_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(TenantId, HeadTenantTypes.MarcaBlanca, false, true, null));

        var response = await NewHandler(tenantLookup: tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC2_MarcaSinPublicarBorrador_DevuelveIdentidadFlit()
    {
        var branding = new TenantBranding
        {
            TenantId = TenantId,
            Draft = new BrandingDraft("Aún sin publicar", Colors, Guid.NewGuid()),
            Published = null,
            RowVersion = 1,
        };
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));

        var response = await NewHandler(brandingRepo, tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC2_MarcaRetirada_DevuelveIdentidadFlit()
    {
        var draft = new BrandingDraft("Retirada", Colors, Guid.NewGuid());
        var branding = new TenantBranding
        {
            TenantId = TenantId,
            Draft = draft,
            Published = draft,
            PublishedVersion = 1,
            DeletedAt = DateTimeOffset.UtcNow,
            RowVersion = 2,
        };
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));

        var response = await NewHandler(brandingRepo, tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    // ── AC6 — fallo de BD/storage ⇒ FLIT, nunca un error al visitante ────────────────

    [Fact]
    public async Task AC6_FalloDelRepositorio_DevuelveIdentidadFlitYNoPropagaLaExcepcion()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns<TenantBranding?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var response = await NewHandler(brandingRepo, tenantLookup)
            .HandleAsync(isNetworkDomain: true, headTenantId: TenantId, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    // ── AC7 — caché 60 s por tenant, positivo y negativo por el mismo camino ────────

    [Fact]
    public async Task AC7_MarcaPublicada_SeCachea60s_UnaSolaConsultaParaDosResoluciones()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        var branding = new TenantBranding { TenantId = TenantId, Draft = draft, Published = draft, PublishedVersion = 1, RowVersion = 1 };
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));
        var handler = NewHandler(brandingRepo, tenantLookup);

        await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);
        await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);

        await brandingRepo.Received(1).GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>());
        await tenantLookup.Received(1).GetAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC7_CabezaSinPublicar_TambienSeCachea_MismoCostoQueElPositivo()
    {
        var branding = new TenantBranding { TenantId = TenantId, Draft = BrandingDraft.Empty, Published = null, RowVersion = 1 };
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));
        var handler = NewHandler(brandingRepo, tenantLookup);

        await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);
        await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);

        await brandingRepo.Received(1).GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC7_ApagarLaClaseMarcaBlanca_InvalidaLaCachePropia()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        var branding = new TenantBranding { TenantId = TenantId, Draft = draft, Published = draft, PublishedVersion = 1, RowVersion = 1 };
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(branding);
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(TenantId));
        var cache = new MemoryPublicBrandingCache(new MemoryCache(new MemoryCacheOptions()));
        var handler = NewHandler(brandingRepo, tenantLookup, cache);

        var first = await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);
        first.PlatformName.Should().Be("Movilidad Andina");

        // Apagar la clase (CompanyWriteRepository invoca esto al cambiar tenant_type).
        ((IBrandingCacheInvalidator)cache).InvalidateTenant(TenantId);
        tenantLookup.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(TenantId, CompanyTenantTypes.Concesion, true, true, null));

        var second = await handler.HandleAsync(true, TenantId, TestContext.Current.CancellationToken);
        AssertIsFlitIdentity(second);
    }

    private static void AssertIsFlitIdentity(BrandIdentityResponse response)
    {
        response.PlatformName.Should().Be(BrandIdentity.Flit.PlatformName);
        response.LogoUrl.Should().BeNull();
        response.Version.Should().Be(0);
        response.Colors.Primary.Should().Be(BrandIdentity.Flit.Colors.Primary);
        response.Colors.Secondary.Should().Be(BrandIdentity.Flit.Colors.Secondary);
        response.Colors.OnPrimary.Should().Be(BrandIdentity.Flit.Colors.OnPrimary);
    }
}
