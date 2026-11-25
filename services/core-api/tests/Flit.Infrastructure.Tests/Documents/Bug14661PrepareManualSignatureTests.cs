using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Bug #14661 — la firma manual (lienzo 760x340 de fondo blanco opaco) debe quedar con fondo transparente para
/// estamparse sin tapar el formulario, y no debe pasar por el filtro de transparencia de Kyverum.
/// </summary>
public sealed class Bug14661PrepareManualSignatureTests
{
    private static byte[] Lienzo(IImageEncoder encoder, bool conTrazo)
    {
        using var image = new Image<Rgba32>(760, 340, new Rgba32(255, 255, 255, 255));
        if (conTrazo)
        {
            for (var x = 60; x < 700; x++)
            {
                for (var t = 0; t < 4; t++)
                    image[x, 150 + t + (x / 40 % 20)] = new Rgba32(0, 0, 0, 255);
            }
        }

        using var ms = new MemoryStream();
        image.Save(ms, encoder);
        return ms.ToArray();
    }

    [Fact]
    public void PngBlancoOpacoConTrazo_DevuelvePngConFondoTransparenteYTrazoConservado()
    {
        var opaco = Lienzo(new PngEncoder(), conTrazo: true);

        var lista = new IdentitySignatureExtractor().PrepareManualSignature(opaco);

        lista.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(lista!).Should().BeTrue();
        using var image = Image.Load<Rgba32>(lista!);
        image.Width.Should().Be(760);
        image[5, 5].A.Should().Be(0, "el fondo blanco pasa a transparente");
        image[100, 150 + (100 / 40 % 20)].A.Should().Be(255, "el trazo negro se conserva opaco");
        image[100, 150 + (100 / 40 % 20)].R.Should().Be(0);
    }

    [Fact]
    public void PngTodoBlancoSinTinta_DevuelveNull() =>
        new IdentitySignatureExtractor().PrepareManualSignature(Lienzo(new PngEncoder(), conTrazo: false))
            .Should().BeNull();

    [Fact]
    public void JpegBlancoConTrazo_SeConvierteAPngTransparente()
    {
        var jpeg = Lienzo(new JpegEncoder { Quality = 90 }, conTrazo: true);

        var lista = new IdentitySignatureExtractor().PrepareManualSignature(jpeg);

        lista.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(lista!).Should().BeTrue();
        using var image = Image.Load<Rgba32>(lista!);
        image[5, 5].A.Should().Be(0);
    }

    [Fact]
    public void BytesQueNoSonImagen_DevuelveNull() =>
        new IdentitySignatureExtractor().PrepareManualSignature([1, 2, 3, 4, 5, 6, 7, 8]).Should().BeNull();

    [Fact]
    public void Kyverum_SigueExigiendoTransparencia_ElLienzoOpacoNoPasaIsUsableInk()
    {
        var extractor = new IdentitySignatureExtractor();
        var opaco = Lienzo(new PngEncoder(), conTrazo: true);

        extractor.IsUsableInk(opaco).Should().BeFalse("el filtro de Kyverum (Bug #13304) no cambia");
        extractor.IsUsableInk(extractor.PrepareManualSignature(opaco)!).Should().BeTrue();
    }
}
