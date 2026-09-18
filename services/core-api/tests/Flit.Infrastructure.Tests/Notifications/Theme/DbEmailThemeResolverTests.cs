using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Theme;

/// <summary>
/// Uso de ejemplo:
/// var resolver = new DbEmailThemeResolver(tenantLookup, brandingRepo, cache, options, logger);
/// var theme = await resolver.ResolveAsync(tenantId, ct);
/// HU #12428 AC1/AC4/AC5 — misma herencia de marca que ResolveSessionBrandingHandler (#12418),
/// aplicada al tema de correo.
/// </summary>
public sealed class DbEmailThemeResolverTests
{
    private static readonly Guid HeadId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChildId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");
    private const string PublicBaseUrl = "https://dev.flitsas.online";

    private static DbEmailThemeResolver NewResolver(
        IBrandingTenantLookup tenantLookup,
        ITenantBrandingRepository? brandingRepository = null,
        IMemoryCache? cache = null) =>
        new(
            tenantLookup,
            brandingRepository ?? Substitute.For<ITenantBrandingRepository>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            new EmailThemePublicBrandingOptions { PublicBaseUrl = PublicBaseUrl },
            NullLogger<DbEmailThemeResolver>.Instance);

    private static BrandingTenantSnapshot ActiveMarcaBlancaHead(Guid id) =>
        new(id, HeadTenantTypes.MarcaBlanca, true, true, null);

    private static TenantBranding PublishedBranding(Guid tenantId, Guid? logoId = null)
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, logoId ?? Guid.NewGuid());
        return new TenantBranding { TenantId = tenantId, Draft = draft, Published = draft, PublishedVersion = 3, RowVersion = 1 };
    }

    [Fact]
    public async Task AC1_CabezaMarcaBlancaActivaConMarcaPublicada_ResuelveSuPropiaMarca()
    {
        var logoId = Guid.NewGuid();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>()).Returns(PublishedBranding(HeadId, logoId));

        var theme = await NewResolver(tenantLookup, brandingRepo)
            .ResolveAsync(HeadId, TestContext.Current.CancellationToken);

        theme.Kind.Should().Be(EmailThemeKind.Brand);
        theme.PlatformName.Should().Be("Movilidad Andina");
        theme.Version.Should().Be(3);
        theme.LogoUrl.Should().Be($"{PublicBaseUrl}/api/v1/public/branding/logos/{logoId}");
    }

    [Fact]
    public async Task AC1_HijaDeCabezaMarcaBlancaActiva_ResuelveLaMarcaDeLaCabezaPorParentTenantId()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(ChildId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(ChildId, CompanyTenantTypes.Renting, false, true, HeadId));
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>()).Returns(PublishedBranding(HeadId));

        var theme = await NewResolver(tenantLookup, brandingRepo)
            .ResolveAsync(ChildId, TestContext.Current.CancellationToken);

        theme.Kind.Should().Be(EmailThemeKind.Brand);
        theme.PlatformName.Should().Be("Movilidad Andina");
        await brandingRepo.Received(1).GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Concesion_ResuelveFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(HeadId, CompanyTenantTypes.Concesion, true, true, null));

        var theme = await NewResolver(tenantLookup).ResolveAsync(HeadId, TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
    }

    [Fact]
    public async Task AC1_CompaniaSinRed_ResuelveFlit()
    {
        var tenantId = Guid.NewGuid();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, CompanyTenantTypes.Renting, false, true, null));

        var theme = await NewResolver(tenantLookup).ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
    }

    [Fact]
    public async Task AC1_CabezaMarcaBlancaSinPublicar_ResuelveFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new TenantBranding { TenantId = HeadId, Draft = BrandingDraft.Empty, Published = null, RowVersion = 1 });

        var theme = await NewResolver(tenantLookup, brandingRepo)
            .ResolveAsync(HeadId, TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
    }

    [Fact]
    public async Task AC1_TenantIdNulo_ResuelveFlitSinConsultarNada()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();

        var theme = await NewResolver(tenantLookup).ResolveAsync(null, TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
        await tenantLookup.DidNotReceiveWithAnyArgs().GetAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC4_FalloDeBd_ResuelveFlitYNoPropagaLaExcepcion()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns<BrandingTenantSnapshot?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var theme = await NewResolver(tenantLookup).ResolveAsync(HeadId, TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
    }

    [Fact]
    public async Task AC5_SegundaResolucionDentroDe60s_NoVuelveAConsultarElRepositorio()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>()).Returns(PublishedBranding(HeadId));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = NewResolver(tenantLookup, brandingRepo, cache);

        await resolver.ResolveAsync(HeadId, TestContext.Current.CancellationToken);
        await resolver.ResolveAsync(HeadId, TestContext.Current.CancellationToken);

        await brandingRepo.Received(1).GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>());
    }
}
