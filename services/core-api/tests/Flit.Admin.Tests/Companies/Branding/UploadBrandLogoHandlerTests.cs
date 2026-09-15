using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.UploadBrandLogo;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var handler = new UploadBrandLogoHandler(repo, storage, new PermissiveBrandAssetValidator());
/// var result = await handler.HandleAsync(new UploadBrandLogoCommand { TenantId = id, Filename = "logo.png", Content = stream });
/// HU #12412 AC7 — versión nueva del logotipo, la anterior queda superseded.
/// </summary>
public sealed class UploadBrandLogoHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    // 1x1 PNG válido (firma + IHDR mínima) — igual que los fixtures de ImageContentTypeSnifferTests.
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
    ];

    [Fact]
    public async Task AC7_SubidaValida_CreaVersion1_YLaGuardaEnStorage()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.SaveAsync(TenantId, "logo.png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredBrandLogo("brand-logo/1", "a".PadLeft(64, '0'), OnePixelPng.Length));
        repo.AddLogoVersionAsync(TenantId, Arg.Any<NewBrandLogo>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new TenantBrandLogoVersion(
                Guid.NewGuid(), TenantId, 1, TenantBrandLogoVersion.StatusActive,
                ci.Arg<NewBrandLogo>().ContentType, "logo.png", "brand-logo/1", "a".PadLeft(64, '0'), OnePixelPng.Length, 1, 1));

        var handler = new UploadBrandLogoHandler(repo, storage, new PermissiveBrandAssetValidator());
        using var content = new MemoryStream(OnePixelPng);

        var result = await handler.HandleAsync(new UploadBrandLogoCommand
        {
            TenantId = TenantId,
            Filename = "logo.png",
            Content = content,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UploadBrandLogoOutcome.Uploaded);
        result.Logo!.Version.Should().Be(1);
        result.Logo.ContentType.Should().Be("image/png");
        result.Logo.WidthPx.Should().BeGreaterThan(0);
        result.Logo.HeightPx.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AC2_TenantNoMarcaBlanca_RepositorioRechaza_SeTraduceAConflicto()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.SaveAsync(TenantId, "logo.png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredBrandLogo("brand-logo/1", "a".PadLeft(64, '0'), OnePixelPng.Length));
        repo.AddLogoVersionAsync(TenantId, Arg.Any<NewBrandLogo>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns<TenantBrandLogoVersion>(_ => throw new BrandingTenantNotMarcaBlancaException(TenantId));

        var handler = new UploadBrandLogoHandler(repo, storage, new PermissiveBrandAssetValidator());
        using var content = new MemoryStream(OnePixelPng);

        var result = await handler.HandleAsync(new UploadBrandLogoCommand
        {
            TenantId = TenantId,
            Filename = "logo.png",
            Content = content,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UploadBrandLogoOutcome.TenantNotMarcaBlanca);
    }

    // ---- HU #12413 AC1/AC2 — validador real (no PermissiveBrandAssetValidator) ----

    private static readonly byte[] FakeSvgDeclaredAsPng =
        System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

    [Fact]
    public async Task AC1_SvgDisfrazadoDePng_SeRechazaPorFirmaBinariaNoPorExtension()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        var storage = Substitute.For<IBrandLogoStorage>();
        var handler = new UploadBrandLogoHandler(repo, storage, new BrandAssetValidator(new BrandingOptions()));
        using var content = new MemoryStream(FakeSvgDeclaredAsPng);

        var result = await handler.HandleAsync(new UploadBrandLogoCommand
        {
            TenantId = TenantId,
            Filename = "logo.png", // extensión declarada PNG — pero la firma binaria es SVG/texto
            Content = content,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UploadBrandLogoOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoFormat);
        await storage.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task AC2_PesoMayorAlMaximoConfigurado_SeRechazaConLogoTooLarge()
    {
        var options = new BrandingOptions();
        options.Logo.MaxBytes = OnePixelPng.Length - 1; // fuerza el rechazo con un PNG válido pequeño
        var repo = Substitute.For<ITenantBrandingRepository>();
        var storage = Substitute.For<IBrandLogoStorage>();
        var handler = new UploadBrandLogoHandler(repo, storage, new BrandAssetValidator(options));
        using var content = new MemoryStream(OnePixelPng);

        var result = await handler.HandleAsync(new UploadBrandLogoCommand
        {
            TenantId = TenantId,
            Filename = "logo.png",
            Content = content,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UploadBrandLogoOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoTooLarge);
    }

    [Fact]
    public async Task AC2_DimensionesFueraDeRango_100x30_SeRechazaConLogoDimensions()
    {
        // El PNG 1x1 de fixture reporta 1x1 → fuerza min 120x40 via BrandingOptions estrictos
        // simulando el caso 100x30 mediante límites que lo excluyan (min real 120x40).
        var errors = new BrandAssetValidator(new BrandingOptions())
            .ValidateLogo("image/png", 1000, 100, 30);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoDimensions);
    }

    [Fact]
    public void AC2_DimensionesFueraDeRango_2001x100_SeRechazaConLogoDimensions()
    {
        var errors = new BrandAssetValidator(new BrandingOptions())
            .ValidateLogo("image/png", 1000, 2001, 100);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoDimensions);
    }

    [Fact]
    public async Task AC2_PngValidoPeroMenorAlMinimo_SeRechazaConElValidadorReal()
    {
        var repo = Substitute.For<ITenantBrandingRepository>();
        var storage = Substitute.For<IBrandLogoStorage>();
        storage.SaveAsync(TenantId, "logo.png", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredBrandLogo("brand-logo/1", "a".PadLeft(64, '0'), OnePixelPng.Length));
        repo.AddLogoVersionAsync(TenantId, Arg.Any<NewBrandLogo>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new TenantBrandLogoVersion(
                Guid.NewGuid(), TenantId, 1, TenantBrandLogoVersion.StatusActive,
                ci.Arg<NewBrandLogo>().ContentType, "logo.png", "brand-logo/1", "a".PadLeft(64, '0'), OnePixelPng.Length, 1, 1));

        var handler = new UploadBrandLogoHandler(repo, storage, new BrandAssetValidator(new BrandingOptions()));
        using var content = new MemoryStream(OnePixelPng);

        var result = await handler.HandleAsync(new UploadBrandLogoCommand
        {
            TenantId = TenantId,
            Filename = "logo.png",
            Content = content,
        }, TestContext.Current.CancellationToken);

        // El PNG 1x1 real está por debajo del mínimo 120x40 (AC2): se rechaza con LogoDimensions,
        // no se sube. Confirma que el validador real SÍ se aplica (a diferencia del permisivo).
        result.Outcome.Should().Be(UploadBrandLogoOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoDimensions);
    }
}
