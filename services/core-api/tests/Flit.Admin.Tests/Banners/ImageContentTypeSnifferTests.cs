using Flit.Admin.Application.Banners.GetBannerImage;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Banners;

/// <summary>
/// HU #12240, AC2 — deteccion de Content-Type por magic numbers (admin.banners no persiste
/// el content-type). Cada test verifica ademas que el stream queda en la posicion 0 tras
/// detectar: el caller debe poder transmitir el binario completo despues.
/// </summary>
public sealed class ImageContentTypeSnifferTests
{
    [Fact]
    public async Task DetectAsync_PngMagicBytes_DevuelveImagePng()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];
        using var stream = new MemoryStream(png);

        var contentType = await ImageContentTypeSniffer.DetectAsync(
            stream, TestContext.Current.CancellationToken);

        contentType.Should().Be("image/png");
        stream.Position.Should().Be(0, "el caller debe poder releer el binario completo despues");
    }

    [Fact]
    public async Task DetectAsync_JpegMagicBytes_DevuelveImageJpeg()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        using var stream = new MemoryStream(jpeg);

        var contentType = await ImageContentTypeSniffer.DetectAsync(
            stream, TestContext.Current.CancellationToken);

        contentType.Should().Be("image/jpeg");
        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task DetectAsync_WebpMagicBytes_DevuelveImageWebp()
    {
        byte[] webp =
        [
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x00, 0x00, 0x00, 0x00, // tamano (irrelevante para la deteccion)
            0x57, 0x45, 0x42, 0x50, // WEBP
        ];
        using var stream = new MemoryStream(webp);

        var contentType = await ImageContentTypeSniffer.DetectAsync(
            stream, TestContext.Current.CancellationToken);

        contentType.Should().Be("image/webp");
        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task DetectAsync_BytesDesconocidos_DevuelveOctetStream()
    {
        byte[] desconocido = [0x00, 0x01, 0x02, 0x03];
        using var stream = new MemoryStream(desconocido);

        var contentType = await ImageContentTypeSniffer.DetectAsync(
            stream, TestContext.Current.CancellationToken);

        contentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task DetectAsync_StreamNoSeekable_DevuelveOctetStreamSinFallar()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        using var stream = new NoSeekStream(png);

        var contentType = await ImageContentTypeSniffer.DetectAsync(
            stream, TestContext.Current.CancellationToken);

        contentType.Should().Be("application/octet-stream");
    }

    /// <summary>MemoryStream que reporta CanSeek=false, para simular un stream de solo avance.</summary>
    private sealed class NoSeekStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
