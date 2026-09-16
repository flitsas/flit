using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.GetPublicBrandLogo;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, logger);
/// var result = await handler.HandleAsync(logoId, ifNoneMatch: null, ct);
/// HU #12418 AC4 — logotipo servido por dirección pública inmutable; 404 indistinguible.
/// </summary>
public sealed class GetPublicBrandLogoHandlerTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    private static TenantBrandLogoVersion NewLogo(Guid id, Guid tenantId, string status = TenantBrandLogoVersion.StatusActive) =>
        new(id, tenantId, Version: 1, status, "image/png", "logo.png", "fm://brand-logo/x", new string('a', 64), 12, 200, 60);

    [Fact]
    public async Task AC4_IdInexistente_Responde404SinConsultarTenant()
    {
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        var storage = Substitute.For<IBrandLogoStorage>();

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(Guid.NewGuid(), ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        await tenantLookup.DidNotReceiveWithAnyArgs().GetAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC4_TenantYaNoEsMarcaBlanca_Responde404IndistinguibleDeInexistente()
    {
        var logoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetLogoVersionByIdAsync(logoId, Arg.Any<CancellationToken>()).Returns(NewLogo(logoId, tenantId));
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, CompanyTenantTypes.Concesion, true, true, null));
        var storage = Substitute.For<IBrandLogoStorage>();

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(logoId, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        await storage.DidNotReceiveWithAnyArgs().OpenReadAsync(string.Empty, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC4_VersionSupersededDeCabezaMarcaBlanca_SeSirveIgual_UrlInmutable()
    {
        var logoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetLogoVersionByIdAsync(logoId, Arg.Any<CancellationToken>())
            .Returns(NewLogo(logoId, tenantId, TenantBrandLogoVersion.StatusSuperseded));
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, HeadTenantTypes.MarcaBlanca, true, true, null));
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.OpenReadAsync("fm://brand-logo/x", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(PngBytes));

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(logoId, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.ContentType.Should().Be("image/png");
        result.Sha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task AC4_IfNoneMatchCoincideConEtagActual_Responde304SinAbrirStorage()
    {
        var logoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sha256 = new string('a', 64);
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetLogoVersionByIdAsync(logoId, Arg.Any<CancellationToken>()).Returns(NewLogo(logoId, tenantId));
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, HeadTenantTypes.MarcaBlanca, true, true, null));
        var storage = Substitute.For<IBrandLogoStorage>();

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(logoId, ifNoneMatch: $"\"{sha256}\"", TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.IsNotModified.Should().BeTrue();
        await storage.DidNotReceiveWithAnyArgs().OpenReadAsync(string.Empty, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC4_FilaEnBdPeroBinarioPerdidoEnStorage_Responde404()
    {
        var logoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetLogoVersionByIdAsync(logoId, Arg.Any<CancellationToken>()).Returns(NewLogo(logoId, tenantId));
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, HeadTenantTypes.MarcaBlanca, true, true, null));
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.OpenReadAsync("fm://brand-logo/x", Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(logoId, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
    }

    // HU #12429 AC5 — fallo simulado del storage (excepción, no solo "no encontrado"): el logotipo
    // responde 404 controlado, nunca 500. Endurecimiento del handler hecho en la misma HU.
    [Fact]
    public async Task AC5_StorageLanzaExcepcion_Responde404SinPropagar()
    {
        var logoId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var brandingRepo = Substitute.For<ITenantBrandingRepository>();
        brandingRepo.GetLogoVersionByIdAsync(logoId, Arg.Any<CancellationToken>()).Returns(NewLogo(logoId, tenantId));
        var tenantLookup = Substitute.For<IBrandingTenantLookup>();
        tenantLookup.GetAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new BrandingTenantSnapshot(tenantId, HeadTenantTypes.MarcaBlanca, true, true, null));
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.OpenReadAsync("fm://brand-logo/x", Arg.Any<CancellationToken>())
            .Returns<Stream?>(_ => throw new InvalidOperationException("caída simulada del storage"));

        var result = await new GetPublicBrandLogoHandler(brandingRepo, tenantLookup, storage, NullLogger<GetPublicBrandLogoHandler>.Instance)
            .HandleAsync(logoId, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
    }
}
