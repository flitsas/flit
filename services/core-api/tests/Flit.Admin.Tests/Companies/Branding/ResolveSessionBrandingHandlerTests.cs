using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolveSessionBranding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new ResolveSessionBrandingHandler(tenantLookup, brandingRepo, logger);
/// var response = await handler.HandleAsync(sessionTenantId, isSuperAdmin: false, ct);
/// HU #12418 AC5/AC6 — herencia de marca dentro de la aplicación, ya autenticado.
/// </summary>
public sealed class ResolveSessionBrandingHandlerTests
{
    private static readonly Guid HeadId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChildId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    private static ResolveSessionBrandingHandler NewHandler(
        IBrandingTenantLookup tenantLookup, ITenantBrandingRepository? brandingRepository = null) =>
        new(tenantLookup, brandingRepository ?? Substitute.For<ITenantBrandingRepository>(), NullLogger<ResolveSessionBrandingHandler>.Instance);

    private static BrandingTenantSnapshot ActiveMarcaBlancaHead(Guid id) =>
        new(id, HeadTenantTypes.MarcaBlanca, true, true, null);

    private static TenantBranding PublishedBranding(Guid tenantId)
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        return new TenantBranding { TenantId = tenantId, Draft = draft, Published = draft, PublishedVersion = 2, RowVersion = 1 };
    }

    [Fact]
    public async Task AC5_SuperAdmin_DevuelveIdentidadFlitSinConsultarNada()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();

        var response = await NewHandler(tenantLookup)
            .HandleAsync(Guid.NewGuid(), isSuperAdmin: true, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
        await tenantLookup.DidNotReceiveWithAnyArgs().GetAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC5_CabezaMarcaBlancaActiva_DevuelveSuPropiaMarca()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>()).Returns(PublishedBranding(HeadId));

        var response = await NewHandler(tenantLookup, brandingRepo)
            .HandleAsync(HeadId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        response.PlatformName.Should().Be("Movilidad Andina");
        response.Version.Should().Be(2);
    }

    [Fact]
    public async Task AC5_HijaDeCabezaMarcaBlancaActiva_DevuelveLaMarcaDeLaCabezaPorParentTenantId()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(ChildId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(ChildId, CompanyTenantTypes.Renting, false, true, HeadId));
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>()).Returns(PublishedBranding(HeadId));

        var response = await NewHandler(tenantLookup, brandingRepo)
            .HandleAsync(ChildId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        response.PlatformName.Should().Be("Movilidad Andina");
        await brandingRepo.Received(1).GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_Concesion_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(HeadId, CompanyTenantTypes.Concesion, true, true, null));

        var response = await NewHandler(tenantLookup)
            .HandleAsync(HeadId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC5_HijaDeConcesion_DevuelveIdentidadFlit()
    {
        var concesionHeadId = Guid.NewGuid();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(ChildId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(ChildId, CompanyTenantTypes.Renting, false, true, concesionHeadId));
        tenantLookup.GetAsync(concesionHeadId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(concesionHeadId, CompanyTenantTypes.Concesion, true, true, null));

        var response = await NewHandler(tenantLookup)
            .HandleAsync(ChildId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC5_CompaniaSinRed_DevuelveIdentidadFlit()
    {
        var tenantId = Guid.NewGuid();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, CompanyTenantTypes.Renting, false, true, null));

        var response = await NewHandler(tenantLookup)
            .HandleAsync(tenantId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC5_CabezaMarcaBlancaSinPublicar_DevuelveIdentidadFlit()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>()).Returns(ActiveMarcaBlancaHead(HeadId));
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetByTenantIdAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new TenantBranding { TenantId = HeadId, Draft = BrandingDraft.Empty, Published = null, RowVersion = 1 });

        var response = await NewHandler(tenantLookup, brandingRepo)
            .HandleAsync(HeadId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    [Fact]
    public async Task AC6_FalloDeBd_DevuelveIdentidadFlitYNoPropagaLaExcepcion()
    {
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns<BrandingTenantSnapshot?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var response = await NewHandler(tenantLookup)
            .HandleAsync(HeadId, isSuperAdmin: false, TestContext.Current.CancellationToken);

        AssertIsFlitIdentity(response);
    }

    private static void AssertIsFlitIdentity(BrandIdentityResponse response)
    {
        response.PlatformName.Should().Be(BrandIdentity.Flit.PlatformName);
        response.LogoUrl.Should().BeNull();
        response.Version.Should().Be(0);
    }
}
