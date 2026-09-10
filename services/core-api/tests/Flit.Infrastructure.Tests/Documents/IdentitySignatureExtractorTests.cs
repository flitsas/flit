using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Filters;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class IdentitySignatureExtractorTests
{
    private static byte[] ScribblePng()
    {
        using var image = new Image<Rgba32>(48, 16);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
                image[x, y] = x % 3 == 0 ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Fact]
    public void PdfVacio_DevuelveNull()
    {
        new IdentitySignatureExtractor().TryExtract([]).Should().BeNull();
        new IdentitySignatureExtractor().TryExtract("%PDF-1.4 not a real file"u8.ToArray()).Should().BeNull();
    }

    [Fact]
    public void PdfConXObjectImagen_ExtraeBytes()
    {
        var png = ScribblePng();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        using (var img = XImage.FromStream(() => new MemoryStream(png)))
        {
            gfx.DrawString(
                "FIRMA Y AUTORIZACION DE TRAMITE DIGITAL",
                new XFont("Arial", 10),
                XBrushes.Black,
                40,
                40);
            gfx.DrawImage(img, 40, 80, 80, 24);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        var crop = new IdentitySignatureExtractor().TryExtract(ms.ToArray());

        crop.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(crop!.PngBytes).Should().BeTrue();
        using var decoded = Image.Load(crop.PngBytes);
        decoded.Width.Should().BeGreaterThan(0);
        decoded.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PdfConRastersFlateDeviceRgb_ExtraePngValido()
    {
        var width = 64;
        var height = 20;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                var ink = x > 8 && x < 56 && y > 6 && y < 14;
                rgb[i] = rgb[i + 1] = rgb[i + 2] = ink ? (byte)20 : (byte)255;
            }
        }

        var pdf = BuildPdfWithFlateRgbImage(width, height, rgb);
        var crop = new IdentitySignatureExtractor().TryExtract(pdf);

        crop.Should().NotBeNull();
        IdentitySignatureImageFormat.IsPng(crop!.PngBytes).Should().BeTrue();
        using var decoded = Image.Load(crop.PngBytes);
        decoded.Width.Should().Be(width);
        decoded.Height.Should().Be(height);
    }

    [Fact]
    public void GrisOscuroSobreNegro_PasaATintaVisibleConFondoTransparente()
    {
        using var source = new Image<Rgba32>(80, 40);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                source[x, y] = new Rgba32(0, 0, 0);
        }

        for (var x = 8; x < 72; x++)
        {
            source[x, 18] = new Rgba32(26, 26, 26);
            source[x, 19] = new Rgba32(30, 30, 30);
            source[x, 20] = new Rgba32(22, 22, 22);
        }

        using var ms = new MemoryStream();
        source.Save(ms, new PngEncoder());
        var ink = PdfXObjectPngDecoder.ToDocumentInk(ms.ToArray());

        using var decoded = Image.Load<Rgba32>(ink);
        var transparent = 0;
        var visibleInk = 0;
        for (var y = 0; y < decoded.Height; y++)
        {
            for (var x = 0; x < decoded.Width; x++)
            {
                var p = decoded[x, y];
                if (p.A == 0)
                    transparent++;
                if (p.A >= 96 && p.R <= 40)
                    visibleInk++;
            }
        }

        transparent.Should().BeGreaterThan(decoded.Width * decoded.Height / 2);
        visibleInk.Should().BeGreaterThan(50);
        PdfXObjectPngDecoder.HasVisibleInk(ink).Should().BeTrue();
        PdfXObjectPngDecoder.HasVisibleInk(ms.ToArray()).Should().BeFalse();
    }

    private static byte[] BuildPdfWithFlateRgbImage(int width, int height, byte[] rgb)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        var image = new PdfDictionary(doc);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/Filter", "/FlateDecode");
        var compressed = new FlateDecode().Encode(rgb);
        image.CreateStream(compressed);
        doc.Internals.AddObject(image);

        var resources = page.Elements.GetDictionary("/Resources") ?? new PdfDictionary(doc);
        page.Elements["/Resources"] = resources;
        var xObjects = resources.Elements.GetDictionary("/XObject") ?? new PdfDictionary();
        resources.Elements["/XObject"] = xObjects;
        xObjects.Elements["/ImSig"] = image;

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}
