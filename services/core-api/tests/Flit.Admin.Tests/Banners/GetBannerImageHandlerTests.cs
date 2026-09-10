using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Domain.Banners;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Banners;

/// <summary>
/// HU #12240, AC2/AC3 — ETag = SHA-256, 304 sin releer el binario, y 404 para banner
/// inexistente o con binario perdido en storage.
/// </summary>
public sealed class GetBannerImageHandlerTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public async Task HandleAsync_BannerInexistente_Responde404YNoAbreStorage()
    {
        var repo = new FakeBannerRepository();
        var storage = new FakeBannerImageStorage();

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(Guid.NewGuid(), ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        storage.OpenReadCalls.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_SinIfNoneMatch_DevuelveElBinarioConSuContentTypeYEtag()
    {
        var id = Guid.NewGuid();
        var sha256 = new string('a', 64);
        var repo = new FakeBannerRepository();
        repo.ImageRefs[id] = new BannerImageRef("fm://banners/x", sha256);
        var storage = new FakeBannerImageStorage();
        storage.Files["fm://banners/x"] = PngBytes;

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(id, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.IsNotModified.Should().BeFalse();
        result.Sha256.Should().Be(sha256);
        result.ContentType.Should().Be("image/png");
        result.Content.Should().NotBeNull();
        storage.OpenReadCalls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_IfNoneMatchCoincideConEtagActual_Responde304SinReleerElBinario()
    {
        var id = Guid.NewGuid();
        var sha256 = new string('a', 64);
        var repo = new FakeBannerRepository();
        repo.ImageRefs[id] = new BannerImageRef("fm://banners/x", sha256);
        var storage = new FakeBannerImageStorage();
        storage.Files["fm://banners/x"] = PngBytes;

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(id, ifNoneMatch: $"\"{sha256}\"", TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.IsNotModified.Should().BeTrue();
        result.Sha256.Should().Be(sha256);
        storage.OpenReadCalls.Should().Be(0, "AC2 exige NO releer el binario en un HIT de cache");
    }

    [Fact]
    public async Task HandleAsync_IfNoneMatchDistintoAlEtagActual_DevuelveElBinarioNuevo()
    {
        var id = Guid.NewGuid();
        var sha256Actual = new string('a', 64);
        var sha256Viejo = new string('z', 64);
        var repo = new FakeBannerRepository();
        repo.ImageRefs[id] = new BannerImageRef("fm://banners/x", sha256Actual);
        var storage = new FakeBannerImageStorage();
        storage.Files["fm://banners/x"] = PngBytes;

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(id, ifNoneMatch: $"\"{sha256Viejo}\"", TestContext.Current.CancellationToken);

        result.IsNotModified.Should().BeFalse("el ETag cambio (imagen reemplazada): debe releerse");
        result.Sha256.Should().Be(sha256Actual);
        storage.OpenReadCalls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_FilaEnBdPeroBinarioPerdidoEnStorage_Responde404()
    {
        var id = Guid.NewGuid();
        var repo = new FakeBannerRepository();
        repo.ImageRefs[id] = new BannerImageRef("fm://banners/perdido", new string('a', 64));
        var storage = new FakeBannerImageStorage(); // storage.Files vacio: no existe el binario

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(id, ifNoneMatch: null, TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_IfNoneMatchComodin_Responde304SinReleerElBinario()
    {
        var id = Guid.NewGuid();
        var repo = new FakeBannerRepository();
        repo.ImageRefs[id] = new BannerImageRef("fm://banners/x", new string('a', 64));
        var storage = new FakeBannerImageStorage();
        storage.Files["fm://banners/x"] = PngBytes;

        var result = await new GetBannerImageHandler(repo, storage)
            .HandleAsync(id, ifNoneMatch: "*", TestContext.Current.CancellationToken);

        result.IsNotModified.Should().BeTrue();
        storage.OpenReadCalls.Should().Be(0);
    }
}
